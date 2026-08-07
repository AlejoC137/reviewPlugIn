using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitToSupabasePlugin
{
    public partial class ManagerFamiliasLadrilloWindow : Window
    {
        // Variables para la cámara 3D
        private System.Windows.Point _lastMousePosition;
        private double _cameraAngleX = 45;
        private double _cameraAngleY = 45;
        private double _cameraDistance = 100;
        private bool _isDragging = false;

        private UIDocument _uidoc;
        private Document _doc;
        private List<FamilySymbol> _loadedBricks;

        public ManagerFamiliasLadrilloWindow(UIDocument uidoc)
        {
            InitializeComponent();
            _uidoc = uidoc;
            _doc = uidoc.Document;
            _loadedBricks = new List<FamilySymbol>();
            LoadBricksFromDocument();
            UpdateCamera();
        }

        private List<Autodesk.Revit.DB.Material> _materials = new List<Autodesk.Revit.DB.Material>();

        private FamilySymbol GetSelectedSymbol()
        {
            if (lstBricks.SelectedItem is BrickTypeItem item) return item.Symbol;
            if (lstBricks.SelectedItem is FamilySymbol symbol) return symbol;
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

        private void LoadBricksFromDocument()
        {
            try
            {
                if (_doc == null) return;
                
                FilteredElementCollector collector = new FilteredElementCollector(_doc);
                
                // Buscar FamilySymbols relacionados a ladrillos/bloques
                var symbols = collector.OfClass(typeof(FamilySymbol))
                                       .Cast<FamilySymbol>()
                                       .Where(s => s.Family.Name.IndexOf("Ladrillo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                   s.Family.Name.IndexOf("Bloque", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                   s.Name.IndexOf("Ladrillo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                   s.Name.IndexOf("Bloque", StringComparison.OrdinalIgnoreCase) >= 0)
                                       .OrderBy(s => s.Family.Name)
                                       .ThenBy(s => s.Name)
                                       .ToList();

                _loadedBricks = symbols;
                var displayItems = symbols.Select(s => new BrickTypeItem(s)).ToList();
                lstBricks.ItemsSource = displayItems;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Error cargando ladrillos: " + ex.Message);
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadMaterials();
            UpdatePreview();
        }

        private void LstBricks_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FamilySymbol selectedSymbol = GetSelectedSymbol();
            if (selectedSymbol != null)
            {
                btnExportBrick.IsEnabled = true;
                btnLocate.IsEnabled = true;
                btnUpdate.IsEnabled = true;
                btnEdit.IsEnabled = true;
                btnDuplicate.IsEnabled = true;
                btnDelete.IsEnabled = true;
                pnlProperties.IsEnabled = true;

                txtFamilyName.Text = selectedSymbol.Family.Name;
                txtTypeName.Text = selectedSymbol.Name;

                // Load dimensions (Depth, Width, Height, Thickness) in cm
                Parameter pDepth = selectedSymbol.LookupParameter("Depth");
                txtDepth.Text = pDepth != null ? (pDepth.AsDouble() * 30.48).ToString("0.0") : "";

                Parameter pWidth = selectedSymbol.LookupParameter("Width");
                txtWidth.Text = pWidth != null ? (pWidth.AsDouble() * 30.48).ToString("0.0") : "";

                Parameter pHeight = selectedSymbol.LookupParameter("Height");
                txtHeight.Text = pHeight != null ? (pHeight.AsDouble() * 30.48).ToString("0.0") : "";

                Parameter pThickness = selectedSymbol.LookupParameter("Thickness");
                txtThickness.Text = pThickness != null ? (pThickness.AsDouble() * 30.48).ToString("0.0") : "";

                // Load properties
                Parameter pCost = selectedSymbol.get_Parameter(BuiltInParameter.ALL_MODEL_COST);
                txtCost.Text = pCost != null ? pCost.AsValueString() ?? pCost.AsDouble().ToString() : "";

                Parameter pDesc = selectedSymbol.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION);
                txtDescription.Text = pDesc != null ? pDesc.AsString() : "";

                Parameter pManuf = selectedSymbol.get_Parameter(BuiltInParameter.ALL_MODEL_MANUFACTURER);
                txtManufacturer.Text = pManuf != null ? pManuf.AsString() : "";

                Parameter pMat = selectedSymbol.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM)
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

                UpdatePreview();
            }
            else
            {
                btnExportBrick.IsEnabled = false;
                btnLocate.IsEnabled = false;
                btnUpdate.IsEnabled = false;
                btnEdit.IsEnabled = false;
                btnDuplicate.IsEnabled = false;
                btnDelete.IsEnabled = false;
                pnlProperties.IsEnabled = false;

                txtDepth.Text = "";
                txtWidth.Text = "";
                txtHeight.Text = "";
                txtThickness.Text = "";
                txtCost.Text = "";
                txtDescription.Text = "";
                txtManufacturer.Text = "";
                
                modelGroup.Children.Clear();
                lblBrickSize.Text = "Selecciona un ladrillo";
            }
        }

        private void BtnAddBrick_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Selecciona el archivo de Familia RFA para agregar",
                Filter = "Revit Family (*.rfa)|*.rfa"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    using (Transaction t = new Transaction(_doc, "Cargar Familia Ladrillo"))
                    {
                        t.Start();
                        bool loaded = _doc.LoadFamily(openFileDialog.FileName, out Family loadedFamily);
                        
                        if (loaded && loadedFamily != null)
                        {
                            // Activate symbols
                            ISet<ElementId> symbolIds = loadedFamily.GetFamilySymbolIds();
                            foreach (ElementId id in symbolIds)
                            {
                                if (_doc.GetElement(id) is FamilySymbol symbol && !symbol.IsActive)
                                {
                                    symbol.Activate();
                                }
                            }
                        }
                        t.Commit();
                    }

                    // Refresh list
                    LoadBricksFromDocument();
                    System.Windows.MessageBox.Show($"La familia ha sido cargada exitosamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error al cargar la familia: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnExportBrick_Click(object sender, RoutedEventArgs e)
        {
            FamilySymbol selectedSymbol = GetSelectedSymbol();
            if (selectedSymbol != null)
            {
                Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Exportar familia de ladrillo",
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
            FamilySymbol selectedSymbol = GetSelectedSymbol();
            if (selectedSymbol != null)
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
                        System.Windows.MessageBox.Show($"Se seleccionaron {instances.Count} instancias en el modelo.", "Ubicar", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        System.Windows.MessageBox.Show($"No se encontraron instancias del tipo '{selectedSymbol.Name}' en el modelo.", "Ubicar", MessageBoxButton.OK, MessageBoxImage.Information);
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
            FamilySymbol selectedSymbol = GetSelectedSymbol();
            if (selectedSymbol != null)
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
                        using (Transaction t = new Transaction(_doc, "Actualizar Familia"))
                        {
                            t.Start();
                            bool loaded = _doc.LoadFamily(openFileDialog.FileName, new BrickFamilyLoadOptions(), out Family loadedFamily);
                            t.Commit();

                            if (loaded)
                            {
                                LoadBricksFromDocument();
                                System.Windows.MessageBox.Show("La familia ha sido actualizada exitosamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                            }
                            else
                            {
                                System.Windows.MessageBox.Show("La familia no se cargó (posiblemente no hubo cambios).", "Información", MessageBoxButton.OK, MessageBoxImage.Information);
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
            FamilySymbol selectedSymbol = GetSelectedSymbol();
            if (selectedSymbol != null)
            {
                try
                {
                    Document familyDoc = _doc.EditFamily(selectedSymbol.Family);
                    if (familyDoc != null)
                    {
                        string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), selectedSymbol.Family.Name + "_edit.rfa");
                        familyDoc.SaveAs(tempPath, new SaveAsOptions { OverwriteExistingFile = true });
                        familyDoc.Close(false);

                        _uidoc.Application.OpenAndActivateDocument(tempPath);
                        this.Close(); // Cerramos el gestor para que edite
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
            FamilySymbol selectedSymbol = GetSelectedSymbol();
            if (selectedSymbol != null)
            {
                string currentName = selectedSymbol.Name;
                string newName = ShowInputDialog("Nuevo Tipo", "Introduce el nombre del nuevo tipo:", currentName + " - Copia");
                
                if (!string.IsNullOrWhiteSpace(newName))
                {
                    try
                    {
                        using (Transaction t = new Transaction(_doc, "Duplicar Tipo Ladrillo"))
                        {
                            t.Start();
                            ElementType newSymbol = selectedSymbol.Duplicate(newName);
                            t.Commit();
                        }
                        
                        LoadBricksFromDocument();
                        System.Windows.MessageBox.Show($"Tipo '{newName}' creado exitosamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"Error al duplicar el tipo: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            FamilySymbol selectedSymbol = GetSelectedSymbol();
            if (selectedSymbol != null)
            {
                var result = System.Windows.MessageBox.Show($"¿Estás seguro de que deseas eliminar la familia '{selectedSymbol.Family.Name}' completamente del proyecto?", "Confirmar Eliminación", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        using (Transaction t = new Transaction(_doc, "Eliminar Familia Ladrillo"))
                        {
                            t.Start();
                            _doc.Delete(selectedSymbol.Family.Id);
                            t.Commit();
                        }
                        
                        LoadBricksFromDocument();
                        System.Windows.MessageBox.Show("Familia eliminada exitosamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
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
            FamilySymbol selectedSymbol = GetSelectedSymbol();
            if (selectedSymbol != null)
            {
                try
                {
                    using (Transaction t = new Transaction(_doc, "Actualizar Propiedades Ladrillo"))
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

                        // Save dimensions (Depth, Width, Height, Thickness)
                        Action<Parameter, string> saveDim = (p, txt) =>
                        {
                            if (p != null && !p.IsReadOnly && !string.IsNullOrWhiteSpace(txt))
                            {
                                if (double.TryParse(txt.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double valCm))
                                {
                                    p.Set(valCm / 30.48);
                                }
                            }
                        };

                        saveDim(selectedSymbol.LookupParameter("Depth"), txtDepth.Text);
                        saveDim(selectedSymbol.LookupParameter("Width"), txtWidth.Text);
                        saveDim(selectedSymbol.LookupParameter("Height"), txtHeight.Text);
                        saveDim(selectedSymbol.LookupParameter("Thickness"), txtThickness.Text);

                        Parameter pCost = selectedSymbol.get_Parameter(BuiltInParameter.ALL_MODEL_COST);
                        if (pCost != null && !pCost.IsReadOnly)
                        {
                            if (double.TryParse(txtCost.Text.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double costValue))
                            {
                                pCost.Set(costValue);
                            }
                            else
                            {
                                pCost.Set(txtCost.Text);
                            }
                        }

                        Parameter pDesc = selectedSymbol.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION);
                        if (pDesc != null && !pDesc.IsReadOnly) pDesc.Set(txtDescription.Text);

                        Parameter pManuf = selectedSymbol.get_Parameter(BuiltInParameter.ALL_MODEL_MANUFACTURER);
                        if (pManuf != null && !pManuf.IsReadOnly) pManuf.Set(txtManufacturer.Text);

                        if (cmbMaterial.SelectedItem is Autodesk.Revit.DB.Material selectedMat)
                        {
                            Parameter pMat = selectedSymbol.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM)
                                           ?? selectedSymbol.LookupParameter("Material")
                                           ?? selectedSymbol.LookupParameter("Material Estructural");
                            if (pMat != null && !pMat.IsReadOnly)
                            {
                                pMat.Set(selectedMat.Id);
                            }
                        }

                        t.Commit();
                    }

                    LoadBricksFromDocument();
                    UpdatePreview();

                    System.Windows.MessageBox.Show("Propiedades actualizadas exitosamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error al actualizar las propiedades: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private string ShowInputDialog(string title, string prompt, string defaultValue)
        {
            Window window = new Window
            {
                Title = title,
                Width = 350,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize
            };

            StackPanel panel = new StackPanel { Margin = new Thickness(10) };
            panel.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 10) });
            
            System.Windows.Controls.TextBox textBox = new System.Windows.Controls.TextBox { Text = defaultValue, Margin = new Thickness(0, 0, 0, 15) };
            panel.Children.Add(textBox);

            System.Windows.Controls.Button okButton = new System.Windows.Controls.Button { Content = "Aceptar", Width = 80, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, IsDefault = true };
            okButton.Click += (sender, e) => { window.DialogResult = true; window.Close(); };
            panel.Children.Add(okButton);

            window.Content = panel;

            if (window.ShowDialog() == true)
            {
                return textBox.Text;
            }
            return null;
        }

        private class BrickFamilyLoadOptions : IFamilyLoadOptions
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

            if (_cameraAngleY < 0) camera3D.UpDirection = new Vector3D(0, 0, -1);
            else camera3D.UpDirection = new Vector3D(0, 0, 1);

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

            if (normY < 15 || normY > 345) lblViewOrientation.Text = "Vista Superior";
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

                _lastMousePosition = currentPos;
                UpdateCamera();
            }
        }

        private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            _cameraDistance -= e.Delta * 0.1;
            if (_cameraDistance < 10) _cameraDistance = 10;
            if (_cameraDistance > 500) _cameraDistance = 500;
            UpdateCamera();
        }

        private void UpdatePreview()
        {
            try
            {
                if (modelGroup == null || gizmoGroup == null) return;
                
                modelGroup.Children.Clear();
                modelGroup.Children.Add(new AmbientLight(System.Windows.Media.Color.FromRgb(100, 100, 100)));
                modelGroup.Children.Add(new DirectionalLight(System.Windows.Media.Colors.White, new Vector3D(-1, -1, -1)));
                modelGroup.Children.Add(new DirectionalLight(System.Windows.Media.Color.FromRgb(100, 100, 100), new Vector3D(1, 1, 1)));

                gizmoGroup.Children.Clear();
                gizmoGroup.Children.Add(new AmbientLight(System.Windows.Media.Colors.White));

                double length = 24.0;
                double width = 11.5;
                double height = 7.0;

                FamilySymbol symbol = GetSelectedSymbol();
                string fullNameStr = "";
                double thickness = 1.5;

                if (symbol != null)
                {
                    fullNameStr = $"{symbol.Family.Name} - {symbol.Name}";
                    Parameter pDepth = symbol.LookupParameter("Depth");
                    Parameter pWidth = symbol.LookupParameter("Width");
                    Parameter pHeight = symbol.LookupParameter("Height");
                    Parameter pThickness = symbol.LookupParameter("Thickness");

                    if (pDepth != null) length = pDepth.AsDouble() * 30.48;
                    if (pWidth != null) width = pWidth.AsDouble() * 30.48;
                    if (pHeight != null) height = pHeight.AsDouble() * 30.48;
                    
                    if (pThickness != null && pThickness.HasValue) thickness = pThickness.AsDouble() * 30.48;

                    lblBrickSize.Text = $"{symbol.Family.Name} - {symbol.Name}\nDepth: {length:F1}cm | Width: {width:F1}cm | Height: {height:F1}cm | Thickness: {thickness:F1}cm";
                }
                else
                {
                    lblBrickSize.Text = "Selecciona un ladrillo";
                    return;
                }

                length = Math.Max(1, length);
                width = Math.Max(1, width);
                height = Math.Max(1, height);

                System.Windows.Media.Color brickColor = System.Windows.Media.Color.FromRgb(79, 209, 197);

                modelGroup.Children.Add(CreateBoxModel(length, width, height, brickColor, 0.9));
                modelGroup.Children.Add(CreateWireframeModel(length, width, height, System.Windows.Media.Colors.LightBlue, 0.2));

                // Dibujar perfil de perforaciones (Celdas/Tubos) según la orientación de extrusión (ExtruH / ExtruV)
                AddCellProfileWireframe(modelGroup, fullNameStr, length, width, height, thickness);

                gizmoGroup.Children.Add(CreateBoxModel(4, 0.2, 0.2, System.Windows.Media.Colors.Red, 1.0)); // X
                gizmoGroup.Children.Add(CreateBoxModel(0.2, 4, 0.2, System.Windows.Media.Colors.Green, 1.0)); // Y
                gizmoGroup.Children.Add(CreateBoxModel(0.2, 0.2, 4, System.Windows.Media.Colors.Blue, 1.0)); // Z
                
                double maxDim = Math.Max(length, Math.Max(width, height));
                if (!_isDragging)
                {
                    _cameraDistance = maxDim * 2.5;
                    UpdateCamera();
                }
            }
            catch { }
        }

        private GeometryModel3D CreateBoxModel(double l, double w, double h, System.Windows.Media.Color color, double opacity)
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

            foreach (int i in indices)
            {
                mesh.TriangleIndices.Add(i);
            }

            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Opacity = opacity;
            DiffuseMaterial mat = new DiffuseMaterial(brush);

            return new GeometryModel3D(mesh, mat);
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

        private void AddCellProfileWireframe(Model3DGroup modelGroup, string nameStr, double l, double w, double h, double wallThick)
        {
            if (wallThick <= 0) wallThick = 1.5;
            bool isExtruH = nameStr.Contains("ExtruH") || nameStr.Contains("Horizontal");

            System.Windows.Media.Color holeColor = System.Windows.Media.Color.FromRgb(246, 173, 85); // Cálido Dorado/Ámbar para perforaciones
            double edgeT = 0.15;

            if (isExtruH)
            {
                // Perforación Horizontal: 3 tubos horizontales a lo largo de X
                double cellW = (w - (wallThick * 4.0)) / 3.0;
                double cellH = h - (wallThick * 2.0);
                if (cellW > 0 && cellH > 0)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        double startY = -w / 2.0 + wallThick + i * (cellW + wallThick);
                        double centerY = startY + cellW / 2.0;

                        Model3DGroup holeEdge = CreateWireframeModel(l * 0.99, cellW, cellH, holeColor, edgeT);
                        holeEdge.Transform = new TranslateTransform3D(0, centerY, 0);
                        modelGroup.Children.Add(holeEdge);
                    }
                }
            }
            else
            {
                // Perforación Vertical: 3 celdas verticales a lo largo de Z (ExtruV)
                double cellL = (l - (wallThick * 4.0)) / 3.0;
                double cellW = w - (wallThick * 2.0);
                if (cellL > 0 && cellW > 0)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        double startX = -l / 2.0 + wallThick + i * (cellL + wallThick);
                        double centerX = startX + cellL / 2.0;

                        Model3DGroup holeEdge = CreateWireframeModel(cellL, cellW, h * 0.99, holeColor, edgeT);
                        holeEdge.Transform = new TranslateTransform3D(centerX, 0, 0);
                        modelGroup.Children.Add(holeEdge);
                    }
                }
            }
        }
    }

    public class BrickTypeItem
    {
        public FamilySymbol Symbol { get; set; }
        public string FullName => $"{Symbol.Family.Name} - {Symbol.Name}";

        public BrickTypeItem(FamilySymbol symbol)
        {
            Symbol = symbol;
        }
    }
}
