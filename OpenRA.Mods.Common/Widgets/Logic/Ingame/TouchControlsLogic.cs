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

using System.Linq;
using OpenRA.Graphics;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	/// <summary>
	/// On-screen controls for functions that are otherwise keyboard-only:
	/// control groups, zoom, and opening the chat input.
	/// </summary>
	public class TouchControlsLogic : ChromeLogic
	{
		[FluentReference]
		const string Assign = "button-touch-controls-assign";

		[FluentReference]
		const string Chat = "button-touch-controls-chat";

		bool assignMode;

		[ObjectCreator.UseCtor]
		public TouchControlsLogic(Widget widget, World world, WorldRenderer worldRenderer)
		{
			var bar = widget.Get("CONTROL_GROUPS_BAR");

			var assignToggle = widget.Get<ButtonWidget>("ASSIGN_TOGGLE");
			var assignLabel = FluentProvider.GetMessage(Assign);
			assignToggle.GetText = () => assignLabel;
			assignToggle.IsHighlighted = () => assignMode;
			assignToggle.OnClick = () => assignMode = !assignMode;

			var template = bar.Get<ButtonWidget>("GROUP_TEMPLATE");
			var groups = world.ControlGroups.Groups;
			for (var i = 0; i < groups.Length; i++)
			{
				var group = i;
				var button = template.Clone();
				button.Id = "GROUP_" + groups[i];
				button.Bounds = new WidgetBounds(
					template.Bounds.X + i * (template.Bounds.Width + 4),
					template.Bounds.Y, template.Bounds.Width, template.Bounds.Height);

				var label = groups[i];
				button.GetText = () => label;
				button.Visible = true;
				button.IsDisabled = () => !assignMode && !world.ControlGroups.GetActorsInControlGroup(group).Any();
				button.OnClick = () =>
				{
					if (assignMode)
					{
						world.ControlGroups.CreateControlGroup(group);
						assignMode = false;
					}
					else
						world.ControlGroups.SelectControlGroup(group);
				};

				button.OnDoubleClick = () =>
				{
					var actors = world.ControlGroups.GetActorsInControlGroup(group).ToList();
					if (actors.Count == 0)
						return;

					world.ControlGroups.SelectControlGroup(group);
					worldRenderer.Viewport.Center(actors);
				};

				bar.AddChild(button);
			}

			var zoomIn = widget.Get<ButtonWidget>("ZOOM_IN_BUTTON");
			zoomIn.GetText = () => "+";
			zoomIn.OnClick = () => worldRenderer.Viewport.AdjustZoom(0.25f);

			var zoomOut = widget.Get<ButtonWidget>("ZOOM_OUT_BUTTON");
			zoomOut.GetText = () => "-";
			zoomOut.OnClick = () => worldRenderer.Viewport.AdjustZoom(-0.25f);

			var chatButton = widget.Get<ButtonWidget>("CHAT_BUTTON");
			var chatLabel = FluentProvider.GetMessage(Chat);
			chatButton.GetText = () => chatLabel;
			chatButton.OnClick = () => Ui.Send(new OpenChatNotification());
		}
	}
}
