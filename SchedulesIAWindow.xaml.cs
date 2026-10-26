using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MessageBox = System.Windows.MessageBox;
using WpfVisibility = System.Windows.Visibility;

namespace RevitToSupabasePlugin
{
    public class ScheduleFieldDisplayItem
    {
        public string Name { get; set; } = string.Empty;
        public string ColumnHeading { get; set; } = string.Empty;
        public bool IsSelected { get; set; } = true;
        public bool IsHidden { get; set; }

        public string DisplayName => string.IsNullOrEmpty(ColumnHeading) || ColumnHeading == Name
            ? Name
            : $"{ColumnHeading} [{Name}]";
    }

    public partial class SchedulesIAWindow : Window
    {
        private readonly Document _doc;
        private readonly UIDocument? _uidoc;
        private List<ScheduleListItemViewModel> _allSchedules = new List<ScheduleListItemViewModel>();
        private List<AssemblyItemViewModel> _allAssemblies = new List<AssemblyItemViewModel>();
        private ScheduleListItemViewModel? _selectedTemplate;
        private List<ScheduleFieldDisplayItem> _currentTemplateFields = new List<ScheduleFieldDisplayItem>();

        public SchedulesIAWindow(Document doc, UIDocument? uidoc = null)
        {
            InitializeComponent();
            _doc = doc;
            _uidoc = uidoc;

            RefreshAll();
        }

        private void RefreshAll()
        {
            LoadSchedules();
            LoadAssemblies();
            UpdateTemplateSelectionState();
        }

        #region 1. Carga y Filtro de Tablas

        private void LoadSchedules()
        {
            try
            {
                _allSchedules = AssemblyScheduleService.GetSchedulesList(_doc);
                ApplyScheduleFilter();
            }
            catch (Exception ex)
            {
                txtSelectedTemplateInfo.Text = $"Error al cargar tablas: {ex.Message}";
            }
        }

        private void ApplyScheduleFilter()
        {
            if (_allSchedules == null || lstSchedules == null) return;

            string filterText = txtSearchSchedules?.Text?.Trim().ToLowerInvariant() ?? "";

            var query = _allSchedules.AsEnumerable();

            // Filtro por alcance
            if (rbFilterProject != null && rbFilterProject.IsChecked == true)
            {
                query = query.Where(s => !s.IsAssemblyView);
            }
            else if (rbFilterAssembly != null && rbFilterAssembly.IsChecked == true)
            {
                query = query.Where(s => s.IsAssemblyView);
            }

            if (!string.IsNullOrEmpty(filterText))
            {
                query = query.Where(s =>
                    s.Name.ToLowerInvariant().Contains(filterText) ||
                    s.CategoryName.ToLowerInvariant().Contains(filterText) ||
                    s.ScheduleTypeDescription.ToLowerInvariant().Contains(filterText) ||
                    s.SheetPlacementsInfo.ToLowerInvariant().Contains(filterText));
            }

            var filtered = query.ToList();
            lstSchedules.ItemsSource = filtered;

            UpdateScheduleSelectionSummary();

            if (_selectedTemplate != null && filtered.Any(s => s.Id == _selectedTemplate.Id))
            {
                lstSchedules.SelectedItem = filtered.First(s => s.Id == _selectedTemplate.Id);
            }
        }

        private void chkScheduleItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.CheckBox cb && cb.DataContext is ScheduleListItemViewModel vm)
            {
                // Al marcar la casilla, seleccionar la tabla para ver inmediatamente sus campos e info
                lstSchedules.SelectedItem = vm;
                UpdateScheduleSelectionSummary();
            }
        }

        private void UpdateScheduleSelectionSummary()
        {
            if (txtScheduleCountSummary == null) return;

            int checkedCount = _allSchedules?.Count(s => s.IsChecked) ?? 0;
            int filteredCount = (lstSchedules?.ItemsSource as IEnumerable<ScheduleListItemViewModel>)?.Count() ?? (_allSchedules?.Count ?? 0);
            int totalCount = _allSchedules?.Count ?? 0;

            if (checkedCount > 0)
            {
                txtScheduleCountSummary.Text = $"☑️ {checkedCount} marcada(s) | {filteredCount} de {totalCount} tablas";
                txtScheduleCountSummary.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(15, 67, 105));
                txtScheduleCountSummary.FontWeight = FontWeights.Bold;
            }
            else
            {
                txtScheduleCountSummary.Text = $"{filteredCount} de {totalCount} tablas";
                txtScheduleCountSummary.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(114, 119, 127));
                txtScheduleCountSummary.FontWeight = FontWeights.Normal;
            }
        }

        private void rbTableScope_Checked(object sender, RoutedEventArgs e)
        {
            if (_allSchedules == null || lstSchedules == null) return;
            ApplyScheduleFilter();
        }

        private void txtSearchSchedules_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyScheduleFilter();
        }

        private void lstSchedules_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstSchedules.SelectedItem is ScheduleListItemViewModel item)
            {
                if (item.IsValid && _doc.GetElement(item.Id) != null)
                {
                    _selectedTemplate = item;
                    UpdateTemplateSelectionState();
                    LoadFieldsForTemplate(item.Schedule);
                }
                else
                {
                    _selectedTemplate = null;
                    UpdateTemplateSelectionState();
                }
            }
        }

        private void btnClearTemplate_Click(object sender, RoutedEventArgs e)
        {
            _selectedTemplate = null;
            lstSchedules.SelectedItem = null;
            UpdateTemplateSelectionState();
        }

        #region Gestión Activa de Tablas (Abrir, Duplicar, Renombrar, Eliminar)

        private void btnOpenSchedule_Click(object sender, RoutedEventArgs e)
        {
            if (_uidoc == null)
            {
                MessageBox.Show("No hay una sesión interactiva activa de Revit para abrir la vista.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var target = lstSchedules.SelectedItem as ScheduleListItemViewModel;
            if (target == null || !target.IsValid || _doc.GetElement(target.Id) == null)
            {
                MessageBox.Show("Por favor, selecciona una tabla válida en la lista para abrirla en Revit.", "Selección Requerida", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                AssemblyScheduleService.OpenScheduleInRevit(_uidoc, target.Schedule);
                txtAssemblyLog.Text = $"👁️ Abierta tabla '{target.Name}' en el viewport activo de Revit.\n{txtAssemblyLog.Text}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al abrir la tabla en Revit: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private List<ScheduleListItemViewModel> GetTargetSchedules()
        {
            var checkedItems = _allSchedules.Where(s => s.IsChecked && s.IsValid && _doc.GetElement(s.Id) != null).ToList();
            if (checkedItems.Count > 0)
            {
                return checkedItems;
            }

            var selectedItems = lstSchedules.SelectedItems.OfType<ScheduleListItemViewModel>()
                .Where(s => s.IsValid && _doc.GetElement(s.Id) != null).ToList();
            if (selectedItems.Count > 0)
            {
                return selectedItems;
            }

            if (lstSchedules.SelectedItem is ScheduleListItemViewModel single && single.IsValid && _doc.GetElement(single.Id) != null)
            {
                return new List<ScheduleListItemViewModel> { single };
            }

            return new List<ScheduleListItemViewModel>();
        }

        private List<ScheduleListItemViewModel>? _schedulesBeingDuplicated;
        private bool _syncFilterWithReplaceName = true;

        private void btnDuplicateSchedule_Click(object sender, RoutedEventArgs e)
        {
            var targets = GetTargetSchedules();
            if (targets.Count == 0)
            {
                MessageBox.Show("Por favor, selecciona o marca con la casilla al menos una tabla para duplicarla.", "Selección Requerida", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _schedulesBeingDuplicated = targets;

            txtDupModalTitle.Text = targets.Count == 1 
                ? "Duplicar Tabla de Planificación" 
                : $"Duplicar {targets.Count} Tablas de Planificación";

            lblDupTablesCount.Text = $"TABLAS SELECCIONADAS ({targets.Count}):";
            icDupSelectedTables.ItemsSource = targets.Select(t => $"• {t.Name}").ToList();

            // Analizar filtros existentes en las tablas seleccionadas
            var filtersInfo = AssemblyScheduleService.GetScheduleFiltersInfo(_doc, targets.Select(t => t.Schedule));
            if (filtersInfo.Count > 0)
            {
                txtDupDetectedFiltersSummary.Text = string.Join("\n", filtersInfo.Select(f => $"• {f.ScheduleName} ➔ [{f.FieldName}] {f.FilterTypeDisplay} '{f.CurrentValue}'"));
                
                string firstVal = filtersInfo[0].CurrentValue;
                txtDupFilterFind.Text = firstVal;

                if (targets.Any(t => t.Name.IndexOf(firstVal, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    txtDupFindName.Text = firstVal;
                }
                else
                {
                    txtDupFindName.Text = "";
                }

                txtDupReplaceName.Text = "";
                txtDupFilterReplace.Text = "";
                chkDupModifyFilters.IsChecked = true;
                if (pnlDupFilterFields != null)
                {
                    pnlDupFilterFields.IsEnabled = true;
                    pnlDupFilterFields.Opacity = 1.0;
                }
            }
            else
            {
                txtDupDetectedFiltersSummary.Text = "(Las tablas seleccionadas no tienen ningún filtro configurado en Revit).";
                txtDupFilterFind.Text = "";
                txtDupFilterReplace.Text = "";
                txtDupFindName.Text = "";
                txtDupReplaceName.Text = "";
                chkDupModifyFilters.IsChecked = false;
                if (pnlDupFilterFields != null)
                {
                    pnlDupFilterFields.IsEnabled = false;
                    pnlDupFilterFields.Opacity = 0.45;
                }
            }

            _syncFilterWithReplaceName = true;
            btnConfirmDuplicate.Content = targets.Count == 1 ? "📑 Duplicar Tabla" : $"📑 Duplicar {targets.Count} Tablas";
            pnlDuplicateModal.Visibility = WpfVisibility.Visible;
            txtDupReplaceName.Focus();
            txtDupReplaceName.SelectAll();
        }

        private void txtDupReplaceName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncFilterWithReplaceName && chkDupModifyFilters.IsChecked == true)
            {
                txtDupFilterReplace.Text = txtDupReplaceName.Text;
            }
        }

        private void txtDupFilterReplace_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (txtDupFilterReplace.IsFocused)
            {
                _syncFilterWithReplaceName = false;
            }
        }

        private void chkDupModifyFilters_Changed(object sender, RoutedEventArgs e)
        {
            if (pnlDupFilterFields != null)
            {
                pnlDupFilterFields.IsEnabled = chkDupModifyFilters.IsChecked == true;
                pnlDupFilterFields.Opacity = chkDupModifyFilters.IsChecked == true ? 1.0 : 0.45;
            }
        }

        private void btnCancelDuplicate_Click(object sender, RoutedEventArgs e)
        {
            pnlDuplicateModal.Visibility = WpfVisibility.Collapsed;
            _schedulesBeingDuplicated = null;
        }

        private void btnConfirmDuplicate_Click(object sender, RoutedEventArgs e)
        {
            if (_schedulesBeingDuplicated == null || _schedulesBeingDuplicated.Count == 0)
            {
                pnlDuplicateModal.Visibility = WpfVisibility.Collapsed;
                return;
            }

            string findName = txtDupFindName.Text?.Trim() ?? "";
            string replaceName = txtDupReplaceName.Text?.Trim() ?? "";
            bool modifyFilters = chkDupModifyFilters.IsChecked == true;
            string filterFind = txtDupFilterFind.Text?.Trim() ?? "";
            string filterReplace = txtDupFilterReplace.Text?.Trim() ?? "";

            pnlDuplicateModal.Visibility = WpfVisibility.Collapsed;

            try
            {
                var targetSchedules = _schedulesBeingDuplicated.Select(s => s.Schedule).ToList();
                var (duplicates, logs) = AssemblyScheduleService.DuplicateSchedules(
                    _doc, 
                    targetSchedules, 
                    findName, 
                    replaceName, 
                    modifyFilters, 
                    filterFind, 
                    filterReplace);

                if (duplicates.Count > 0)
                {
                    foreach (var log in logs)
                    {
                        txtAssemblyLog.Text = $"📑 {log}\n{txtAssemblyLog.Text}";
                    }

                    LoadSchedules();

                    // Marcar y enfocar las nuevas tablas duplicadas
                    var newIds = new HashSet<ElementId>(duplicates.Select(d => d.Id));
                    foreach (var s in _allSchedules)
                    {
                        s.IsChecked = newIds.Contains(s.Id);
                    }
                    var firstNew = _allSchedules.FirstOrDefault(s => newIds.Contains(s.Id));
                    if (firstNew != null)
                    {
                        lstSchedules.SelectedItem = firstNew;
                        lstSchedules.ScrollIntoView(firstNew);
                    }

                    string filterSummary = (modifyFilters && !string.IsNullOrEmpty(filterReplace))
                        ? $"\nFiltro asignado: '{filterReplace}'"
                        : "";

                    MessageBox.Show(
                        $"Se duplicaron {duplicates.Count} tabla(s) exitosamente:{filterSummary}\n\n" +
                        string.Join("\n", duplicates.Select(d => $"• {d.Name}")), 
                        "Tablas Duplicadas con Éxito", 
                        MessageBoxButton.OK, 
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("No se pudo duplicar ninguna de las tablas seleccionadas.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error durante la duplicación de tablas: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _schedulesBeingDuplicated = null;
            }
        }

        private ScheduleListItemViewModel? _scheduleBeingRenamed;

        private void btnRenameSchedule_Click(object sender, RoutedEventArgs e)
        {
            var target = lstSchedules.SelectedItem as ScheduleListItemViewModel;
            if (target == null || !target.IsValid || _doc.GetElement(target.Id) == null)
            {
                MessageBox.Show("Por favor, selecciona una tabla válida en la lista para renombrarla.", "Selección Requerida", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _scheduleBeingRenamed = target;
            txtRenameScheduleName.Text = target.Name;
            pnlRenameModal.Visibility = WpfVisibility.Visible;
            txtRenameScheduleName.Focus();
            txtRenameScheduleName.SelectAll();
        }

        private void btnCancelRename_Click(object sender, RoutedEventArgs e)
        {
            pnlRenameModal.Visibility = WpfVisibility.Collapsed;
            _scheduleBeingRenamed = null;
        }

        private void btnSaveRename_Click(object sender, RoutedEventArgs e)
        {
            if (_scheduleBeingRenamed == null || !_scheduleBeingRenamed.IsValid || _doc.GetElement(_scheduleBeingRenamed.Id) == null)
            {
                pnlRenameModal.Visibility = WpfVisibility.Collapsed;
                return;
            }

            string newName = txtRenameScheduleName.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(newName))
            {
                MessageBox.Show("El nombre no puede estar vacío.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (newName == _scheduleBeingRenamed.Name)
            {
                pnlRenameModal.Visibility = WpfVisibility.Collapsed;
                return;
            }

            var (success, msg) = AssemblyScheduleService.RenameSchedule(_doc, _scheduleBeingRenamed.Schedule, newName);
            if (success)
            {
                pnlRenameModal.Visibility = WpfVisibility.Collapsed;
                txtAssemblyLog.Text = $"✏️ {msg}\n{txtAssemblyLog.Text}";
                LoadSchedules();
            }
            else
            {
                MessageBox.Show(msg, "Error al Renombrar", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void btnDeleteSchedule_Click(object sender, RoutedEventArgs e)
        {
            var targetsToDelete = GetTargetSchedules();

            if (targetsToDelete.Count == 0)
            {
                MessageBox.Show("Por favor, selecciona o marca al menos una tabla para eliminar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Validar si alguna tabla está en lámina
            var onSheetTargets = targetsToDelete.Where(s => s.IsOnSheet).ToList();
            StringBuilder confirmMsg = new StringBuilder();

            if (targetsToDelete.Count == 1)
            {
                var single = targetsToDelete[0];
                confirmMsg.AppendLine($"¿Estás seguro de que deseas eliminar permanentemente la tabla '{single.Name}' del modelo?");
                if (single.IsOnSheet)
                {
                    confirmMsg.AppendLine($"\n⚠️ ADVERTENCIA: Esta tabla está colocada en la(s) lámina(s): {single.SheetPlacementsInfo}. Si la eliminas, desaparecerá de los planos correspondientes.");
                }
            }
            else
            {
                confirmMsg.AppendLine($"¿Estás seguro de que deseas eliminar permanentemente {targetsToDelete.Count} tablas de planificación del modelo?");
                if (onSheetTargets.Count > 0)
                {
                    confirmMsg.AppendLine($"\n⚠️ ADVERTENCIA: {onSheetTargets.Count} de las tablas seleccionadas están colocadas en láminas del proyecto:");
                    foreach (var os in onSheetTargets.Take(5))
                    {
                        confirmMsg.AppendLine($"• {os.Name} -> En lámina {os.SheetPlacementsInfo}");
                    }
                    if (onSheetTargets.Count > 5) confirmMsg.AppendLine($"• ... y {onSheetTargets.Count - 5} más.");
                }
            }

            confirmMsg.AppendLine("\nEsta acción no se puede deshacer.");

            var res = MessageBox.Show(confirmMsg.ToString(), "Confirmar Eliminación de Tablas", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes) return;

            try
            {
                var targetIds = targetsToDelete
                    .Select(t => t.Id)
                    .Where(id => id != null && id != ElementId.InvalidElementId)
                    .ToHashSet();

                // Si la plantilla activa seleccionada está entre las eliminadas, deseleccionarla antes de borrar
                if (_selectedTemplate != null && targetIds.Contains(_selectedTemplate.Id))
                {
                    _selectedTemplate = null;
                }

                // Limpiar selección previa en el ListBox para evitar reentradas de SelectionChanged
                lstSchedules.SelectedItem = null;

                var (count, msg) = AssemblyScheduleService.DeleteSchedules(_doc, targetIds);

                txtAssemblyLog.Text = $"🗑️ {msg}\n{txtAssemblyLog.Text}";

                LoadSchedules();
                LoadAssemblies(); // Para refrescar status de tablas existentes en ensamblajes
                UpdateTemplateSelectionState();

                MessageBox.Show(msg, "Eliminación Completada", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al eliminar tabla(s): {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnSelectAllSchedules_Click(object sender, RoutedEventArgs e)
        {
            if (lstSchedules.ItemsSource is IEnumerable<ScheduleListItemViewModel> items)
            {
                foreach (var s in items) s.IsChecked = true;
                lstSchedules.Items.Refresh();
                UpdateScheduleSelectionSummary();
            }
        }

        private void btnDeselectAllSchedules_Click(object sender, RoutedEventArgs e)
        {
            if (lstSchedules.ItemsSource is IEnumerable<ScheduleListItemViewModel> items)
            {
                foreach (var s in items) s.IsChecked = false;
                lstSchedules.Items.Refresh();
                UpdateScheduleSelectionSummary();
            }
        }

        #endregion

        private void UpdateTemplateSelectionState()
        {
            if (_selectedTemplate != null)
            {
                if (!_selectedTemplate.IsValid || _doc.GetElement(_selectedTemplate.Id) == null)
                {
                    _selectedTemplate = null;
                }
            }

            if (_selectedTemplate != null)
            {
                txtSelectedTemplateInfo.Text = $"📋 Plantilla activa: {_selectedTemplate.Name} ({_selectedTemplate.DisplaySubtitle})";
                txtSelectedTemplateInfo.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(6, 95, 70));
                pnlActiveTemplateBadge.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(5, 150, 105));
                pnlActiveTemplateBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(209, 250, 229));
                pnlStandardOptions.Visibility = WpfVisibility.Collapsed;
                pnlTemplateOptions.Visibility = WpfVisibility.Visible;
                grpFieldDetails.Visibility = WpfVisibility.Visible;
                btnExecute.Content = $"⚡ REPLICAR PLANTILLA '{_selectedTemplate.Name}' EN ENSAMBLAJES SELECCIONADOS";
            }
            else
            {
                txtSelectedTemplateInfo.Text = "📦 Modo activo: Generación Estándar Automática (Sin Plantilla). Crea tablas limpias y optimizadas para cada ensamble.";
                txtSelectedTemplateInfo.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(15, 67, 105));
                pnlActiveTemplateBadge.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(15, 67, 105));
                pnlActiveTemplateBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(209, 228, 249));
                pnlStandardOptions.Visibility = WpfVisibility.Visible;
                pnlTemplateOptions.Visibility = WpfVisibility.Collapsed;
                grpFieldDetails.Visibility = WpfVisibility.Collapsed;
                _currentTemplateFields.Clear();
                lstTemplateFields.ItemsSource = null;
                txtFieldsSummary.Text = "Sin plantilla seleccionada. Modo estándar creará campos esenciales automáticamente.";
                btnExecute.Content = "⚡ GENERAR TABLAS ESTÁNDAR EN ENSAMBLAJES SELECCIONADOS";
            }
        }

        private void LoadFieldsForTemplate(ViewSchedule schedule)
        {
            _currentTemplateFields.Clear();
            if (schedule == null || !schedule.IsValidObject || _doc.GetElement(schedule.Id) == null)
            {
                lstTemplateFields.ItemsSource = null;
                txtFieldsSummary.Text = "Tabla no disponible o eliminada.";
                return;
            }

            try
            {
                ScheduleDefinition def = schedule.Definition;
                var fieldOrder = def.GetFieldOrder();
                foreach (ScheduleFieldId fId in fieldOrder)
                {
                    ScheduleField sf = def.GetField(fId);
                    _currentTemplateFields.Add(new ScheduleFieldDisplayItem
                    {
                        Name = sf.GetName(),
                        ColumnHeading = sf.ColumnHeading,
                        IsSelected = true,
                        IsHidden = sf.IsHidden
                    });
                }
            }
            catch (Exception ex)
            {
                txtFieldsSummary.Text = $"Error al leer campos: {ex.Message}";
            }

            lstTemplateFields.ItemsSource = null;
            lstTemplateFields.ItemsSource = _currentTemplateFields;
            txtFieldsSummary.Text = $"{_currentTemplateFields.Count} campos disponibles ({_currentTemplateFields.Count(f => !f.IsHidden)} visibles en tabla).";
        }

        private void btnSelectAllFields_Click(object sender, RoutedEventArgs e)
        {
            foreach (var f in _currentTemplateFields) f.IsSelected = true;
            lstTemplateFields.Items.Refresh();
        }

        private void btnDeselectAllFields_Click(object sender, RoutedEventArgs e)
        {
            foreach (var f in _currentTemplateFields) f.IsSelected = false;
            lstTemplateFields.Items.Refresh();
        }

        #endregion

        #region 2. Carga y Filtro de Ensamblajes

        private void LoadAssemblies()
        {
            try
            {
                _allAssemblies = AssemblyScheduleService.GetAssemblyViewModels(_doc);
                ApplyAssemblyFilter();
            }
            catch (Exception ex)
            {
                txtAssembliesSummary.Text = $"Error al cargar ensamblajes: {ex.Message}";
            }
        }

        private void ApplyAssemblyFilter()
        {
            string filterText = txtSearchAssembly.Text?.Trim().ToLowerInvariant() ?? "";

            var filtered = string.IsNullOrEmpty(filterText)
                ? _allAssemblies
                : _allAssemblies.Where(a =>
                    a.TypeName.ToLowerInvariant().Contains(filterText) ||
                    a.Name.ToLowerInvariant().Contains(filterText) ||
                    a.CategoriesSummary.ToLowerInvariant().Contains(filterText)).ToList();

            lstAssemblies.ItemsSource = filtered;
            UpdateAssemblySummaryText();
        }

        private void txtSearchAssembly_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyAssemblyFilter();
        }

        private void btnSelectAllAssemblies_Click(object sender, RoutedEventArgs e)
        {
            if (lstAssemblies.ItemsSource is IEnumerable<AssemblyItemViewModel> items)
            {
                foreach (var item in items) item.IsSelected = true;
                lstAssemblies.Items.Refresh();
                UpdateAssemblySummaryText();
            }
        }

        private void btnDeselectAllAssemblies_Click(object sender, RoutedEventArgs e)
        {
            if (lstAssemblies.ItemsSource is IEnumerable<AssemblyItemViewModel> items)
            {
                foreach (var item in items) item.IsSelected = false;
                lstAssemblies.Items.Refresh();
                UpdateAssemblySummaryText();
            }
        }

        private void btnSelectWithoutSchedule_Click(object sender, RoutedEventArgs e)
        {
            if (lstAssemblies.ItemsSource is IEnumerable<AssemblyItemViewModel> items)
            {
                foreach (var item in items)
                {
                    item.IsSelected = !item.HasExistingSchedule;
                }
                lstAssemblies.Items.Refresh();
                UpdateAssemblySummaryText();
            }
        }

        private void btnSelectFromRevit_Click(object sender, RoutedEventArgs e)
        {
            if (_uidoc == null)
            {
                MessageBox.Show("No se detectó un documento activo interactivo para leer la selección.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var selIds = _uidoc.Selection.GetElementIds();
                if (selIds.Count == 0)
                {
                    MessageBox.Show("No hay elementos seleccionados en Revit.\nPor favor, selecciona ensamblajes o elementos en Revit y vuelve a presionar este botón.", "Selección Vacía", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                HashSet<ElementId> targetAssemblyIds = new HashSet<ElementId>();
                foreach (ElementId id in selIds)
                {
                    Element elem = _doc.GetElement(id);
                    if (elem is AssemblyInstance ai)
                    {
                        targetAssemblyIds.Add(ai.Id);
                    }
                    else if (elem != null && elem.AssemblyInstanceId != ElementId.InvalidElementId)
                    {
                        targetAssemblyIds.Add(elem.AssemblyInstanceId);
                    }
                }

                if (targetAssemblyIds.Count == 0)
                {
                    MessageBox.Show("Los elementos seleccionados en Revit no pertenecen a ningún Ensamblaje.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                foreach (var item in _allAssemblies)
                {
                    item.IsSelected = targetAssemblyIds.Contains(item.Id);
                }

                lstAssemblies.Items.Refresh();
                UpdateAssemblySummaryText();

                MessageBox.Show($"Se marcaron {targetAssemblyIds.Count} ensamblaje(s) correspondientes a la selección de Revit.", "Selección Actualizada", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al procesar la selección de Revit: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateAssemblySummaryText()
        {
            int total = _allAssemblies.Count;
            int selected = _allAssemblies.Count(a => a.IsSelected);
            int withSchedule = _allAssemblies.Count(a => a.HasExistingSchedule);
            txtAssembliesSummary.Text = total == 0
                ? "No se encontraron ensamblajes en el modelo."
                : $"{total} ensamblajes en el modelo | {withSchedule} ya tienen tabla | {selected} seleccionados para procesar";
        }

        #endregion

        #region 3. Generación y Replicación Ejecutiva

        private void btnExecute_Click(object sender, RoutedEventArgs e)
        {
            var selectedAssemblies = _allAssemblies.Where(a => a.IsSelected).ToList();
            if (selectedAssemblies.Count == 0)
            {
                MessageBox.Show("Por favor, selecciona al menos un ensamblaje en la lista para generar o replicar sus tablas.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StringBuilder logSb = new StringBuilder();
            logSb.AppendLine($"=== INICIO DE OPERACIÓN DE TABLAS DE ENSAMBLAJE ===");
            logSb.AppendLine($"Fecha/Hora: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            logSb.AppendLine($"Ensamblajes seleccionados: {selectedAssemblies.Count}");

            bool useTemplate = _selectedTemplate != null;
            HashSet<string>? allowedFields = null;

            if (useTemplate)
            {
                if (!_selectedTemplate!.IsValid || _doc.GetElement(_selectedTemplate!.Id) == null)
                {
                    MessageBox.Show("La tabla plantilla seleccionada ya no es válida o fue eliminada del proyecto.", "Plantilla No Válida", MessageBoxButton.OK, MessageBoxImage.Warning);
                    _selectedTemplate = null;
                    UpdateTemplateSelectionState();
                    return;
                }

                logSb.AppendLine($"Modo: Réplica Segura de Plantilla -> '{_selectedTemplate!.Name}'");
                if (chkOnlySelectedFields.IsChecked == true)
                {
                    allowedFields = _currentTemplateFields.Where(f => f.IsSelected).Select(f => f.Name).ToHashSet();
                    logSb.AppendLine($"Campos filtrados por usuario: {allowedFields.Count} de {_currentTemplateFields.Count}");
                }
                else
                {
                    logSb.AppendLine($"Replicando todos los campos ({_currentTemplateFields.Count}) con orden y formato original.");
                }
            }
            else
            {
                bool isTakeoff = rbMaterialTakeoff.IsChecked == true;
                logSb.AppendLine($"Modo: Generación Estándar Automática (Sin Plantilla)");
                logSb.AppendLine($"Tipo de tabla: {(isTakeoff ? "Cómputo de Materiales" : "Lista de Piezas")}");
                logSb.AppendLine($"Agrupar ejemplares: {(chkGroupIdentical.IsChecked == true ? "Sí" : "No")}");
            }
            logSb.AppendLine("--------------------------------------------------");

            int successCount = 0;
            int warningCount = 0;
            int totalFields = 0;

            try
            {
                using (Transaction tx = new Transaction(_doc, useTemplate ? "Replicar Tabla en Ensamblajes" : "Generar Tablas Estándar en Ensamblajes"))
                {
                    tx.Start();

                    foreach (var vm in selectedAssemblies)
                    {
                        AssemblyScheduleResult res;

                        if (useTemplate)
                        {
                            res = AssemblyScheduleService.CreateOrUpdateFromTemplate(
                                _doc,
                                vm.Assembly,
                                _selectedTemplate!.Schedule,
                                allowedFields,
                                chkReplicateFormat.IsChecked ?? true,
                                chkReplicateSorting.IsChecked ?? true,
                                chkReplicateTotals.IsChecked ?? true,
                                chkReplicateFilters.IsChecked ?? false,
                                false);
                        }
                        else
                        {
                            res = AssemblyScheduleService.CreateStandardAssemblySchedule(
                                _doc,
                                vm.Assembly,
                                rbMaterialTakeoff.IsChecked == true,
                                chkGroupIdentical.IsChecked ?? true);
                        }

                        if (res.Success)
                        {
                            successCount++;
                            totalFields += res.FieldsReplicatedCount;
                            logSb.AppendLine($"✅ {res.AssemblyName}: {res.Message}");
                        }
                        else
                        {
                            warningCount++;
                            logSb.AppendLine($"⚠️ {res.AssemblyName}: {res.Message}");
                        }
                    }

                    tx.Commit();
                }

                logSb.AppendLine("--------------------------------------------------");
                logSb.AppendLine($"RESUMEN DE EJECUCIÓN:");
                logSb.AppendLine($"• Ensamblajes procesados exitosamente: {successCount}");
                logSb.AppendLine($"• Ensamblajes con advertencia / omitidos: {warningCount}");
                logSb.AppendLine($"• Total de campos asignados: {totalFields}");
                logSb.AppendLine($"Hora de término: {DateTime.Now:HH:mm:ss}");

                txtAssemblyLog.Text = logSb.ToString();

                // Recargar listados para refrescar insignias y tablas creadas
                LoadAssemblies();
                LoadSchedules();

                MessageBox.Show(
                    $"¡Proceso finalizado con éxito!\n\n" +
                    $"• Ensamblajes procesados: {successCount}\n" +
                    $"• Campos y formatos configurados: {totalFields}\n" +
                    (warningCount > 0 ? $"• Con observaciones: {warningCount} (consultar registro para detalles)\n" : "") +
                    $"\nLas tablas están listas y organizadas bajo cada Ensamblaje en el Navegador de Proyectos.",
                    "Operación Completada",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                logSb.AppendLine($"❌ ERROR INESPERADO: {ex.Message}");
                txtAssemblyLog.Text = logSb.ToString();
                MessageBox.Show($"Error durante la generación de tablas: {ex.Message}", "Error Crítico", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region 4. Acciones Rápidas del Ribbon Integrado

        private void btnQuickAlbanileria_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using (Transaction t = new Transaction(_doc, "Crear Tablas de Albañilería"))
                {
                    t.Start();
                    CommandCrearTablasAlbanileria.CreateAlbanileriaSchedules(_doc);
                    t.Commit();
                }

                RefreshAll();
                MessageBox.Show("Tablas de Albañilería creadas con éxito:\n• 📊 Cómputo de Pegas (Mortero)\n• 📊 Cómputo de Ladrillos y Bloques", "Cómputos Creados", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al crear tablas de albañilería: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnQuickPisos_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using (Transaction t = new Transaction(_doc, "Crear Tablas de Enchapes y Pisos"))
                {
                    t.Start();
                    CommandCrearTablasPisos.CreatePisosSchedules(_doc);
                    t.Commit();
                }

                RefreshAll();
                MessageBox.Show("Tablas de Enchapes y Pisos creadas con éxito:\n• 📊 Cómputo de Enchapes y Baldosas\n• 📊 Cómputo de Pega y Fragua\n• 📊 Cómputo de Zócalos y Rodapiés", "Cómputos Creados", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al crear tablas de enchapes y pisos: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnQuickSteelDeck_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using (Transaction t = new Transaction(_doc, "Crear Tablas de Steel Deck"))
                {
                    t.Start();
                    CommandCrearTablasSteelDecks.CreateSteelDeckSchedules(_doc);
                    t.Commit();
                }

                RefreshAll();
                MessageBox.Show("Tablas de Steel Deck creadas con éxito:\n• 📊 Cómputo de Lámina Steel Deck\n• 📊 Cómputo de Concreto en Losas\n• 📊 Cómputo de Conectores de Cortante", "Cómputos Creados", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al crear tablas de Steel Deck: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnQuickPlumbing_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using (Transaction t = new Transaction(_doc, "Crear Tablas de Plumbing Sheet"))
                {
                    t.Start();
                    CommandPlumbingSheet.CreatePlumbingSchedules(_doc);
                    t.Commit();
                }

                RefreshAll();
                MessageBox.Show("Tablas de Plumbing Sheet creadas con éxito:\n• 🚰 Plumbing Sheet - Cómputo General HIDRO (Multicategoría)\n• 🚿 Cómputo de Aparatos Sanitarios\n• 🧪 Cómputo de Tuberías (Pipes)\n• 🔌 Cómputo de Uniones y Conexiones\n• 🚰 Cómputo de Válvulas y Accesorios\n• ⚙️ Cómputo de Equipos Hidro", "Cómputos Creados", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al crear tablas de Plumbing Sheet: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnRefreshAll_Click(object sender, RoutedEventArgs e)
        {
            RefreshAll();
        }

        #endregion
    }
}
