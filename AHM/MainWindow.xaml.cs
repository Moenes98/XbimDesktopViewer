using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using Microsoft.Win32;
using Xbim.Common;
using Xbim.Ifc;
using Xbim.IO;
using Xbim.ModelGeometry.Scene;
using Xbim.Presentation;
using Xbim.Presentation.LayerStyling;


namespace AHM
{
	partial class MainWindow : Window
	{
		private BackgroundWorker _loadFileBackgroundWorker;
		private string _temporaryXbimFileName;
		private bool _meshModel = true; // Set to true to generate geometry
		private bool _multiThreading = true; // Set to true for multi-threaded geometry generation

		public MainWindow()
		{
			InitializeComponent();

		}


		private void LoadButton_Click(object sender, RoutedEventArgs e)
		{
			// Create an OpenFileDialog to select an IFC file
			OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Filter = "IFC Files|*.ifc;*.ifcxml;*.ifczip|All Files|*.*",
				Title = "Select an IFC File"
			};

			// Show the dialog and check if the user selected a file
			if (openFileDialog.ShowDialog() == true)
			{
				string filePath = openFileDialog.FileName;
				LoadAnyModel(filePath); // Call the method to load the IFC file
			}
		}

		private void LoadAnyModel(string modelFileName)
		{
			var fInfo = new FileInfo(modelFileName);
			if (!fInfo.Exists) // File does not exist; do nothing
				return;

			// Close any existing model
			CloseAndDeleteTemporaryFiles();

			// Set up the background worker for file loading
			SetWorkerForFileLoad();

			// Start the background worker
			_loadFileBackgroundWorker.RunWorkerAsync(modelFileName);
		}
		private void SetWorkerForFileLoad()
		{
			_loadFileBackgroundWorker = new BackgroundWorker
			{
				WorkerReportsProgress = false, // No progress reporting
				WorkerSupportsCancellation = true
			};

			_loadFileBackgroundWorker.DoWork += OpenAcceptableExtension;
			_loadFileBackgroundWorker.RunWorkerCompleted += FileLoadCompleted;
		}

		private void OpenAcceptableExtension(object s, DoWorkEventArgs args)
		{
			var selectedFilename = args.Argument as string;

			try
			{
				// Create a temporary file for the model
				_temporaryXbimFileName = Path.GetTempFileName();

				// Open the IFC file using IfcStore
				var model = IfcStore.Open(selectedFilename, null, null, null, Xbim.IO.XbimDBAccess.Read);

				// Generate 3D geometry if needed
				if (_meshModel && model.GeometryStore.IsEmpty)
				{
					var context = new Xbim3DModelContext(model);
					if (!_multiThreading)
						context.MaxThreads = 1;

					// Set deflection and create geometry context
					SetDeflection(model);
					context.CreateContext(null, true); // No progress reporting
				}

				args.Result = model; // Return the loaded model
			}
			catch (Exception ex)
			{
				args.Result = ex; // Return the exception if something goes wrong
			}
		}

		private void FileLoadCompleted(object s, RunWorkerCompletedEventArgs args)
		{
			if (args.Result is IfcStore model) // Successfully loaded
			{
				// Update the UI with the loaded model
				DrawingControl.Model = model;
				Title = $"IFC Viewer - [{_temporaryXbimFileName}]"; // Update window title
			}
			else if (args.Result is Exception exception) // Error occurred
			{
				MessageBox.Show(exception.Message, "Error Opening File", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		private void CloseAndDeleteTemporaryFiles()
		{
			if (_loadFileBackgroundWorker != null && _loadFileBackgroundWorker.IsBusy)
				_loadFileBackgroundWorker.CancelAsync();

			if (!string.IsNullOrWhiteSpace(_temporaryXbimFileName) && File.Exists(_temporaryXbimFileName))
				File.Delete(_temporaryXbimFileName);

			_temporaryXbimFileName = null;
		}
		private void SetDeflection(IModel model)
		{
			var mf = model.ModelFactors;
			if (mf == null)
				return;

			// Example: Set deflection values (customize as needed)
			mf.DeflectionAngle = 0.5; // Angular deflection
			mf.DeflectionTolerance = mf.OneMilliMetre * 1.0; // Linear deflection
		}
	}
}

