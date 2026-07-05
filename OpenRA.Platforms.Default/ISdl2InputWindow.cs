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

using OpenRA.Primitives;

namespace OpenRA.Platforms.Default
{
	/// <summary>The window state that the SDL input pump reads and updates, shared by all SDL-based windows.</summary>
	interface ISdl2InputWindow
	{
		Size EffectiveWindowSize { get; }
		Size SurfaceSize { get; }
		float EffectiveWindowScale { get; }
		float NativeWindowScale { get; }

		bool HasInputFocus { get; set; }
		bool IsSuspended { get; set; }

		void WindowSizeChanged();
	}
}
