using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using Microsoft.Win32;
using Xbim.Common;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;
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

		#region LoadIFC
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

		#endregion
		private void AboutButton_Click(object sender, RoutedEventArgs e)
		{
			MessageBox.Show(
			"This fork was done by:\n" +
			"- Ahmed\n" +
			"- Hazem\n" +
			"- Mostafa\n" +
			"@ ITI-AEC Intake 45",
			"About",
			MessageBoxButton.OK,
			MessageBoxImage.Information
			);
		}

		#region Properties Retrieval
		public class PropertyItem
		{
			public string PropertySetName { get; set; } // Name of the property set
			public string Name { get; set; }           // Name of the property
			public string Value { get; set; }          // Value of the property
			public int IfcLabel { get; set; }          // IFC label of the property (optional)
		}

		private ObservableCollection<PropertyItem> _properties = new ObservableCollection<PropertyItem>();

		/// <summary>
		/// Loads properties for the selected IFC entity.
		/// </summary>
		/// <param name="entity">The selected IFC entity.</param>
		public void LoadProperties(IPersistEntity entity)
		{
			if (entity == null)
				return;

			// Clear existing properties
			_properties.Clear();

			// Fill properties
			FillPropertyData(entity);
		}

		/// <summary>
		/// Extracts properties from the IFC entity.
		/// </summary>
		/// <param name="entity">The selected IFC entity.</param>
		private void FillPropertyData(IPersistEntity entity)
		{
			if (entity is IIfcObject ifcObject)
			{
				// Extract properties from IfcObject
				foreach (var relDef in ifcObject.IsDefinedBy)
				{
					if (relDef.RelatingPropertyDefinition is IIfcPropertySet pSet)
						AddPropertySet(pSet);
				}
			}
			else if (entity is IIfcTypeObject ifcTypeObject)
			{
				// Extract properties from IfcTypeObject
				if (ifcTypeObject.HasPropertySets != null)
				{
					foreach (var pSet in ifcTypeObject.HasPropertySets.OfType<IIfcPropertySet>())
						AddPropertySet(pSet);
				}
			}
		}

		/// <summary>
		/// Adds properties from a property set to the collection.
		/// </summary>
		/// <param name="pSet">The property set.</param>
		private void AddPropertySet(IIfcPropertySet pSet)
		{
			if (pSet == null)
				return;

			// Add single-value properties
			foreach (var item in pSet.HasProperties.OfType<IIfcPropertySingleValue>())
				AddProperty(item, pSet.Name);

			// Add complex properties (nested properties)
			foreach (var item in pSet.HasProperties.OfType<IIfcComplexProperty>())
			{
				foreach (var composingProperty in item.HasProperties.OfType<IIfcPropertySingleValue>())
					AddProperty(composingProperty, $"{pSet.Name} / {item.Name}");
			}

			// Add enumerated properties
			foreach (var item in pSet.HasProperties.OfType<IIfcPropertyEnumeratedValue>())
				AddProperty(item, pSet.Name);
		}

		/// <summary>
		/// Adds a single-value property to the collection.
		/// </summary>
		/// <param name="item">The property.</param>
		/// <param name="groupName">The name of the property set or group.</param>
		private void AddProperty(IIfcPropertySingleValue item, string groupName)
		{
			_properties.Add(new PropertyItem
			{
				IfcLabel = item.EntityLabel,
				PropertySetName = groupName,
				Name = item.Name,
				Value = item.NominalValue?.ToString() ?? ""
			});
		}

		/// <summary>
		/// Adds an enumerated property to the collection.
		/// </summary>
		/// <param name="item">The property.</param>
		/// <param name="groupName">The name of the property set or group.</param>
		private void AddProperty(IIfcPropertyEnumeratedValue item, string groupName)
		{
			foreach (var nomVal in item.EnumerationValues)
			{
				_properties.Add(new PropertyItem
				{
					IfcLabel = item.EntityLabel,
					PropertySetName = groupName,
					Name = item.Name,
					Value = nomVal?.ToString() ?? ""
				});
			}
		}

		public void OnEntitySelected(IPersistEntity selectedEntity)
		{
			LoadProperties(selectedEntity);
		}
	}
	#endregion
}

