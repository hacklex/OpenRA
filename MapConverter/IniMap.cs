#region Copyright & License Information
/*
 * Copyright 2007,2009,2010 Chris Forbes, Robert Pepperell, Matthew Bowra-Dean, Paul Chote, Alli Witheford.
 * This file is part of OpenRA.
 * 
 *  OpenRA is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 * 
 *  OpenRA is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 * 
 *  You should have received a copy of the GNU General Public License
 *  along with OpenRA.  If not, see <http://www.gnu.org/licenses/>.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenRA;
using OpenRA.FileFormats;

namespace MapConverter
{
	public class MapData
	{
		// General info
		public int MapFormat;
		public string Title;
		public string Description;
		public string Author;
		public int Players;
		
		// 'Simple' map data
		public string Tileset;
		public int2 Size;
		public int[] Bounds;
		
		// 'Complex' map data
		public string TileData;
		public string ResourceData;
		public string[] Waypoints;
		public string[] Actors;
	}
	
	public class IniMap
	{
		public readonly string Title;
		public readonly string Theater;
		public readonly int INIFormat;

		public readonly int MapSize;
		public readonly int XOffset;
		public readonly int YOffset;
		public int2 Offset { get { return new int2( XOffset, YOffset ); } }

		public readonly int Width;
		public readonly int Height;
		public int2 Size { get { return new int2(Width, Height); } }

		public readonly TileReference[ , ] MapTiles;
		public readonly List<ActorReference> Actors = new List<ActorReference>();

		public readonly IEnumerable<int2> SpawnPoints;

		static string Truncate( string s, int maxLength )
		{
			return s.Length <= maxLength ? s : s.Substring(0,maxLength );
		}
		
		public void Save(string filename)
		{
			Dictionary< string, Pair<byte,byte> > resourceMapping = new Dictionary<string, Pair<byte, byte>>() {
				{ "gold01", new Pair<byte,byte>(1,0) },
				{ "gold02", new Pair<byte,byte>(1,1) },
				{ "gold03", new Pair<byte,byte>(1,2) },
				{ "gold04", new Pair<byte,byte>(1,3) },
				
				{ "gem01", new Pair<byte,byte>(2,0) },
				{ "gem02", new Pair<byte,byte>(2,1) },
				{ "gem03", new Pair<byte,byte>(2,2) },
				{ "gem04", new Pair<byte,byte>(2,3) },
			};
			
			// Metadata
			var data = new MapData();
			data.MapFormat = 1;
			data.Title = Title;
			data.Players = SpawnPoints.Count();
			data.Author = "Westwood Studios";
			data.Description = "";
			data.Tileset = Theater;
			data.Bounds = new int[] {XOffset, YOffset, Width, Height};
			data.Size = new int2(MapSize,MapSize);
			
			
			// Tile data is stored as a base-64 encoded stream of
			// {(2-byte) tile index, (1-byte) image index} pairs
			MemoryStream tiles = new MemoryStream();
			BinaryWriter writer = new BinaryWriter( tiles );
			
			for( int i = 0 ; i < MapSize ; i++ )
				for( int j = 0 ; j < MapSize ; j++ )
				{			
					writer.Write( MapTiles[j,i].tile );
					// Semi-hack: Convert clear and water tiles to "pick an image for me" magic number
					byte image = (MapTiles[ j, i ].tile == 0xff || MapTiles[ j, i ].tile == 0xffff) ? byte.MaxValue : MapTiles[j,i].image;
					writer.Write(image);
				}
			writer.Flush();
			data.TileData = Convert.ToBase64String(tiles.ToArray());	
			
			// Resource data is stored as a base-64 encoded stream of
			// {(1-byte) resource index, (1-byte) image index} pairs
			MemoryStream resources = new MemoryStream();
			writer = new BinaryWriter( resources );
			
			for( int i = 0 ; i < MapSize ; i++ )
				for( int j = 0 ; j < MapSize ; j++ )
				{			
					byte type = 0;
					byte image = 0;
					if (MapTiles[j,i].overlay != null)
					{
						var res = resourceMapping[MapTiles[j,i].overlay];
						type = res.First;
						image = res.Second;
					}
					
					writer.Write(type);
					writer.Write(image);
				}
			writer.Flush();
			data.ResourceData = Convert.ToBase64String(resources.ToArray());	
			
			// Spawn points
			int s = 0;
			data.Waypoints = SpawnPoints.Select(t => "spawn{0}={1}|{2}".F(s++,t.X,t.Y)).ToArray();
			
			// Actors
			s = 0;
			data.Actors = Actors.Select(t=>"actor{0}={1}|{2}|{3}".F(s++,t.Name,t.Location.X,t.Location.Y)).ToArray();
			
			Dictionary<string, MiniYaml> Nodes = new Dictionary<string,MiniYaml>();
			Nodes.Add("MAP",FieldSaver.Save(data));
			Nodes.WriteToFile(filename);
			
		}

		public IniMap(string filename)
		{			
			IniFile file = new IniFile(FileSystem.Open(filename));
			
			IniSection basic = file.GetSection("Basic");
			Title = basic.GetValue("Name", "(null)");
			INIFormat = int.Parse(basic.GetValue("NewINIFormat", "0"));

			IniSection map = file.GetSection("Map");
			Theater = Truncate(map.GetValue("Theater", "TEMPERAT"), 8);

			XOffset = int.Parse(map.GetValue("X", "0"));
			YOffset = int.Parse(map.GetValue("Y", "0"));

			Width = int.Parse(map.GetValue("Width", "0"));
			Height = int.Parse(map.GetValue("Height", "0"));
			MapSize = (INIFormat == 3) ? 128 : 64;
			
			MapTiles = new TileReference[ MapSize, MapSize ];
			for (int j = 0; j < MapSize; j++)
				for (int i = 0; i < MapSize; i++)
					MapTiles[i, j] = new TileReference();

			
			if (INIFormat == 3) // RA map
			{
				UnpackRATileData(ReadPackedSection(file.GetSection("MapPack")));
				UnpackRAOverlayData(ReadPackedSection(file.GetSection("OverlayPack")));
				ReadRATrees(file);
			}
			else // CNC
			{
				UnpackCncTileData(FileSystem.Open(filename.Substring(0,filename.Length-4)+".bin"));
				ReadCncOverlay(file);
				ReadCncTrees(file);	
			}
			
			LoadActors(file, "STRUCTURES");
			LoadActors(file, "UNITS");
			LoadActors(file, "INFANTRY");
			
			SpawnPoints = file.GetSection("Waypoints")
					.Where(kv => int.Parse(kv.Value) > 0)
					.Select(kv => Pair.New(int.Parse(kv.Key), new int2(int.Parse(kv.Value) % MapSize, int.Parse(kv.Value) / MapSize)))
					.Where(a => a.First < 8)
					.Select(a => a.Second)
					.ToArray();
		}

		static MemoryStream ReadPackedSection(IniSection mapPackSection)
		{
			StringBuilder sb = new StringBuilder();
			for (int i = 1; ; i++)
			{
				string line = mapPackSection.GetValue(i.ToString(), null);
				if (line == null)
					break;

				sb.Append(line.Trim());
			}

			byte[] data = Convert.FromBase64String(sb.ToString());
			List<byte[]> chunks = new List<byte[]>();
			BinaryReader reader = new BinaryReader(new MemoryStream(data));

			try
			{
				while (true)
				{
					uint length = reader.ReadUInt32() & 0xdfffffff;
					byte[] dest = new byte[8192];
					byte[] src = reader.ReadBytes((int)length);

					/*int actualLength =*/ Format80.DecodeInto(src, dest);

					chunks.Add(dest);
				}
			}
			catch (EndOfStreamException) { }

			MemoryStream ms = new MemoryStream();
			foreach (byte[] chunk in chunks)
				ms.Write(chunk, 0, chunk.Length);

			ms.Position = 0;

			return ms;
		}

		static byte ReadByte( Stream s )
		{
			int ret = s.ReadByte();
			if( ret == -1 )
				throw new NotImplementedException();
			return (byte)ret;
		}

		static ushort ReadWord(Stream s)
		{
			ushort ret = ReadByte(s);
			ret |= (ushort)(ReadByte(s) << 8);

			return ret;
		}

		void UnpackRATileData( MemoryStream ms )
		{
			for( int i = 0 ; i < MapSize ; i++ )
				for( int j = 0 ; j < MapSize ; j++ )
					MapTiles[j, i].tile = ReadWord(ms);

			for( int i = 0 ; i < MapSize ; i++ )
				for( int j = 0 ; j < MapSize ; j++ )
				{
					MapTiles[j, i].image = (byte)ms.ReadByte();
					if( MapTiles[ j, i ].tile == 0xff || MapTiles[ j, i ].tile == 0xffff )
						MapTiles[ j, i ].image = (byte)( i % 4 + ( j % 4 ) * 4 );
				}
		}
		
		static string[] raOverlayNames =
		{
			"sbag", "cycl", "brik", "fenc", "wood",
			"gold01", "gold02", "gold03", "gold04",
			"gem01", "gem02", "gem03", "gem04",
			"v12", "v13", "v14", "v15", "v16", "v17", "v18",
			"fpls", "wcrate", "scrate", "barb", "sbag",
		};
	
		void UnpackRAOverlayData( MemoryStream ms )
		{
			for( int i = 0 ; i < MapSize ; i++ )
				for( int j = 0 ; j < MapSize ; j++ )
				{
					byte o = ReadByte( ms );
					MapTiles[ j, i ].overlay = (o == 255) ? null : raOverlayNames[o];
				}
		}

		void ReadRATrees( IniFile file )
		{
			IniSection terrain = file.GetSection( "TERRAIN", true );
			if( terrain == null )
				return;
			
			foreach( KeyValuePair<string, string> kv in terrain )
			{
				var loc = int.Parse( kv.Key );
				Actors.Add( new ActorReference(kv.Value, new int2(loc % MapSize, loc / MapSize), null ) );
			}
		}
		
		void UnpackCncTileData( Stream ms )
		{		
			for( int i = 0 ; i < MapSize ; i++ )
				for( int j = 0 ; j < MapSize ; j++ )
				{
					MapTiles[j, i].tile = (byte)ms.ReadByte();	
					MapTiles[j, i].image = (byte)ms.ReadByte();
				
					if( MapTiles[ j, i ].tile == 0xff )
						MapTiles[ j, i ].image = (byte)( i % 4 + ( j % 4 ) * 4 );
				}
		}
		
		void ReadCncOverlay( IniFile file )
		{
			IniSection overlay = file.GetSection( "OVERLAY", true );
			if( overlay == null )
				return;

			foreach( KeyValuePair<string, string> kv in overlay )
			{
				var loc = int.Parse( kv.Key );
				int2 cell = new int2(loc % MapSize, loc / MapSize);
				MapTiles[ cell.X, cell.Y ].overlay = kv.Value.ToLower();
			}
		}
		
		
		void ReadCncTrees( IniFile file )
		{
			IniSection terrain = file.GetSection( "TERRAIN", true );
			if( terrain == null )
				return;

			foreach( KeyValuePair<string, string> kv in terrain )
			{
				var loc = int.Parse( kv.Key );
				Actors.Add( new ActorReference( kv.Value.Split(',')[0], new int2(loc % MapSize, loc / MapSize),null));
			}
		}
		
		void LoadActors(IniFile file, string section)
		{
			foreach (var s in file.GetSection(section, true))
			{
				//num=owner,type,health,location,facing,...
				var parts = s.Value.Split( ',' );
				var loc = int.Parse(parts[3]);			
				Actors.Add( new ActorReference( parts[1].ToLowerInvariant(), new int2(loc % MapSize, loc / MapSize), parts[0]));
			}
		}
		
		public bool IsInMap(int2 xy)
		{
			return IsInMap(xy.X,xy.Y);
		}
		
		public bool IsInMap(int x, int y)
		{
			return (x >= XOffset && y >= YOffset && x < XOffset + Width && y < YOffset + Height);
		}
	}
}
