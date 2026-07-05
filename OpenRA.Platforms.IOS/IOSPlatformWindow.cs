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
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using ObjCRuntime;
using OpenRA.Platforms.Default;
using OpenRA.Primitives;
using SDL2;

namespace OpenRA.Platforms.IOS
{
	[SuppressMessage("Usage", "CA2216:Disposable types should declare finalizer",
		Justification = "The window lives for the application lifetime and SDL teardown must happen on the main thread.")]
	sealed class IOSPlatformWindow : ThreadAffine, IPlatformWindow, ISdl2InputWindow
	{
		// The event watch fires from inside SDL_PumpEvents, and iOS only ever has one window
		static IOSPlatformWindow instance;

		// Rooted so the marshaled callback is never garbage collected
		static readonly SDL.SDL_EventFilter LifecycleWatch = HandleLifecycleEvent;

		readonly Sdl2Input input;
		readonly IntPtr window;
		readonly IntPtr metalView;
		readonly AngleGraphicsContext angleContext;
		readonly ThreadedGraphicsContext threadedContext;
		readonly Size windowSize;
		Size surfaceSize;
		float windowScale;
		float scaleModifier;
		bool disposed;

		public IGraphicsContext Context => threadedContext;

		internal IntPtr Window => window;

		public Size NativeWindowSize => windowSize;
		public Size EffectiveWindowSize => new((int)(windowSize.Width / scaleModifier), (int)(windowSize.Height / scaleModifier));
		public float NativeWindowScale => windowScale;
		public float EffectiveWindowScale => windowScale * scaleModifier;
		public Size SurfaceSize => surfaceSize;

		public int DisplayCount => 1;
		public int CurrentDisplay => 0;

		public bool HasInputFocus { get; set; } = true;
		public bool IsSuspended { get; set; }

		public GLProfile GLProfile => GLProfile.ANGLE;
		public GLProfile[] SupportedGLProfiles { get; } = [GLProfile.ANGLE];

		public event Action<float, float, float, float> OnWindowScaleChanged = (oldNative, oldEffective, newNative, newEffective) => { };

		public IOSPlatformWindow(Size requestEffectiveWindowSize, WindowMode windowMode,
			float scaleModifier, int vertexBatchSize, int indexBatchSize, int videoDisplay, GLProfile requestProfile)
		{
			// iOS windows are always fullscreen: the requested size, mode, display, and
			// profile are dictated by the device instead of the incoming configuration.
			this.scaleModifier = scaleModifier;

			SDL.SDL_SetHint("SDL_IOS_ORIENTATIONS", "LandscapeLeft LandscapeRight");
			SDL.SDL_SetHint("SDL_IOS_HIDE_HOME_INDICATOR", "2");

			// SDL's built-in touch-to-mouse synthesis scaffolds the UI until the
			// managed gesture recognizer is enabled by the touch input setting.
			var useTouchInput = Game.Settings != null && Game.Settings.Game.UseTouchInput;
			SDL.SDL_SetHint("SDL_TOUCH_MOUSE_EVENTS", useTouchInput ? "0" : "1");
			SDL.SDL_SetHint("SDL_MOUSE_TOUCH_EVENTS", "0");

			if (SDL.SDL_Init(SDL.SDL_INIT_VIDEO) != 0)
				throw new InvalidOperationException($"SDL initialization failed: {SDL.SDL_GetError()}");

			window = SDL.SDL_CreateWindow("OpenRA", 0, 0, 0, 0,
				SDL.SDL_WindowFlags.SDL_WINDOW_METAL | SDL.SDL_WindowFlags.SDL_WINDOW_ALLOW_HIGHDPI |
				SDL.SDL_WindowFlags.SDL_WINDOW_FULLSCREEN | SDL.SDL_WindowFlags.SDL_WINDOW_BORDERLESS |
				SDL.SDL_WindowFlags.SDL_WINDOW_SHOWN);

			if (window == IntPtr.Zero)
				throw new InvalidOperationException($"Failed to create the SDL window: {SDL.SDL_GetError()}");

			// ANGLE renders into the CAMetalLayer that backs the SDL metal view
			metalView = SDL.SDL_Metal_CreateView(window);
			if (metalView == IntPtr.Zero)
				throw new InvalidOperationException($"Failed to create the Metal view: {SDL.SDL_GetError()}");

			var metalLayer = SDL.SDL_Metal_GetLayer(metalView);
			if (metalLayer == IntPtr.Zero)
				throw new InvalidOperationException("Failed to query the CAMetalLayer.");

			// The window size is measured in points; the drawable in pixels
			SDL.SDL_GetWindowSize(window, out var width, out var height);
			windowSize = new Size(width, height);
			SDL.SDL_Metal_GetDrawableSize(window, out var surfaceWidth, out var surfaceHeight);
			surfaceSize = new Size(surfaceWidth, surfaceHeight);
			windowScale = surfaceSize.Width * 1f / windowSize.Width;

			Console.WriteLine($"Using SDL {GetSDLVersion()} with OpenGL (ANGLE) renderer");
			Console.WriteLine($"Window size: {windowSize}, surface size: {surfaceSize}, scale: {windowScale}");

			instance = this;
			SDL.SDL_AddEventWatch(LifecycleWatch, IntPtr.Zero);

			angleContext = new AngleGraphicsContext(this, metalLayer);
			threadedContext = new ThreadedGraphicsContext(angleContext, vertexBatchSize, indexBatchSize);

			SDL.SDL_SetModState(SDL.SDL_Keymod.KMOD_NONE);
			input = new Sdl2Input();
		}

		[MonoPInvokeCallback(typeof(SDL.SDL_EventFilter))]
		static int HandleLifecycleEvent(IntPtr userdata, IntPtr eventPtr)
		{
			// UIKit delivers these synchronously from inside the event pump, which is
			// the only reliable point to quiesce the GPU before iOS suspends the app
			var e = Marshal.PtrToStructure<SDL.SDL_Event>(eventPtr);
			var window = instance;
			if (window == null || window.disposed)
				return 0;

			switch (e.type)
			{
				case SDL.SDL_EventType.SDL_APP_WILLENTERBACKGROUND:
					window.IsSuspended = true;
					window.angleContext.Suspended = true;
					window.threadedContext.Finish();
					break;

				case SDL.SDL_EventType.SDL_APP_DIDENTERBACKGROUND:
					IOSAudioSession.Pause();
					break;

				case SDL.SDL_EventType.SDL_APP_WILLENTERFOREGROUND:
					IOSAudioSession.Resume();
					break;

				case SDL.SDL_EventType.SDL_APP_DIDENTERFOREGROUND:
					window.angleContext.Suspended = false;
					window.IsSuspended = false;
					window.HasInputFocus = true;
					break;

				case SDL.SDL_EventType.SDL_APP_LOWMEMORY:
					Log.Write("debug", "Received a low-memory warning.");
					GC.Collect();
					break;

				case SDL.SDL_EventType.SDL_APP_TERMINATING:
					Game.Exit();
					break;
			}

			return 0;
		}

		public void PumpInput(IInputHandler inputHandler)
		{
			VerifyThreadAffinity();
			input.PumpInput(this, inputHandler, null);
		}

		public void WindowSizeChanged()
		{
			// The window size cannot change on iOS (no multitasking resize in v1),
			// but the drawable is worth revalidating after lifecycle transitions
			SDL.SDL_Metal_GetDrawableSize(window, out var width, out var height);
			surfaceSize = new Size(width, height);
		}

		public string GetClipboardText()
		{
			VerifyThreadAffinity();
			return Sdl2Input.GetClipboardText();
		}

		public bool SetClipboardText(string text)
		{
			VerifyThreadAffinity();
			return Sdl2Input.SetClipboardText(text);
		}

		public bool TryOpenUrl(string url)
		{
			try
			{
				return SDL.SDL_OpenURL(url) == 0;
			}
			catch
			{
				return false;
			}
		}

		public void GrabWindowMouseFocus() { }
		public void ReleaseWindowMouseFocus() { }
		public void SetRelativeMouseMode(bool mode) { }
		public void SetWindowTitle(string title) { }

		public void StartTextInput()
		{
			VerifyThreadAffinity();
			SDL.SDL_StartTextInput();
		}

		public void StopTextInput()
		{
			VerifyThreadAffinity();
			SDL.SDL_StopTextInput();
		}

		public void SetTextInputRect(Rectangle rect)
		{
			VerifyThreadAffinity();

			// Positions the input rect in window points so iOS can avoid covering it
			var s = EffectiveWindowScale / NativeWindowScale;
			var sdlRect = new SDL.SDL_Rect
			{
				x = (int)(rect.X * s),
				y = (int)(rect.Y * s),
				w = (int)(rect.Width * s),
				h = (int)(rect.Height * s)
			};

			SDL.SDL_SetTextInputRect(ref sdlRect);
		}

		public bool IsTextInputActive
		{
			get
			{
				VerifyThreadAffinity();
				return SDL.SDL_IsTextInputActive() == SDL.SDL_bool.SDL_TRUE;
			}
		}

		public IHardwareCursor CreateHardwareCursor(string name, Size size, byte[] data, int2 hotspot, bool pixelDouble)
		{
			// No pointer on a touchscreen: fall back to the software cursor
			return null;
		}

		public void SetHardwareCursor(IHardwareCursor cursor) { }

		public void SetScaleModifier(float scale)
		{
			var oldScaleModifier = scaleModifier;
			scaleModifier = scale;
			OnWindowScaleChanged(windowScale, windowScale * oldScaleModifier, windowScale, windowScale * scaleModifier);
		}

		static string GetSDLVersion()
		{
			SDL.SDL_GetVersion(out var version);
			return $"{version.major}.{version.minor}.{version.patch}";
		}

		public void Dispose()
		{
			if (disposed)
				return;

			disposed = true;
			instance = null;

			SDL.SDL_DelEventWatch(LifecycleWatch, IntPtr.Zero);

			threadedContext?.Dispose();

			if (metalView != IntPtr.Zero)
				SDL.SDL_Metal_DestroyView(metalView);

			if (window != IntPtr.Zero)
				SDL.SDL_DestroyWindow(window);

			SDL.SDL_Quit();
		}
	}
}
