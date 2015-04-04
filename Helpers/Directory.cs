using System;
using System.IO;

/*
 * Adapted from http://msdn.microsoft.com/en-us/library/bb762914.aspx
 */

namespace System.IO
{
	public static partial class DirectoryHelper
	{
		public static void Copy(string sourceDirName, string destDirName, bool recursive, bool overwrite)
		{
			DirectoryInfo dir = new DirectoryInfo(sourceDirName);
			DirectoryInfo[] dirs = dir.GetDirectories();

			if (!dir.Exists)
			{
				throw new DirectoryNotFoundException("Source directory does not exist or could not be found: " + sourceDirName);
			}

			if (!Directory.Exists(destDirName))
			{
				Directory.CreateDirectory(destDirName);
			}

			FileInfo[] files = dir.GetFiles();

			foreach (FileInfo file in files)
			{
				file.CopyTo(Path.Combine(destDirName, file.Name), overwrite);
			}

			if (recursive)
			{
				foreach (DirectoryInfo subdir in dirs)
				{
					Copy(subdir.FullName, Path.Combine(destDirName, subdir.Name), recursive, overwrite);
				}
			}
		}
	}
}
