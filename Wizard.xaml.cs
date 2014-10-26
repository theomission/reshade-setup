using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Documents;
using Microsoft.Win32;

namespace ReShade.Setup
{
	public partial class Wizard : Window
	{
		public Wizard()
		{
			InitializeComponent();

			Directory.SetCurrentDirectory(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location));
		}

		private string mGamePath = null;
		private bool mFinished = false;
		private ManualResetEventSlim mApiCondition = new ManualResetEventSlim();

		private void OnLoaded(object sender, RoutedEventArgs e)
		{
			Glass.ExtendFrame(this);
			
			string[] args = Environment.GetCommandLineArgs();

			if (args.Length == 2 && File.Exists(args[1]))
			{
				Thread worker = new Thread(delegate() { Install(args[1]); });
				worker.Start();
			}
		}
		private void OnButton(object sender, RoutedEventArgs e)
		{
			if (this.mFinished && !String.IsNullOrEmpty(this.mGamePath))
			{
				ProcessStartInfo info = new ProcessStartInfo(this.mGamePath);
				info.WorkingDirectory = Path.GetDirectoryName(this.mGamePath);

				Process.Start(info);

				Close();
				return;
			}

			OpenFileDialog dlg = new OpenFileDialog();
			dlg.Filter = "Games|*.exe";
			dlg.DefaultExt = ".exe";
			dlg.Multiselect = false;
			dlg.ValidateNames = true;
			dlg.CheckFileExists = true;

			bool? result = dlg.ShowDialog(this);

			if (result.HasValue && result.Value)
			{
				Thread worker = new Thread(delegate() { Install(dlg.FileName); });
				worker.Start();
			}
		}
		private void OnApiChecked(object sender, RoutedEventArgs e)
		{
			this.mApiCondition.Set();
		}

		public bool AnalyzeExe(string path, out bool is64Bit, out string module)
		{
			PEInfo exeInfo = new PEInfo(path);
			is64Bit = exeInfo.Type == PEInfo.BinaryType.SCS_64BIT_BINARY;

			var res = exeInfo.Imports.FirstOrDefault(s =>
				s.Item1.StartsWith("d3d8", StringComparison.OrdinalIgnoreCase) ||
				s.Item1.StartsWith("d3d9", StringComparison.OrdinalIgnoreCase) ||
				s.Item1.StartsWith("dxgi", StringComparison.OrdinalIgnoreCase) ||
				s.Item1.StartsWith("opengl32", StringComparison.OrdinalIgnoreCase));

			if (res != null)
			{
				module = res.Item1;
			}
			else
			{
				module = null;
			}

			return module != null;
		}
		public bool AnalyzeRuntime(string path, out string module)
		{
			module = null;

			ProcessStartInfo info = new ProcessStartInfo(path);
			info.WindowStyle = ProcessWindowStyle.Hidden;
			info.CreateNoWindow = true;

			using (Process process = Process.Start(info))
			{
				while (module == null && (DateTime.Now - process.StartTime).Seconds < 5)
				{
					Thread.Sleep(500);

					string[] modules;

					try
					{
						modules = ProcessEx.GetProcessModules(process.Id);
					}
					catch (ApplicationException)
					{
						continue;
					}

					foreach (string file in modules)
					{
						string name = Path.GetFileName(file);

						if (name.StartsWith("d3d8", StringComparison.OrdinalIgnoreCase) ||
							name.StartsWith("d3d9", StringComparison.OrdinalIgnoreCase) ||
							name.StartsWith("dxgi", StringComparison.OrdinalIgnoreCase) ||
							name.StartsWith("opengl32", StringComparison.OrdinalIgnoreCase))
						{
							module = name;
							break;
						}
					}
				}

				if (!process.HasExited)
				{
					process.Kill();
				}
			}

			return module != null;
		}
		public void Install(string path)
		{
			FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
			string name = !String.IsNullOrEmpty(info.ProductName) ? info.ProductName : Path.GetFileNameWithoutExtension(path);
	
			this.Dispatcher.Invoke(delegate()
			{
				this.mGamePath = path;
				this.Title = "Installing to " + name + " ...";
				this.Button.IsEnabled = false;
				this.Message.Content = "Analyzing " + name + " ...";
				this.Progress.Visibility = Visibility.Visible;
				this.Progress.IsIndeterminate = true;
			});

			#region Analyze Game
			bool is64Bit;
			string nameModule;

			if (!AnalyzeExe(path, out is64Bit, out nameModule))
			{
				if (!AnalyzeRuntime(path, out nameModule))
				{
					this.Dispatcher.Invoke(delegate()
					{
						this.mFinished = true;
						this.Title = "Failed!";
						this.Message.Content = "Auto-detection failed.";
						this.Progress.Visibility = Visibility.Collapsed;
						this.ApiGroup.IsEnabled = true;
					});

					this.mApiCondition.Wait();

					this.Dispatcher.Invoke(delegate()
					{
						if (this.ApiDirect3D8.IsChecked.Value)
						{
							nameModule = "d3d8.dll";
						}
						else if (this.ApiDirect3D9.IsChecked.Value)
						{
							nameModule = "d3d9.dll";
						}
						else if (this.ApiDirectXGI.IsChecked.Value)
						{
							nameModule = "dxgi.dll";
						}
						else if (this.ApiOpenGL.IsChecked.Value)
						{
							nameModule = "opengl32.dll";
						}

						this.ApiGroup.IsEnabled = false;
					});
				}
			}

			this.Dispatcher.Invoke(delegate()
			{
				if (nameModule.StartsWith("d3d8", StringComparison.InvariantCultureIgnoreCase))
				{
					this.ApiDirect3D8.IsChecked = true;
				}
				if (nameModule.StartsWith("d3d9", StringComparison.InvariantCultureIgnoreCase))
				{
					this.ApiDirect3D9.IsChecked = true;
				}
				if (nameModule.StartsWith("dxgi", StringComparison.InvariantCultureIgnoreCase))
				{
					this.ApiDirectXGI.IsChecked = true;
				}
				if (nameModule.StartsWith("opengl32", StringComparison.InvariantCultureIgnoreCase))
				{
					this.ApiOpenGL.IsChecked = true;
				}
			});
			#endregion

			string pathDll = is64Bit ? "ReShade64.dll" : "ReShade32.dll";
			string pathModule = Path.Combine(Path.GetDirectoryName(path), nameModule);

			if (File.Exists(pathModule))
			{
				if (MessageBox.Show("Do you want to overwrite the existing installation?", "Existing installation found.", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
				{
					this.Dispatcher.Invoke(delegate()
					{
						this.mFinished = true;
						this.Title = "Failed!";
						this.Message.Content = "Existing installation found.";
						this.Progress.Visibility = Visibility.Collapsed;
					});

					return;
				}
			}

			#region Copy Files
			List<Tuple<string, string>> files = new List<Tuple<string, string>>();
			files.Add(new Tuple<string, string>(pathDll, pathModule));

			string[] effects = Directory.GetFiles(".", "*.fx");

			if (effects.Length > 1)
			{
				this.Dispatcher.Invoke(delegate()
				{
					this.mFinished = true;
					this.Title = "Failed!";
					this.Message.Content = "Cannot decide which effect file to use.";
					this.Progress.Visibility = Visibility.Collapsed;
				});

				return;
			}
			else if (effects.Length == 1)
			{
				files.Add(new Tuple<string, string>(effects[0], Path.Combine(Path.GetDirectoryName(pathModule), Path.GetFileNameWithoutExtension(nameModule) + ".fx")));
			}

			if (Directory.Exists("SweetFX"))
			{
				foreach (string file in Directory.EnumerateFiles("SweetFX", "*", SearchOption.AllDirectories).Select(f => f))
				{
					files.Add(new Tuple<string, string>(file, Path.GetDirectoryName(pathModule) + Path.DirectorySeparatorChar + file));
				}
			}

			for (int i = 0; i < files.Count; ++i)
			{
				string sourcePath = files[i].Item1;
				string destinationPath = files[i].Item2;

				this.Dispatcher.Invoke(delegate()
				{
					this.Message.Content = "Installing \"" + sourcePath + "\" ...";
					this.Progress.IsIndeterminate = false;
					this.Progress.Progress = i * 100.0 / files.Count;
					this.Progress.Visibility = Visibility.Visible;
				});

				try
				{
					Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
					
					if (Directory.Exists(sourcePath))
					{
						DirectoryCopy(sourcePath, destinationPath, true, true);
					}
					else
					{
						File.Copy(sourcePath, destinationPath, true);
					}
				}
				catch (IOException)
				{
					this.Dispatcher.Invoke(delegate()
					{
						this.mFinished = true;
						this.Title = "Failed!";
						this.Message.Content = "Unable to copy file \"" + sourcePath + "\".";
						this.Progress.Visibility = Visibility.Collapsed;
					});

					return;
				}
			}
			#endregion

			this.Dispatcher.Invoke(delegate()
			{
				this.mFinished = true;
				this.Title = "Success!";
				this.Button.IsEnabled = true;
				this.Message.Content = "Run " + name;
				this.Progress.Visibility = Visibility.Collapsed;
			});
		}

		private static void DirectoryCopy(string sourceDirName, string destDirName, bool recursive, bool overwrite)
		{
			// Adapted from http://msdn.microsoft.com/en-us/library/bb762914.aspx
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
					DirectoryCopy(subdir.FullName, Path.Combine(destDirName, subdir.Name), recursive, overwrite);
				}
			}
		}
	}
}
