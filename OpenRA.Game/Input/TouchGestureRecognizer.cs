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
	/// The first press is deferred until it can be disambiguated, so no events leak
	/// out of gestures that turn out to be something else.
	/// </summary>
	public sealed class TouchGestureRecognizer
	{
		enum GestureState { Idle, TouchPending, DragActive, LongPressFired, IgnoreUntilAllUp }

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
					OnFingerDown(touch, now);
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

		void OnFingerDown(TouchInput touch, long now)
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
					// A second finger before the press was disambiguated: this is not a tap,
					// drag, or long-press, and no mouse events have been emitted yet.
					state = GestureState.IgnoreUntilAllUp;
					break;

				default:
					// Extra fingers never interrupt an active drag or an already-fired press
					break;
			}
		}

		void OnFingerMove(TouchInput touch, IInputHandler handler)
		{
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
	}
}
