using System.IO;
using System.Reflection;
using System.Windows;

namespace ReShade.Setup
{
	public partial class App
	{
		void OnStartup(object sender, StartupEventArgs e)
		{
			Directory.SetCurrentDirectory(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location));
		}
	}
}