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
using AVFoundation;

namespace OpenRA.Platforms.IOS
{
	/// <summary>
	/// Configures the AVAudioSession for the OpenAL device and pauses the
	/// mixer while the application is backgrounded or interrupted.
	/// </summary>
	[SuppressMessage("Style", "IDE1006:Naming Styles",
		Justification = "C-style naming is kept for consistency with the underlying native API.")]
	static class IOSAudioSession
	{
		[DllImport("soft_oal", CallingConvention = CallingConvention.Cdecl)]
		static extern void alcDevicePauseSOFT(IntPtr device);

		[DllImport("soft_oal", CallingConvention = CallingConvention.Cdecl)]
		static extern void alcDeviceResumeSOFT(IntPtr device);

		static IntPtr device;
		static bool paused;

		public static void Initialize()
		{
			try
			{
				var session = AVAudioSession.SharedInstance();
				session.SetCategory(AVAudioSessionCategory.Ambient);
				session.SetActive(true);

				AVAudioSession.Notifications.ObserveInterruption((sender, e) =>
				{
					if (e.InterruptionType == AVAudioSessionInterruptionType.Began)
						Pause();
					else if (e.InterruptionType == AVAudioSessionInterruptionType.Ended)
						Resume();
				});
			}
			catch (Exception e)
			{
				Log.Write("sound", "Failed to configure the audio session. Error was");
				Log.Write("sound", e);
			}
		}

		/// <summary>Registers the OpenAL device whose mixer should follow the application lifecycle.</summary>
		public static void RegisterDevice(IntPtr alcDevice)
		{
			device = alcDevice;
		}

		public static void Pause()
		{
			if (device == IntPtr.Zero || paused)
				return;

			paused = true;
			try
			{
				alcDevicePauseSOFT(device);
			}
			catch (Exception e)
			{
				Log.Write("sound", "Failed to pause the OpenAL device. Error was");
				Log.Write("sound", e);
			}
		}

		public static void Resume()
		{
			if (device == IntPtr.Zero || !paused)
				return;

			paused = false;
			try
			{
				AVAudioSession.SharedInstance().SetActive(true);
				alcDeviceResumeSOFT(device);
			}
			catch (Exception e)
			{
				Log.Write("sound", "Failed to resume the OpenAL device. Error was");
				Log.Write("sound", e);
			}
		}
	}
}
