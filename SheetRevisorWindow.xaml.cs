using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
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
        private bool _isInitialized;

        public SheetRevisorWindow(UIDocument uidoc)
        {
            InitializeComponent();
            _uidoc = uidoc ?? throw new ArgumentNullException(nameof(uidoc));
            _doc = uidoc.Document;

            dgSheets.ItemsSource = _displayedSheets;
            LoadTitleBlocks();
            LoadModelSheets();
            _isInitialized = true;
        }

        private void LoadModelSheets()
        {
            _allSheets.Clear();

            var sheets = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsTemplate)
                .OrderBy(s => s.SheetNumber, NaturalStringComparer.Instance)
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
            if (lblStatusSummary == null) return;
            int total = _displayedSheets.Count;
            int changed = _displayedSheets.Count(s => s.IsSelected && (s.NewSheetNumber != s.OriginalSheetNumber || s.NewSheetName != s.OriginalSheetName));
            lblStatusSummary.Text = $"Total láminas: {total} | {changed} seleccionadas con cambios";
        }

        private void RulesChanged(object sender, EventArgs e)
        {
            // Los eventos TextChanged/SelectionChanged se disparan durante InitializeComponent, antes de que existan todos los controles
            if (!_isInitialized) return;
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

        // =====================================================================
        // INSERCIÓN / DESPLAZAMIENTO EN CASCADA
        // =====================================================================

        /// <summary>Divide un número de lámina en prefijo + última parte numérica + sufijo. Ej: "A-0301b" → ("A-", 301, 4, "b").</summary>
        private static readonly Regex SheetNumberRegex = new Regex(@"^(.*?)(\d+)(\D*)$", RegexOptions.Compiled);

        private sealed class ParsedSheetNumber
        {
            public string Prefix = string.Empty;
            public long Number;
            public int Width;
            public string Suffix = string.Empty;

            public string Format(long number) => Prefix + number.ToString(CultureInfo.InvariantCulture).PadLeft(Width, '0') + Suffix;

            public bool SameSeries(ParsedSheetNumber other) =>
                string.Equals(Prefix, other.Prefix, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Suffix, other.Suffix, StringComparison.OrdinalIgnoreCase);
        }

        private static ParsedSheetNumber? ParseSheetNumber(string sheetNumber)
        {
            if (string.IsNullOrWhiteSpace(sheetNumber)) return null;
            var m = SheetNumberRegex.Match(sheetNumber.Trim());
            if (!m.Success || !long.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long n)) return null;

            return new ParsedSheetNumber
            {
                Prefix = m.Groups[1].Value,
                Number = n,
                Width = m.Groups[2].Value.Length,
                Suffix = m.Groups[3].Value
            };
        }

        private sealed class TitleBlockOption
        {
            public string Label { get; set; } = string.Empty;
            /// <summary>null = usar la misma cartela que la lámina de referencia.</summary>
            public ElementId? SymbolId { get; set; }
            public override string ToString() => Label;
        }

        private void LoadTitleBlocks()
        {
            var options = new List<TitleBlockOption>
            {
                new TitleBlockOption { Label = "🔁 Igual a la lámina seleccionada", SymbolId = null },
                new TitleBlockOption { Label = "(Sin cartela)", SymbolId = ElementId.InvalidElementId }
            };

            var symbols = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .OrderBy(s => s.FamilyName, NaturalStringComparer.Instance)
                .ThenBy(s => s.Name, NaturalStringComparer.Instance);

            foreach (var s in symbols)
            {
                options.Add(new TitleBlockOption { Label = $"{s.FamilyName} : {s.Name}", SymbolId = s.Id });
            }

            cmbTitleBlock.ItemsSource = options;
            cmbTitleBlock.SelectedIndex = 0;
        }

        private ElementId ResolveTitleBlockFor(SheetRevisorItem reference)
        {
            var opt = cmbTitleBlock.SelectedItem as TitleBlockOption;
            if (opt?.SymbolId != null) return opt.SymbolId;

            // Misma cartela que la lámina de referencia
            var tb = new FilteredElementCollector(_doc, reference.SheetId)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .FirstElement() as FamilyInstance;

            return tb?.Symbol?.Id ?? ElementId.InvalidElementId;
        }

        /// <summary>
        /// Calcula los nuevos números para las láminas de la misma serie con número &gt;= fromNumber, desplazadas en 'delta'.
        /// Devuelve null y un mensaje de error si el resultado produce colisiones o números negativos.
        /// </summary>
        private List<(SheetRevisorItem Item, string NewNumber)>? BuildShiftPlan(
            ParsedSheetNumber series, long fromNumber, int delta, bool stopAtGap, out string error)
        {
            error = string.Empty;

            var candidates = _allSheets
                .Select(s => (Item: s, Parsed: ParseSheetNumber(s.OriginalSheetNumber)))
                .Where(x => x.Parsed != null && x.Parsed.SameSeries(series) && x.Parsed.Number >= fromNumber)
                .OrderBy(x => x.Parsed!.Number)
                .ToList();

            var plan = new List<(SheetRevisorItem Item, string NewNumber)>();

            if (delta > 0 && stopAtGap)
            {
                // Efecto dominó: solo mover lo necesario hasta encontrar hueco libre
                long occupiedUpTo = fromNumber + delta - 1;
                foreach (var c in candidates)
                {
                    if (c.Parsed!.Number > occupiedUpTo) break;
                    long newN = c.Parsed.Number + delta;
                    plan.Add((c.Item, c.Parsed.Format(newN)));
                    occupiedUpTo = Math.Max(occupiedUpTo, newN);
                }
            }
            else
            {
                foreach (var c in candidates)
                {
                    long newN = c.Parsed!.Number + delta;
                    if (newN < 0)
                    {
                        error = $"La lámina '{c.Item.OriginalSheetNumber}' quedaría con un número negativo.";
                        return null;
                    }
                    plan.Add((c.Item, c.Parsed.Format(newN)));
                }
            }

            // Validar colisiones contra las láminas que NO se mueven
            var movingIds = new HashSet<ElementId>(plan.Select(p => p.Item.SheetId));
            var fixedNumbers = new HashSet<string>(
                _allSheets.Where(s => !movingIds.Contains(s.SheetId)).Select(s => s.OriginalSheetNumber),
                StringComparer.OrdinalIgnoreCase);

            var collisions = plan.Where(p => fixedNumbers.Contains(p.NewNumber)).Select(p => p.NewNumber).ToList();
            if (collisions.Count > 0)
            {
                error = "El desplazamiento choca con láminas existentes que no se mueven:\n\n" + string.Join(", ", collisions.Take(8)) +
                        (collisions.Count > 8 ? " ..." : "");
                return null;
            }

            return plan;
        }

        private static string DescribePlan(List<(SheetRevisorItem Item, string NewNumber)> plan)
        {
            if (plan.Count == 0) return "No se desplazará ninguna lámina.";

            var lines = new List<string> { $"Se renumerarán {plan.Count} lámina(s):" };
            const int head = 4;
            if (plan.Count <= head * 2)
            {
                lines.AddRange(plan.Select(p => $"   {p.Item.OriginalSheetNumber}  ➔  {p.NewNumber}"));
            }
            else
            {
                lines.AddRange(plan.Take(head).Select(p => $"   {p.Item.OriginalSheetNumber}  ➔  {p.NewNumber}"));
                lines.Add("   ...");
                lines.AddRange(plan.Skip(plan.Count - 2).Select(p => $"   {p.Item.OriginalSheetNumber}  ➔  {p.NewNumber}"));
            }
            return string.Join("\n", lines);
        }

        private bool HasPendingPreviewChanges() =>
            _allSheets.Any(s => s.NewSheetNumber != s.OriginalSheetNumber || s.NewSheetName != s.OriginalSheetName);

        private SheetRevisorItem? GetReferenceSheet(out ParsedSheetNumber? parsed)
        {
            parsed = null;
            if (dgSheets.SelectedItem is not SheetRevisorItem item)
            {
                TaskDialog.Show("ProSheets", "Selecciona primero una lámina de referencia en la tabla (haz clic sobre su fila).");
                return null;
            }

            parsed = ParseSheetNumber(item.OriginalSheetNumber);
            if (parsed == null)
            {
                TaskDialog.Show("ProSheets", $"El número '{item.OriginalSheetNumber}' no contiene una parte numérica que se pueda incrementar.");
                return null;
            }
            return item;
        }

        /// <summary>Aplica el plan de renumeración usando números temporales para evitar colisiones. Lanza excepción si algo falla.</summary>
        private void ExecuteShiftPlan(List<(SheetRevisorItem Item, string NewNumber)> plan)
        {
            var sheets = plan.Select(p => (Sheet: _doc.GetElement(p.Item.SheetId) as ViewSheet, p.NewNumber, p.Item.OriginalSheetNumber)).ToList();

            // FASE 1: números temporales
            string token = Guid.NewGuid().ToString("N").Substring(0, 6);
            for (int i = 0; i < sheets.Count; i++)
            {
                var sheet = sheets[i].Sheet ?? throw new InvalidOperationException($"No se encontró la lámina '{sheets[i].OriginalSheetNumber}'.");
                sheet.SheetNumber = $"__SHIFT_{token}_{i}__";
            }

            // FASE 2: números definitivos
            foreach (var s in sheets)
            {
                s.Sheet!.SheetNumber = s.NewNumber;
            }
        }

        private void BtnInsertBefore_Click(object sender, RoutedEventArgs e) => InsertSheet(insertAfter: false);
        private void BtnInsertAfter_Click(object sender, RoutedEventArgs e) => InsertSheet(insertAfter: true);

        private void InsertSheet(bool insertAfter)
        {
            var reference = GetReferenceSheet(out var parsed);
            if (reference == null || parsed == null) return;

            long newNumberValue = insertAfter ? parsed.Number + 1 : parsed.Number;
            string newNumber = parsed.Format(newNumberValue);
            string newName = string.IsNullOrWhiteSpace(txtInsertName.Text) ? "NUEVA LÁMINA" : txtInsertName.Text.Trim();

            var plan = BuildShiftPlan(parsed, newNumberValue, 1, chkStopAtGap.IsChecked == true, out string error);
            if (plan == null)
            {
                TaskDialog.Show("ProSheets - No se puede insertar", error);
                return;
            }

            // El número nuevo no debe chocar con láminas que no se mueven
            var movingIds = new HashSet<ElementId>(plan.Select(p => p.Item.SheetId));
            if (_allSheets.Any(s => !movingIds.Contains(s.SheetId) && string.Equals(s.OriginalSheetNumber, newNumber, StringComparison.OrdinalIgnoreCase)))
            {
                TaskDialog.Show("ProSheets - No se puede insertar", $"El número '{newNumber}' ya está ocupado por una lámina que no pertenece a la serie desplazable.");
                return;
            }

            ElementId titleBlockId = ResolveTitleBlockFor(reference);

            string msg = $"Se creará la lámina:\n   {newNumber} - {newName}\n\n{DescribePlan(plan)}";
            if (HasPendingPreviewChanges())
                msg += "\n\n⚠️ Los cambios de preview no aplicados en la tabla se descartarán.";

            if (TaskDialog.Show("Confirmar Inserción en Cascada", msg, TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No) != TaskDialogResult.Yes)
                return;

            ElementId newSheetId = ElementId.InvalidElementId;
            string? copyReport = null;
            using (var trans = new Transaction(_doc, $"ProSheets: Insertar lámina {newNumber} en cascada"))
            {
                try
                {
                    trans.Start();

                    ExecuteShiftPlan(plan);

                    if (titleBlockId != ElementId.InvalidElementId && _doc.GetElement(titleBlockId) is FamilySymbol symbol && !symbol.IsActive)
                    {
                        symbol.Activate();
                        _doc.Regenerate();
                    }

                    var newSheet = ViewSheet.Create(_doc, titleBlockId);
                    newSheet.SheetNumber = newNumber;
                    newSheet.Name = newName;
                    newSheetId = newSheet.Id;

                    if (chkCopyProps.IsChecked == true && _doc.GetElement(reference.SheetId) is ViewSheet refSheet)
                    {
                        _doc.Regenerate();
                        copyReport = CopySheetProperties(refSheet, newSheet);
                    }

                    trans.Commit();
                }
                catch (Exception ex)
                {
                    if (trans.HasStarted()) trans.RollBack();
                    TaskDialog.Show("ProSheets - Error", "No se pudo insertar la lámina. No se realizó ningún cambio.\n\n" + ex.Message);
                    return;
                }
            }

            LoadModelSheets();
            SelectSheetInGrid(newSheetId);
            txtInsertName.Text = string.Empty;

            if (!string.IsNullOrEmpty(copyReport))
                TaskDialog.Show("ProSheets", $"✅ Lámina {newNumber} creada.\n\n{copyReport}");
        }

        // Parámetros que nunca se copian (identidad de la lámina o gestionados por Revit)
        private static readonly HashSet<BuiltInParameter> ExcludedSheetParams = new HashSet<BuiltInParameter>
        {
            BuiltInParameter.SHEET_NUMBER,
            BuiltInParameter.SHEET_NAME,
            BuiltInParameter.VIEW_NAME,
            BuiltInParameter.ELEM_TYPE_PARAM,
            BuiltInParameter.ELEM_FAMILY_PARAM,
            BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM,
            BuiltInParameter.SYMBOL_ID_PARAM,
        };

        /// <summary>
        /// Copia al máximo las propiedades de la lámina de referencia: parámetros de instancia editables
        /// (incorporados, de proyecto y compartidos), revisiones adicionales, y parámetros + posición de la cartela.
        /// </summary>
        private string CopySheetProperties(ViewSheet source, ViewSheet target)
        {
            int sheetCopied = CopyParameters(source, target, out var sheetFailed);

            int revisions = 0;
            try
            {
                var revIds = source.GetAdditionalRevisionIds();
                if (revIds != null && revIds.Count > 0)
                {
                    target.SetAdditionalRevisionIds(revIds);
                    revisions = revIds.Count;
                }
            }
            catch (Exception ex) { sheetFailed.Add($"Revisiones: {ex.Message}"); }

            // Cartela: mismos parámetros de instancia y misma posición (si ambas tienen cartela del mismo tipo)
            int tbCopied = 0;
            var tbFailed = new List<string>();
            var srcTb = GetTitleBlockInstance(source);
            var dstTb = GetTitleBlockInstance(target);
            if (srcTb != null && dstTb != null)
            {
                tbCopied = CopyParameters(srcTb, dstTb, out tbFailed);

                try
                {
                    if (srcTb.Location is LocationPoint sp && dstTb.Location is LocationPoint dp)
                    {
                        var move = sp.Point - dp.Point;
                        if (!move.IsZeroLength()) ElementTransformUtils.MoveElement(_doc, dstTb.Id, move);
                    }
                }
                catch (Exception ex) { tbFailed.Add($"Posición cartela: {ex.Message}"); }
            }

            var report = $"Propiedades copiadas desde {source.SheetNumber}:\n" +
                         $"• Parámetros de lámina: {sheetCopied}\n" +
                         $"• Revisiones asignadas: {revisions}\n" +
                         $"• Parámetros de cartela: {tbCopied}";

            var failed = sheetFailed.Concat(tbFailed).ToList();
            if (failed.Count > 0)
                report += $"\n\n⚠️ {failed.Count} parámetro(s) no se pudieron copiar:\n" + string.Join("\n", failed.Take(6)) + (failed.Count > 6 ? "\n..." : "");

            return report;
        }

        private FamilyInstance? GetTitleBlockInstance(ViewSheet sheet) =>
            new FilteredElementCollector(_doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .FirstElement() as FamilyInstance;

        private static Parameter? FindMatchingParameter(Element target, Parameter srcParam)
        {
            if (srcParam.Definition is InternalDefinition idef && idef.BuiltInParameter != BuiltInParameter.INVALID)
                return target.get_Parameter(idef.BuiltInParameter);

            if (srcParam.IsShared)
                return target.get_Parameter(srcParam.GUID);

            var name = srcParam.Definition?.Name;
            if (string.IsNullOrEmpty(name)) return null;
            return target.GetParameters(name).FirstOrDefault(p => !p.IsReadOnly && p.StorageType == srcParam.StorageType);
        }

        private static int CopyParameters(Element source, Element target, out List<string> failed)
        {
            failed = new List<string>();
            int copied = 0;

            foreach (Parameter src in source.Parameters)
            {
                if (src == null || src.Definition == null || !src.HasValue || src.StorageType == StorageType.None) continue;
                if (src.Definition is InternalDefinition idef && ExcludedSheetParams.Contains(idef.BuiltInParameter)) continue;

                Parameter? dst = FindMatchingParameter(target, src);
                if (dst == null || dst.IsReadOnly || dst.StorageType != src.StorageType) continue;

                try
                {
                    bool ok;
                    switch (src.StorageType)
                    {
                        case StorageType.String:
                            var s = src.AsString() ?? string.Empty;
                            if (dst.AsString() == s) continue;
                            ok = dst.Set(s);
                            break;
                        case StorageType.Integer:
                            if (dst.AsInteger() == src.AsInteger()) continue;
                            ok = dst.Set(src.AsInteger());
                            break;
                        case StorageType.Double:
                            if (Math.Abs(dst.AsDouble() - src.AsDouble()) < 1e-9) continue;
                            ok = dst.Set(src.AsDouble());
                            break;
                        case StorageType.ElementId:
                            if (dst.AsElementId() == src.AsElementId()) continue;
                            ok = dst.Set(src.AsElementId());
                            break;
                        default:
                            continue;
                    }
                    if (ok) copied++;
                }
                catch (Exception ex)
                {
                    failed.Add($"{src.Definition.Name}: {ex.Message}");
                }
            }
            return copied;
        }

        private void BtnShiftUp_Click(object sender, RoutedEventArgs e) => ShiftSheets(+1);
        private void BtnShiftDown_Click(object sender, RoutedEventArgs e) => ShiftSheets(-1);

        private void ShiftSheets(int direction)
        {
            var reference = GetReferenceSheet(out var parsed);
            if (reference == null || parsed == null) return;

            if (!int.TryParse(txtShiftAmount.Text, out int amount) || amount <= 0)
            {
                TaskDialog.Show("ProSheets", "La cantidad a desplazar debe ser un número entero mayor que 0.");
                return;
            }

            int delta = amount * direction;
            var plan = BuildShiftPlan(parsed, parsed.Number, delta, chkStopAtGap.IsChecked == true, out string error);
            if (plan == null)
            {
                TaskDialog.Show("ProSheets - No se puede desplazar", error);
                return;
            }
            if (plan.Count == 0)
            {
                TaskDialog.Show("ProSheets", "No hay láminas para desplazar.");
                return;
            }

            string msg = DescribePlan(plan);
            if (HasPendingPreviewChanges())
                msg += "\n\n⚠️ Los cambios de preview no aplicados en la tabla se descartarán.";

            if (TaskDialog.Show("Confirmar Desplazamiento", msg, TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No) != TaskDialogResult.Yes)
                return;

            using (var trans = new Transaction(_doc, $"ProSheets: Desplazar láminas {(delta > 0 ? "+" : "")}{delta}"))
            {
                try
                {
                    trans.Start();
                    ExecuteShiftPlan(plan);
                    trans.Commit();
                }
                catch (Exception ex)
                {
                    if (trans.HasStarted()) trans.RollBack();
                    TaskDialog.Show("ProSheets - Error", "No se pudo desplazar. No se realizó ningún cambio.\n\n" + ex.Message);
                    return;
                }
            }

            LoadModelSheets();
            SelectSheetInGrid(reference.SheetId);
        }

        private void SelectSheetInGrid(ElementId id)
        {
            var item = _displayedSheets.FirstOrDefault(s => s.SheetId == id);
            if (item == null) return;
            dgSheets.SelectedItem = item;
            dgSheets.ScrollIntoView(item);
        }
    }

    /// <summary>Orden natural: "A-2" &lt; "A-10" &lt; "A-100".</summary>
    internal sealed class NaturalStringComparer : IComparer<string>
    {
        public static readonly NaturalStringComparer Instance = new NaturalStringComparer();

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            int i = 0, j = 0;
            while (i < x.Length && j < y.Length)
            {
                if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
                {
                    int si = i, sj = j;
                    while (i < x.Length && char.IsDigit(x[i])) i++;
                    while (j < y.Length && char.IsDigit(y[j])) j++;

                    string a = x.Substring(si, i - si).TrimStart('0');
                    string b = y.Substring(sj, j - sj).TrimStart('0');
                    if (a.Length != b.Length) return a.Length.CompareTo(b.Length);
                    int c = string.CompareOrdinal(a, b);
                    if (c != 0) return c;
                }
                else
                {
                    int c = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                    if (c != 0) return c;
                    i++; j++;
                }
            }
            return (x.Length - i).CompareTo(y.Length - j);
        }
    }
}
