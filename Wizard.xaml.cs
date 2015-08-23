using System;
using System.Collections.Generic;
using System.ComponentModel;
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
		}

		private bool mElevated = false;
		private bool mFinished = false;
		private string mTargetPath = null;
		private ManualResetEventSlim mApiCondition = new ManualResetEventSlim();

		private void OnWindowInit(object sender, EventArgs e)
		{
			Glass.RemoveIcon(this);
			Glass.ExtendFrame(this);
		}
		private void OnWindowLoaded(object sender, RoutedEventArgs e)
		{
			string[] args = Environment.GetCommandLineArgs();

			if (args.Length > 2)
			{
				this.mElevated = args[2].StartsWith("ElEVATED");

				if (this.mElevated)
				{
					this.Top = Double.Parse(args[2].Substring(args[2].IndexOf('|') + 1, args[2].IndexOf(']') - args[2].IndexOf('|') - 1));
					this.Left = Double.Parse(args[2].Substring(args[2].IndexOf('[') + 1, args[2].IndexOf('|') - args[2].IndexOf('[') - 1));
				}
			}
			if (args.Length > 1 && File.Exists(args[1]))
			{
				Install(args[1]);
			}
		}
		private void OnWindowClosing(object sender, CancelEventArgs e)
		{
			this.mFinished = true;
			this.mApiCondition.Set();
		}

		private void OnButtonClick(object sender, RoutedEventArgs e)
		{
			if (!this.mFinished)
			{
				OpenFileDialog dlg = new OpenFileDialog();
				dlg.Filter = "Applications|*.exe";
				dlg.DefaultExt = ".exe";
				dlg.Multiselect = false;
				dlg.ValidateNames = true;
				dlg.CheckFileExists = true;

				if (dlg.ShowDialog(this) == true)
				{
					Install(dlg.FileName);
				}
			}
			else if (!String.IsNullOrEmpty(this.mTargetPath))
			{
				Process.Start(new ProcessStartInfo(this.mTargetPath) { WorkingDirectory = Path.GetDirectoryName(this.mTargetPath) });

				Close();
			}
		}
		private void OnButtonDragDrop(object sender, DragEventArgs e)
		{
			if (!this.mFinished && e.Data.GetDataPresent(DataFormats.FileDrop))
			{
				string[] files = e.Data.GetData(DataFormats.FileDrop, true) as string[];

				Install(files[0]);
			}
		}
		private void OnApiChecked(object sender, RoutedEventArgs e)
		{
			this.mApiCondition.Set();
		}

		public void Install(string path)
		{
			if (!this.mElevated && !DirectoryHelper.IsWritable(Path.GetDirectoryName(path)))
			{
				Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "\"" + path + "\" \"ElEVATED[" + this.Left.ToString() + "|" + this.Top.ToString() + "]\"") { Verb = "runas" });

				Close();
				return;
			}

			if (Thread.CurrentThread == this.Dispatcher.Thread)
			{
				new Thread(() => { Install(path); }).Start();
				return;
			}

			FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
			string name = !String.IsNullOrEmpty(info.ProductName) ? info.ProductName : Path.GetFileNameWithoutExtension(path);

			this.Dispatcher.Invoke(new Action(() =>
			{
				this.mTargetPath = path;
				this.Title = "Installing to " + name + " ...";
				this.Button.IsEnabled = false;
				this.Message.Content = "Analyzing " + name + " ...";
				this.Progress.Visibility = Visibility.Visible;
				this.Progress.IsIndeterminate = true;
			}));

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
				this.Dispatcher.Invoke(new Action(() =>
				{
					this.Title = "Warning!";
					this.Message.Content = "Auto-detection failed. Please select:";
					this.Progress.Visibility = Visibility.Collapsed;
					this.ApiGroup.IsEnabled = true;
				}));

				this.mApiCondition.Wait();

				this.Dispatcher.Invoke(new Action(() =>
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
				}));
			}

			if (nameModule == null || this.mFinished)
			{
				return;
			}

			this.Dispatcher.Invoke(new Action(() =>
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
			}));
			#endregion

			string pathDll = is64Bit ? "ReShade64.dll" : "ReShade32.dll";
			string pathModule = Path.Combine(Path.GetDirectoryName(path), nameModule);

			if (File.Exists(pathModule))
			{
				if (MessageBox.Show("Do you want to overwrite the existing installation?", "Existing installation found.", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
				{
					this.Dispatcher.Invoke(new Action(() =>
					{
						this.mFinished = true;
						this.Title = "Failed!";
						this.Message.Content = "Existing installation found.";
						this.Progress.Visibility = Visibility.Collapsed;
					}));

					return;
				}

				string pathModuleFx = Path.ChangeExtension(pathModule, "fx");

				if (File.Exists(pathModuleFx))
				{
					try
					{
						File.Delete(pathModuleFx);
					}
					catch
					{
						this.Dispatcher.Invoke(new Action(() =>
						{
							this.mFinished = true;
							this.Title = "Failed!";
							this.Message.Content = "Unable to delete existing installation.";
							this.Progress.Visibility = Visibility.Collapsed;
						}));

						return;
					}
				}
			}

			#region Copy Files
			List<Tuple<string, string>> files = new List<Tuple<string, string>>();
			files.Add(new Tuple<string, string>(pathDll, pathModule));

			if (File.Exists("ReShade.fx"))
			{
				files.Add(new Tuple<string, string>("ReShade.fx", Path.Combine(Path.GetDirectoryName(pathModule), "ReShade.fx")));
			}
			if (File.Exists("Sweet.fx"))
			{
				files.Add(new Tuple<string, string>("Sweet.fx", Path.Combine(Path.GetDirectoryName(pathModule), "Sweet.fx")));
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

				this.Dispatcher.Invoke(new Action(() =>
				{
					this.Message.Content = "Installing \"" + sourcePath + "\" ...";
					this.Progress.IsIndeterminate = false;
					this.Progress.Progress = i * 100.0 / files.Count;
					this.Progress.Visibility = Visibility.Visible;
				}));

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
				catch
				{
					this.Dispatcher.Invoke(new Action(() =>
					{
						this.mFinished = true;
						this.Title = "Failed!";
						this.Message.Content = "Unable to copy file \"" + sourcePath + "\".";
						this.Progress.Visibility = Visibility.Collapsed;
					}));

					return;
				}
			}
			#endregion

			this.Dispatcher.Invoke(new Action(() =>
			{
				this.mFinished = true;
				this.Title = "Success!";
				this.Button.IsEnabled = true;
				this.Message.Content = "Run " + name;
				this.Progress.Visibility = Visibility.Collapsed;
			}));
		}
	}
}
