using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

/*
 * Adapted from http://blogs.msdn.com/b/adam_nathan/archive/2006/05/04/589686.aspx
 */

public static class Glass
{
	[StructLayout(LayoutKind.Sequential)]
	private struct MARGINS
	{
		public int cxLeftWidth;
		public int cxRightWidth;
		public int cyTopHeight;
		public int cyBottomHeight;
	}

	[DllImport("dwmapi")]
	private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);
	[DllImport("dwmapi")]
	private static extern int DwmIsCompositionEnabled(out bool enabled);
	[DllImport("user32")]
	private static extern int GetWindowLong(IntPtr hwnd, int index);
	[DllImport("user32")]
	private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
	[DllImport("user32")]
	private static extern bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int width, int height, uint flags);
	[DllImport("user32")]
	private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

	public static void RemoveIcon(Window window)
	{
		IntPtr hwnd = new WindowInteropHelper(window).Handle;

		SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x0001);
		SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0020);

		SendMessage(hwnd, 0x0080, new IntPtr(1), IntPtr.Zero);
		SendMessage(hwnd, 0x0080, IntPtr.Zero, IntPtr.Zero);
	}
	public static bool ExtendFrame(Window window)
	{
		return ExtendFrame(window, new Thickness(-1, -1, -1, -1));
	}
	public static bool ExtendFrame(Window window, Thickness margin)
	{
		if (!IsEnabled)
		{
			return false;
		}

		IntPtr hwnd = new WindowInteropHelper(window).Handle;

		if (hwnd == IntPtr.Zero)
		{
			throw new InvalidOperationException("The Window must be shown before extending glass.");
		}

		window.Background = Brushes.Transparent;
		HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;

		MARGINS margins = new MARGINS { cxLeftWidth = (int)margin.Left, cxRightWidth = (int)margin.Right, cyTopHeight = (int)margin.Top, cyBottomHeight = (int)margin.Bottom };
		DwmExtendFrameIntoClientArea(hwnd, ref margins);

		return true;
	}

	public static bool IsEnabled
	{
		get
		{
			bool enabled = false;
			DwmIsCompositionEnabled(out enabled);

			return enabled;
		}
	}
}