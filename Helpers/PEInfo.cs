using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security;

/*
 * Adapted from http://stackoverflow.com/a/4696857/2055880, Inspirations:
 */

public unsafe class PEInfo
{
	public enum BinaryType : uint
	{
		SCS_32BIT_BINARY = 0, // A 32-bit Windows-based application
		SCS_64BIT_BINARY = 6, // A 64-bit Windows-based application.
		SCS_DOS_BINARY = 1, // An MS-DOS – based application
		SCS_OS216_BINARY = 5, // A 16-bit OS/2-based application
		SCS_PIF_BINARY = 3, // A PIF file that executes an MS-DOS – based application
		SCS_POSIX_BINARY = 4, // A POSIX – based application
		SCS_WOW_BINARY = 2 // A 16-bit Windows-based application
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct LOADED_IMAGE
	{
		public IntPtr moduleName;
		public IntPtr hFile;
		public IntPtr MappedAddress;
		public IntPtr FileHeader;
		public IntPtr lastRvaSection;
		public UInt32 numbOfSections;
		public IntPtr firstRvaSection;
		public UInt32 charachteristics;
		public ushort systemImage;
		public ushort dosImage;
		public ushort readOnly;
		public ushort version;
		public IntPtr links_1;
		public IntPtr links_2;
		public UInt32 sizeOfImage;
	}
	[StructLayout(LayoutKind.Explicit)]
	private struct IMAGE_IMPORT_DESCRIPTOR
	{
		#region union
		[FieldOffset(0)]
		public uint Characteristics;
		[FieldOffset(0)]
		public uint OriginalFirstThunk;
		#endregion

		[FieldOffset(4)]
		public uint TimeDateStamp;
		[FieldOffset(8)]
		public uint ForwarderChain;
		[FieldOffset(12)]
		public uint Name;
		[FieldOffset(16)]
		public uint FirstThunk;
	}

	[DllImport("dbghelp"), SuppressUnmanagedCodeSecurity]
	private static extern void* ImageDirectoryEntryToData(void* pBase, bool mappedAsImage, ushort directoryEntry, out uint size);
	[DllImport("dbghelp"), SuppressUnmanagedCodeSecurity]
	private static extern IntPtr ImageRvaToVa(IntPtr pNtHeaders, IntPtr pBase, uint rva, IntPtr pLastRvaSection);
	[DllImport("imagehlp"), SuppressUnmanagedCodeSecurity]
	private static extern bool MapAndLoad(string imageName, string dllPath, out LOADED_IMAGE loadedImage, bool dotDll, bool readOnly);
	[DllImport("kernel32"), SuppressUnmanagedCodeSecurity]
	private static extern bool GetBinaryType(string lpApplicationName, out BinaryType lpBinaryType);

	private readonly BinaryType mBinaryType;
	private readonly List<string> mModules = new List<string>();

	public PEInfo(string path)
	{
		GetBinaryType(path, out this.mBinaryType);

		LOADED_IMAGE image;

		if (MapAndLoad(path, null, out image, true, true) && image.MappedAddress != IntPtr.Zero)
		{
			uint size;
			var pImportDir = (IMAGE_IMPORT_DESCRIPTOR*)ImageDirectoryEntryToData((void*)image.MappedAddress, false, 1, out size);

			if (pImportDir != null)
			{
				while (pImportDir->OriginalFirstThunk != 0)
				{
					this.mModules.Add(Marshal.PtrToStringAnsi(ImageRvaToVa(image.FileHeader, image.MappedAddress, pImportDir->Name, IntPtr.Zero)));

					++pImportDir;
				}
			}
		}
	}

	public BinaryType Type
	{
		get
		{
			return this.mBinaryType;
		}
	}
	public IEnumerable<string> Modules
	{
		get
		{
			return this.mModules;
		}
	}
}