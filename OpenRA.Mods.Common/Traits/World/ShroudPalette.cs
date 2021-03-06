#region Copyright & License Information
/*
 * Copyright 2007-2015 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation. For more information,
 * see COPYING.
 */
#endregion

using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Adds the hard-coded shroud palette to the game")]
	class ShroudPaletteInfo : ITraitInfo
	{
		[Desc("Internal palette name")]
		[FieldLoader.Require, PaletteDefinition]
		public readonly string Name = "shroud";

		[Desc("Palette type")]
		public readonly bool Fog = false;

		public object Create(ActorInitializer init) { return new ShroudPalette(this); }
	}

	class ShroudPalette : ILoadsPalettes, IProvidesAssetBrowserPalettes
	{
		readonly ShroudPaletteInfo info;

		public ShroudPalette(ShroudPaletteInfo info) { this.info = info; }

		public void LoadPalettes(WorldRenderer wr)
		{
			var c = info.Fog ? Fog : Shroud;
			wr.AddPalette(info.Name, new ImmutablePalette(Enumerable.Range(0, Palette.Size).Select(i => (uint)c[i % 8].ToArgb())));
		}

		static readonly Color[] Fog = new[]
		{
			Color.FromArgb(0, 0, 0, 0),
			Color.Green, Color.Blue, Color.Yellow,
			Color.FromArgb(128, 0, 0, 0),
			Color.FromArgb(96, 0, 0, 0),
			Color.FromArgb(64, 0, 0, 0),
			Color.FromArgb(32, 0, 0, 0)
		};

		static readonly Color[] Shroud = new[]
		{
			Color.FromArgb(0, 0, 0, 0),
			Color.Green, Color.Blue, Color.Yellow,
			Color.Black,
			Color.FromArgb(160, 0, 0, 0),
			Color.FromArgb(128, 0, 0, 0),
			Color.FromArgb(64, 0, 0, 0)
		};

		public IEnumerable<string> PaletteNames { get { yield return info.Name; } }
	}
}
