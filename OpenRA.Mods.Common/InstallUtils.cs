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
using System.IO;
using System.Linq;
using ICSharpCode.SharpZipLib;
using ICSharpCode.SharpZipLib.Zip;
using OpenRA.FileSystem;

namespace OpenRA.Mods.Common
{
	public static class InstallUtils
	{
		static IEnumerable<ZipEntry> GetEntries(this ZipInputStream z)
		{
			for (;;)
			{
				var e = z.GetNextEntry();
				if (e != null) yield return e; else break;
			}
		}

		public static string GetMountedDisk(Func<string, bool> isValidDisk)
		{
			var volumes = DriveInfo.GetDrives()
				.Where(v => v.DriveType == DriveType.CDRom && v.IsReady)
				.Select(v => v.RootDirectory.FullName);

			return volumes.FirstOrDefault(isValidDisk);
		}

		// TODO: The package should be mounted into its own context to avoid name collisions with installed files
		public static bool ExtractFromPackage(string srcPath, string package, string annotation, Dictionary<string, string[]> filesByDirectory,
			string destPath, bool overwrite, Action<string> onProgress, Action<string> onError)
		{
			if (!Directory.Exists(destPath))
				Directory.CreateDirectory(destPath);

			Log.Write("debug", "Mounting {0}".F(srcPath));
			GlobalFileSystem.Mount(srcPath);
			Log.Write("debug", "Mounting {0}".F(package));
			GlobalFileSystem.Mount(package, annotation);

			foreach (var directory in filesByDirectory)
			{
				var targetDir = directory.Key;

				foreach (var file in directory.Value)
				{
					var containingDir = Path.Combine(destPath, targetDir);
					var dest = Path.Combine(containingDir, file.ToLowerInvariant());
					if (File.Exists(dest))
					{
						if (overwrite)
							File.Delete(dest);
						else
						{
							Log.Write("debug", "Skipping {0}".F(dest));
							continue;
						}
					}

					Directory.CreateDirectory(containingDir);

					using (var sourceStream = GlobalFileSystem.Open(file))
					using (var destStream = File.Create(dest))
					{
						Log.Write("debug", "Extracting {0} to {1}".F(file, dest));
						onProgress("Extracting " + file);
						destStream.Write(sourceStream.ReadAllBytes());
					}
				}
			}

			return true;
		}

		public static bool CopyFiles(string srcPath, Dictionary<string, string[]> files, string destPath,
			bool overwrite, Action<string> onProgress, Action<string> onError)
		{
			foreach (var folder in files)
			{
				var targetDir = folder.Key;

				foreach (var file in folder.Value)
				{
					var sourcePath = Path.Combine(srcPath, file);
					if (!File.Exists(sourcePath))
					{
						onError("Cannot find " + file);
						return false;
					}

					var destFile = Path.GetFileName(file);
					var containingDir = Path.Combine(destPath, targetDir);
					var dest = Path.Combine(containingDir, destFile.ToLowerInvariant());
					if (File.Exists(dest) && !overwrite)
					{
						Log.Write("debug", "Skipping {0}".F(dest));
						continue;
					}

					Directory.CreateDirectory(containingDir);

					onProgress("Copying " + destFile);
					Log.Write("debug", "Copy {0} to {1}".F(sourcePath, dest));
					File.Copy(sourcePath, dest, true);
				}
			}

			return true;
		}

		public static bool ExtractZip(string zipFile, string dest, Action<string> onProgress, Action<string> onError)
		{
			if (!File.Exists(zipFile))
			{
				onError("Invalid path: " + zipFile);
				return false;
			}

			var extracted = new List<string>();
			try
			{
				using (var stream = File.OpenRead(zipFile))
				using (var z = new ZipInputStream(stream))
					z.ExtractZip(dest, extracted, s => onProgress("Extracting " + s));
			}
			catch (SharpZipBaseException)
			{
				foreach (var f in extracted)
					File.Delete(f);

				onError("Invalid archive");
				return false;
			}

			return true;
		}

		// TODO: this belongs in FileSystem/ZipFile
		static void ExtractZip(this ZipInputStream z, string destPath, List<string> extracted, Action<string> onProgress)
		{
			foreach (var entry in z.GetEntries())
			{
				if (!entry.IsFile) continue;

				onProgress(entry.Name);

				Directory.CreateDirectory(Path.Combine(destPath, Path.GetDirectoryName(entry.Name)));
				var path = Path.Combine(destPath, entry.Name);
				extracted.Add(path);

				using (var f = File.Create(path))
					z.CopyTo(f);
			}

			z.Close();
		}
	}
}
