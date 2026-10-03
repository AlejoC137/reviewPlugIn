using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitToSupabasePlugin
{
    public partial class ManagerFamiliasPisoWindow : Window
    {
        // Variables para la cámara 3D
        private System.Windows.Point _lastMousePosition;
        private double _cameraAngleX = 45;
        private double _cameraAngleY = 35;
        private double _cameraDistance = 140;
        private bool _isDragging = false;
        private bool _isUpdatingUI = false;

        private UIDocument? _uidoc;
        private Document? _doc;
        private List<FamilySymbol> _loadedTiles = new List<FamilySymbol>();
        private List<FamilySymbol> _loadedPegas = new List<FamilySymbol>();
        private List<Autodesk.Revit.DB.Material> _materials = new List<Autodesk.Revit.DB.Material>();

        public ManagerFamiliasPisoWindow(UIDocument? uidoc)
        {
            InitializeComponent();
            _uidoc = uidoc;
            _doc = uidoc?.Document;
            LoadTilesFromDocument();
            UpdateCamera();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadMaterials();
            LoadPegasFromDocument();

            // Si no hay baldosas cargadas en el proyecto, intentar autocargar Tiles Base.rfa
            if (_loadedTiles.Count == 0)
            {
                TryAutoLoadDefaultTileFamilies();
            }

            if (lstTiles.Items.Count > 0)
            {
                lstTiles.SelectedIndex = 0;
            }
            else if (lstPegas.Items.Count > 0)
            {
                lstPegas.SelectedIndex = 0;
            }
            else
            {
                UpdatePreview();
            }
        }

        private FamilySymbol? GetSelectedSymbol()
        {
            if (lstTiles?.SelectedItem is TileTypeItem item) return item.Symbol;
            if (lstTiles?.SelectedItem is FamilySymbol symbol) return symbol;
            if (lstPegas?.SelectedItem is TileTypeItem pegaItem) return pegaItem.Symbol;
            if (lstPegas?.SelectedItem is FamilySymbol pegaSymbol) return pegaSymbol;
            return null;
        }

        private void LoadMaterials()
        {
            try
            {
                if (_doc == null) return;
                _materials = new FilteredElementCollector(_doc)
                    .OfClass(typeof(Autodesk.Revit.DB.Material))
                    .Cast<Autodesk.Revit.DB.Material>()
                    .OrderBy(m => m.Name)
                    .ToList();
                cmbMaterial.ItemsSource = _materials;
            }
            catch { }
        }

        private void LoadTilesFromDocument()
        {
            try
            {
                if (_doc == null) return;

                FilteredElementCollector collector = new FilteredElementCollector(_doc);
                var symbols = collector.OfClass(typeof(FamilySymbol))
                                       .Cast<FamilySymbol>()
                                       .Where(s =>
                                       {
                                           string fName = s.Family.Name;
                                           string sName = s.Name;

                                           // Excluir pegas/morteros explicitamente para que queden solo baldosas
                                           bool isPega = fName.IndexOf("Pega", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                         fName.IndexOf("Mortero", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                         fName.IndexOf("Fragua", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                         sName.IndexOf("Pega", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                         sName.IndexOf("Fragua", StringComparison.OrdinalIgnoreCase) >= 0;

                                           if (isPega) return false;

                                           return fName.IndexOf("Tile", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                  fName.IndexOf("Baldosa", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                  fName.IndexOf("Porcelanato", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                  fName.IndexOf("Enchape", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                  sName.IndexOf("Tile", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                  sName.IndexOf("Baldosa", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                  sName.IndexOf("Porcelanato", StringComparison.OrdinalIgnoreCase) >= 0;
                                       })
                                       .OrderBy(s => s.Family.Name)
                                       .ThenBy(s => s.Name)
                                       .ToList();

                _loadedTiles = symbols;
                var displayItems = symbols.Select(s => new TileTypeItem(s)).ToList();
                lstTiles.ItemsSource = displayItems;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Error cargando baldosas: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadPegasFromDocument()
        {
            try
            {
                if (_doc == null) return;

                FilteredElementCollector collector = new FilteredElementCollector(_doc);
                var pegas = collector.OfClass(typeof(FamilySymbol))
                                     .Cast<FamilySymbol>()
                                     .Where(s =>
                                     {
                                         string fName = s.Family.Name;
                                         string sName = s.Name;
                                         return fName.IndexOf("Pega", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                fName.IndexOf("Mortero", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                fName.IndexOf("Fragua", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                fName.IndexOf("Junta", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                sName.IndexOf("Pega", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                sName.IndexOf("Fragua", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                sName.IndexOf("Junta", StringComparison.OrdinalIgnoreCase) >= 0;
                                     })
                                     .OrderBy(s => s.Family.Name)
                                     .ThenBy(s => s.Name)
                                     .ToList();

                _loadedPegas = pegas;
                var displayPegas = pegas.Select(s => new TileTypeItem(s)).ToList();
                lstPegas.ItemsSource = displayPegas;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Error cargando familias de pega/fraguas: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TryAutoLoadDefaultTileFamilies()
        {
            if (_doc == null) return;
            try
            {
                string baseDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
                string tilesBaseRfa = Path.Combine(baseDir, "Familias", "Floors-Tiles", "Tiles Base.rfa");
                if (!File.Exists(tilesBaseRfa))
                {
                    tilesBaseRfa = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Familias", "Floors-Tiles", "Tiles Base.rfa");
                }

                if (File.Exists(tilesBaseRfa))
                {
                    using (Transaction t = new Transaction(_doc, "Cargar Familia Tiles Base"))
                    {
                        t.Start();
                        if (_doc.LoadFamily(tilesBaseRfa, out Family loadedFamily) && loadedFamily != null)
                        {
                            foreach (ElementId id in loadedFamily.GetFamilySymbolIds())
                            {
                                if (_doc.GetElement(id) is FamilySymbol symbol && !symbol.IsActive)
                                    symbol.Activate();
                            }
                        }
                        t.Commit();
                    }
                    LoadTilesFromDocument();
                }
            }
            catch { }
        }

        private void LstTiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstTiles.SelectedItem != null && lstPegas != null)
            {
                lstPegas.UnselectAll();
            }
            OnSymbolSelected();
        }

        private void LstPegas_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstPegas.SelectedItem != null && lstTiles != null)
            {
                lstTiles.UnselectAll();
            }
            OnSymbolSelected();
        }

        private void OnSymbolSelected()
        {
            FamilySymbol? selectedSymbol = GetSelectedSymbol();
            if (selectedSymbol != null)
            {
                _isUpdatingUI = true;

                btnExportTile.IsEnabled = lstTiles.SelectedItem != null;
                btnExportPega.IsEnabled = lstPegas.SelectedItem != null;
                btnLocate.IsEnabled = true;
                btnUpdate.IsEnabled = true;
                btnEdit.IsEnabled = true;
                btnDuplicate.IsEnabled = true;
                btnDeleteType.IsEnabled = true;
                btnDelete.IsEnabled = true;
                pnlProperties.IsEnabled = true;

                txtFamilyName.Text = selectedSymbol.Family.Name;
                txtTypeName.Text = selectedSymbol.Name;

                // Dimensiones (Largo, Ancho, Espesor en cm, Bisel en mm)
                Parameter? pLength = selectedSymbol.LookupParameter("Length")
                                  ?? selectedSymbol.LookupParameter("Largo")
                                  ?? selectedSymbol.LookupParameter("Depth")
                                  ?? selectedSymbol.LookupParameter("Longitud");
                double lengthCm = pLength != null ? pLength.AsDouble() * 30.48 : 60.0;
                txtLength.Text = lengthCm.ToString("0.0");

                Parameter? pWidth = selectedSymbol.LookupParameter("Width")
                                 ?? selectedSymbol.LookupParameter("Ancho");
                double widthCm = pWidth != null ? pWidth.AsDouble() * 30.48 : 60.0;
                txtWidth.Text = widthCm.ToString("0.0");

                Parameter? pHeight = selectedSymbol.LookupParameter("Height")
                                  ?? selectedSymbol.LookupParameter("Thickness")
                                  ?? selectedSymbol.LookupParameter("Espesor")
                                  ?? selectedSymbol.LookupParameter("Alto");
                double heightCm = pHeight != null ? pHeight.AsDouble() * 30.48 : 1.0;
                txtThickness.Text = heightCm.ToString("0.0");

                Parameter? pBevel = selectedSymbol.LookupParameter("Bisel")
                                 ?? selectedSymbol.LookupParameter("Chaflan")
                                 ?? selectedSymbol.LookupParameter("Joint")
                                 ?? selectedSymbol.LookupParameter("GrosorPega")
                                 ?? selectedSymbol.LookupParameter("Junta");
                double bevelMm = pBevel != null ? (pBevel.AsDouble() * 304.8) : 2.0;
                txtBevel.Text = bevelMm.ToString("0.0");

                // Propiedades de Costo, Descripción, Fabricante
                Parameter? pCost = selectedSymbol.get_Parameter(BuiltInParameter.ALL_MODEL_COST);
                txtCost.Text = pCost != null ? pCost.AsValueString() ?? pCost.AsDouble().ToString("0.00") : "25.0";

                Parameter? pDesc = selectedSymbol.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION);
                txtDescription.Text = pDesc != null ? pDesc.AsString() ?? "" : "";

                Parameter? pManuf = selectedSymbol.get_Parameter(BuiltInParameter.ALL_MODEL_MANUFACTURER);
                txtManufacturer.Text = pManuf != null ? pManuf.AsString() ?? "" : "";

                // Material
                Parameter? pMat = selectedSymbol.LookupParameter("Mat")
                                ?? selectedSymbol.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM)
                                ?? selectedSymbol.LookupParameter("Material")
                                ?? selectedSymbol.LookupParameter("Material Estructural");
                if (pMat != null && pMat.HasValue)
                {
                    ElementId matId = pMat.AsElementId();
                    cmbMaterial.SelectedItem = _materials.FirstOrDefault(m => m.Id == matId);
                }
                else
                {
                    cmbMaterial.SelectedIndex = -1;
                }

                // Ajustar categoría según el nombre del tipo o familia
                SelectCategoryFromName(selectedSymbol.Family.Name + " " + selectedSymbol.Name);

                _isUpdatingUI = false;

                UpdateMetrics(lengthCm, widthCm);
                UpdatePreview();
            }
            else
            {
                btnExportTile.IsEnabled = false;
                btnExportPega.IsEnabled = false;
                btnLocate.IsEnabled = false;
                btnUpdate.IsEnabled = false;
                btnEdit.IsEnabled = false;
                btnDuplicate.IsEnabled = false;
                btnDeleteType.IsEnabled = false;
                btnDelete.IsEnabled = false;
                pnlProperties.IsEnabled = false;

                txtLength.Text = "";
                txtWidth.Text = "";
                txtThickness.Text = "";
                txtBevel.Text = "";
                txtCost.Text = "";
                txtDescription.Text = "";
                txtManufacturer.Text = "";

                lblTileArea.Text = "0.000 m²";
                lblYield.Text = "0.00 pz/m²";
                lblAspectRatio.Text = "-";

                modelGroup.Children.Clear();
                lblTileSize.Text = "Selecciona una baldosa o elemento";
            }
        }

        private void SelectCategoryFromName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            string n = name.ToLowerInvariant();

            foreach (ComboBoxItem item in cmbCategory.Items)
            {
                string c = item.Content.ToString()?.ToLowerInvariant() ?? "";
                if (n.Contains("madera") || n.Contains("parquet"))
                {
                    if (c.Contains("madera")) { cmbCategory.SelectedItem = item; return; }
                }
                else if (n.Contains("ceram") || n.Contains("cerám"))
                {
                    if (c.Contains("cerámica")) { cmbCategory.SelectedItem = item; return; }
                }
                else if (n.Contains("marmol") || n.Contains("mármol") || n.Contains("granit") || n.Contains("piedra"))
                {
                    if (c.Contains("mármol") || c.Contains("piedra")) { cmbCategory.SelectedItem = item; return; }
                }
                else if (n.Contains("terrazo") || n.Contains("concreto") || n.Contains("hormig"))
                {
                    if (c.Contains("terrazo")) { cmbCategory.SelectedItem = item; return; }
                }
                else if (n.Contains("adoquin") || n.Contains("adoquín") || n.Contains("exterior"))
                {
                    if (c.Contains("adoquín")) { cmbCategory.SelectedItem = item; return; }
                }
            }
            if (cmbCategory.Items.Count > 0) cmbCategory.SelectedIndex = 0;
        }

        private void DimensionField_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingUI) return;

            if (double.TryParse(txtLength.Text.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double l) &&
                double.TryParse(txtWidth.Text.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double w))
            {
                UpdateMetrics(l, w);
                UpdatePreview();
            }
        }

        private void UpdateMetrics(double lengthCm, double widthCm)
        {
            if (lengthCm <= 0 || widthCm <= 0) return;
            double areaM2 = (lengthCm * widthCm) / 10000.0;
            double yieldPieces = areaM2 > 0 ? 1.0 / areaM2 : 0;
            double ratio = widthCm > 0 ? lengthCm / widthCm : 1.0;

            lblTileArea.Text = $"{areaM2:0.000} m²";
            lblYield.Text = $"{yieldPieces:0.00} pz/m²";
            lblAspectRatio.Text = $"{ratio:0.00} {(Math.Abs(ratio - 1.0) < 0.05 ? "(Cuadrada 1:1)" : "(Rectangular)")}";
        }

        private void BtnAddTile_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Selecciona el archivo de Familia RFA de Baldosa para agregar",
                Filter = "Revit Family (*.rfa)|*.rfa"
            };

            string defaultDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Familias", "Floors-Tiles");
            if (Directory.Exists(defaultDir)) openFileDialog.InitialDirectory = defaultDir;

            if (openFileDialog.ShowDialog() == true && _doc != null)
            {
                try
                {
                    using (Transaction t = new Transaction(_doc, "Cargar Familia Baldosa"))
                    {
                        t.Start();
                        bool loaded = _doc.LoadFamily(openFileDialog.FileName, out Family loadedFamily);

                        if (loaded && loadedFamily != null)
                        {
                            foreach (ElementId id in loadedFamily.GetFamilySymbolIds())
                            {
                                if (_doc.GetElement(id) is FamilySymbol symbol && !symbol.IsActive)
                                {
                                    symbol.Activate();
                                }
                            }
                        }
                        t.Commit();
                    }

                    LoadTilesFromDocument();
                    System.Windows.MessageBox.Show("La familia de baldosa ha sido cargada exitosamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error al cargar la familia: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnExportTile_Click(object sender, RoutedEventArgs e)
        {
            ExportSelectedSymbolToRfa("Exportar familia de baldosa");
        }

        private void BtnAddPega_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Selecciona el archivo de Familia RFA de Pega / Fragua",
                Filter = "Revit Family (*.rfa)|*.rfa"
            };

            string defaultDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Familias", "Floors-Tiles");
            if (Directory.Exists(defaultDir)) openFileDialog.InitialDirectory = defaultDir;

            if (openFileDialog.ShowDialog() == true && _doc != null)
            {
                try
                {
                    using (Transaction t = new Transaction(_doc, "Cargar Familia Pega/Fragua"))
                    {
                        t.Start();
                        bool loaded = _doc.LoadFamily(openFileDialog.FileName, out Family loadedFamily);

                        if (loaded && loadedFamily != null)
                        {
                            foreach (ElementId id in loadedFamily.GetFamilySymbolIds())
                            {
                                if (_doc.GetElement(id) is FamilySymbol symbol && !symbol.IsActive)
                                {
                                    symbol.Activate();
                                }
                            }
                        }
                        t.Commit();
                    }

                    LoadPegasFromDocument();
                    System.Windows.MessageBox.Show("La familia de pega/junta ha sido cargada exitosamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error al cargar la familia: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnExportPega_Click(object sender, RoutedEventArgs e)
        {
            ExportSelectedSymbolToRfa("Exportar familia de pega / junta");
        }

        private void ExportSelectedSymbolToRfa(string dialogTitle)
        {
            FamilySymbol? selectedSymbol = GetSelectedSymbol();
            if (selectedSymbol != null && _doc != null)
            {
                Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = dialogTitle,
                    Filter = "Revit Family (*.rfa)|*.rfa",
                    FileName = $"{selectedSymbol.Family.Name}.rfa"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    try
                    {
                        Document familyDoc = _doc.EditFamily(selectedSymbol.Family);
                        if (familyDoc != null)
                        {
                            SaveAsOptions options = new SaveAsOptions { OverwriteExistingFile = true };
                            familyDoc.SaveAs(saveFileDialog.FileName, options);
                            familyDoc.Close(false);
                            System.Windows.MessageBox.Show($"Familia exportada exitosamente a:\n{saveFileDialog.FileName}", "Exportar", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"Error al exportar la familia: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void BtnLocate_Click(object sender, RoutedEventArgs e)
        {
            FamilySymbol? selectedSymbol = GetSelectedSymbol();
            if (selectedSymbol != null && _doc != null && _uidoc != null)
            {
                try
                {
                    FilteredElementCollector collector = new FilteredElementCollector(_doc);
                    var instances = collector.OfClass(typeof(FamilyInstance))
                                             .WhereElementIsNotElementType()
                                             .Cast<FamilyInstance>()
                                             .Where(fi => fi.Symbol.Id == selectedSymbol.Id)
                                             .Select(fi => fi.Id)
                                             .ToList();

                    if (instances.Any())
                    {
                        _uidoc.Selection.SetElementIds(instances);
                        System.Windows.MessageBox.Show($"Se seleccionaron {instances.Count} instancias de baldosa en el modelo.", "Ubicar", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        System.Windows.MessageBox.Show($"No se encontraron instancias del tipo '{selectedSymbol.Name}' colocadas en el modelo.", "Ubicar", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error al ubicar instancias: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnUpdate_Click(object sender, RoutedEventArgs e)
        {
            FamilySymbol? selectedSymbol = GetSelectedSymbol();
            if (selectedSymbol != null && _doc != null)
            {
                Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = $"Selecciona el archivo para actualizar '{selectedSymbol.Family.Name}'",
                    Filter = "Revit Family (*.rfa)|*.rfa"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    try
                    {
                        using (Transaction t = new Transaction(_doc, "Actualizar Familia Baldosa"))
                        {
                            t.Start();
                            bool loaded = _doc.LoadFamily(openFileDialog.FileName, new TileFamilyLoadOptions(), out Family loadedFamily);
                            t.Commit();

                            if (loaded)
                            {
                                LoadTilesFromDocument();
                                LoadPegasFromDocument();
                                System.Windows.MessageBox.Show("La familia ha sido actualizada exitosamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                            }
                            else
                            {
                                System.Windows.MessageBox.Show("La familia no se recargó (posiblemente sin cambios estructurales).", "Información", MessageBoxButton.OK, MessageBoxImage.Information);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"Error al actualizar la familia: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            FamilySymbol? selectedSymbol = GetSelectedSymbol();
            if (selectedSymbol != null && _doc != null && _uidoc != null)
            {
                try
                {
                    Document familyDoc = _doc.EditFamily(selectedSymbol.Family);
                    if (familyDoc != null)
                    {
                        string tempPath = Path.Combine(Path.GetTempPath(), selectedSymbol.Family.Name + "_edit.rfa");
                        familyDoc.SaveAs(tempPath, new SaveAsOptions { OverwriteExistingFile = true });
                        familyDoc.Close(false);

                        _uidoc.Application.OpenAndActivateDocument(tempPath);
                        this.Close();
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error al editar la familia: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnDuplicate_Click(object sender, RoutedEventArgs e)
        {
            FamilySymbol? selectedSymbol = GetSelectedSymbol();
            if (selectedSymbol != null && _doc != null)
            {
                string currentName = selectedSymbol.Name;
                string? newName = ShowInputDialog("Nuevo Tipo de Baldosa", "Introduce el nombre del nuevo tipo:", currentName + " - Copia");

                if (!string.IsNullOrWhiteSpace(newName))
                {
                    try
                    {
                        using (Transaction t = new Transaction(_doc, "Duplicar Tipo Baldosa"))
                        {
                            t.Start();
                            ElementType newSymbol = selectedSymbol.Duplicate(newName);
                            t.Commit();
                        }

                        LoadTilesFromDocument();
                        LoadPegasFromDocument();
                        System.Windows.MessageBox.Show($"Tipo '{newName}' creado exitosamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"Error al duplicar el tipo: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void BtnDeleteType_Click(object sender, RoutedEventArgs e)
        {
            FamilySymbol? selectedSymbol = GetSelectedSymbol();
            if (selectedSymbol != null && _doc != null)
            {
                var result = System.Windows.MessageBox.Show(
                    $"¿Estás seguro de que deseas eliminar el tipo '{selectedSymbol.Name}' de la familia '{selectedSymbol.Family.Name}'?",
                    "Confirmar Eliminación de Tipo",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        using (Transaction t = new Transaction(_doc, "Eliminar Tipo Baldosa"))
                        {
                            t.Start();
                            _doc.Delete(selectedSymbol.Id);
                            t.Commit();
                        }

                        LoadTilesFromDocument();
                        LoadPegasFromDocument();
                        System.Windows.MessageBox.Show($"Tipo '{selectedSymbol.Name}' eliminado exitosamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"Error al eliminar el tipo: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            FamilySymbol? selectedSymbol = GetSelectedSymbol();
            if (selectedSymbol != null && _doc != null)
            {
                var result = System.Windows.MessageBox.Show(
                    $"¿Estás seguro de que deseas eliminar la familia '{selectedSymbol.Family.Name}' completamente del proyecto?",
                    "Confirmar Eliminación",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        using (Transaction t = new Transaction(_doc, "Eliminar Familia Baldosa"))
                        {
                            t.Start();
                            _doc.Delete(selectedSymbol.Family.Id);
                            t.Commit();
                        }

                        LoadTilesFromDocument();
                        LoadPegasFromDocument();
                        System.Windows.MessageBox.Show($"Familia '{selectedSymbol.Family.Name}' eliminada exitosamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"Error al eliminar la familia: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void BtnSaveProperties_Click(object sender, RoutedEventArgs e)
        {
            FamilySymbol? selectedSymbol = GetSelectedSymbol();
            if (selectedSymbol != null && _doc != null)
            {
                try
                {
                    using (Transaction t = new Transaction(_doc, "Actualizar Propiedades Baldosa"))
                    {
                        t.Start();

                        // Renombrar Familia si cambió
                        if (!string.IsNullOrWhiteSpace(txtFamilyName.Text) && txtFamilyName.Text != selectedSymbol.Family.Name)
                        {
                            selectedSymbol.Family.Name = txtFamilyName.Text;
                        }

                        // Renombrar Tipo si cambió
                        if (!string.IsNullOrWhiteSpace(txtTypeName.Text) && txtTypeName.Text != selectedSymbol.Name)
                        {
                            selectedSymbol.Name = txtTypeName.Text;
                        }

                        // Guardar Dimensiones en pies
                        Action<Parameter?, string, double> saveDim = (p, txt, factor) =>
                        {
                            if (p != null && !p.IsReadOnly && !string.IsNullOrWhiteSpace(txt))
                            {
                                if (double.TryParse(txt.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
                                {
                                    p.Set(val / factor);
                                }
                            }
                        };

                        // Largo / Depth en cm
                        Parameter? pLength = selectedSymbol.LookupParameter("Length")
                                          ?? selectedSymbol.LookupParameter("Largo")
                                          ?? selectedSymbol.LookupParameter("Depth")
                                          ?? selectedSymbol.LookupParameter("Longitud");
                        saveDim(pLength, txtLength.Text, 30.48);

                        // Ancho / Width en cm
                        Parameter? pWidth = selectedSymbol.LookupParameter("Width")
                                         ?? selectedSymbol.LookupParameter("Ancho");
                        saveDim(pWidth, txtWidth.Text, 30.48);

                        // Espesor / Height en cm
                        Parameter? pHeight = selectedSymbol.LookupParameter("Height")
                                          ?? selectedSymbol.LookupParameter("Thickness")
                                          ?? selectedSymbol.LookupParameter("Espesor")
                                          ?? selectedSymbol.LookupParameter("Alto");
                        saveDim(pHeight, txtThickness.Text, 30.48);

                        // Bisel / Junta en mm (factor: 304.8 mm por pie)
                        Parameter? pBevel = selectedSymbol.LookupParameter("Bisel")
                                         ?? selectedSymbol.LookupParameter("Chaflan")
                                         ?? selectedSymbol.LookupParameter("Joint")
                                         ?? selectedSymbol.LookupParameter("GrosorPega")
                                         ?? selectedSymbol.LookupParameter("Junta");
                        saveDim(pBevel, txtBevel.Text, 304.8);

                        // Costo
                        Parameter? pCost = selectedSymbol.get_Parameter(BuiltInParameter.ALL_MODEL_COST);
                        if (pCost != null && !pCost.IsReadOnly)
                        {
                            if (double.TryParse(txtCost.Text.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double costValue))
                                pCost.Set(costValue);
                            else
                                pCost.Set(txtCost.Text);
                        }

                        // Descripción y Fabricante
                        Parameter? pDesc = selectedSymbol.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION);
                        if (pDesc != null && !pDesc.IsReadOnly) pDesc.Set(txtDescription.Text);

                        Parameter? pManuf = selectedSymbol.get_Parameter(BuiltInParameter.ALL_MODEL_MANUFACTURER);
                        if (pManuf != null && !pManuf.IsReadOnly) pManuf.Set(txtManufacturer.Text);

                        // Material
                        if (cmbMaterial.SelectedItem is Autodesk.Revit.DB.Material selectedMat)
                        {
                            Parameter? pMat = selectedSymbol.LookupParameter("Mat")
                                           ?? selectedSymbol.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM)
                                           ?? selectedSymbol.LookupParameter("Material")
                                           ?? selectedSymbol.LookupParameter("Material Estructural");
                            if (pMat != null && !pMat.IsReadOnly)
                            {
                                pMat.Set(selectedMat.Id);
                            }
                        }

                        t.Commit();
                    }

                    LoadTilesFromDocument();
                    LoadPegasFromDocument();
                    UpdatePreview();

                    System.Windows.MessageBox.Show("Propiedades de la baldosa actualizadas exitosamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error al actualizar las propiedades: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private string? ShowInputDialog(string title, string prompt, string defaultValue)
        {
            Window window = new Window
            {
                Title = title,
                Width = 360,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(26, 28, 32)),
                Foreground = System.Windows.Media.Brushes.White
            };

            StackPanel panel = new StackPanel { Margin = new Thickness(14) };
            panel.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8), Foreground = System.Windows.Media.Brushes.LightGray });

            System.Windows.Controls.TextBox textBox = new System.Windows.Controls.TextBox
            {
                Text = defaultValue,
                Margin = new Thickness(0, 0, 0, 14),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 55, 72)),
                Foreground = System.Windows.Media.Brushes.White,
                Padding = new Thickness(4)
            };
            panel.Children.Add(textBox);

            System.Windows.Controls.Button okButton = new System.Windows.Controls.Button
            {
                Content = "Aceptar",
                Width = 90,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                IsDefault = true,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(43, 108, 176)),
                Foreground = System.Windows.Media.Brushes.White,
                Padding = new Thickness(6, 4, 6, 4)
            };
            okButton.Click += (sender, e) => { window.DialogResult = true; window.Close(); };
            panel.Children.Add(okButton);

            window.Content = panel;

            if (window.ShowDialog() == true)
            {
                return textBox.Text;
            }
            return null;
        }

        private class TileFamilyLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = true;
                return true;
            }

            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = true;
                return true;
            }
        }

        #region Renderizado 3D Interactivo y Gizmo

        private void UpdateCamera()
        {
            if (camera3D == null || gizmoCamera == null) return;

            double radX = _cameraAngleX * Math.PI / 180.0;
            double radY = _cameraAngleY * Math.PI / 180.0;

            double x = _cameraDistance * Math.Sin(radY) * Math.Cos(radX);
            double y = _cameraDistance * Math.Sin(radY) * Math.Sin(radX);
            double z = _cameraDistance * Math.Cos(radY);

            Point3D pos = new Point3D(x, y, z);
            Vector3D look = new Vector3D(-x, -y, -z);
            look.Normalize();

            camera3D.Position = pos;
            camera3D.LookDirection = look;
            camera3D.UpDirection = _cameraAngleY < 0 ? new Vector3D(0, 0, -1) : new Vector3D(0, 0, 1);

            double gx = 10 * Math.Sin(radY) * Math.Cos(radX);
            double gy = 10 * Math.Sin(radY) * Math.Sin(radX);
            double gz = 10 * Math.Cos(radY);

            gizmoCamera.Position = new Point3D(gx, gy, gz);
            gizmoCamera.LookDirection = new Vector3D(-gx, -gy, -gz);
            gizmoCamera.UpDirection = camera3D.UpDirection;

            UpdateOrientationLabel();
        }

        private void UpdateOrientationLabel()
        {
            if (lblViewOrientation == null) return;

            double normX = (_cameraAngleX % 360 + 360) % 360;
            double normY = (_cameraAngleY % 360 + 360) % 360;

            if (normY < 15 || normY > 345) lblViewOrientation.Text = "Vista Superior (Planta)";
            else if (normY > 165 && normY < 195) lblViewOrientation.Text = "Vista Inferior";
            else
            {
                if (normX < 45 || normX > 315) lblViewOrientation.Text = "Vista Lateral (X)";
                else if (normX >= 45 && normX <= 135) lblViewOrientation.Text = "Vista Frontal (Y)";
                else if (normX > 135 && normX < 225) lblViewOrientation.Text = "Vista Lateral Trasera (-X)";
                else lblViewOrientation.Text = "Vista Trasera (-Y)";

                if (normX % 90 > 15 && normX % 90 < 75)
                    lblViewOrientation.Text = "Vista Isométrica";
            }
        }

        private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _lastMousePosition = e.GetPosition(this);
            Mouse.Capture((IInputElement)sender);
        }

        private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            Mouse.Capture(null);
        }

        private void Viewport_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isDragging)
            {
                System.Windows.Point currentPos = e.GetPosition(this);
                double deltaX = currentPos.X - _lastMousePosition.X;
                double deltaY = currentPos.Y - _lastMousePosition.Y;

                _cameraAngleX -= deltaX * 0.5;
                _cameraAngleY -= deltaY * 0.5;

                _cameraAngleY = Math.Max(5, Math.Min(85, _cameraAngleY));

                _lastMousePosition = currentPos;
                UpdateCamera();
            }
        }

        private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            _cameraDistance -= e.Delta * 0.1;
            if (_cameraDistance < 15) _cameraDistance = 15;
            if (_cameraDistance > 600) _cameraDistance = 600;
            UpdateCamera();
        }

        private void UpdatePreview()
        {
            try
            {
                if (modelGroup == null || gizmoGroup == null) return;

                modelGroup.Children.Clear();
                modelGroup.Children.Add(new AmbientLight(System.Windows.Media.Color.FromRgb(100, 100, 100)));
                modelGroup.Children.Add(new DirectionalLight(System.Windows.Media.Colors.White, new Vector3D(-1, -1, -2)));
                modelGroup.Children.Add(new DirectionalLight(System.Windows.Media.Color.FromRgb(90, 95, 110), new Vector3D(1, 2, 1)));

                gizmoGroup.Children.Clear();
                gizmoGroup.Children.Add(new AmbientLight(System.Windows.Media.Colors.White));

                double length = 60.0;
                double width = 60.0;
                double height = 1.0;

                FamilySymbol? symbol = GetSelectedSymbol();

                if (symbol != null)
                {
                    Parameter? pLength = symbol.LookupParameter("Length")
                                      ?? symbol.LookupParameter("Largo")
                                      ?? symbol.LookupParameter("Depth")
                                      ?? symbol.LookupParameter("Longitud");
                    Parameter? pWidth = symbol.LookupParameter("Width")
                                     ?? symbol.LookupParameter("Ancho");
                    Parameter? pHeight = symbol.LookupParameter("Height")
                                      ?? symbol.LookupParameter("Thickness")
                                      ?? symbol.LookupParameter("Espesor")
                                      ?? symbol.LookupParameter("Alto");

                    if (pLength != null) length = pLength.AsDouble() * 30.48;
                    if (pWidth != null) width = pWidth.AsDouble() * 30.48;
                    if (pHeight != null) height = pHeight.AsDouble() * 30.48;

                    lblTileSize.Text = $"{symbol.Family.Name} - {symbol.Name}\nLargo: {length:F1}cm | Ancho: {width:F1}cm | Espesor: {height:F1}cm";
                }
                else
                {
                    lblTileSize.Text = "Selecciona una baldosa o elemento";
                    return;
                }

                length = Math.Max(1, length);
                width = Math.Max(1, width);
                height = Math.Max(0.2, height);

                // Determinar color de baldosa según categoría o tipo
                string catName = (cmbCategory.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "";
                System.Windows.Media.Color tileColor = System.Windows.Media.Color.FromRgb(225, 230, 238); // Porcelanato claro
                if (catName.Contains("Madera")) tileColor = System.Windows.Media.Color.FromRgb(182, 122, 73);
                else if (catName.Contains("Cerámica")) tileColor = System.Windows.Media.Color.FromRgb(215, 138, 98);
                else if (catName.Contains("Mármol") || catName.Contains("Piedra")) tileColor = System.Windows.Media.Color.FromRgb(210, 205, 195);
                else if (catName.Contains("Terrazo") || catName.Contains("Hormigón")) tileColor = System.Windows.Media.Color.FromRgb(165, 170, 175);
                else if (catName.Contains("Adoquín")) tileColor = System.Windows.Media.Color.FromRgb(140, 92, 82);

                // Modelo de la baldosa principal
                modelGroup.Children.Add(CreateTile3DModel(length, width, height, tileColor));

                // Wireframe sutil de contorno perimetral
                modelGroup.Children.Add(CreateWireframeModel(length, width, height, System.Windows.Media.Colors.LightSkyBlue, 0.25));

                // Rejilla de apoyo inferior (plano de colocación)
                modelGroup.Children.Add(CreateGroundGrid(Math.Max(length, width) * 1.6, -height / 2.0));

                // Ejes Gizmo en esquina
                gizmoGroup.Children.Add(CreateBoxModel(4, 0.2, 0.2, System.Windows.Media.Colors.Red, 1.0));   // X
                gizmoGroup.Children.Add(CreateBoxModel(0.2, 4, 0.2, System.Windows.Media.Colors.Green, 1.0)); // Y
                gizmoGroup.Children.Add(CreateBoxModel(0.2, 0.2, 4, System.Windows.Media.Colors.Blue, 1.0));  // Z

                double maxDim = Math.Max(length, Math.Max(width, height));
                if (!_isDragging)
                {
                    _cameraDistance = maxDim * 2.3;
                    UpdateCamera();
                }
            }
            catch { }
        }

        private GeometryModel3D CreateTile3DModel(double l, double w, double h, System.Windows.Media.Color color)
        {
            MeshGeometry3D mesh = new MeshGeometry3D();
            double x = l / 2.0;
            double y = w / 2.0;
            double z = h / 2.0;

            mesh.Positions.Add(new Point3D(-x, -y, -z)); // 0
            mesh.Positions.Add(new Point3D( x, -y, -z)); // 1
            mesh.Positions.Add(new Point3D( x,  y, -z)); // 2
            mesh.Positions.Add(new Point3D(-x,  y, -z)); // 3
            mesh.Positions.Add(new Point3D(-x, -y,  z)); // 4
            mesh.Positions.Add(new Point3D( x, -y,  z)); // 5
            mesh.Positions.Add(new Point3D( x,  y,  z)); // 6
            mesh.Positions.Add(new Point3D(-x,  y,  z)); // 7

            int[] indices = new int[]
            {
                0, 1, 2,  0, 2, 3,
                4, 6, 5,  4, 7, 6,
                0, 5, 1,  0, 4, 5,
                3, 2, 6,  3, 6, 7,
                0, 3, 7,  0, 7, 4,
                1, 5, 6,  1, 6, 2
            };

            foreach (int i in indices) mesh.TriangleIndices.Add(i);

            var diffuse = new DiffuseMaterial(new SolidColorBrush(color));
            var specular = new SpecularMaterial(new SolidColorBrush(System.Windows.Media.Color.FromRgb(70, 75, 80)), 35);
            var matGroup = new MaterialGroup();
            matGroup.Children.Add(diffuse);
            matGroup.Children.Add(specular);

            return new GeometryModel3D(mesh, matGroup);
        }

        private GeometryModel3D CreateBoxModel(double l, double w, double h, System.Windows.Media.Color color, double opacity)
        {
            MeshGeometry3D mesh = new MeshGeometry3D();
            double x = l / 2.0;
            double y = w / 2.0;
            double z = h / 2.0;

            mesh.Positions.Add(new Point3D(-x, -y, -z));
            mesh.Positions.Add(new Point3D( x, -y, -z));
            mesh.Positions.Add(new Point3D( x,  y, -z));
            mesh.Positions.Add(new Point3D(-x,  y, -z));
            mesh.Positions.Add(new Point3D(-x, -y,  z));
            mesh.Positions.Add(new Point3D( x, -y,  z));
            mesh.Positions.Add(new Point3D( x,  y,  z));
            mesh.Positions.Add(new Point3D(-x,  y,  z));

            int[] indices = new int[]
            {
                0, 1, 2,  0, 2, 3,
                4, 6, 5,  4, 7, 6,
                0, 5, 1,  0, 4, 5,
                3, 2, 6,  3, 6, 7,
                0, 3, 7,  0, 7, 4,
                1, 5, 6,  1, 6, 2
            };

            foreach (int i in indices) mesh.TriangleIndices.Add(i);

            SolidColorBrush brush = new SolidColorBrush(color) { Opacity = opacity };
            return new GeometryModel3D(mesh, new DiffuseMaterial(brush));
        }

        private Model3DGroup CreateWireframeModel(double l, double w, double h, System.Windows.Media.Color color, double thickness)
        {
            Model3DGroup edgesGroup = new Model3DGroup();
            double x = l / 2.0;
            double y = w / 2.0;
            double z = h / 2.0;

            edgesGroup.Children.Add(CreateEdge(l, thickness, thickness, 0, y, z, color));
            edgesGroup.Children.Add(CreateEdge(l, thickness, thickness, 0, -y, z, color));
            edgesGroup.Children.Add(CreateEdge(l, thickness, thickness, 0, y, -z, color));
            edgesGroup.Children.Add(CreateEdge(l, thickness, thickness, 0, -y, -z, color));

            edgesGroup.Children.Add(CreateEdge(thickness, w, thickness, x, 0, z, color));
            edgesGroup.Children.Add(CreateEdge(thickness, w, thickness, -x, 0, z, color));
            edgesGroup.Children.Add(CreateEdge(thickness, w, thickness, x, 0, -z, color));
            edgesGroup.Children.Add(CreateEdge(thickness, w, thickness, -x, 0, -z, color));

            edgesGroup.Children.Add(CreateEdge(thickness, thickness, h, x, y, 0, color));
            edgesGroup.Children.Add(CreateEdge(thickness, thickness, h, -x, y, 0, color));
            edgesGroup.Children.Add(CreateEdge(thickness, thickness, h, x, -y, 0, color));
            edgesGroup.Children.Add(CreateEdge(thickness, thickness, h, -x, -y, 0, color));

            return edgesGroup;
        }

        private GeometryModel3D CreateEdge(double l, double w, double h, double cx, double cy, double cz, System.Windows.Media.Color color)
        {
            GeometryModel3D edge = CreateBoxModel(l, w, h, color, 1.0);
            edge.Transform = new TranslateTransform3D(cx, cy, cz);
            return edge;
        }

        private GeometryModel3D CreateGroundGrid(double size, double baseZ)
        {
            MeshGeometry3D mesh = new MeshGeometry3D();
            double hs = size / 2.0;
            double z = baseZ - 0.05;

            mesh.Positions.Add(new Point3D(-hs, -hs, z));
            mesh.Positions.Add(new Point3D( hs, -hs, z));
            mesh.Positions.Add(new Point3D( hs,  hs, z));
            mesh.Positions.Add(new Point3D(-hs,  hs, z));

            mesh.TriangleIndices.Add(0); mesh.TriangleIndices.Add(1); mesh.TriangleIndices.Add(2);
            mesh.TriangleIndices.Add(0); mesh.TriangleIndices.Add(2); mesh.TriangleIndices.Add(3);

            var gridMat = new DiffuseMaterial(new SolidColorBrush(System.Windows.Media.Color.FromArgb(35, 255, 255, 255)));
            return new GeometryModel3D(mesh, gridMat);
        }

        #endregion
    }

    public class TileTypeItem
    {
        public FamilySymbol Symbol { get; set; }
        public string FullName => $"{Symbol.Family.Name} - {Symbol.Name}";

        public TileTypeItem(FamilySymbol symbol)
        {
            Symbol = symbol;
        }
    }
}
