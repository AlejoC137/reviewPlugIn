using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace RevitToSupabasePlugin
{
    public partial class GestorCategoriasWindow : Window
    {
        private UIDocument? _uidoc;
        private Document? _doc;
        private ExternalEvent? _extEvent;
        private GestorCategoriasExternalEventHandler? _handler;
        private List<ScannedElementItem> _allItems = new List<ScannedElementItem>();
        private ObservableCollection<ScannedElementItem> _displayedItems = new ObservableCollection<ScannedElementItem>();
        private List<FloorType> _floorTypes = new List<FloorType>();
        private bool _isLoaded = false;

        public GestorCategoriasWindow(UIDocument? uidoc, ExternalEvent? extEvent = null, GestorCategoriasExternalEventHandler? handler = null)
        {
            InitializeComponent();
            _uidoc = uidoc;
            _doc = uidoc?.Document;
            _extEvent = extEvent;
            _handler = handler;
        }

        private void RunInRevitContext(Action<UIApplication> action)
        {
            if (_handler != null && _extEvent != null)
            {
                _handler.Run(action);
            }
            else if (_uidoc?.Application != null)
            {
                action(_uidoc.Application);
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                LoadTargetCategories();
                LoadFloorTypes();
                ScanElements(activeViewOnly: false);
                PreselectCurrentRevitSelection();
                _isLoaded = true;
                UpdateInspector(null);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error al inicializar el gestor de categorías:\n{ex.Message}", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void LoadTargetCategories()
        {
            try
            {
                var categories = CategoryAssignmentService.GetAvailableSystemCategories();
                cmbTargetCategory.ItemsSource = categories;
                if (categories.Count > 0)
                {
                    cmbTargetCategory.SelectedIndex = 0;
                }
            }
            catch { }
        }

        private void LoadFloorTypes()
        {
            if (_doc == null) return;
            try
            {
                _floorTypes = new FilteredElementCollector(_doc)
                    .OfClass(typeof(FloorType))
                    .Cast<FloorType>()
                    .OrderBy(ft => ft.Name)
                    .ToList();

                cmbFloorTypes.ItemsSource = _floorTypes;
                if (_floorTypes.Count > 0)
                {
                    cmbFloorTypes.SelectedIndex = 0;
                }
            }
            catch { }
        }

        private void PreselectCurrentRevitSelection()
        {
            if (_uidoc == null || _doc == null) return;
            try
            {
                var selIds = _uidoc.Selection.GetElementIds();
                if (selIds != null && selIds.Count > 0)
                {
                    foreach (ElementId id in selIds)
                    {
                        var match = _allItems.FirstOrDefault(i => i.Id == id);
                        if (match != null)
                        {
                            match.IsSelected = true;
                        }
                        else
                        {
                            Element elem = _doc.GetElement(id);
                            if (elem != null && !(elem is ElementType))
                            {
                                Category? cat = elem.Category;
                                BuiltInCategory bic = BuiltInCategory.INVALID;
                                try { bic = cat != null ? cat.BuiltInCategory : BuiltInCategory.INVALID; } catch { }

                                var newItem = new ScannedElementItem
                                {
                                    Id = elem.Id,
                                    Name = elem.Name ?? "",
                                    ElementTypeKind = elem is DirectShape ? "DirectShape" : (elem is FamilyInstance ? "FamilyInstance" : elem.GetType().Name),
                                    CurrentCategoryName = cat?.Name ?? "Desconocido",
                                    CurrentBuiltInCategory = bic,
                                    Comments = elem.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? "",
                                    MaterialName = "Por Selección",
                                    LevelName = elem.LevelId != ElementId.InvalidElementId ? (_doc.GetElement(elem.LevelId)?.Name ?? "Sin Nivel") : "Sin Nivel",
                                    LevelId = elem.LevelId,
                                    IsPluginElement = true,
                                    PluginRole = "Pre-seleccionado en Modelo",
                                    ElementRef = elem,
                                    IsSelected = true
                                };
                                _allItems.Insert(0, newItem);
                            }
                        }
                    }
                    ApplyFilter();
                }
            }
            catch { }
        }

        private void ScanElements(bool activeViewOnly)
        {
            if (_uidoc?.Application?.ActiveUIDocument != null)
            {
                _uidoc = _uidoc.Application.ActiveUIDocument;
                _doc = _uidoc.Document;
            }
            if (_doc == null) return;

            bool pluginOnly = chkOnlyPlugin?.IsChecked == true;
            _allItems = CategoryAssignmentService.ScanElements(_doc, pluginOnly, activeViewOnly);
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (txtSearch == null || chkOnlyPlugin == null || gridElements == null) return;

            string filterText = txtSearch.Text?.Trim().ToLowerInvariant() ?? "";
            bool pluginOnly = chkOnlyPlugin.IsChecked == true;

            var filtered = _allItems.Where(item =>
            {
                if (item == null) return false;
                if (pluginOnly && !item.IsPluginElement) return false;

                if (!string.IsNullOrEmpty(filterText))
                {
                    bool match = (item.Name?.ToLowerInvariant().Contains(filterText) ?? false) ||
                                 (item.IdString?.Contains(filterText) ?? false) ||
                                 (item.PluginRole?.ToLowerInvariant().Contains(filterText) ?? false) ||
                                 (item.CurrentCategoryName?.ToLowerInvariant().Contains(filterText) ?? false) ||
                                 (item.Comments?.ToLowerInvariant().Contains(filterText) ?? false) ||
                                 (item.MaterialName?.ToLowerInvariant().Contains(filterText) ?? false);
                    if (!match) return false;
                }
                return true;
            }).ToList();

            _displayedItems = new ObservableCollection<ScannedElementItem>(filtered);
            gridElements.ItemsSource = _displayedItems;
            UpdateStatusLabel();
        }

        private void UpdateStatusLabel()
        {
            if (lblStatus == null || _displayedItems == null) return;
            int total = _displayedItems.Count;
            int selected = _displayedItems.Count(i => i != null && i.IsSelected);
            lblStatus.Text = $"{total} elementos listados ({selected} seleccionados)";
        }

        private void UpdateInspector(ScannedElementItem? item)
        {
            if (inspId == null) return;

            if (item == null)
            {
                inspId.Text = "-";
                inspName.Text = "Ningún elemento seleccionado";
                inspCategory.Text = "-";
                inspRole.Text = "-";
                inspMaterial.Text = "-";
                inspArea.Text = "-";
                inspVolume.Text = "-";
                inspComments.Text = "-";
                return;
            }

            inspId.Text = item.IdString;
            inspName.Text = !string.IsNullOrEmpty(item.Name) ? item.Name : "(Sin Nombre)";
            inspCategory.Text = item.CurrentCategoryName;
            inspRole.Text = item.PluginRole;
            inspMaterial.Text = item.MaterialName;
            inspArea.Text = $"{item.AreaM2:0.00} m²";
            inspVolume.Text = $"{item.VolumeM3:0.0000} m³";
            inspComments.Text = !string.IsNullOrEmpty(item.Comments) ? item.Comments : "Sin comentarios";
        }

        private void gridElements_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            ScannedElementItem? selected = gridElements.SelectedItem as ScannedElementItem;
            UpdateInspector(selected);
            UpdateStatusLabel();
        }

        private void btnScanProject_Click(object sender, RoutedEventArgs e)
        {
            ScanElements(activeViewOnly: false);
        }

        private void btnScanActiveView_Click(object sender, RoutedEventArgs e)
        {
            ScanElements(activeViewOnly: true);
        }

        private void btnPickInRevit_Click(object sender, RoutedEventArgs e)
        {
            RunInRevitContext(app =>
            {
                var uidoc = app.ActiveUIDocument;
                var doc = uidoc?.Document;
                if (uidoc == null || doc == null) return;

                _uidoc = uidoc;
                _doc = doc;

                try
                {
                    this.WindowState = WindowState.Minimized;

                    IList<Reference> selectedRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        "Selecciona los elementos que deseas asignar a categorías de sistema.");

                    this.WindowState = WindowState.Normal;
                    this.Activate();

                    if (selectedRefs != null && selectedRefs.Count > 0)
                    {
                        List<ElementId> ids = selectedRefs.Select(r => r.ElementId).ToList();
                        var pickedElements = ids.Select(id => doc.GetElement(id)).Where(elem => elem != null).ToList();

                        foreach (var elem in pickedElements)
                        {
                            var match = _allItems.FirstOrDefault(i => i.Id == elem.Id);
                            if (match != null)
                            {
                                match.IsSelected = true;
                            }
                            else
                            {
                                Category? cat = elem.Category;
                                BuiltInCategory bic = cat != null ? cat.BuiltInCategory : BuiltInCategory.INVALID;

                                _allItems.Insert(0, new ScannedElementItem
                                {
                                    Id = elem.Id,
                                    Name = elem.Name ?? "",
                                    ElementTypeKind = elem is DirectShape ? "DirectShape" : (elem is FamilyInstance ? "FamilyInstance" : elem.GetType().Name),
                                    CurrentCategoryName = cat?.Name ?? "Desconocido",
                                    CurrentBuiltInCategory = bic,
                                    Comments = elem.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? "",
                                    MaterialName = "Por Selección",
                                    LevelName = elem.LevelId != ElementId.InvalidElementId ? (doc.GetElement(elem.LevelId)?.Name ?? "Sin Nivel") : "Sin Nivel",
                                    LevelId = elem.LevelId,
                                    IsPluginElement = true,
                                    PluginRole = "Seleccionado en Modelo",
                                    ElementRef = elem,
                                    IsSelected = true
                                });
                            }
                        }

                        ApplyFilter();
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    this.WindowState = WindowState.Normal;
                    this.Activate();
                }
                catch (Exception ex)
                {
                    this.WindowState = WindowState.Normal;
                    this.Activate();
                    System.Windows.MessageBox.Show($"Error al seleccionar en Revit: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
        }

        private void chkOnlyPlugin_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            ApplyFilter();
        }

        private void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isLoaded) return;
            ApplyFilter();
        }

        private void btnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _displayedItems) item.IsSelected = true;
            gridElements.Items.Refresh();
            UpdateStatusLabel();
        }

        private void btnUnselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _displayedItems) item.IsSelected = false;
            gridElements.Items.Refresh();
            UpdateStatusLabel();
        }

        private void btnCreateFloorSchedule_Click(object sender, RoutedEventArgs e)
        {
            RunInRevitContext(app =>
            {
                var doc = app.ActiveUIDocument?.Document ?? _doc;
                if (doc == null) return;
                try
                {
                    using (Transaction t = new Transaction(doc, "Crear Tabla de Planificación de Suelos"))
                    {
                        t.Start();
                        var sched = CategoryAssignmentService.CreateFloorSchedule(doc);
                        t.Commit();

                        if (sched != null)
                        {
                            System.Windows.MessageBox.Show($"Tabla creada exitosamente:\n'{sched.Name}'\n\nDisponible en el Project Browser > Tablas de Planificación / Cantidades.", "Tabla de Suelos Creada", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error al crear tabla de suelos: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
        }

        private void cmbTargetCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            if (cmbTargetCategory?.SelectedItem is SystemCategoryItem selCat)
            {
                if (lblCategoryDescription != null) lblCategoryDescription.Text = selCat.Description;

                if (selCat.Category != BuiltInCategory.OST_Floors)
                {
                    if (rbDirectShape != null) rbDirectShape.IsChecked = true;
                    if (rbNativeFloor != null) rbNativeFloor.IsEnabled = false;
                }
                else
                {
                    if (rbNativeFloor != null) rbNativeFloor.IsEnabled = true;
                }
            }
        }

        private void rbMethod_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            if (gridFloorTypeSelector != null && rbNativeFloor != null)
            {
                gridFloorTypeSelector.Visibility = (rbNativeFloor.IsChecked == true) 
                    ? System.Windows.Visibility.Visible 
                    : System.Windows.Visibility.Collapsed;
            }
        }

        private void btnHighlightInRevit_Click(object sender, RoutedEventArgs e)
        {
            HighlightSelectedElement();
        }

        private void gridElements_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            HighlightSelectedElement();
        }

        private void HighlightSelectedElement()
        {
            ScannedElementItem? selected = gridElements.SelectedItem as ScannedElementItem;
            if (selected == null) return;

            RunInRevitContext(app =>
            {
                try
                {
                    var uidoc = app.ActiveUIDocument ?? _uidoc;
                    if (uidoc != null)
                    {
                        uidoc.Selection.SetElementIds(new List<ElementId> { selected.Id });
                        uidoc.ShowElements(selected.Id);
                    }
                }
                catch { }
            });

            UpdateInspector(selected);
        }

        private void btnApplyCategory_Click(object sender, RoutedEventArgs e)
        {
            var itemsToProcess = _displayedItems.Where(i => i.IsSelected).ToList();
            if (itemsToProcess.Count == 0)
            {
                System.Windows.MessageBox.Show("Por favor selecciona al menos un elemento de la lista para asignar categoría.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!(cmbTargetCategory.SelectedItem is SystemCategoryItem targetCatItem))
            {
                System.Windows.MessageBox.Show("Por favor selecciona la Categoría de Sistema de destino.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool isDirectShapeMode = rbDirectShape.IsChecked == true;
            bool preserveMeta = chkPreserveMetadata.IsChecked == true;
            bool deleteOriginals = chkDeleteOriginals.IsChecked == true;
            FloorType? selFloorType = cmbFloorTypes.SelectedItem as FloorType;

            if (!isDirectShapeMode && selFloorType == null)
            {
                System.Windows.MessageBox.Show("Selecciona un Tipo de Piso (FloorType) para generar los suelos nativos.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            btnApplyCategory.IsEnabled = false;

            RunInRevitContext(app =>
            {
                var uidoc = app.ActiveUIDocument ?? _uidoc;
                var doc = uidoc?.Document ?? _doc;

                if (doc == null)
                {
                    btnApplyCategory.IsEnabled = true;
                    return;
                }

                try
                {
                    CategoryAssignmentResult result;

                    // MODO 1: Reasignar Categoría de Sistema (DirectShape)
                    if (isDirectShapeMode)
                    {
                        result = CategoryAssignmentService.ReassignCategoryToElements(
                            doc,
                            itemsToProcess,
                            targetCatItem.Category,
                            preserveMeta);
                    }
                    // MODO 2: Convertir a Suelo Nativo (Floor)
                    else
                    {
                        Level? fallbackLevel = new FilteredElementCollector(doc)
                            .OfClass(typeof(Level))
                            .Cast<Level>()
                            .FirstOrDefault();

                        ElementId fallbackLevelId = fallbackLevel?.Id ?? ElementId.InvalidElementId;

                        result = CategoryAssignmentService.ConvertToNativeFloors(
                            doc,
                            itemsToProcess,
                            selFloorType!.Id,
                            fallbackLevelId,
                            deleteOriginals);
                    }

                    // Mostrar reporte
                    string message = $"Proceso de Asignación Completado:\n\n" +
                                     $"• Elementos convertidos con éxito: {result.SuccessCount}\n" +
                                     $"• Elementos con advertencias/errores: {result.ErrorCount}\n" +
                                     $"• Categoría asignada: {targetCatItem.DisplayName}\n\n" +
                                     $"Los elementos ahora adquieren las propiedades, visibilidad y cómputos de la categoría {targetCatItem.DisplayName} en Revit.";

                    if (result.Messages.Count > 0)
                    {
                        message += "\n\nDetalles:\n" + string.Join("\n", result.Messages.Take(5));
                    }

                    System.Windows.MessageBox.Show(message, "Resultado de Asignación", MessageBoxButton.OK, MessageBoxImage.Information);

                    // Re-escanear elementos para actualizar la lista
                    ScanElements(activeViewOnly: false);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error durante la asignación: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    btnApplyCategory.IsEnabled = true;
                }
            });
        }
    }
}
