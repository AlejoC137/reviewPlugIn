using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;

namespace RevitToSupabasePlugin
{
    public class ScheduleFieldItem
    {
        public string Name { get; set; } = string.Empty;
        public string ColumnHeading { get; set; } = string.Empty;
        public bool IsSelected { get; set; } = true;
        public int ColumnIndex { get; set; }
        public bool IsHidden { get; set; }

        public string DisplayName => string.IsNullOrEmpty(ColumnHeading) || ColumnHeading == Name
            ? Name
            : $"{ColumnHeading} [{Name}]";
    }

    public partial class SchedulesIAWindow : Window
    {
        private Document _doc;
        private UIDocument? _uidoc;
        private List<ViewSchedule>? _schedules;
        private List<AssemblyItemViewModel> _allAssemblies = new List<AssemblyItemViewModel>();

        public SchedulesIAWindow(Document doc, UIDocument? uidoc = null)
        {
            InitializeComponent();
            _doc = doc;
            _uidoc = uidoc;

            LoadSchedules();
            LoadAssemblies();
        }

        private void LoadSchedules()
        {
            _schedules = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(vs => !vs.IsTemplate && !vs.IsInternalKeynoteSchedule && vs.Name != "<Revision Schedule>")
                .OrderBy(vs => vs.Name)
                .ToList();

            cmbSchedules.ItemsSource = _schedules;
            if (_schedules.Count > 0)
            {
                cmbSchedules.SelectedIndex = 0;
            }
            else
            {
                txtScheduleInfo.Text = "No se encontraron tablas de planificación en el modelo.";
            }
        }

        private void btnRefreshSchedules_Click(object sender, RoutedEventArgs e)
        {
            LoadSchedules();
            LoadAssemblies();
        }

        private void cmbSchedules_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbSchedules.SelectedItem is ViewSchedule selectedSchedule)
            {
                UpdateScheduleMetadata(selectedSchedule);
                LoadFieldsForSchedule(selectedSchedule);
            }
        }

        private void UpdateScheduleMetadata(ViewSchedule schedule)
        {
            try
            {
                ScheduleDefinition def = schedule.Definition;
                string catName = "Multi-categoría";
                if (def.CategoryId != ElementId.InvalidElementId)
                {
                    Category? cat = Category.GetCategory(_doc, def.CategoryId);
                    catName = cat?.Name ?? $"Categoría ID: {def.CategoryId}";
                }

                string kind = def.IsMaterialTakeoff ? "Cómputo de Materiales" : "Tabla de Planificación";
                string itemized = def.IsItemized ? "Ejemplares desglosados" : "Filas agrupadas";
                string grandTotal = def.ShowGrandTotal ? "Total general activo" : "Sin total general";

                txtScheduleInfo.Text = $"Tipo: {kind} | Categoría: {catName} | {itemized} | {grandTotal}";
            }
            catch
            {
                txtScheduleInfo.Text = "Información de tabla disponible.";
            }
        }

        private void LoadFieldsForSchedule(ViewSchedule schedule)
        {
            List<ScheduleFieldItem> fields = new List<ScheduleFieldItem>();
            try
            {
                ScheduleDefinition definition = schedule.Definition;
                IList<ScheduleFieldId> fieldIds = definition.GetFieldOrder();
                
                int visibleColIndex = 0;
                foreach (ScheduleFieldId id in fieldIds)
                {
                    ScheduleField field = definition.GetField(id);
                    fields.Add(new ScheduleFieldItem 
                    { 
                        Name = field.GetName(),
                        ColumnHeading = field.ColumnHeading,
                        IsSelected = true,
                        IsHidden = field.IsHidden,
                        ColumnIndex = visibleColIndex 
                    });

                    if (!field.IsHidden)
                    {
                        visibleColIndex++;
                    }
                }
            }
            catch (Exception ex)
            {
                txtAssemblyLog.Text = $"Error al leer campos: {ex.Message}";
            }

            lstProperties.ItemsSource = fields;
            txtFieldsCount.Text = $"{fields.Count} campos encontrados ({fields.Count(f => !f.IsHidden)} visibles)";
        }

        #region Assemblies Tab Logic

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
            int total = _allAssemblies.Count;
            int selected = _allAssemblies.Count(a => a.IsSelected);
            int withSchedule = _allAssemblies.Count(a => a.HasExistingSchedule);

            txtAssembliesSummary.Text = total == 0
                ? "No se encontraron ensamblajes (Assemblies) en el modelo."
                : $"{total} ensamblajes en el modelo | {withSchedule} ya tienen tabla | {selected} seleccionados";
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
                MessageBox.Show("No se detectó un documento interactivo para leer la selección actual.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var selIds = _uidoc.Selection.GetElementIds();
                if (selIds.Count == 0)
                {
                    MessageBox.Show("No hay elementos seleccionados en Revit.\nSelecciona uno o más ensamblajes en el modelo e intenta de nuevo.", "Selección Vacía", MessageBoxButton.OK, MessageBoxImage.Information);
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
                    MessageBox.Show("Los elementos seleccionados en Revit no pertenecen a ningún Ensamblaje (Assembly).", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                foreach (var item in _allAssemblies)
                {
                    item.IsSelected = targetAssemblyIds.Contains(item.Id);
                }

                lstAssemblies.Items.Refresh();
                UpdateAssemblySummaryText();

                MessageBox.Show($"Se seleccionaron {targetAssemblyIds.Count} ensamblaje(s) según los elementos marcados en Revit.", "Selección Actualizada", MessageBoxButton.OK, MessageBoxImage.Information);
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
            txtAssembliesSummary.Text = $"{total} ensamblajes en el modelo | {withSchedule} ya tienen tabla | {selected} seleccionados";
        }

        private void btnApplyToAssemblies_Click(object sender, RoutedEventArgs e)
        {
            if (cmbSchedules.SelectedItem is not ViewSchedule selectedSchedule)
            {
                MessageBox.Show("Por favor, selecciona primero una tabla de planificación maestra en el Paso 1.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedAssemblies = _allAssemblies.Where(a => a.IsSelected).ToList();
            if (selectedAssemblies.Count == 0)
            {
                MessageBox.Show("Por favor, selecciona al menos un ensamblaje en la lista para aplicar la replicación.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            HashSet<string>? allowedFields = null;
            if (chkOnlySelectedFields.IsChecked == true)
            {
                var checkedItems = (lstProperties.ItemsSource as List<ScheduleFieldItem>)
                    ?.Where(f => f.IsSelected)
                    .Select(f => f.Name)
                    .ToHashSet();

                if (checkedItems == null || checkedItems.Count == 0)
                {
                    MessageBox.Show("Has marcado la opción de 'Replicar solo campos seleccionados' pero no hay ningún campo marcado en la lista izquierda.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                allowedFields = checkedItems;
            }

            StringBuilder logSb = new StringBuilder();
            logSb.AppendLine($"=== REPLICACIÓN DE TABLAS DE ENSAMBLAJE ===");
            logSb.AppendLine($"Tabla Plantilla: {selectedSchedule.Name}");
            logSb.AppendLine($"Hora de inicio: {DateTime.Now:HH:mm:ss}");
            logSb.AppendLine($"Ensamblajes seleccionados: {selectedAssemblies.Count}");
            logSb.AppendLine("--------------------------------------------------");

            int successCount = 0;
            int warningCount = 0;
            int totalFields = 0;

            try
            {
                using (Transaction tx = new Transaction(_doc, "Replicar Formato de Tabla en Ensamblajes"))
                {
                    tx.Start();

                    foreach (var vm in selectedAssemblies)
                    {
                        var result = AssemblyScheduleService.CreateOrUpdateAssemblySchedule(
                            _doc,
                            vm.Assembly,
                            selectedSchedule,
                            allowedFields,
                            chkReplicateFilters.IsChecked ?? true,
                            chkReplicateSorting.IsChecked ?? true,
                            chkReplicateTemplate.IsChecked ?? true);

                        if (result.Success)
                        {
                            successCount++;
                            totalFields += result.FieldsReplicatedCount;
                            logSb.AppendLine($"✅ {result.AssemblyName}: {result.Message}");
                        }
                        else
                        {
                            warningCount++;
                            logSb.AppendLine($"⚠️ {result.AssemblyName}: {result.Message}");
                        }
                    }

                    tx.Commit();
                }

                logSb.AppendLine("--------------------------------------------------");
                logSb.AppendLine($"RESULTADO FINAL:");
                logSb.AppendLine($"• Ensamblajes configurados con éxito: {successCount}");
                logSb.AppendLine($"• Ensamblajes con advertencia / omitidos: {warningCount}");
                logSb.AppendLine($"• Total de campos y formatos replicados: {totalFields}");
                logSb.AppendLine($"Hora de finalización: {DateTime.Now:HH:mm:ss}");

                txtAssemblyLog.Text = logSb.ToString();

                // Recargar listado para actualizar insignias de estado (✅ Con Tabla)
                LoadAssemblies();

                MessageBox.Show(
                    $"¡Operación completada con éxito!\n\n" +
                    $"• Tablas de ensamblajes procesadas: {successCount}\n" +
                    $"• Campos y formato clonados: {totalFields}\n" +
                    (warningCount > 0 ? $"• Advertencias: {warningCount} (Ver registro para detalles)\n" : "") +
                    $"\nYa puedes ver las tablas estructuradas bajo cada Ensamblaje en el Project Browser.",
                    "Replicación Exitosa",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                logSb.AppendLine($"❌ ERROR CRÍTICO: {ex.Message}");
                txtAssemblyLog.Text = logSb.ToString();
                MessageBox.Show($"Ocurrió un error al procesar las tablas: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Properties & Gemini Tab Logic

        private void btnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (lstProperties.ItemsSource is List<ScheduleFieldItem> items)
            {
                foreach (var item in items) item.IsSelected = true;
                lstProperties.Items.Refresh();
            }
        }

        private void btnDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            if (lstProperties.ItemsSource is List<ScheduleFieldItem> items)
            {
                foreach (var item in items) item.IsSelected = false;
                lstProperties.Items.Refresh();
            }
        }

        private void btnPrePromptFormula_Click(object sender, RoutedEventArgs e)
        {
            txtPrompt.Text = "Crea una nueva propiedad calculada usando una fórmula matemática basada en las siguientes dos propiedades: [PROPIEDAD_1] y [PROPIEDAD_2]. Explica la lógica.";
        }

        private void btnPrePromptFormat_Click(object sender, RoutedEventArgs e)
        {
            txtPrompt.Text = "Formatea la vista de la tabla estableciendo reglas de formato condicional (ej. cambiar color de fondo) para los elementos que cumplan con la condición: [CONDICIÓN].";
        }

        private void btnPrePromptModify_Click(object sender, RoutedEventArgs e)
        {
            txtPrompt.Text = "Modifica las propiedades de los elementos de esta tabla. Para todos los elementos donde [PROPIEDAD_CONDICION] sea [VALOR], cambia el valor de [PROPIEDAD_A_MODIFICAR] a [NUEVO_VALOR].";
        }

        private void btnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (cmbSchedules.SelectedItem is ViewSchedule selectedSchedule)
            {
                string prompt = txtPrompt.Text.Trim();
                if (string.IsNullOrEmpty(prompt))
                {
                    MessageBox.Show("Por favor, escribe un prompt para Gemini.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string scheduleData = ExtractScheduleData(selectedSchedule);
                
                string finalClipboardText = $"[PROMPT]\n{prompt}\n\n[DATOS DE LA TABLA: {selectedSchedule.Name}]\n{scheduleData}";

                try
                {
                    Clipboard.SetText(finalClipboardText);
                    MessageBox.Show("Información y prompt copiados al portapapeles con éxito.\n\nYa puedes pegarlo en Gemini.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error al copiar al portapapeles: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show("Selecciona una tabla de planificación primero.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private string ExtractScheduleData(ViewSchedule schedule)
        {
            StringBuilder sb = new StringBuilder();
            
            var selectedFields = (lstProperties.ItemsSource as List<ScheduleFieldItem>)
                                    ?.Where(f => f.IsSelected && !f.IsHidden).ToList() ?? new List<ScheduleFieldItem>();

            if (selectedFields.Count > 0)
            {
                sb.AppendLine(string.Join(",", selectedFields.Select(f => "\"" + f.Name.Replace("\"", "\"\"") + "\"")));
            }

            TableData tableData = schedule.GetTableData();
            TableSectionData sectionData = tableData.GetSectionData(SectionType.Body);

            if (sectionData != null)
            {
                int rows = sectionData.NumberOfRows;
                int cols = sectionData.NumberOfColumns;

                for (int r = 0; r < rows; r++)
                {
                    List<string> rowValues = new List<string>();
                    
                    foreach (var field in selectedFields)
                    {
                        if (field.ColumnIndex < cols)
                        {
                            string cellText = schedule.GetCellText(SectionType.Body, r, field.ColumnIndex);
                            if (cellText.Contains(",") || cellText.Contains("\"") || cellText.Contains("\n"))
                            {
                                cellText = "\"" + cellText.Replace("\"", "\"\"") + "\"";
                            }
                            rowValues.Add(cellText);
                        }
                    }
                    
                    if (rowValues.Count > 0)
                    {
                        sb.AppendLine(string.Join(",", rowValues));
                    }
                }
            }

            return sb.ToString();
        }

        #endregion
    }
}
