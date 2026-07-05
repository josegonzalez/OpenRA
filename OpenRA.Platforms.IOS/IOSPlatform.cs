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
using OpenRA.Primitives;

namespace OpenRA.Platforms.IOS
{
	public class IOSPlatform : IPlatform
	{
		public IPlatformWindow CreateWindow(
			Size size, WindowMode windowMode, float scaleModifier, int vertexBatchSize, int indexBatchSize, int videoDisplay, GLProfile profile)
		{
			return new IOSPlatformWindow(size, windowMode, scaleModifier, vertexBatchSize, indexBatchSize, videoDisplay, profile);
		}

		public ISoundEngine CreateSound(string device)
		{
			IOSAudioSession.Initialize();

			try
			{
				var engine = new OpenAlSoundEngine(device);
				IOSAudioSession.RegisterDevice(engine.DeviceHandle);
				return engine;
			}
			catch (InvalidOperationException e)
			{
				Log.Write("sound", "Failed to initialize OpenAL device. Error was");
				Log.Write("sound", e);
				return new DummySoundEngine();
			}
		}

		public IFont CreateFont(byte[] data)
		{
			return new FreeTypeFont(data);
		}
	}
}
