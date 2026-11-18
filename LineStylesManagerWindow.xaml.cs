using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using RevitColor = Autodesk.Revit.DB.Color;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace RevitToSupabasePlugin
{
    public class CurveElementSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) => elem is CurveElement;
        public bool AllowReference(Reference reference, XYZ position) => false;
    }

    public partial class LineStylesManagerWindow : Window
    {
        private readonly UIDocument _uidoc;
        private readonly Document _doc;

        private List<LineStyleItem> _allStyles = new List<LineStyleItem>();
        private List<LinePatternOption> _allPatterns = new List<LinePatternOption>();
        private LineStyleItem? _selectedStyle;
        private byte _currentR = 0;
        private byte _currentG = 0;
        private byte _currentB = 0;
        private bool _isUpdatingWeightControls = false;
        private bool _isInitialized = false;

        public LineStylesManagerWindow(UIDocument uidoc)
        {
            InitializeComponent();
            _uidoc = uidoc ?? throw new ArgumentNullException(nameof(uidoc));
            _doc = uidoc.Document;

            for (int i = 1; i <= 16; i++)
            {
                cmbWeight.Items.Add(i);
            }
            cmbWeight.SelectedIndex = 0;

            LoadPatterns();
            LoadStyles();
            UpdateSelectionInfo();
            UpdatePrintBanner();
            _isInitialized = true;
        }

        private void LoadPatterns()
        {
            _allPatterns = LineStylesService.GetAllLinePatterns(_doc);
            cmbPattern.ItemsSource = _allPatterns;
            if (_allPatterns.Count > 0)
                cmbPattern.SelectedIndex = 0;
        }

        private void LoadStyles(ElementId? selectCategoryId = null)
        {
            _allStyles = LineStylesService.GetAllLineStyles(_doc);
            cmbSourceStyle.ItemsSource = _allStyles;
            if (_allStyles.Count > 0 && cmbSourceStyle.SelectedIndex < 0)
                cmbSourceStyle.SelectedIndex = 0;

            ApplyFilter(selectCategoryId);
        }

        private void ApplyFilter(ElementId? selectCategoryId = null)
        {
            if (dgStyles == null || txtSearch == null || cmbFilter == null) return;

            string query = txtSearch.Text?.Trim() ?? string.Empty;
            int filterIdx = cmbFilter.SelectedIndex;

            IEnumerable<LineStyleItem> filtered = _allStyles;

            if (!string.IsNullOrEmpty(query))
            {
                filtered = filtered.Where(s => s.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            if (filterIdx == 1)
                filtered = filtered.Where(s => s.IsNonPrintable);
            else if (filterIdx == 2)
                filtered = filtered.Where(s => !s.IsBuiltIn);
            else if (filterIdx == 3)
                filtered = filtered.Where(s => s.IsBuiltIn);

            var list = filtered.ToList();
            dgStyles.ItemsSource = list;

            if (selectCategoryId != null && selectCategoryId != ElementId.InvalidElementId)
            {
                var match = list.FirstOrDefault(s => s.CategoryId == selectCategoryId);
                if (match != null)
                {
                    dgStyles.SelectedItem = match;
                    dgStyles.ScrollIntoView(match);
                }
            }
            else if (list.Count > 0 && dgStyles.SelectedItem == null)
            {
                dgStyles.SelectedIndex = 0;
            }
        }

        private void UpdateSelectionInfo()
        {
            try
            {
                var selIds = _uidoc.Selection.GetElementIds();
                int lineCount = selIds.Count(id => _doc.GetElement(id) is CurveElement);
                txtSelectionInfo.Text = $"Selección actual en Revit: {lineCount} línea(s)";
            }
            catch
            {
                txtSelectionInfo.Text = "Selección actual en Revit: 0 línea(s)";
            }
        }

        private void UpdatePrintBanner()
        {
            try
            {
                var st = LineStylesService.GetPrintModeStatus(_doc);
                txtPrintModeDetail.Text = $"{st.VisibleNonPrintLines} línea(s) auxiliar(es) visibles | {st.HiddenNonPrintLines} oculta(s) para impresión";

                if (st.IsPrintModeActive)
                {
                    txtPrintModeTitle.Text = "🖨️ MODO IMPRESIÓN ACTIVO: Líneas auxiliares ocultas (Cotas intactas)";
                    txtPrintModeTitle.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 83, 9));
                    borderPrintBanner.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 251, 235));
                    borderPrintBanner.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11));
                }
                else
                {
                    txtPrintModeTitle.Text = "👁️ MODO TRABAJO: Líneas auxiliares visibles en pantalla";
                    txtPrintModeTitle.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(15, 67, 105));
                    borderPrintBanner.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255));
                    borderPrintBanner.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(216, 210, 199));
                }
            }
            catch { }
        }

        private void SetCurrentColor(byte r, byte g, byte b)
        {
            _currentR = r;
            _currentG = g;
            _currentB = b;
            rectColorPreview.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
            txtColorRgb.Text = $"RGB({r}, {g}, {b})";
        }

        private void DgStyles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedStyle = dgStyles.SelectedItem as LineStyleItem;
            if (_selectedStyle == null) return;

            txtStyleName.Text = _selectedStyle.Name;

            _isUpdatingWeightControls = true;
            sldWeight.Value = _selectedStyle.LineWeight;
            cmbWeight.SelectedItem = _selectedStyle.LineWeight;
            _isUpdatingWeightControls = false;

            SetCurrentColor(_selectedStyle.ColorR, _selectedStyle.ColorG, _selectedStyle.ColorB);

            var pat = _allPatterns.FirstOrDefault(p => p.Id == _selectedStyle.LinePatternId)
                      ?? _allPatterns.FirstOrDefault();
            cmbPattern.SelectedItem = pat;

            chkNonPrintable.IsChecked = _selectedStyle.IsNonPrintable;
            btnDeleteStyle.IsEnabled = !_selectedStyle.IsBuiltIn;
        }

        private void SldWeight_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingWeightControls || cmbWeight == null || sldWeight == null) return;
            _isUpdatingWeightControls = true;
            cmbWeight.SelectedItem = (int)Math.Round(sldWeight.Value);
            _isUpdatingWeightControls = false;
        }

        private void CmbWeight_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingWeightControls || sldWeight == null || cmbWeight == null) return;
            if (cmbWeight.SelectedItem is int w)
            {
                _isUpdatingWeightControls = true;
                sldWeight.Value = w;
                _isUpdatingWeightControls = false;
            }
        }

        private void BtnPickColor_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new System.Windows.Forms.ColorDialog())
            {
                dlg.Color = System.Drawing.Color.FromArgb(_currentR, _currentG, _currentB);
                dlg.FullOpen = true;
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    SetCurrentColor(dlg.Color.R, dlg.Color.G, dlg.Color.B);
                }
            }
        }

        private void BtnPresetColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string tag)
            {
                var parts = tag.Split(',');
                if (parts.Length == 3 &&
                    byte.TryParse(parts[0], out byte r) &&
                    byte.TryParse(parts[1], out byte g) &&
                    byte.TryParse(parts[2], out byte b))
                {
                    SetCurrentColor(r, g, b);
                }
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized) return;
            ApplyFilter(_selectedStyle?.CategoryId);
        }

        private void CmbFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            ApplyFilter(_selectedStyle?.CategoryId);
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadPatterns();
            LoadStyles(_selectedStyle?.CategoryId);
            UpdateSelectionInfo();
            UpdatePrintBanner();
            txtStatus.Text = "Lista de estilos actualizada.";
        }

        private void BtnSaveStyle_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedStyle == null)
            {
                TaskDialog.Show("Aviso", "Selecciona un estilo de la tabla para guardar cambios.");
                return;
            }

            try
            {
                int weight = (int)Math.Round(sldWeight.Value);
                var color = new RevitColor(_currentR, _currentG, _currentB);
                ElementId patId = (cmbPattern.SelectedItem as LinePatternOption)?.Id ?? LinePatternElement.GetSolidPatternId();
                bool nonPrint = chkNonPrintable.IsChecked == true;

                LineStylesService.UpdateLineStyle(_doc, _selectedStyle.CategoryId, weight, color, patId, nonPrint);

                ElementId savedId = _selectedStyle.CategoryId;
                LoadStyles(savedId);
                UpdatePrintBanner();
                txtStatus.Text = $"✅ Estilo '{_selectedStyle.Name}' actualizado (Calibre {weight}, {(nonPrint ? "No Imprimible" : "Imprimible")}).";
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error al Guardar", ex.Message);
            }
        }

        private void BtnCreateStyle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string name = txtStyleName.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    TaskDialog.Show("Nombre requerido", "Escribe un nombre para el nuevo estilo de línea.");
                    return;
                }

                int weight = (int)Math.Round(sldWeight.Value);
                var color = new RevitColor(_currentR, _currentG, _currentB);
                ElementId patId = (cmbPattern.SelectedItem as LinePatternOption)?.Id ?? LinePatternElement.GetSolidPatternId();
                bool nonPrint = chkNonPrintable.IsChecked == true;

                ElementId newId = LineStylesService.CreateLineStyle(_doc, name, weight, color, patId, nonPrint);
                LoadStyles(newId);
                UpdatePrintBanner();
                txtStatus.Text = $"✅ Estilo '{name}' creado exitosamente.";
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error al Crear Estilo", ex.Message);
            }
        }

        private void BtnCreateDefaultAux_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var existing = _allStyles.FirstOrDefault(s =>
                    string.Equals(s.Name, LineStylesService.DefaultAuxiliaryStyleName, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    if (!existing.IsNonPrintable)
                    {
                        LineStylesService.UpdateLineStyle(
                            _doc,
                            existing.CategoryId,
                            existing.LineWeight,
                            new RevitColor(existing.ColorR, existing.ColorG, existing.ColorB),
                            existing.LinePatternId,
                            true);
                    }
                    LoadStyles(existing.CategoryId);
                    UpdatePrintBanner();
                    txtStatus.Text = $"✨ El estilo '{LineStylesService.DefaultAuxiliaryStyleName}' ya existe y está seleccionado.";
                    return;
                }

                // Prefer a dashed pattern if available
                var dashPattern = _allPatterns.FirstOrDefault(p =>
                    !p.IsSolid &&
                    (p.Name.IndexOf("Dash", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     p.Name.IndexOf("Traz", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     p.Name.IndexOf("Hidden", StringComparison.OrdinalIgnoreCase) >= 0))
                    ?? _allPatterns.FirstOrDefault(p => !p.IsSolid)
                    ?? _allPatterns.First();

                ElementId newId = LineStylesService.CreateLineStyle(
                    _doc,
                    LineStylesService.DefaultAuxiliaryStyleName,
                    2,
                    new RevitColor(217, 70, 239), // Magenta visible
                    dashPattern.Id,
                    true);

                LoadStyles(newId);
                UpdatePrintBanner();
                txtStatus.Text = $"✨ Estilo '{LineStylesService.DefaultAuxiliaryStyleName}' creado y marcado como No Imprimible.";
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", ex.Message);
            }
        }

        private void BtnDeleteStyle_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedStyle == null || _selectedStyle.IsBuiltIn) return;

            var confirm = System.Windows.MessageBox.Show(
                $"¿Eliminar el estilo personalizado '{_selectedStyle.Name}'?\nLas líneas que usen este estilo se reasignarán a líneas finas.",
                "Confirmar Eliminación",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                string deletedName = _selectedStyle.Name;
                int reassigned = LineStylesService.DeleteLineStyle(_doc, _selectedStyle.CategoryId);
                LoadStyles();
                UpdatePrintBanner();
                txtStatus.Text = $"🗑️ Estilo '{deletedName}' eliminado. {reassigned} línea(s) reasignadas.";
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error al Eliminar", ex.Message);
            }
        }

        private void BtnPickLines_Click(object sender, RoutedEventArgs e)
        {
            Window? ownerWin = Owner;
            try
            {
                if (ownerWin != null) ownerWin.Hide();
                Hide();

                var refs = _uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new CurveElementSelectionFilter(),
                    "Selecciona las líneas en Revit y haz clic en Finalizar (arriba a la izquierda).");

                if (refs != null && refs.Count > 0)
                {
                    var ids = refs.Select(r => r.ElementId).ToList();
                    _uidoc.Selection.SetElementIds(ids);
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // Usuario canceló con ESC
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error de Selección", ex.Message);
            }
            finally
            {
                if (ownerWin != null) ownerWin.Show();
                Show();
                Activate();
                UpdateSelectionInfo();
            }
        }

        private void BtnConvertSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedStyle == null)
            {
                TaskDialog.Show("Aviso", "Selecciona primero el estilo de línea destino en la tabla.");
                return;
            }

            var selIds = _uidoc.Selection.GetElementIds();
            if (selIds == null || selIds.Count == 0)
            {
                TaskDialog.Show("Sin Selección", "No hay líneas seleccionadas en Revit. Usa 'Seleccionar Líneas en Revit...' primero.");
                return;
            }

            try
            {
                var (converted, skipped) = LineStylesService.ConvertElementsToLineStyle(
                    _doc, selIds, _selectedStyle.GraphicsStyleId);

                UpdatePrintBanner();
                txtStatus.Text = $"🔄 {converted} línea(s) convertidas a '{_selectedStyle.Name}'." +
                                 (skipped > 0 ? $" ({skipped} omitidas por estar en grupos/bloqueadas)." : "");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error al Convertir", ex.Message);
            }
        }

        private void BtnConvertBySourceInView_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedStyle == null)
            {
                TaskDialog.Show("Aviso", "Selecciona primero el estilo destino en la tabla.");
                return;
            }

            if (!(cmbSourceStyle.SelectedItem is LineStyleItem sourceStyle))
            {
                TaskDialog.Show("Aviso", "Selecciona el estilo origen en el desplegable.");
                return;
            }

            if (_doc.ActiveView == null)
            {
                TaskDialog.Show("Aviso", "No hay una vista activa.");
                return;
            }

            try
            {
                var (converted, skipped) = LineStylesService.ConvertBySourceStyle(
                    _doc, sourceStyle.GraphicsStyleId, _selectedStyle.GraphicsStyleId, _doc.ActiveView.Id);

                UpdatePrintBanner();
                txtStatus.Text = $"📋 Vista activa: {converted} línea(s) de '{sourceStyle.Name}' convertidas a '{_selectedStyle.Name}'." +
                                 (skipped > 0 ? $" ({skipped} en grupos omitidas)." : "");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error en Conversión Masiva", ex.Message);
            }
        }

        private void BtnSelectAllOfStyleInView_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedStyle == null || _doc.ActiveView == null) return;

            var ids = LineStylesService.FindLinesByStyle(_doc, _selectedStyle.GraphicsStyleId, _doc.ActiveView.Id);
            _uidoc.Selection.SetElementIds(ids);
            UpdateSelectionInfo();
            txtStatus.Text = $"🔍 {ids.Count} línea(s) de '{_selectedStyle.Name}' seleccionadas en la vista activa.";
        }

        private void BtnActivatePrintMode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var (hidden, skipped) = LineStylesService.ActivatePrintMode(_doc);
                UpdatePrintBanner();
                if (hidden == 0 && skipped == 0)
                {
                    txtStatus.Text = "ℹ️ No se encontraron líneas visibles con estilos marcados como No Imprimibles.";
                }
                else
                {
                    txtStatus.Text = $"🖨️ Modo Impresión activado: {hidden} línea(s) ocultadas como <Invisible lines>. Sus cotas siguen intactas." +
                                     (skipped > 0 ? $" ({skipped} en grupos omitidas)." : "");
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error al Activar Modo Impresión", ex.Message);
            }
        }

        private void BtnActivateWorkMode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var (restored, skipped) = LineStylesService.ActivateWorkMode(_doc);
                UpdatePrintBanner();
                if (restored == 0 && skipped == 0)
                {
                    txtStatus.Text = "ℹ️ No había líneas auxiliares ocultas para restaurar.";
                }
                else
                {
                    txtStatus.Text = $"👁️ Modo Trabajo activado: {restored} línea(s) auxiliares restauradas en pantalla." +
                                     (skipped > 0 ? $" ({skipped} omitidas)." : "");
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error al Activar Modo Trabajo", ex.Message);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
