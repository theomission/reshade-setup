using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using Microsoft.Win32;

namespace ReShade.Setup
{
	public partial class Wizard
	{
		bool _finished, _elevated;
		string _targetPath;
		readonly ManualResetEventSlim _apiCondition = new ManualResetEventSlim();

		public Wizard()
		{
			InitializeComponent();
		}

		void OnWindowInit(object sender, EventArgs e)
		{
			Glass.RemoveIcon(this);
			Glass.ExtendFrame(this);
		}
		void OnWindowLoaded(object sender, RoutedEventArgs e)
		{
			var args = Environment.GetCommandLineArgs();

			if (args.Length > 2)
			{
				_elevated = args[2].StartsWith("ELEVATED");

				if (_elevated)
				{
					Top = double.Parse(args[2].Substring(args[2].IndexOf('|') + 1, args[2].IndexOf(']') - args[2].IndexOf('|') - 1));
					Left = double.Parse(args[2].Substring(args[2].IndexOf('[') + 1, args[2].IndexOf('|') - args[2].IndexOf('[') - 1));
				}
			}
			if (args.Length > 1 && File.Exists(args[1]))
			{
				Install(args[1]);
			}
		}
		void OnWindowClosing(object sender, CancelEventArgs e)
		{
			_finished = true;
			_apiCondition.Set();
		}

		void OnApiChecked(object sender, RoutedEventArgs e)
		{
			_apiCondition.Set();
		}
		void OnSetupButtonClick(object sender, RoutedEventArgs e)
		{
			if (!_finished)
			{
				var dlg = new OpenFileDialog
				{
					Filter = "Applications|*.exe",
					DefaultExt = ".exe",
					Multiselect = false,
					ValidateNames = true,
					CheckFileExists = true
				};

				if (dlg.ShowDialog(this) == true)
				{
					Install(dlg.FileName);
				}
			}
			else if (!string.IsNullOrEmpty(_targetPath))
			{
				Process.Start(new ProcessStartInfo(_targetPath) { WorkingDirectory = Path.GetDirectoryName(_targetPath) });

				Close();
			}
		}
		void OnSetupButtonDragDrop(object sender, DragEventArgs e)
		{
			if (_finished || !e.Data.GetDataPresent(DataFormats.FileDrop))
			{
				return;
			}

			var files = e.Data.GetData(DataFormats.FileDrop, true) as string[];

			if (files != null)
			{
				Install(files[0]);
			}
		}

		private void Install(string path)
		{
			if (!_elevated && !DirectoryHelper.IsWritable(Path.GetDirectoryName(path)))
			{
				Process.Start(
					new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,
					"\"" + path + "\" \"ELEVATED[" + Left + "|" + Top + "]\"") { Verb = "runas" });

				Close();
				return;
			}

			if (Thread.CurrentThread == Dispatcher.Thread)
			{
				new Thread(() => { Install(path); }).Start();
				return;
			}

			var info = FileVersionInfo.GetVersionInfo(path);
			var name = !string.IsNullOrEmpty(info.ProductName) ? info.ProductName : Path.GetFileNameWithoutExtension(path);

			Dispatcher.Invoke(new Action(() =>
			{
				_targetPath = path;
				Title = "Installing to " + name + " ...";
				SetupButton.IsEnabled = false;
				Message.Content = "Analyzing " + name + " ...";
				Progress.Visibility = Visibility.Visible;
				Progress.IsIndeterminate = true;
			}));

			#region Analyze Game

			var exeInfo = new PEInfo(path);
			var is64Bit = exeInfo.Type == PEInfo.BinaryType.IMAGE_FILE_MACHINE_AMD64;

			var nameModule = exeInfo.Modules.FirstOrDefault(s =>
				s.StartsWith("d3d8", StringComparison.OrdinalIgnoreCase) ||
				s.StartsWith("d3d9", StringComparison.OrdinalIgnoreCase) ||
				s.StartsWith("dxgi", StringComparison.OrdinalIgnoreCase) ||
				s.StartsWith("opengl32", StringComparison.OrdinalIgnoreCase));

			if (nameModule == null)
			{
				Dispatcher.Invoke(new Action(() =>
				{
					Title = "Warning!";
					Message.Content = "Auto-detection failed. Please select:";
					Progress.Visibility = Visibility.Collapsed;
					ApiGroup.IsEnabled = true;
				}));

				_apiCondition.Wait();

				Dispatcher.Invoke(new Action(() =>
				{
					if (ApiDirect3D8.IsChecked == true)
					{
						nameModule = "d3d8.dll";
					}
					else if (ApiDirect3D9.IsChecked == true)
					{
						nameModule = "d3d9.dll";
					}
					else if (ApiDirectXGI.IsChecked == true)
					{
						nameModule = "dxgi.dll";
					}
					else if (ApiOpenGL.IsChecked == true)
					{
						nameModule = "opengl32.dll";
					}

					ApiGroup.IsEnabled = false;
				}));
			}

			if (nameModule == null || _finished)
			{
				return;
			}

			Dispatcher.Invoke(new Action(() =>
			{
				if (nameModule.StartsWith("d3d8", StringComparison.InvariantCultureIgnoreCase))
				{
					ApiDirect3D8.IsChecked = true;
				}
				if (nameModule.StartsWith("d3d9", StringComparison.InvariantCultureIgnoreCase))
				{
					ApiDirect3D9.IsChecked = true;
				}
				if (nameModule.StartsWith("dxgi", StringComparison.InvariantCultureIgnoreCase))
				{
					ApiDirectXGI.IsChecked = true;
				}
				if (nameModule.StartsWith("opengl32", StringComparison.InvariantCultureIgnoreCase))
				{
					ApiOpenGL.IsChecked = true;
				}
			}));

			#endregion

			var pathDll = is64Bit ? "ReShade64.dll" : "ReShade32.dll";
			var pathTargetDirectory = Path.GetDirectoryName(path);
			var pathModule = Path.Combine(pathTargetDirectory, nameModule);

			if (File.Exists(pathModule))
			{
				if (MessageBox.Show("Do you want to overwrite the existing installation?", "Existing installation found.", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
				{
					Dispatcher.Invoke(new Action(() =>
					{
						_finished = true;
						Title = "Failed!";
						Message.Content = "Existing installation found.";
						Progress.Visibility = Visibility.Collapsed;
					}));

					return;
				}

				var pathModuleFx = Path.ChangeExtension(pathModule, "fx");

				if (File.Exists(pathModuleFx))
				{
					try
					{
						File.Delete(pathModuleFx);
					}
					catch
					{
						Dispatcher.Invoke(new Action(() =>
						{
							_finished = true;
							Title = "Failed!";
							Message.Content = "Unable to delete existing installation.";
							Progress.Visibility = Visibility.Collapsed;
						}));

						return;
					}
				}
			}

			#region Copy Files

			var files = new List<Tuple<string, string>> { new Tuple<string, string>(pathDll, pathModule) };

			if (File.Exists("ReShade.fx"))
			{
				files.Add(new Tuple<string, string>("ReShade.fx", Path.Combine(pathTargetDirectory, "ReShade.fx")));
			}
			if (File.Exists("Sweet.fx"))
			{
				files.Add(new Tuple<string, string>("Sweet.fx", Path.Combine(pathTargetDirectory, "Sweet.fx")));
			}

			if (Directory.Exists("ReShade"))
			{
				files.AddRange(
					Directory.EnumerateFiles("ReShade", "*", SearchOption.AllDirectories)
						.Select(f => f)
						.Select(file => new Tuple<string, string>(file, Path.Combine(pathTargetDirectory, file))));
			}
			if (Directory.Exists("SweetFX"))
			{
				files.AddRange(
					Directory.EnumerateFiles("SweetFX", "*", SearchOption.AllDirectories)
						.Select(f => f)
						.Select(file => new Tuple<string, string>(file, Path.Combine(pathTargetDirectory, file))));
			}

			for (var i = 0; i < files.Count; i++)
			{
				var sourcePath = files[i].Item1;
				var destinationPath = files[i].Item2;

				Dispatcher.Invoke(new Action(() =>
				{
					Message.Content = "Installing \"" + sourcePath + "\" ...";
					Progress.IsIndeterminate = false;
					Progress.Progress = i * 100.0 / files.Count;
					Progress.Visibility = Visibility.Visible;
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
					Dispatcher.Invoke(new Action(() =>
					{
						_finished = true;
						Title = "Failed!";
						Message.Content = "Unable to copy file \"" + sourcePath + "\".";
						Progress.Visibility = Visibility.Collapsed;
					}));

					return;
				}
			}

			#endregion

			Dispatcher.Invoke(new Action(() =>
			{
				_finished = true;
				Title = "Success!";
				SetupButton.IsEnabled = true;
				Message.Content = "Run " + name;
				Progress.Visibility = Visibility.Collapsed;
			}));
		}
	}
}