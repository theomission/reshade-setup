using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

/*
 * Adapted from sample at http://www.pinvoke.net/default.aspx/kernel32.CreateToolhelp32Snapshot
 */

public static class ProcessEx
{
	[Flags]
	private enum SnapshotFlags : uint
	{
		HeapList = 0x00000001,
		Process = 0x00000002,
		Thread = 0x00000004,
		Module = 0x00000008,
		Module32 = 0x00000010,
		Inherit = 0x80000000,
		All = 0x0000001F,
		NoHeaps = 0x40000000
	}
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
	private struct PROCESSENTRY32
	{
		const int MAX_PATH = 260;

		internal UInt32 dwSize;
		internal UInt32 cntUsage;
		internal UInt32 th32ProcessID;
		internal IntPtr th32DefaultHeapID;
		internal UInt32 th32ModuleID;
		internal UInt32 cntThreads;
		internal UInt32 th32ParentProcessID;
		internal Int32 pcPriClassBase;
		internal UInt32 dwFlags;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)]
		internal string szExeFile;
	}
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
	private struct MODULEENTRY32
	{
		const int MAX_PATH = 260;
		const int MAX_MODULE_NAME32 = 255;

		internal uint dwSize;
		internal uint th32ModuleID;
		internal uint th32ProcessID;
		internal uint GlblcntUsage;
		internal uint ProccntUsage;
		internal IntPtr modBaseAddr;
		internal uint modBaseSize;
		internal IntPtr hModule;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_MODULE_NAME32 + 1)]
		internal string szModule;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)]
		internal string szExePath;
	}

	private static readonly IntPtr INVALID_HANDLE_VALUE = (IntPtr)(-1);

	[DllImport("kernel32", SetLastError = true)]
	private static extern IntPtr CreateToolhelp32Snapshot(UInt32 dwFlags, UInt32 th32ProcessID);
	[DllImport("kernel32", SetLastError = true)]
	private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);
	[DllImport("kernel32", SetLastError = true)]
	private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);
	[DllImport("kernel32", SetLastError = true)]
	private static extern bool Module32First(IntPtr hSnapshot, ref MODULEENTRY32 lpme);
	[DllImport("kernel32", SetLastError = true)]
	private static extern bool Module32Next(IntPtr hSnapshot, ref MODULEENTRY32 lpme);
	[DllImport("kernel32", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CloseHandle(IntPtr hObject);

	public static Process GetParentProcess(int pid)
	{
		Process parentProc = null;
		IntPtr handleToSnapshot = IntPtr.Zero;

		try
		{
			PROCESSENTRY32 procEntry = new PROCESSENTRY32();
			procEntry.dwSize = (UInt32)Marshal.SizeOf(typeof(PROCESSENTRY32));
			handleToSnapshot = CreateToolhelp32Snapshot((uint)SnapshotFlags.Process, 0);
				
			if (Process32First(handleToSnapshot, ref procEntry))
			{
				do
				{
					if (pid == procEntry.th32ProcessID)
					{
						parentProc = Process.GetProcessById((int)procEntry.th32ParentProcessID);
						break;
					}
				}
				while (Process32Next(handleToSnapshot, ref procEntry));
			}
			else
			{
				throw new ApplicationException(string.Format("Failed with win32 error code {0}", Marshal.GetLastWin32Error()));
			}
		}
		catch (Exception ex)
		{
			throw new ApplicationException("Can't get the process.", ex);
		}
		finally
		{
			CloseHandle(handleToSnapshot);
		}

		return parentProc;
	}
	public static string[] GetProcessModules(int pid)
	{
		List<string> modules = new List<string>();
		IntPtr snapshot = IntPtr.Zero;

		try
		{
			snapshot = CreateToolhelp32Snapshot((uint)(SnapshotFlags.Module | SnapshotFlags.Module32), (uint)pid);

			if (snapshot == INVALID_HANDLE_VALUE)
			{
				throw new ApplicationException(string.Format("CreateToolhelp32Snapshot failed with error {0}", Marshal.GetLastWin32Error()));
			}

			MODULEENTRY32 entry = new MODULEENTRY32();
			entry.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));

			if (Module32First(snapshot, ref entry))
			{
				do
				{
					modules.Add(entry.szExePath);
				}
				while (Module32Next(snapshot, ref entry));
			}
			else
			{
				throw new ApplicationException(string.Format("Module32First failed with error {0}", Marshal.GetLastWin32Error()));
			}
		}
		catch (Exception)
		{
			throw;
		}
		finally
		{
			CloseHandle(snapshot);
		}

		return modules.ToArray();
	}

	public static Process CurrentParentProcess
	{
		get
		{
			return GetParentProcess(Process.GetCurrentProcess().Id);
		}
	}
}