#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;

namespace OpenRA
{
	/// <summary>
	/// Translates raw touch events into the mouse events that the widget and order
	/// systems consume: a tap becomes a left click, a drag becomes a left-button drag
	/// (selection box), and a long-press becomes a right click (contextual/cancel).
	/// Two fingers become a pan/pinch gesture. The first press is deferred until it
	/// can be disambiguated, so no events leak out of gestures that turn out to be
	/// something else.
	/// </summary>
	public sealed class TouchGestureRecognizer
	{
		enum GestureState { Idle, TouchPending, DragActive, LongPressFired, TwoFingerActive, IgnoreUntilAllUp }

		/// <summary>Maximum finger travel in effective window units before a press becomes a drag.</summary>
		const int TapSlop = 12;

		/// <summary>Successive releases within this time and distance increase the multi-tap count.</summary>
		const int MultiTapDurationMs = 300;
		const int MultiTapSlop = 24;

		readonly Func<int> longPressDelayMs;
		readonly HashSet<long> fingersDown = [];

		GestureState state = GestureState.Idle;
		long activeFinger;
		int2 origin;
		int2 lastPosition;
		long downTime;
		Modifiers lastModifiers;

		// Two-finger gesture tracking
		long secondFinger;
		int2 firstFingerPosition;
		int2 secondFingerPosition;
		int2 lastCentroid;
		float lastFingerDistance;

		// Release history for multi-tap detection. The sentinel is old enough to never
		// match a real release but small enough to avoid arithmetic overflow.
		(long Time, int2 Location) previousRelease = (-MultiTapDurationMs, int2.Zero);
		(long Time, int2 Location) olderRelease = (-MultiTapDurationMs, int2.Zero);

		public TouchGestureRecognizer(Func<int> longPressDelayMs)
		{
			this.longPressDelayMs = longPressDelayMs;
		}

		public void Process(TouchInput touch, long now, IInputHandler handler)
		{
			switch (touch.Event)
			{
				case TouchInputEvent.Down:
					OnFingerDown(touch, now, handler);
					break;
				case TouchInputEvent.Move:
					OnFingerMove(touch, handler);
					break;
				case TouchInputEvent.Up:
					OnFingerUp(touch, now, handler, cancelled: false);
					break;
				case TouchInputEvent.Cancel:
					OnFingerUp(touch, now, handler, cancelled: true);
					break;
			}
		}

		/// <summary>Called once per frame to fire time-based transitions (long-press).</summary>
		public void Tick(long now, IInputHandler handler)
		{
			if (state != GestureState.TouchPending || now - downTime < longPressDelayMs())
				return;

			state = GestureState.LongPressFired;
			EmitMouse(handler, MouseInputEvent.Move, MouseButton.None, lastPosition, 0);
			EmitMouse(handler, MouseInputEvent.Down, MouseButton.Right, lastPosition, 1);
			EmitMouse(handler, MouseInputEvent.Up, MouseButton.Right, lastPosition, 1);
		}

		void OnFingerDown(TouchInput touch, long now, IInputHandler handler)
		{
			fingersDown.Add(touch.FingerId);
			lastModifiers = touch.Modifiers;

			switch (state)
			{
				case GestureState.Idle:
					state = GestureState.TouchPending;
					activeFinger = touch.FingerId;
					origin = lastPosition = touch.Location;
					downTime = now;
					break;

				case GestureState.TouchPending:
					// A second finger before the press was disambiguated starts a two-finger
					// pan/pinch gesture. No mouse events have been emitted yet.
					state = GestureState.TwoFingerActive;
					secondFinger = touch.FingerId;
					firstFingerPosition = lastPosition;
					secondFingerPosition = touch.Location;
					lastCentroid = (firstFingerPosition + secondFingerPosition) / 2;
					lastFingerDistance = Distance(firstFingerPosition, secondFingerPosition);
					EmitGesture(handler, GestureType.TwoFingerBegin, lastCentroid, int2.Zero, 0f);
					break;

				default:
					// Extra fingers never interrupt an active drag, gesture, or already-fired press
					break;
			}
		}

		static float Distance(int2 a, int2 b)
		{
			float dx = a.X - b.X;
			float dy = a.Y - b.Y;
			return MathF.Sqrt(dx * dx + dy * dy);
		}

		void OnFingerMove(TouchInput touch, IInputHandler handler)
		{
			if (state == GestureState.TwoFingerActive)
			{
				if (touch.FingerId == activeFinger)
					firstFingerPosition = touch.Location;
				else if (touch.FingerId == secondFinger)
					secondFingerPosition = touch.Location;
				else
					return;

				var centroid = (firstFingerPosition + secondFingerPosition) / 2;
				var distance = Distance(firstFingerPosition, secondFingerPosition);

				// Pan and pinch are not mutually exclusive: both apply every update,
				// which keeps the world glued to the fingers.
				var zoomDelta = distance > float.Epsilon && lastFingerDistance > float.Epsilon
					? MathF.Log(distance / lastFingerDistance) : 0f;

				EmitGesture(handler, GestureType.TwoFingerUpdate, centroid, centroid - lastCentroid, zoomDelta);
				lastCentroid = centroid;
				lastFingerDistance = distance;
				return;
			}

			if (touch.FingerId != activeFinger)
				return;

			lastModifiers = touch.Modifiers;

			switch (state)
			{
				case GestureState.TouchPending:
					lastPosition = touch.Location;
					if ((touch.Location - origin).Length > TapSlop)
					{
						// The press is a drag: anchor the button press at the original
						// contact so selection boxes measure from where the finger landed.
						state = GestureState.DragActive;
						EmitMouse(handler, MouseInputEvent.Move, MouseButton.None, origin, 0);
						EmitMouse(handler, MouseInputEvent.Down, MouseButton.Left, origin, 1);
						EmitMouse(handler, MouseInputEvent.Move, MouseButton.Left, touch.Location, 0, touch.Delta);
					}

					break;

				case GestureState.DragActive:
					lastPosition = touch.Location;
					EmitMouse(handler, MouseInputEvent.Move, MouseButton.Left, touch.Location, 0, touch.Delta);
					break;

				default:
					break;
			}
		}

		void OnFingerUp(TouchInput touch, long now, IInputHandler handler, bool cancelled)
		{
			fingersDown.Remove(touch.FingerId);

			if (state == GestureState.TwoFingerActive)
			{
				// Either tracked finger ends the gesture; the survivor must not become a phantom tap
				if (touch.FingerId == activeFinger || touch.FingerId == secondFinger)
				{
					EmitGesture(handler, GestureType.TwoFingerEnd, lastCentroid, int2.Zero, 0f);
					state = fingersDown.Count > 0 ? GestureState.IgnoreUntilAllUp : GestureState.Idle;
				}
				else if (fingersDown.Count == 0)
					state = GestureState.Idle;

				return;
			}

			if (touch.FingerId != activeFinger)
			{
				// The active finger may have lifted first: don't get stuck ignoring input
				if (fingersDown.Count == 0 && state == GestureState.IgnoreUntilAllUp)
					state = GestureState.Idle;

				return;
			}

			lastModifiers = touch.Modifiers;

			switch (state)
			{
				case GestureState.TouchPending:
					if (!cancelled)
					{
						var tapCount = GetTapCount(touch.Location, now);
						EmitMouse(handler, MouseInputEvent.Move, MouseButton.None, touch.Location, 0);
						EmitMouse(handler, MouseInputEvent.Down, MouseButton.Left, touch.Location, tapCount);
						EmitMouse(handler, MouseInputEvent.Up, MouseButton.Left, touch.Location, tapCount);
					}

					break;

				case GestureState.DragActive:
					EmitMouse(handler, MouseInputEvent.Up, MouseButton.Left, cancelled ? lastPosition : touch.Location, 1);
					break;

				default:
					break;
			}

			state = fingersDown.Count > 0 ? GestureState.IgnoreUntilAllUp : GestureState.Idle;
		}

		int GetTapCount(int2 location, long now)
		{
			var count = 1;
			if (now - previousRelease.Time < MultiTapDurationMs && (location - previousRelease.Location).Length < MultiTapSlop)
			{
				count = 2;
				if (previousRelease.Time - olderRelease.Time < MultiTapDurationMs && (previousRelease.Location - olderRelease.Location).Length < MultiTapSlop)
					count = 3;
			}

			olderRelease = previousRelease;
			previousRelease = (now, location);
			return count;
		}

		void EmitMouse(IInputHandler handler, MouseInputEvent e, MouseButton button, int2 location, int tapCount, int2 delta = default)
		{
			handler.OnMouseInput(new MouseInput(e, button, location, delta, lastModifiers, tapCount));
		}

		static void EmitGesture(IInputHandler handler, GestureType type, int2 location, int2 delta, float zoomDelta)
		{
			(handler as IGestureHandler)?.OnGestureInput(new GestureInput(type, location, delta, zoomDelta));
		}
	}
}
