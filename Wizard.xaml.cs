using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Documents;
using System.ComponentModel;
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

		private bool mFinished = false;
		private string mGamePath = null;
		private ManualResetEventSlim mApiCondition = new ManualResetEventSlim();

		private void OnLoaded(object sender, RoutedEventArgs e)
		{
			Glass.ExtendFrame(this);

			bool isRedistributableInstalled = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "msvcp110.dll"));

			if (Environment.Is64BitOperatingSystem)
			{
				isRedistributableInstalled = isRedistributableInstalled && File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "msvcp110.dll"));
			}

			if (!isRedistributableInstalled)
			{
				MessageBox.Show(this, "Unable to find the Microsoft Visual C++ 2012 Redistributable on your system. Please install " + (Environment.Is64BitOperatingSystem ? "both the x86 and x64" : "the x86") + " version first!", "Missing Visual C++ Redistributable", MessageBoxButton.OK, MessageBoxImage.Error);

				Close();
				return;
			}
			
			string[] args = Environment.GetCommandLineArgs();

			if (args.Length == 2 && File.Exists(args[1]))
			{
				Thread worker = new Thread(delegate() { Install(args[1]); });
				worker.Start();
			}
		}
		private void OnClosing(object sender, CancelEventArgs e)
		{
			this.mFinished = true;
			this.mApiCondition.Set();
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
			dlg.Filter = "Applications|*.exe";
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
			string nameModule = null;
			PEInfo exeInfo = new PEInfo(path);
			bool is64Bit = exeInfo.Type == PEInfo.BinaryType.SCS_64BIT_BINARY;

			var res = exeInfo.Imports.FirstOrDefault(s =>
				s.Item1.StartsWith("d3d8", StringComparison.OrdinalIgnoreCase) ||
				s.Item1.StartsWith("d3d9", StringComparison.OrdinalIgnoreCase) ||
				s.Item1.StartsWith("dxgi", StringComparison.OrdinalIgnoreCase) ||
				s.Item1.StartsWith("opengl32", StringComparison.OrdinalIgnoreCase));

			if (res != null)
			{
				nameModule = res.Item1;
			}
			else
			{
				this.Dispatcher.Invoke(delegate()
				{
					this.Title = "Warning!";
					this.Message.Content = "Auto-detection failed. Please select:";
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

			if (nameModule == null || this.mFinished)
			{
				return;
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

				string pathModuleFx = Path.ChangeExtension(pathModule, "fx");

				if (File.Exists(pathModuleFx))
				{
					File.Delete(pathModuleFx);
				}
			}

			#region Copy Files
			List<Tuple<string, string>> files = new List<Tuple<string, string>>();
			files.Add(new Tuple<string, string>(pathDll, pathModule));

			if (File.Exists("ReShade.fx"))
			{
				files.Add(new Tuple<string, string>("ReShade.fx", Path.Combine(Path.GetDirectoryName(pathModule), "ReShade.fx")));
			}

			if (Directory.Exists("ReShade"))
			{
				foreach (string file in Directory.EnumerateFiles("ReShade", "*", SearchOption.AllDirectories).Select(f => f))
				{
					files.Add(new Tuple<string, string>(file, Path.Combine(Path.GetDirectoryName(pathModule), file)));
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
