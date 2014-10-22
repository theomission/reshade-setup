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