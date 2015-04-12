using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security;

/*
 * Adapted from http://stackoverflow.com/a/4696857/2055880, Inspirations:
 */

public unsafe class PEInfo
{
	public enum BinaryType : ushort
	{
		IMAGE_FILE_MACHINE_UNKNOWN = 0x0,
		IMAGE_FILE_MACHINE_I386 = 0x14c,
		IMAGE_FILE_MACHINE_AMD64 = 0x8664,
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct LOADED_IMAGE
	{
		public IntPtr ModuleName;
		public IntPtr hFile;
		public IntPtr MappedAddress;
		public IntPtr FileHeader;
		public IntPtr LastRvaSection;
		public UInt32 NumberOfSections;
		public IntPtr Sections;
		public UInt32 Characteristics;
		public ushort fSystemImage;
		public ushort fDOSImage;
		public ushort fReadOnly;
		public ushort Version;
		public IntPtr Flink;
		public IntPtr BLink;
		public UInt32 SizeOfImage;
	}
	[StructLayout(LayoutKind.Explicit)]
	private struct IMAGE_NT_HEADERS
	{
		[FieldOffset(0)]
		public UInt32 Signature;
		[FieldOffset(4)]
		public IMAGE_FILE_HEADER FileHeader;
	}
	[StructLayout(LayoutKind.Sequential)]
	private struct IMAGE_FILE_HEADER
	{
		public BinaryType Machine;
		public UInt16 NumberOfSections;
		public UInt32 TimeDateStamp;
		public UInt32 PointerToSymbolTable;
		public UInt32 NumberOfSymbols;
		public UInt16 SizeOfOptionalHeader;
		public UInt16 Characteristics;
	}
	[StructLayout(LayoutKind.Explicit)]
	private struct IMAGE_IMPORT_DESCRIPTOR
	{
		#region union
		[FieldOffset(0)]
		public UInt32 Characteristics;
		[FieldOffset(0)]
		public UInt32 OriginalFirstThunk;
		#endregion

		[FieldOffset(4)]
		public UInt32 TimeDateStamp;
		[FieldOffset(8)]
		public UInt32 ForwarderChain;
		[FieldOffset(12)]
		public UInt32 Name;
		[FieldOffset(16)]
		public UInt32 FirstThunk;
	}

	[DllImport("dbghelp"), SuppressUnmanagedCodeSecurity]
	private static extern void* ImageDirectoryEntryToData(void* pBase, bool mappedAsImage, ushort directoryEntry, out uint size);
	[DllImport("dbghelp"), SuppressUnmanagedCodeSecurity]
	private static extern IntPtr ImageRvaToVa(IntPtr pNtHeaders, IntPtr pBase, uint rva, IntPtr pLastRvaSection);
	[DllImport("imagehlp"), SuppressUnmanagedCodeSecurity]
	private static extern bool MapAndLoad(string imageName, string dllPath, out LOADED_IMAGE loadedImage, bool dotDll, bool readOnly);

	private readonly BinaryType mBinaryType = BinaryType.IMAGE_FILE_MACHINE_UNKNOWN;
	private readonly List<string> mModules = new List<string>();

	public PEInfo(string path)
	{
		LOADED_IMAGE image;

		if (MapAndLoad(path, null, out image, true, true) && image.MappedAddress != IntPtr.Zero)
		{
			uint size;
			var imports = (IMAGE_IMPORT_DESCRIPTOR*)ImageDirectoryEntryToData((void*)image.MappedAddress, false, 1, out size);

			if (imports != null)
			{
				while (imports->OriginalFirstThunk != 0)
				{
					this.mModules.Add(Marshal.PtrToStringAnsi(ImageRvaToVa(image.FileHeader, image.MappedAddress, imports->Name, IntPtr.Zero)));

					++imports;
				}
			}

			this.mBinaryType = ((IMAGE_NT_HEADERS*)image.FileHeader)->FileHeader.Machine;
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