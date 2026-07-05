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
using OpenRA.Platforms.Default;

namespace OpenRA.Platforms.IOS
{
	/// <summary>A GL context provided by ANGLE's Metal backend, rendering into the SDL window's CAMetalLayer.</summary>
	sealed class AngleGraphicsContext : GlGraphicsContext
	{
		readonly IntPtr metalLayer;
		IntPtr display;
		IntPtr context;
		IntPtr surface;

		/// <summary>Set while the application is in the background, where Metal work is forbidden.</summary>
		public volatile bool Suspended;

		public AngleGraphicsContext(IOSPlatformWindow window, IntPtr metalLayer)
			: base(window)
		{
			this.metalLayer = metalLayer;
		}

		internal override void InitializeOpenGL()
		{
			// Unlike SDL GL contexts, EGL contexts can be created directly
			// on the render thread that will use them.
			SetThreadAffinity();

			display = EGL.eglGetPlatformDisplayEXT(
				EGL.EGL_PLATFORM_ANGLE_ANGLE,
				IntPtr.Zero,
				[EGL.EGL_PLATFORM_ANGLE_TYPE_ANGLE, EGL.EGL_PLATFORM_ANGLE_TYPE_METAL_ANGLE, EGL.EGL_NONE]);

			if (display == IntPtr.Zero)
				throw new InvalidOperationException($"Failed to open the ANGLE Metal display. (EGL error 0x{EGL.eglGetError():x})");

			if (EGL.eglInitialize(display, out _, out _) != EGL.EGL_TRUE)
				throw new InvalidOperationException($"Failed to initialize EGL. (EGL error 0x{EGL.eglGetError():x})");

			var configs = new IntPtr[1];
			int[] configAttribs =
			[
				EGL.EGL_RED_SIZE, 8,
				EGL.EGL_GREEN_SIZE, 8,
				EGL.EGL_BLUE_SIZE, 8,
				EGL.EGL_ALPHA_SIZE, 8,
				EGL.EGL_DEPTH_SIZE, 16,
				EGL.EGL_STENCIL_SIZE, 0,
				EGL.EGL_SURFACE_TYPE, EGL.EGL_WINDOW_BIT,
				EGL.EGL_RENDERABLE_TYPE, EGL.EGL_OPENGL_ES3_BIT,
				EGL.EGL_NONE
			];

			if (EGL.eglChooseConfig(display, configAttribs, configs, 1, out var numConfig) != EGL.EGL_TRUE || numConfig < 1)
				throw new InvalidOperationException($"No suitable EGL config found. (EGL error 0x{EGL.eglGetError():x})");

			var config = configs[0];
			context = EGL.eglCreateContext(display, config, IntPtr.Zero, [EGL.EGL_CONTEXT_CLIENT_VERSION, 3, EGL.EGL_NONE]);
			if (context == IntPtr.Zero)
				throw new InvalidOperationException($"Failed to create an OpenGL ES 3 context. (EGL error 0x{EGL.eglGetError():x})");

			// ANGLE accepts the CAMetalLayer backing the SDL window as a native window
			surface = EGL.eglCreateWindowSurface(display, config, metalLayer, null);
			if (surface == IntPtr.Zero)
				throw new InvalidOperationException($"Failed to create the EGL window surface. (EGL error 0x{EGL.eglGetError():x})");

			if (EGL.eglMakeCurrent(display, surface, surface, context) != EGL.EGL_TRUE)
				throw new InvalidOperationException($"Failed to bind the OpenGL ES context. (EGL error 0x{EGL.eglGetError():x})");

			// The drawable must match the surface that SDL configured for the layer
			EGL.eglQuerySurface(display, surface, EGL.EGL_WIDTH, out var width);
			EGL.eglQuerySurface(display, surface, EGL.EGL_HEIGHT, out var height);
			if (width != Window.SurfaceSize.Width || height != Window.SurfaceSize.Height)
				Log.Write("graphics", $"EGL surface size {width}x{height} does not match the window surface {Window.SurfaceSize}.");

			OpenGL.GetProcAddress = EGL.eglGetProcAddress;
			InitializeGLState();
		}

		public override void Present()
		{
			VerifyThreadAffinity();

			// Metal command submission while backgrounded terminates the app
			if (Suspended)
				return;

			if (EGL.eglSwapBuffers(display, surface) != EGL.EGL_TRUE)
				Log.Write("graphics", $"eglSwapBuffers failed. (EGL error 0x{EGL.eglGetError():x})");
		}

		public override void SetVSyncEnabled(bool enabled)
		{
			VerifyThreadAffinity();
			EGL.eglSwapInterval(display, enabled ? 1 : 0);
		}

		protected override void Dispose(bool disposing)
		{
			if (display != IntPtr.Zero)
			{
				EGL.eglMakeCurrent(display, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

				if (surface != IntPtr.Zero)
					EGL.eglDestroySurface(display, surface);

				if (context != IntPtr.Zero)
					EGL.eglDestroyContext(display, context);

				EGL.eglTerminate(display);
				display = surface = context = IntPtr.Zero;
			}

			base.Dispose(disposing);
		}

		~AngleGraphicsContext()
		{
			Dispose(false);
		}
	}
}
