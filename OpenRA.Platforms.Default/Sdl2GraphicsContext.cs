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
using SDL2;

namespace OpenRA.Platforms.Default
{
	sealed class Sdl2GraphicsContext : GlGraphicsContext
	{
		readonly Sdl2PlatformWindow sdlWindow;
		IntPtr context;

		public Sdl2GraphicsContext(Sdl2PlatformWindow window)
			: base(window)
		{
			sdlWindow = window;

			// SDL requires us to create the GL context on the main thread to avoid various platform-specific issues.
			// We must then release it from the main thread before we rebind it to the render thread (in InitializeOpenGL below).
			context = SDL.SDL_GL_CreateContext(window.Window);
			if (context == IntPtr.Zero || SDL.SDL_GL_MakeCurrent(window.Window, IntPtr.Zero) < 0)
				throw new InvalidOperationException($"Can not create OpenGL context. (Error: {SDL.SDL_GetError()})");
		}

		internal override void InitializeOpenGL()
		{
			SetThreadAffinity();

			if (SDL.SDL_GL_MakeCurrent(sdlWindow.Window, context) < 0)
				throw new InvalidOperationException($"Can not bind OpenGL context. (Error: {SDL.SDL_GetError()})");

			InitializeGLState();
		}

		public override void Present()
		{
			VerifyThreadAffinity();
			SDL.SDL_GL_SwapWindow(sdlWindow.Window);
		}

		public override void SetVSyncEnabled(bool enabled)
		{
			VerifyThreadAffinity();
			SDL.SDL_GL_SetSwapInterval(enabled ? 1 : 0);
		}

		protected override void Dispose(bool disposing)
		{
			if (context != IntPtr.Zero)
			{
				SDL.SDL_GL_DeleteContext(context);
				context = IntPtr.Zero;
			}

			base.Dispose(disposing);
		}

		~Sdl2GraphicsContext()
		{
			Dispose(false);
		}
	}
}
