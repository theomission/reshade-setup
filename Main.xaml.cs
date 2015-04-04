using System;
using System.IO;
using System.Reflection;
using System.Windows;

namespace ReShade.Setup
{
	public partial class App : Application
	{
		private void OnStartup(object sender, StartupEventArgs e)
		{
			bool isRedistributableInstalled = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "msvcp110.dll"));

			if (Environment.Is64BitOperatingSystem)
			{
				isRedistributableInstalled = isRedistributableInstalled && File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "msvcp110.dll"));
			}

			if (!isRedistributableInstalled)
			{
				MessageBox.Show("Unable to find the Microsoft Visual C++ 2012 Redistributable on your system. Please install " + (Environment.Is64BitOperatingSystem ? "both the x86 and x64" : "the x86") + " version first!", "Missing Visual C++ Redistributable", MessageBoxButton.OK, MessageBoxImage.Error);

				Shutdown();
				return;
			}

			Directory.SetCurrentDirectory(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location));
		}
	}
}