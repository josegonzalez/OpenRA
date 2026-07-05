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

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace OpenRA.Test
{
	sealed class MouseEventRecorder : IInputHandler, IGestureHandler
	{
		public readonly List<MouseInput> Events = [];
		public readonly List<GestureInput> Gestures = [];

		public void ModifierKeys(Modifiers mods) { }
		public void OnKeyInput(KeyInput input) { }
		public void OnTextInput(string text) { }
		public void OnMouseInput(MouseInput input) { Events.Add(input); }
		public void OnGestureInput(GestureInput input) { Gestures.Add(input); }
	}

	[TestFixture]
	sealed class TouchGestureRecognizerTest
	{
		const int LongPressDelay = 500;

		TouchGestureRecognizer recognizer;
		MouseEventRecorder recorder;

		[SetUp]
		public void SetUp()
		{
			recognizer = new TouchGestureRecognizer(() => LongPressDelay);
			recorder = new MouseEventRecorder();
		}

		void Touch(TouchInputEvent e, long fingerId, int2 location, long now)
		{
			recognizer.Process(new TouchInput(e, fingerId, location, int2.Zero, Modifiers.None), now, recorder);
		}

		[TestCase(TestName = "A quick tap synthesizes a left click at the tap position.")]
		public void TapSynthesizesLeftClick()
		{
			Touch(TouchInputEvent.Down, 1, new int2(100, 100), 0);
			Assert.That(recorder.Events, Is.Empty, "No events may leak before the gesture is disambiguated");

			Touch(TouchInputEvent.Up, 1, new int2(102, 101), 80);

			Assert.That(recorder.Events.Select(e => e.Event), Is.EqualTo(
				new[] { MouseInputEvent.Move, MouseInputEvent.Down, MouseInputEvent.Up }));
			Assert.That(recorder.Events[1].Button, Is.EqualTo(MouseButton.Left));
			Assert.That(recorder.Events[1].Location, Is.EqualTo(new int2(102, 101)));
			Assert.That(recorder.Events[1].MultiTapCount, Is.EqualTo(1));
		}

		[TestCase(TestName = "A second tap close in time and space raises the multi-tap count.")]
		public void DoubleTapRaisesMultiTapCount()
		{
			Touch(TouchInputEvent.Down, 1, new int2(100, 100), 0);
			Touch(TouchInputEvent.Up, 1, new int2(100, 100), 50);
			Touch(TouchInputEvent.Down, 2, new int2(105, 103), 150);
			Touch(TouchInputEvent.Up, 2, new int2(105, 103), 200);

			var second = recorder.Events.Skip(3).ToArray();
			Assert.That(second.Select(e => e.Event), Is.EqualTo(
				new[] { MouseInputEvent.Move, MouseInputEvent.Down, MouseInputEvent.Up }));
			Assert.That(second[1].MultiTapCount, Is.EqualTo(2));
			Assert.That(second[2].MultiTapCount, Is.EqualTo(2));
		}

		[TestCase(TestName = "Movement beyond the slop starts a left-button drag anchored at the initial contact.")]
		public void DragAnchorsAtOrigin()
		{
			Touch(TouchInputEvent.Down, 1, new int2(100, 100), 0);
			Touch(TouchInputEvent.Move, 1, new int2(105, 105), 30);
			Assert.That(recorder.Events, Is.Empty, "Movement within the slop must not start a drag");

			Touch(TouchInputEvent.Move, 1, new int2(140, 150), 60);

			Assert.That(recorder.Events.Select(e => e.Event), Is.EqualTo(
				new[] { MouseInputEvent.Move, MouseInputEvent.Down, MouseInputEvent.Move }));
			Assert.That(recorder.Events[1].Button, Is.EqualTo(MouseButton.Left));
			Assert.That(recorder.Events[1].Location, Is.EqualTo(new int2(100, 100)), "The press must anchor at the initial contact");
			Assert.That(recorder.Events[2].Location, Is.EqualTo(new int2(140, 150)));

			Touch(TouchInputEvent.Up, 1, new int2(150, 160), 90);
			Assert.That(recorder.Events[3].Event, Is.EqualTo(MouseInputEvent.Up));
			Assert.That(recorder.Events[3].Button, Is.EqualTo(MouseButton.Left));
			Assert.That(recorder.Events[3].Location, Is.EqualTo(new int2(150, 160)));
		}

		[TestCase(TestName = "Holding a press synthesizes a right click after the long-press delay.")]
		public void LongPressSynthesizesRightClick()
		{
			Touch(TouchInputEvent.Down, 1, new int2(100, 100), 0);
			recognizer.Tick(LongPressDelay - 1, recorder);
			Assert.That(recorder.Events, Is.Empty, "The long-press must not fire before the delay elapses");

			recognizer.Tick(LongPressDelay, recorder);

			Assert.That(recorder.Events.Select(e => e.Event), Is.EqualTo(
				new[] { MouseInputEvent.Move, MouseInputEvent.Down, MouseInputEvent.Up }));
			Assert.That(recorder.Events[1].Button, Is.EqualTo(MouseButton.Right));

			// The lingering finger must not produce further events
			Touch(TouchInputEvent.Move, 1, new int2(200, 200), LongPressDelay + 100);
			Touch(TouchInputEvent.Up, 1, new int2(200, 200), LongPressDelay + 200);
			Assert.That(recorder.Events, Has.Count.EqualTo(3));
		}

		[TestCase(TestName = "A second finger before disambiguation suppresses all mouse synthesis.")]
		public void SecondFingerSuppressesMouseSynthesis()
		{
			Touch(TouchInputEvent.Down, 1, new int2(100, 100), 0);
			Touch(TouchInputEvent.Down, 2, new int2(200, 100), 20);
			Touch(TouchInputEvent.Move, 1, new int2(150, 150), 50);
			Touch(TouchInputEvent.Up, 1, new int2(150, 150), 100);
			Touch(TouchInputEvent.Up, 2, new int2(200, 100), 120);

			Assert.That(recorder.Events, Is.Empty);

			// And the recognizer must recover for the next gesture
			Touch(TouchInputEvent.Down, 3, new int2(50, 50), 200);
			Touch(TouchInputEvent.Up, 3, new int2(50, 50), 260);
			Assert.That(recorder.Events, Has.Count.EqualTo(3));
		}

		[TestCase(TestName = "Two fingers moving together emit pan updates with the centroid delta.")]
		public void TwoFingerPanEmitsCentroidDelta()
		{
			Touch(TouchInputEvent.Down, 1, new int2(100, 100), 0);
			Touch(TouchInputEvent.Down, 2, new int2(200, 100), 20);

			Assert.That(recorder.Gestures, Has.Count.EqualTo(1));
			Assert.That(recorder.Gestures[0].Type, Is.EqualTo(GestureType.TwoFingerBegin));
			Assert.That(recorder.Gestures[0].Location, Is.EqualTo(new int2(150, 100)));

			// Both fingers translate by (20, 30): centroid moves in two steps
			Touch(TouchInputEvent.Move, 1, new int2(120, 130), 40);
			Touch(TouchInputEvent.Move, 2, new int2(220, 130), 45);

			var updates = recorder.Gestures.Skip(1).ToArray();
			Assert.That(updates.Select(g => g.Type), Is.All.EqualTo(GestureType.TwoFingerUpdate));
			var total = updates.Aggregate(int2.Zero, (acc, g) => acc + g.Delta);
			Assert.That(total, Is.EqualTo(new int2(20, 30)));
			Assert.That(updates.Sum(g => g.ZoomDelta), Is.EqualTo(0f).Within(1e-4), "A pure translation must not zoom");

			Touch(TouchInputEvent.Up, 1, new int2(120, 130), 80);
			Assert.That(recorder.Gestures[^1].Type, Is.EqualTo(GestureType.TwoFingerEnd));
			Assert.That(recorder.Events, Is.Empty, "Gestures must not synthesize mouse events");
		}

		[TestCase(TestName = "Spreading two fingers emits the log of the pinch ratio as the zoom delta.")]
		public void PinchEmitsLogZoomDelta()
		{
			Touch(TouchInputEvent.Down, 1, new int2(100, 100), 0);
			Touch(TouchInputEvent.Down, 2, new int2(200, 100), 20);

			// Distance doubles from 100 to 200 around the same centroid
			Touch(TouchInputEvent.Move, 1, new int2(50, 100), 40);
			Touch(TouchInputEvent.Move, 2, new int2(250, 100), 45);

			var zoom = recorder.Gestures.Where(g => g.Type == GestureType.TwoFingerUpdate).Sum(g => g.ZoomDelta);
			Assert.That(zoom, Is.EqualTo(System.MathF.Log(2f)).Within(1e-4));
		}

		[TestCase(TestName = "The finger surviving a two-finger gesture does not become a phantom tap.")]
		public void SurvivingFingerIsNotAPhantomTap()
		{
			Touch(TouchInputEvent.Down, 1, new int2(100, 100), 0);
			Touch(TouchInputEvent.Down, 2, new int2(200, 100), 20);
			Touch(TouchInputEvent.Up, 2, new int2(200, 100), 50);

			Assert.That(recorder.Gestures[^1].Type, Is.EqualTo(GestureType.TwoFingerEnd));

			Touch(TouchInputEvent.Move, 1, new int2(150, 150), 80);
			Touch(TouchInputEvent.Up, 1, new int2(150, 150), 120);

			Assert.That(recorder.Events, Is.Empty);
		}

		[TestCase(TestName = "The recognizer recovers when the active finger lifts before the second finger.")]
		public void RecoversWhenActiveFingerLiftsFirst()
		{
			Touch(TouchInputEvent.Down, 1, new int2(100, 100), 0);
			Touch(TouchInputEvent.Down, 2, new int2(200, 100), 20);
			Touch(TouchInputEvent.Up, 1, new int2(100, 100), 50);
			Touch(TouchInputEvent.Up, 2, new int2(200, 100), 80);

			Assert.That(recorder.Events, Is.Empty);

			Touch(TouchInputEvent.Down, 3, new int2(50, 50), 200);
			Touch(TouchInputEvent.Up, 3, new int2(50, 50), 260);
			Assert.That(recorder.Events, Has.Count.EqualTo(3), "The recognizer must return to idle after all fingers lift");
		}

		[TestCase(TestName = "A cancelled press synthesizes no click.")]
		public void CancelledPressSynthesizesNothing()
		{
			Touch(TouchInputEvent.Down, 1, new int2(100, 100), 0);
			Touch(TouchInputEvent.Cancel, 1, new int2(100, 100), 50);

			Assert.That(recorder.Events, Is.Empty);
		}

		[TestCase(TestName = "A cancelled drag releases the synthesized button.")]
		public void CancelledDragReleasesButton()
		{
			Touch(TouchInputEvent.Down, 1, new int2(100, 100), 0);
			Touch(TouchInputEvent.Move, 1, new int2(140, 150), 30);
			Touch(TouchInputEvent.Cancel, 1, new int2(140, 150), 60);

			Assert.That(recorder.Events[^1].Event, Is.EqualTo(MouseInputEvent.Up));
			Assert.That(recorder.Events[^1].Button, Is.EqualTo(MouseButton.Left));
		}

		[TestCase(TestName = "Extra fingers do not interrupt an active drag.")]
		public void ExtraFingersDoNotInterruptDrag()
		{
			Touch(TouchInputEvent.Down, 1, new int2(100, 100), 0);
			Touch(TouchInputEvent.Move, 1, new int2(140, 150), 30);
			var countAfterDragStart = recorder.Events.Count;

			Touch(TouchInputEvent.Down, 2, new int2(300, 300), 40);
			Touch(TouchInputEvent.Move, 1, new int2(160, 170), 50);

			Assert.That(recorder.Events, Has.Count.EqualTo(countAfterDragStart + 1));
			Assert.That(recorder.Events[^1].Event, Is.EqualTo(MouseInputEvent.Move));
			Assert.That(recorder.Events[^1].Button, Is.EqualTo(MouseButton.Left));
		}
	}
}
