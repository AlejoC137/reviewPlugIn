using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace RevitToSupabasePlugin
{
    public class SheetRevisorItem : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        private string _newSheetNumber = string.Empty;
        private string _newSheetName = string.Empty;
        private string _statusText = "Sin cambios";

        public ElementId SheetId { get; set; } = ElementId.InvalidElementId;
        public string OriginalSheetNumber { get; set; } = string.Empty;
        public string OriginalSheetName { get; set; } = string.Empty;
        public int ViewportsCount { get; set; } = 0;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public string NewSheetNumber
        {
            get => _newSheetNumber;
            set
            {
                if (_newSheetNumber != value)
                {
                    _newSheetNumber = value;
                    OnPropertyChanged(nameof(NewSheetNumber));
                    UpdateStatus();
                }
            }
        }

        public string NewSheetName
        {
            get => _newSheetName;
            set
            {
                if (_newSheetName != value)
                {
                    _newSheetName = value;
                    OnPropertyChanged(nameof(NewSheetName));
                    UpdateStatus();
                }
            }
        }

        public string StatusText
        {
            get => _statusText;
            set
            {
                if (_statusText != value)
                {
                    _statusText = value;
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        public void UpdateStatus()
        {
            bool numChanged = !string.IsNullOrEmpty(NewSheetNumber) && NewSheetNumber != OriginalSheetNumber;
            bool nameChanged = !string.IsNullOrEmpty(NewSheetName) && NewSheetName != OriginalSheetName;

            if (numChanged && nameChanged)
                StatusText = "✏️ Número y Nombre modificados";
            else if (numChanged)
                StatusText = "✏️ Número modificado";
            else if (nameChanged)
                StatusText = "✏️ Nombre modificado";
            else
                StatusText = "Sin cambios";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    public partial class SheetRevisorWindow : Window
    {
        private readonly UIDocument _uidoc;
        private readonly Document _doc;
        private readonly List<SheetRevisorItem> _allSheets = new();
        private readonly ObservableCollection<SheetRevisorItem> _displayedSheets = new();

        public SheetRevisorWindow(UIDocument uidoc)
        {
            InitializeComponent();
            _uidoc = uidoc ?? throw new ArgumentNullException(nameof(uidoc));
            _doc = uidoc.Document;

            dgSheets.ItemsSource = _displayedSheets;
            LoadModelSheets();
        }

        private void LoadModelSheets()
        {
            _allSheets.Clear();

            var sheets = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsTemplate)
                .OrderBy(s => s.SheetNumber)
                .ToList();

            foreach (var s in sheets)
            {
                int vpCount = s.GetAllViewports().Count;

                _allSheets.Add(new SheetRevisorItem
                {
                    SheetId = s.Id,
                    OriginalSheetNumber = s.SheetNumber,
                    NewSheetNumber = s.SheetNumber,
                    OriginalSheetName = s.Name,
                    NewSheetName = s.Name,
                    ViewportsCount = vpCount
                });
            }

            _displayedSheets.Clear();
            foreach (var item in _allSheets)
            {
                _displayedSheets.Add(item);
            }

            UpdateSummaryLabel();
        }

        private void ApplyRulesToSheets()
        {
            // Reglas de Número
            string numPrefix = txtNumPrefix?.Text ?? string.Empty;
            string numSuffix = txtNumSuffix?.Text ?? string.Empty;
            string numFind = txtNumFind?.Text ?? string.Empty;
            string numReplace = txtNumReplace?.Text ?? string.Empty;
            bool doRenumber = chkRenumber?.IsChecked == true;

            int startNum = 101;
            int stepNum = 1;
            int digits = 3;

            if (doRenumber)
            {
                if (int.TryParse(txtNumStart?.Text, out int s)) startNum = s;
                if (int.TryParse(txtNumStep?.Text, out int st)) stepNum = st;
                if (int.TryParse(txtNumDigits?.Text, out int d)) digits = Math.Max(1, d);
            }

            // Reglas de Nombre
            string namePrefix = txtNamePrefix?.Text ?? string.Empty;
            string nameSuffix = txtNameSuffix?.Text ?? string.Empty;
            string nameFind = txtNameFind?.Text ?? string.Empty;
            string nameReplace = txtNameReplace?.Text ?? string.Empty;
            string nameCase = (cmbNameCase?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Mantener Original";

            int currentSeq = startNum;

            foreach (var item in _displayedSheets)
            {
                if (!item.IsSelected)
                {
                    item.NewSheetNumber = item.OriginalSheetNumber;
                    item.NewSheetName = item.OriginalSheetName;
                    continue;
                }

                // 1. Transformar Número
                if (doRenumber)
                {
                    string seqFormatted = currentSeq.ToString().PadLeft(digits, '0');
                    item.NewSheetNumber = $"{numPrefix}{seqFormatted}{numSuffix}";
                    currentSeq += stepNum;
                }
                else
                {
                    string num = item.OriginalSheetNumber;
                    if (!string.IsNullOrEmpty(numFind))
                    {
                        num = num.Replace(numFind, numReplace);
                    }
                    if (!string.IsNullOrEmpty(numPrefix)) num = numPrefix + num;
                    if (!string.IsNullOrEmpty(numSuffix)) num = num + numSuffix;
                    item.NewSheetNumber = num;
                }

                // 2. Transformar Nombre
                string name = item.OriginalSheetName;
                if (!string.IsNullOrEmpty(nameFind))
                {
                    name = name.Replace(nameFind, nameReplace);
                }
                if (!string.IsNullOrEmpty(namePrefix)) name = namePrefix + name;
                if (!string.IsNullOrEmpty(nameSuffix)) name = name + nameSuffix;

                switch (nameCase)
                {
                    case "MAYÚSCULAS":
                        name = name.ToUpperInvariant();
                        break;
                    case "minúsculas":
                        name = name.ToLowerInvariant();
                        break;
                    case "Formato Título":
                        name = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name.ToLowerInvariant());
                        break;
                }
                item.NewSheetName = name;
            }

            UpdateSummaryLabel();
        }

        private void UpdateSummaryLabel()
        {
            int total = _displayedSheets.Count;
            int changed = _displayedSheets.Count(s => s.IsSelected && (s.NewSheetNumber != s.OriginalSheetNumber || s.NewSheetName != s.OriginalSheetName));
            lblStatusSummary.Text = $"Total láminas: {total} | {changed} seleccionadas con cambios";
        }

        private void RulesChanged(object sender, EventArgs e)
        {
            ApplyRulesToSheets();
        }

        private void BtnResetRules_Click(object sender, RoutedEventArgs e)
        {
            txtNumPrefix.Text = string.Empty;
            txtNumSuffix.Text = string.Empty;
            txtNumFind.Text = string.Empty;
            txtNumReplace.Text = string.Empty;
            chkRenumber.IsChecked = false;
            txtNumStart.Text = "101";
            txtNumStep.Text = "1";
            txtNumDigits.Text = "3";

            txtNamePrefix.Text = string.Empty;
            txtNameSuffix.Text = string.Empty;
            txtNameFind.Text = string.Empty;
            txtNameReplace.Text = string.Empty;
            cmbNameCase.SelectedIndex = 0;

            foreach (var s in _allSheets)
            {
                s.NewSheetNumber = s.OriginalSheetNumber;
                s.NewSheetName = s.OriginalSheetName;
                s.UpdateStatus();
            }

            UpdateSummaryLabel();
        }

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _displayedSheets) item.IsSelected = true;
            ApplyRulesToSheets();
        }

        private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _displayedSheets) item.IsSelected = false;
            ApplyRulesToSheets();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            var sheetsToChange = _displayedSheets
                .Where(s => s.IsSelected && (s.NewSheetNumber != s.OriginalSheetNumber || s.NewSheetName != s.OriginalSheetName))
                .ToList();

            if (sheetsToChange.Count == 0)
            {
                TaskDialog.Show("ProSheets", "No hay láminas seleccionadas con cambios para aplicar.");
                return;
            }

            // Validar si hay duplicados en los nuevos números de plano proyectados
            var duplicateNums = sheetsToChange
                .GroupBy(s => s.NewSheetNumber)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicateNums.Count > 0)
            {
                TaskDialog.Show("Error de Validación",
                    $"No se pueden aplicar los cambios porque existen números de lámina duplicados:\n\n{string.Join(", ", duplicateNums.Take(5))}\n\nPor favor ajusta la numeración antes de continuar.");
                return;
            }

            var confirm = TaskDialog.Show("Confirmar Cambios en Láminas",
                $"¿Deseas aplicar los cambios en {sheetsToChange.Count} láminas del proyecto activo?",
                TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);

            if (confirm != TaskDialogResult.Yes) return;

            int sheetsRenumbered = 0;
            int sheetsRenamed = 0;
            var errors = new List<string>();

            using (Transaction trans = new Transaction(_doc, "ProSheets: Renombramiento y Renumeración Masiva"))
            {
                trans.Start();

                // FASE 1 (Anti-Colisión): Asignar números temporales a las láminas cuyo SheetNumber cambiará
                // Revit arroja excepción si se intenta asignar un SheetNumber que ya existe en otro ViewSheet.
                for (int i = 0; i < sheetsToChange.Count; i++)
                {
                    var item = sheetsToChange[i];
                    if (item.NewSheetNumber != item.OriginalSheetNumber)
                    {
                        try
                        {
                            if (_doc.GetElement(item.SheetId) is ViewSheet sheet)
                            {
                                string tempNum = $"__TEMP_{Guid.NewGuid().ToString("N").Substring(0, 6)}_{i}__";
                                sheet.SheetNumber = tempNum;
                            }
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"Fase Temp '{item.OriginalSheetNumber}': {ex.Message}");
                        }
                    }
                }

                // FASE 2: Asignar nuevos números definitivos
                foreach (var item in sheetsToChange)
                {
                    if (item.NewSheetNumber != item.OriginalSheetNumber)
                    {
                        try
                        {
                            if (_doc.GetElement(item.SheetId) is ViewSheet sheet)
                            {
                                sheet.SheetNumber = item.NewSheetNumber;
                                sheetsRenumbered++;
                            }
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"Número '{item.NewSheetNumber}': {ex.Message}");
                        }
                    }
                }

                // FASE 3: Asignar nuevos nombres de plano
                foreach (var item in sheetsToChange)
                {
                    if (item.NewSheetName != item.OriginalSheetName)
                    {
                        try
                        {
                            if (_doc.GetElement(item.SheetId) is ViewSheet sheet)
                            {
                                sheet.Name = item.NewSheetName;
                                sheetsRenamed++;
                            }
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"Nombre '{item.NewSheetName}': {ex.Message}");
                        }
                    }
                }

                trans.Commit();
            }

            string resultMsg = $"✅ Láminas actualizadas exitosamente.\n\n• Números renumerados: {sheetsRenumbered}\n• Nombres renombrados: {sheetsRenamed}";
            if (errors.Count > 0)
            {
                resultMsg += $"\n\n⚠️ Ocurrieron {errors.Count} advertencias/errores:\n" + string.Join("\n", errors.Take(5));
            }

            TaskDialog.Show("ProSheets", resultMsg);
            LoadModelSheets(); // Recargar datos frescos
        }
    }
}
