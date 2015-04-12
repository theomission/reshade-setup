using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Documents;

namespace ReShade.Setup
{
	public partial class Wizard : Window
	{
		public Wizard()
		{
			InitializeComponent();
		}

		private bool mFinished = false;
		private string mTargetPath = null;
		private ManualResetEventSlim mApiCondition = new ManualResetEventSlim();

		private void OnSourceInitialized(object sender, EventArgs e)
		{
			Glass.RemoveIcon(this);
			Glass.ExtendFrame(this);
		}
		private void OnLoaded(object sender, RoutedEventArgs e)
		{
			string[] args = Environment.GetCommandLineArgs();

			if (args.Length == 2 && File.Exists(args[1]))
			{
				new Thread(delegate() { Install(args[1]); }).Start();
			}
		}
		private void OnClosing(object sender, CancelEventArgs e)
		{
			this.mFinished = true;
			this.mApiCondition.Set();
		}
		private void OnButton(object sender, RoutedEventArgs e)
		{
			if (this.mFinished && !String.IsNullOrEmpty(this.mTargetPath))
			{
				ProcessStartInfo info = new ProcessStartInfo(this.mTargetPath);
				info.WorkingDirectory = Path.GetDirectoryName(this.mTargetPath);

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
				new Thread(delegate() { Install(dlg.FileName); }).Start();
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
				this.mTargetPath = path;
				this.Title = "Installing to " + name + " ...";
				this.Button.IsEnabled = false;
				this.Message.Content = "Analyzing " + name + " ...";
				this.Progress.Visibility = Visibility.Visible;
				this.Progress.IsIndeterminate = true;
			});

			#region Analyze Game
			PEInfo exeInfo = new PEInfo(path);
			bool is64Bit = exeInfo.Type == PEInfo.BinaryType.IMAGE_FILE_MACHINE_AMD64;

			string nameModule = exeInfo.Modules.FirstOrDefault(s =>
				s.StartsWith("d3d8", StringComparison.OrdinalIgnoreCase) ||
				s.StartsWith("d3d9", StringComparison.OrdinalIgnoreCase) ||
				s.StartsWith("dxgi", StringComparison.OrdinalIgnoreCase) ||
				s.StartsWith("opengl32", StringComparison.OrdinalIgnoreCase));

			if (nameModule == null)
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
			if (Directory.Exists("SweetFX"))
			{
				foreach (string file in Directory.EnumerateFiles("SweetFX", "*", SearchOption.AllDirectories).Select(f => f))
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
						DirectoryHelper.Copy(sourcePath, destinationPath, true, true);
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
	}
}
