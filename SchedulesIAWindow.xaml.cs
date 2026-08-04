using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using Autodesk.Revit.DB;
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;

namespace RevitToSupabasePlugin
{
    public class ScheduleFieldItem
    {
        public string Name { get; set; } = string.Empty;
        public bool IsSelected { get; set; }
        public int ColumnIndex { get; set; }
    }

    public partial class SchedulesIAWindow : Window
    {
        private Document _doc;
        private List<ViewSchedule>? _schedules;

        public SchedulesIAWindow(Document doc)
        {
            InitializeComponent();
            _doc = doc;
            LoadSchedules();
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
        }

        private void cmbSchedules_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (cmbSchedules.SelectedItem is ViewSchedule selectedSchedule)
            {
                LoadFieldsForSchedule(selectedSchedule);
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
                    if (!field.IsHidden)
                    {
                        fields.Add(new ScheduleFieldItem 
                        { 
                            Name = string.IsNullOrEmpty(field.ColumnHeading) ? field.GetName() : field.ColumnHeading, 
                            IsSelected = true, 
                            ColumnIndex = visibleColIndex 
                        });
                        visibleColIndex++;
                    }
                }
            }
            catch (Exception)
            {
                // Fallback
            }
            lstProperties.ItemsSource = fields;
        }

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
                                    ?.Where(f => f.IsSelected).ToList() ?? new List<ScheduleFieldItem>();

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
    }
}
