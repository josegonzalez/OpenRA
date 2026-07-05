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

namespace OpenRA.Platforms.IOS
{
	/// <summary>The minimal EGL surface needed to drive ANGLE's Metal backend.</summary>
	[SuppressMessage("Microsoft.StyleCop.CSharp.NamingRules", "SA1310:FieldNamesMustNotContainUnderscore",
		Justification = "C-style naming is kept for consistency with the underlying native API.")]
	[SuppressMessage("Style", "IDE1006:Naming Styles",
		Justification = "C-style naming is kept for consistency with the underlying native API.")]
	static class EGL
	{
		public const int EGL_PLATFORM_ANGLE_ANGLE = 0x3202;
		public const int EGL_PLATFORM_ANGLE_TYPE_ANGLE = 0x3203;
		public const int EGL_PLATFORM_ANGLE_TYPE_METAL_ANGLE = 0x3489;

		public const int EGL_NONE = 0x3038;
		public const int EGL_TRUE = 1;

		public const int EGL_ALPHA_SIZE = 0x3021;
		public const int EGL_BLUE_SIZE = 0x3022;
		public const int EGL_GREEN_SIZE = 0x3023;
		public const int EGL_RED_SIZE = 0x3024;
		public const int EGL_DEPTH_SIZE = 0x3025;
		public const int EGL_STENCIL_SIZE = 0x3026;
		public const int EGL_SURFACE_TYPE = 0x3033;
		public const int EGL_WINDOW_BIT = 0x0004;
		public const int EGL_RENDERABLE_TYPE = 0x3040;
		public const int EGL_OPENGL_ES3_BIT = 0x0040;
		public const int EGL_CONTEXT_CLIENT_VERSION = 0x3098;

		public const int EGL_WIDTH = 0x3057;
		public const int EGL_HEIGHT = 0x3056;

		[DllImport("libEGL", CallingConvention = CallingConvention.Cdecl)]
		public static extern IntPtr eglGetPlatformDisplayEXT(int platform, IntPtr nativeDisplay, int[] attribList);

		[DllImport("libEGL", CallingConvention = CallingConvention.Cdecl)]
		public static extern int eglInitialize(IntPtr display, out int major, out int minor);

		[DllImport("libEGL", CallingConvention = CallingConvention.Cdecl)]
		public static extern int eglChooseConfig(IntPtr display, int[] attribList, IntPtr[] configs, int configSize, out int numConfig);

		[DllImport("libEGL", CallingConvention = CallingConvention.Cdecl)]
		public static extern IntPtr eglCreateContext(IntPtr display, IntPtr config, IntPtr shareContext, int[] attribList);

		[DllImport("libEGL", CallingConvention = CallingConvention.Cdecl)]
		public static extern IntPtr eglCreateWindowSurface(IntPtr display, IntPtr config, IntPtr nativeWindow, int[] attribList);

		[DllImport("libEGL", CallingConvention = CallingConvention.Cdecl)]
		public static extern int eglMakeCurrent(IntPtr display, IntPtr draw, IntPtr read, IntPtr context);

		[DllImport("libEGL", CallingConvention = CallingConvention.Cdecl)]
		public static extern int eglSwapBuffers(IntPtr display, IntPtr surface);

		[DllImport("libEGL", CallingConvention = CallingConvention.Cdecl)]
		public static extern int eglSwapInterval(IntPtr display, int interval);

		[DllImport("libEGL", CallingConvention = CallingConvention.Cdecl)]
		public static extern int eglQuerySurface(IntPtr display, IntPtr surface, int attribute, out int value);

		[DllImport("libEGL", CallingConvention = CallingConvention.Cdecl)]
		public static extern IntPtr eglGetProcAddress(string name);

		[DllImport("libEGL", CallingConvention = CallingConvention.Cdecl)]
		public static extern int eglGetError();

		[DllImport("libEGL", CallingConvention = CallingConvention.Cdecl)]
		public static extern int eglDestroySurface(IntPtr display, IntPtr surface);

		[DllImport("libEGL", CallingConvention = CallingConvention.Cdecl)]
		public static extern int eglDestroyContext(IntPtr display, IntPtr context);

		[DllImport("libEGL", CallingConvention = CallingConvention.Cdecl)]
		public static extern int eglTerminate(IntPtr display);
	}
}
