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
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Foundation;
using ObjCRuntime;
using OpenRA.Platforms.IOS;
using SDL2;

namespace OpenRA.iOS
{
	static class Program
	{
		static string[] launchArgs = [];

		static int Main(string[] args)
		{
			// Process arguments (e.g. Launch.Connect for automated testing) are
			// forwarded into the engine once SDL hands control back to GameMain
			launchArgs = args;

			// Native symbol resolution must be in place before anything touches SDL, Lua, or ANGLE
			NativeLibraries.Initialize();

			// Lock to landscape before SDL creates its UIKit app delegate and root view
			// controller: OpenRA renders a fixed-size landscape surface and cannot reflow
			// to portrait, so the view controller must never rotate the app there.
			SDL.SDL_SetHint("SDL_IOS_ORIENTATIONS", "LandscapeLeft LandscapeRight");

			// SDL owns UIApplicationMain and its delegate: it invokes GameMain on the
			// UIKit main thread once the application has finished launching, which keeps
			// the launch watchdog happy and lets SDL_PollEvent pump the UIKit run loop.
			return SDL.SDL_UIKitRunApp(0, IntPtr.Zero, GameMain);
		}

		[MonoPInvokeCallback(typeof(SDL.SDL_main_func))]
		static int GameMain(int argc, IntPtr argv)
		{
			// Statically-linked assemblies stand in for the runtime loading
			// that the engine performs on desktop platforms
			ObjectCreator.RegisterAssembly(typeof(OpenRA.Mods.Common.Traits.Mobile).Assembly);
			ObjectCreator.RegisterAssembly(typeof(OpenRA.Mods.Cnc.Traits.Chronoshiftable).Assembly);
			ObjectCreator.RegisterAssembly(typeof(OpenRA.Mods.D2k.Traits.AttractsWorms).Assembly);
			Game.PlatformFactory = () => new IOSPlatform();

			IOSAppEnvironment.ExcludeContentFromBackup();

			var result = (int)Game.InitializeAndRun(["Game.Mod=ra", .. launchArgs]);

			// SDL keeps the UIKit run loop alive after the game loop exits, so the
			// process would linger with a torn-down game. Terminate so the in-game
			// Quit button actually closes the app.
			Environment.Exit(result);
			return result;
		}
	}

	static class NativeLibraries
	{
		public static void Initialize()
		{
			// Static libraries resolve through the main executable under full AOT;
			// the ANGLE dynamic frameworks are embedded next to the app binary.
			foreach (var assembly in new[]
			{
				typeof(SDL.SDL_version).Assembly, // SDL2-CS
				typeof(OpenAL.AL10).Assembly, // OpenAL-CS
				typeof(Eluant.LuaRuntime).Assembly, // Eluant (lua51)
				typeof(IOSPlatform).Assembly, // EGL, freetype6, soft_oal DllImports
			})
				NativeLibrary.SetDllImportResolver(assembly, Resolve);
		}

		static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
		{
			if (NativeLibrary.TryLoad(name, assembly, searchPath, out var handle))
				return handle;

			var framework = Path.Combine(NSBundle.MainBundle.BundlePath, "Frameworks", name + ".framework", name);
			if (File.Exists(framework) && NativeLibrary.TryLoad(framework, out handle))
				return handle;

			// Statically linked symbols live in the app binary itself
			return NativeLibrary.GetMainProgramHandle();
		}
	}
}
