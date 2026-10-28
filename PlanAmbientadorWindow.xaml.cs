using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitView = Autodesk.Revit.DB.View;
using RevitColor = Autodesk.Revit.DB.Color;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace RevitToSupabasePlugin
{
    public partial class PlanAmbientadorWindow : Window
    {
        private readonly UIDocument _uidoc;
        private readonly Document _doc;
        private RevitView? _activeView;
        private AssemblyInstance? _activeAssembly;
        private FamilyInstance? _activeDoor;
        private List<DoorAssemblyViewModel> _allAssemblies = new List<DoorAssemblyViewModel>();
        private List<DimensionType> _dimTypes = new List<DimensionType>();

        public PlanAmbientadorWindow(UIDocument uidoc)
        {
            InitializeComponent();
            _uidoc = uidoc;
            _doc = uidoc.Document;

            LoadDimensionStyles();
            LoadActiveViewInfo();
            LoadAssembliesList();
        }

        private void LoadDimensionStyles()
        {
            try
            {
                _dimTypes = DoorAssemblyDimensionService.GetLinearDimensionTypes(_doc);
                cmbDimensionType.ItemsSource = _dimTypes;
                if (_dimTypes.Count > 0)
                {
                    cmbDimensionType.SelectedIndex = 0;
                }
            }
            catch { }
        }

        private void LoadActiveViewInfo()
        {
            try
            {
                _activeView = _doc.ActiveView;
                _activeAssembly = null;
                _activeDoor = null;

                if (_activeView == null)
                {
                    txtViewName.Text = "No hay vista activa";
                    txtStatus.Text = "⚠️ No hay vista activa en Revit.";
                    return;
                }

                txtViewName.Text = _activeView.Name;
                txtViewScale.Text = $"1:{_activeView.Scale}";

                // Verificar si es vista de ensamblaje
                if (_activeView.IsAssemblyView && _activeView.AssociatedAssemblyInstanceId != ElementId.InvalidElementId)
                {
                    _activeAssembly = _doc.GetElement(_activeView.AssociatedAssemblyInstanceId) as AssemblyInstance;
                }

                // Buscar puerta en la vista o ensamblaje
                if (_activeAssembly != null)
                {
                    foreach (var mId in _activeAssembly.GetMemberIds())
                    {
                        var elem = _doc.GetElement(mId);
                        if (elem is FamilyInstance fi && fi.Category != null &&
                            fi.Category.Id == new ElementId(BuiltInCategory.OST_Doors))
                        {
                            _activeDoor = fi;
                            break;
                        }
                    }

                    string assName = _activeAssembly.AssemblyTypeName ?? _activeAssembly.Name;
                    txtViewAssembly.Text = assName;
                    txtViewBadge.Text = "🎯 VISTA DE ENSAMBLAJE DE PUERTA:";
                    borderActiveView.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(5, 150, 105)); // Verde
                }
                else
                {
                    // Buscar puertas visibles en la vista
                    _activeDoor = new FilteredElementCollector(_doc, _activeView.Id)
                        .OfCategory(BuiltInCategory.OST_Doors)
                        .WhereElementIsNotElementType()
                        .Cast<FamilyInstance>()
                        .FirstOrDefault();

                    if (_activeDoor != null)
                    {
                        txtViewAssembly.Text = "Puerta visible en vista (Sin Ensamblaje)";
                        txtViewBadge.Text = "🔍 VISTA CON PUERTA DETECTADA:";
                        borderActiveView.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(15, 67, 105));
                    }
                    else
                    {
                        txtViewAssembly.Text = "Ninguno (Vista General)";
                        txtViewBadge.Text = "📍 VISTA ACTIVA GENERAL:";
                        borderActiveView.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(208, 213, 221));
                    }
                }

                if (_activeDoor != null)
                {
                    var orient = DoorAssemblyDimensionService.DetectViewOrientation(_activeView, _activeDoor);
                    txtViewOrientation.Text = GetOrientationDisplayName(orient);
                    txtViewDoorDims.Text = GetDoorDimensionsString(_activeDoor);
                    btnQuickAcotarVista.IsEnabled = true;
                    btnAcotarVista.IsEnabled = true;
                    btnAcotarEnsamblajeActivo.IsEnabled = _activeAssembly != null;
                    txtStatus.Text = $"Vista lista: '{_activeView.Name}'. Pulsa '⚡ Acotar Esta Vista Ahora' para generar las cotas.";
                }
                else
                {
                    txtViewOrientation.Text = _activeView.ViewType.ToString();
                    txtViewDoorDims.Text = "-";
                    btnQuickAcotarVista.IsEnabled = false;
                    btnAcotarVista.IsEnabled = false;
                    btnAcotarEnsamblajeActivo.IsEnabled = false;
                    txtStatus.Text = "💡 La vista activa no contiene puertas ni es de ensamblaje. Selecciona un ensamblaje del listado inferior para acotar sus vistas.";
                }
            }
            catch (Exception ex)
            {
                txtStatus.Text = "Error al leer vista activa: " + ex.Message;
            }
        }

        private static string GetOrientationDisplayName(DoorAssemblyViewOrientation orient)
        {
            switch (orient)
            {
                case DoorAssemblyViewOrientation.ElevationFront: return "Elevación Frontal (Alzado)";
                case DoorAssemblyViewOrientation.ElevationBack: return "Elevación Posterior";
                case DoorAssemblyViewOrientation.HorizontalDetail: return "Planta de Detalle (Horizontal)";
                case DoorAssemblyViewOrientation.SectionSide: return "Sección Lateral / Perfil";
                case DoorAssemblyViewOrientation.ThreeD: return "Vista 3D";
                case DoorAssemblyViewOrientation.Schedule: return "Tabla de Despiece";
                default: return "Vista Técnica";
            }
        }

        private string GetDoorDimensionsString(FamilyInstance d)
        {
            try
            {
                var pW = d.Symbol?.get_Parameter(BuiltInParameter.DOOR_WIDTH) ?? d.get_Parameter(BuiltInParameter.DOOR_WIDTH);
                var pH = d.Symbol?.get_Parameter(BuiltInParameter.DOOR_HEIGHT) ?? d.get_Parameter(BuiltInParameter.DOOR_HEIGHT);

                if (pW != null && pH != null)
                {
                    double w = pW.AsDouble() * 0.3048;
                    double h = pH.AsDouble() * 0.3048;
                    return $"{w:0.00} x {h:0.00} m";
                }
            }
            catch { }
            return "-";
        }

        private void LoadAssembliesList()
        {
            try
            {
                _allAssemblies = DoorAssemblyDimensionService.GetDoorAssemblies(_doc);
                ApplyFilter();
                txtAssemblyCount.Text = $"({_allAssemblies.Count} encontrados)";
            }
            catch (Exception ex)
            {
                txtStatus.Text = "Error al cargar ensamblajes: " + ex.Message;
            }
        }

        private void ApplyFilter()
        {
            string q = txtSearchAssembly?.Text?.Trim().ToLowerInvariant() ?? "";
            if (string.IsNullOrEmpty(q))
            {
                dgAssemblies.ItemsSource = _allAssemblies.ToList();
            }
            else
            {
                dgAssemblies.ItemsSource = _allAssemblies.Where(a =>
                    a.AssemblyName.ToLowerInvariant().Contains(q) ||
                    a.FamilyName.ToLowerInvariant().Contains(q) ||
                    a.TypeName.ToLowerInvariant().Contains(q) ||
                    a.Mark.ToLowerInvariant().Contains(q)).ToList();
            }
        }

        private DoorDimensionOptions GetConfiguredOptions()
        {
            double paperOffset = 10.0;
            if (cmbPaperOffset != null && cmbPaperOffset.SelectedIndex >= 0)
            {
                switch (cmbPaperOffset.SelectedIndex)
                {
                    case 0: paperOffset = 8.0; break;
                    case 1: paperOffset = 10.0; break;
                    case 2: paperOffset = 12.0; break;
                    case 3: paperOffset = 15.0; break;
                }
            }

            ElementId? dimTypeId = null;
            if (cmbDimensionType?.SelectedItem is DimensionType dt)
            {
                dimTypeId = dt.Id;
            }

            return new DoorDimensionOptions
            {
                DimTotalWidth = chkDimTotalWidth?.IsChecked ?? true,
                DimTotalHeight = chkDimTotalHeight?.IsChecked ?? true,
                DimJambsAndOpening = chkDimJambs?.IsChecked ?? true,
                DimClearHeight = chkDimClearHeight?.IsChecked ?? true,
                DimFrameDepth = chkDimFrameDepth?.IsChecked ?? true,
                ReplaceExistingDimensions = chkReplaceExisting?.IsChecked ?? true,
                PaperOffsetMm = paperOffset,
                DimensionTypeId = dimTypeId
            };
        }

        #region Eventos de Botones

        private void BtnRefreshView_Click(object sender, RoutedEventArgs e)
        {
            LoadActiveViewInfo();
            LoadAssembliesList();
        }

        private void BtnAcotarVistaActiva_Click(object sender, RoutedEventArgs e)
        {
            if (_activeView == null)
            {
                TaskDialog.Show("Aviso", "No hay ninguna vista activa.");
                return;
            }

            try
            {
                var opts = GetConfiguredOptions();
                var (created, logs) = DoorAssemblyDimensionService.DimensionView(_doc, _activeView, null, opts);

                _uidoc.RefreshActiveView();

                string resumen = string.Join("\n• ", logs);
                txtStatus.Text = $"✅ Acotado completado en '{_activeView.Name}': {created} cota(s) creadas.";
                TaskDialog.Show("Ambientador de Cotas",
                    $"Proceso completado en la vista '{_activeView.Name}':\n\n• Cotas generadas: {created}\n\nDetalles:\n• " + resumen);
            }
            catch (Exception ex)
            {
                txtStatus.Text = "Error al acotar vista: " + ex.Message;
                TaskDialog.Show("Error", "Ocurrió un error al acotar la vista: " + ex.Message);
            }
        }

        private void BtnAcotarEnsamblajeActivo_Click(object sender, RoutedEventArgs e)
        {
            if (_activeAssembly == null)
            {
                TaskDialog.Show("Aviso", "La vista activa no pertenece a ningún ensamblaje de puerta.");
                return;
            }

            try
            {
                var opts = GetConfiguredOptions();
                var (created, logs) = DoorAssemblyDimensionService.DimensionAssembly(_doc, _activeAssembly, null, opts);

                _uidoc.RefreshActiveView();

                string aName = _activeAssembly.AssemblyTypeName ?? _activeAssembly.Name;
                txtStatus.Text = $"✅ Ensamblaje '{aName}' acotado: {created} cota(s) generadas en todas sus vistas.";
                TaskDialog.Show("Ambientador de Cotas",
                    $"Ensamblaje '{aName}' acotado con éxito:\n\n• Total de cotas creadas: {created}\n\nDetalles:\n• " + string.Join("\n• ", logs.Take(15)));
            }
            catch (Exception ex)
            {
                txtStatus.Text = "Error al acotar ensamblaje: " + ex.Message;
                TaskDialog.Show("Error", "Error al acotar ensamblaje: " + ex.Message);
            }
        }

        private void BtnAcotarBatch_Click(object sender, RoutedEventArgs e)
        {
            var selected = _allAssemblies.Where(a => a.IsSelected).ToList();
            if (selected.Count == 0)
            {
                TaskDialog.Show("Aviso", "No has seleccionado ningún ensamblaje del listado para acotar.");
                return;
            }

            try
            {
                var assembliesToProcess = new List<AssemblyInstance>();
                foreach (var item in selected)
                {
                    if (_doc.GetElement(item.AssemblyId) is AssemblyInstance ai)
                    {
                        assembliesToProcess.Add(ai);
                    }
                }

                var opts = GetConfiguredOptions();
                var (created, logs) = DoorAssemblyDimensionService.DimensionBatchDoorAssemblies(_doc, assembliesToProcess, null, opts);

                _uidoc.RefreshActiveView();

                txtStatus.Text = $"✅ Proceso Batch completado: {created} cotas generadas en {assembliesToProcess.Count} ensamblajes.";
                TaskDialog.Show("Acotado Batch Completado",
                    $"¡Se han acotado las vistas técnicas en lote!\n\n• Ensamblajes procesados: {assembliesToProcess.Count}\n• Cotas generadas: {created}\n\nPuedes revisar las vistas en el Navegador de Proyectos.");
            }
            catch (Exception ex)
            {
                txtStatus.Text = "Error en proceso Batch: " + ex.Message;
                TaskDialog.Show("Error", "Error durante el acotado batch: " + ex.Message);
            }
        }

        private void BtnLimpiarCotas_Click(object sender, RoutedEventArgs e)
        {
            if (_activeView == null) return;

            try
            {
                using (Transaction t = new Transaction(_doc, "Limpiar Cotas de Puerta en Vista"))
                {
                    t.Start();
                    int deleted = 0;
                    var dims = new FilteredElementCollector(_doc, _activeView.Id)
                        .OfClass(typeof(Dimension))
                        .Cast<Dimension>()
                        .Where(d => !d.IsLocked)
                        .ToList();

                    foreach (var d in dims)
                    {
                        try { _doc.Delete(d.Id); deleted++; } catch { }
                    }

                    t.Commit();
                    _uidoc.RefreshActiveView();
                    txtStatus.Text = $"Se eliminaron {deleted} cota(s) de la vista activa '{_activeView.Name}'.";
                }
            }
            catch (Exception ex)
            {
                txtStatus.Text = "Error al limpiar cotas: " + ex.Message;
            }
        }

        private void TxtSearchAssembly_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void BtnSelectAllAssemblies_Click(object sender, RoutedEventArgs e)
        {
            foreach (var a in _allAssemblies) a.IsSelected = true;
            ApplyFilter();
        }

        private void BtnDeselectAllAssemblies_Click(object sender, RoutedEventArgs e)
        {
            foreach (var a in _allAssemblies) a.IsSelected = false;
            ApplyFilter();
        }

        private void BtnCerrar_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        #endregion

        #region Pestaña Opcional de Ambientación Gráfica

        private void BtnSoloEstilo_Click(object sender, RoutedEventArgs e)
        {
            if (_activeView == null) return;

            try
            {
                using (Transaction t = new Transaction(_doc, "Ambientación Gráfica de Planta"))
                {
                    t.Start();
                    var report = new List<string>();

                    // Sombras
                    try
                    {
                        if (chkAmbientShadows.IsChecked == true)
                        if (chkCastShadows.IsChecked == true)
                            _activeView.get_Parameter(BuiltInParameter.GRAPHIC_DISPLAY_OPTIONS_SHADOWS)?.Set(1);
                        report.Add("Sombras suaves calibradas.");
                    }
                    catch { }

                    // Mayúsculas en rooms
                    if (chkUppercaseRooms.IsChecked == true && _activeView is ViewPlan vp && vp.GenLevel != null)
                    {
                        var rooms = new FilteredElementCollector(_doc)
                            .OfCategory(BuiltInCategory.OST_Rooms)
                            .WherePasses(new ElementLevelFilter(vp.GenLevel.Id))
                            .WhereElementIsNotElementType()
                            .ToList();

                        int ren = 0;
                        foreach (var r in rooms)
                        {
                            var pName = r.get_Parameter(BuiltInParameter.ROOM_NAME);
                            if (pName != null && !string.IsNullOrEmpty(pName.AsString()))
                            {
                                string u = pName.AsString().ToUpperInvariant();
                                if (u != pName.AsString()) { pName.Set(u); ren++; }
                            }
                        }
                        if (ren > 0) report.Add($"Nombres de {ren} recintos en MAYÚSCULAS.");
                    }

                    t.Commit();
                    _uidoc.RefreshActiveView();
                    txtStatus.Text = "Estilo gráfico aplicado a la planta activa.";
                    TaskDialog.Show("Estilo Gráfico", "Acciones aplicadas:\n\n• " + string.Join("\n• ", report));
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", "No se pudo aplicar estilo gráfico: " + ex.Message);
            }
        }

        #endregion
    }
}
