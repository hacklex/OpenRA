#region Copyright & License Information
/*
 * Copyright 2007-2015 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation. For more information,
 * see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Graphics
{
	public sealed class TerrainSpriteLayer : IDisposable
	{
		public readonly Sheet Sheet;
		public readonly BlendMode BlendMode;

		readonly Sprite emptySprite;

		readonly IVertexBuffer<Vertex> vertexBuffer;
		readonly Vertex[] vertices;
		readonly HashSet<int> dirtyRows = new HashSet<int>();
		readonly int rowStride;
		readonly bool restrictToBounds;

		readonly WorldRenderer worldRenderer;
		readonly Map map;

		readonly PaletteReference palette;

		public TerrainSpriteLayer(World world, WorldRenderer wr, Sheet sheet, BlendMode blendMode, PaletteReference palette, bool restrictToBounds)
		{
			worldRenderer = wr;
			this.restrictToBounds = restrictToBounds;
			Sheet = sheet;
			BlendMode = blendMode;
			this.palette = palette;

			map = world.Map;
			rowStride = 4 * map.MapSize.X;

			vertices = new Vertex[rowStride * map.MapSize.Y];
			vertexBuffer = Game.Renderer.Device.CreateVertexBuffer(vertices.Length);
			emptySprite = new Sprite(sheet, Rectangle.Empty, TextureChannel.Alpha);

			wr.PaletteInvalidated += UpdatePaletteIndices;
		}

		void UpdatePaletteIndices()
		{
			// Everything in the layer uses the same palette,
			// so we can fix the indices in one pass
			for (var i = 0; i < vertices.Length; i++)
			{
				var v = vertices[i];
				vertices[i] = new Vertex(v.X, v.Y, v.Z, v.U, v.V, palette.TextureIndex, v.C);
			}

			for (var row = 0; row < map.MapSize.Y; row++)
				dirtyRows.Add(row);
		}

		public void Update(CPos cell, Sprite sprite)
		{
			var pos = sprite == null ? float2.Zero :
				worldRenderer.ScreenPosition(map.CenterOfCell(cell)) + sprite.Offset - 0.5f * sprite.Size;
			Update(cell.ToMPos(map.Grid.Type), sprite, pos);
		}

		public void Update(MPos uv, Sprite sprite, float2 pos)
		{
			if (sprite != null)
			{
				if (sprite.Sheet != Sheet)
					throw new InvalidDataException("Attempted to add sprite from a different sheet");

				if (sprite.BlendMode != BlendMode)
					throw new InvalidDataException("Attempted to add sprite with a different blend mode");
			}
			else
				sprite = emptySprite;

			var offset = rowStride * uv.V + 4 * uv.U;
			Util.FastCreateQuad(vertices, pos, sprite, palette.TextureIndex, offset, sprite.Size);

			dirtyRows.Add(uv.V);
		}

		public void Draw(Viewport viewport)
		{
			var cells = restrictToBounds ? viewport.VisibleCellsInsideBounds : viewport.AllVisibleCells;

			// Only draw the rows that are visible.
			var firstRow = cells.CandidateMapCoords.TopLeft.V.Clamp(0, map.MapSize.Y);
			var lastRow = (cells.CandidateMapCoords.BottomRight.V + 1).Clamp(firstRow, map.MapSize.Y);

			Game.Renderer.Flush();

			// Flush any visible changes to the GPU
			for (var row = firstRow; row <= lastRow; row++)
			{
				if (!dirtyRows.Remove(row))
					continue;

				var rowOffset = rowStride * row;

				unsafe
				{
					// The compiler / language spec won't let us calculate a pointer to
					// an offset inside a generic array T[], and so we are forced to
					// calculate the start-of-row pointer here to pass in to SetData.
					fixed (Vertex* vPtr = &vertices[0])
						vertexBuffer.SetData((IntPtr)(vPtr + rowOffset), rowOffset, rowStride);
				}
			}

			Game.Renderer.WorldSpriteRenderer.DrawVertexBuffer(
				vertexBuffer, rowStride * firstRow, rowStride * (lastRow - firstRow),
				PrimitiveType.QuadList, Sheet, BlendMode);

			Game.Renderer.Flush();
		}

		public void Dispose()
		{
			worldRenderer.PaletteInvalidated -= UpdatePaletteIndices;
			vertexBuffer.Dispose();
		}
	}
}
