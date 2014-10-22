using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security;

/*
 * Adapted from http://stackoverflow.com/a/4696857/2055880, Inspirations:
 * http://www.bearcanyon.com/dotnet/#AssemblyParser (Mike Woodring's "Parsing PE File Headers to Determine if a DLL or EXE is an Assembly")
 * http://stackoverflow.com/questions/1563134/how-do-i-read-the-pe-header-of-a-module-loaded-in-memory ("How do I read the PE header of a module loaded in memory?")
 * http://stackoverflow.com/questions/2975639/resolving-rvas-for-import-and-export-tables-within-a-pe-file ("Resolving RVA's for Import and Export tables within a PE file.")
 * http://www.lenholgate.com/blog/2006/04/i-love-it-when-a-plan-comes-together.html
 * http://www.gamedev.net/community/forums/topic.asp?topic_id=409936
 * http://stackoverflow.com/questions/4571088/how-to-programatically-read-native-dll-imports-in-c
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
		public IntPtr links_1; // these two comprise the LIST_ENTRY
		public IntPtr links_2;
		public UInt32 sizeOfImage;
	}
	[StructLayout(LayoutKind.Explicit)]
	private unsafe struct IMAGE_IMPORT_BY_NAME
	{
		[FieldOffset(0)]
		public ushort Hint;
		[FieldOffset(2)]
		public fixed char Name[1];
	}
	[StructLayout(LayoutKind.Explicit)]
	private struct IMAGE_IMPORT_DESCRIPTOR
	{
		#region union
		[FieldOffset(0)]
		public uint Characteristics; // 0 for terminating null import descriptor
		[FieldOffset(0)]
		public uint OriginalFirstThunk; // RVA to original unbound IAT (PIMAGE_THUNK_DATA)
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
	[StructLayout(LayoutKind.Sequential)]
	private struct IMAGE_EXPORT_DIRECTORY
	{
		public UInt32 Characteristics;
		public UInt32 TimeDateStamp;
		public UInt16 MajorVersion;
		public UInt16 MinorVersion;
		public UInt32 Name;
		public UInt32 Base;
		public UInt32 NumberOfFunctions;
		public UInt32 NumberOfNames;
		public IntPtr AddressOfFunctions; // RVA from base of image
		public IntPtr AddressOfNames; // RVA from base of image
		public IntPtr AddressOfNameOrdinals; // RVA from base of image
	}
	[StructLayout(LayoutKind.Explicit)]
	private struct THUNK_DATA
	{
		[FieldOffset(0)]
		public uint ForwarderString; // PBYTE 
		[FieldOffset(0)]
		public uint Function; // PDWORD
		[FieldOffset(0)]
		public uint Ordinal;
		[FieldOffset(0)]
		public IntPtr AddressOfData; // PIMAGE_IMPORT_BY_NAME
	}

	[DllImport("kernel32"), SuppressUnmanagedCodeSecurity]
	private static extern bool IsBadReadPtr(void* lpBase, uint ucb);
	[DllImport("dbghelp"), SuppressUnmanagedCodeSecurity]
	private static extern void* ImageDirectoryEntryToData(void* pBase, bool mappedAsImage, ushort directoryEntry, out uint size);
	[DllImport("dbghelp"), SuppressUnmanagedCodeSecurity]
	private static extern IntPtr ImageRvaToVa(IntPtr pNtHeaders, IntPtr pBase, uint rva, IntPtr pLastRvaSection);
	[DllImport("ImageHlp"), SuppressUnmanagedCodeSecurity]
	private static extern bool MapAndLoad(string imageName, string dllPath, out LOADED_IMAGE loadedImage, bool dotDll, bool readOnly);
	[DllImport("kernel32"), SuppressUnmanagedCodeSecurity]
	private static extern bool GetBinaryType(string lpApplicationName, out BinaryType lpBinaryType);

	private const ushort IMAGE_DIRECTORY_ENTRY_IMPORT = 1;
	private const ushort IMAGE_DIRECTORY_ENTRY_EXPORT = 0;

	private readonly BinaryType mBinaryType;
	private readonly List<string> mExports = new List<string>();
	private readonly List<Tuple<string, List<string>>> mImports = new List<Tuple<string, List<string>>>();

	public PEInfo(string path)
	{
		GetBinaryType(path, out this.mBinaryType);

		LOADED_IMAGE image;

		if (MapAndLoad(path, null, out image, true, true))
		{
			LoadExports(image);
			LoadImports(image);
		}
	}

	private void LoadExports(LOADED_IMAGE loadedImage)
	{
		var hMod = (void*)loadedImage.MappedAddress;

		if (hMod != null)
		{
			uint size;
			var pExportDir = (IMAGE_EXPORT_DIRECTORY*)ImageDirectoryEntryToData((void*)loadedImage.MappedAddress, false, IMAGE_DIRECTORY_ENTRY_EXPORT, out size);

			if (pExportDir != null)
			{
				var pFuncNames = (uint*)ImageRvaToVa(loadedImage.FileHeader, loadedImage.MappedAddress, (uint)pExportDir->AddressOfNames.ToInt32(), IntPtr.Zero);

				for (uint i = 0; i < pExportDir->NumberOfNames; i++)
				{
					uint funcNameRva = pFuncNames[i];

					if (funcNameRva != 0)
					{
						var funcName = (char*)ImageRvaToVa(loadedImage.FileHeader, loadedImage.MappedAddress, funcNameRva, IntPtr.Zero);
						var name = Marshal.PtrToStringAnsi((IntPtr)funcName);
						
						this.mExports.Add(name);
					}
				}
			}
		}
	}
	private void LoadImports(LOADED_IMAGE loadedImage)
	{
		var hMod = (void*)loadedImage.MappedAddress;

		if (hMod != null)
		{
			uint size;
			var pImportDir = (IMAGE_IMPORT_DESCRIPTOR*)ImageDirectoryEntryToData(hMod, false, IMAGE_DIRECTORY_ENTRY_IMPORT, out size);

			if (pImportDir != null)
			{
				while (pImportDir->OriginalFirstThunk != 0)
				{
					try
					{
						var szName = (char*)ImageRvaToVa(loadedImage.FileHeader, loadedImage.MappedAddress, pImportDir->Name, IntPtr.Zero);
						string name = Marshal.PtrToStringAnsi((IntPtr) szName);

						var pr = new Tuple<string, List<string>>(name, new List<string>());

						this.mImports.Add(pr);

						var pThunkOrg = (THUNK_DATA*)ImageRvaToVa(loadedImage.FileHeader, loadedImage.MappedAddress, pImportDir->OriginalFirstThunk, IntPtr.Zero);

						while (pThunkOrg->AddressOfData != IntPtr.Zero)
						{
							uint ord;

							if ((pThunkOrg->Ordinal & 0x80000000) > 0)
							{
								ord = pThunkOrg->Ordinal & 0xffff;
							}
							else
							{
								var pImageByName = (IMAGE_IMPORT_BY_NAME*)ImageRvaToVa(loadedImage.FileHeader, loadedImage.MappedAddress, (uint)pThunkOrg->AddressOfData.ToInt32(), IntPtr.Zero);

								if (!IsBadReadPtr(pImageByName, (uint)sizeof(IMAGE_IMPORT_BY_NAME)))
								{
									ord = pImageByName->Hint;
									var szImportName = pImageByName->Name;
									string sImportName = Marshal.PtrToStringAnsi((IntPtr)szImportName);
									
									pr.Item2.Add( sImportName );
								}
								else
								{
									break;
								}
							}

							pThunkOrg++;
						}
					}
					catch (AccessViolationException)
					{
					}

					pImportDir++;
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
	public IEnumerable<string> Exports
	{
		get
		{
			return this.mExports;
		}
	}
	public IEnumerable<Tuple<string, List<string>>> Imports
	{
		get
		{
			return this.mImports;
		}
	}
}