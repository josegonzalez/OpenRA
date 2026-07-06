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
using Foundation;

namespace OpenRA.iOS
{
	static class IOSAppEnvironment
	{
		/// <summary>
		/// Downloaded game assets are large and redownloadable, so they must not
		/// be backed up to iCloud. Idempotent, applied on every launch.
		/// </summary>
		public static void ExcludeContentFromBackup()
		{
			try
			{
				var content = Path.Combine(Platform.SupportDir, "Content");
				Directory.CreateDirectory(content);

				var url = NSUrl.FromFilename(content);
				url.SetResource(NSUrl.IsExcludedFromBackupKey, NSNumber.FromBoolean(true), out var error);
				if (error != null)
					Console.WriteLine($"Failed to exclude {content} from backup: {error.LocalizedDescription}");
			}
			catch (Exception e)
			{
				Console.WriteLine($"Failed to exclude the content directory from backup: {e.Message}");
			}
		}
	}
}
