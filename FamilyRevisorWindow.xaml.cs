using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace RevitToSupabasePlugin
{
    public enum RevisorValidationStatus
    {
        Unchanged,
        Modified,
        Valid,
        Error
    }

    public class FamilyRevisorItem : INotifyPropertyChanged
    {
        private static readonly char[] RevitForbiddenChars = new[] { '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~' };

        private bool _isSelected = true;
        private string _newFamilyName = string.Empty;
        private string _newTypeName = string.Empty;
        private string _statusText = "Sin cambios";
        private RevisorValidationStatus _validationStatus = RevisorValidationStatus.Unchanged;
        private string _errorMessage = string.Empty;

        public ElementId? FamilyId { get; set; }
        public ElementId? TypeId { get; set; }
        public bool IsLoadableFamily { get; set; }

        public string CategoryName { get; set; } = string.Empty;
        public string OriginalFamilyName { get; set; } = string.Empty;
        public string OriginalTypeName { get; set; } = string.Empty;

        public string FamilyKindText => IsLoadableFamily ? "Cargable (.rfa)" : "Sistema";

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

        public string NewFamilyName
        {
            get => _newFamilyName;
            set
            {
                if (_newFamilyName != value)
                {
                    _newFamilyName = value;
                    OnPropertyChanged(nameof(NewFamilyName));
                    Validate();
                }
            }
        }

        public string NewTypeName
        {
            get => _newTypeName;
            set
            {
                if (_newTypeName != value)
                {
                    _newTypeName = value;
                    OnPropertyChanged(nameof(NewTypeName));
                    Validate();
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

        public RevisorValidationStatus ValidationStatus
        {
            get => _validationStatus;
            set
            {
                if (_validationStatus != value)
                {
                    _validationStatus = value;
                    OnPropertyChanged(nameof(ValidationStatus));
                    OnPropertyChanged(nameof(HasError));
                }
            }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set
            {
                if (_errorMessage != value)
                {
                    _errorMessage = value;
                    OnPropertyChanged(nameof(ErrorMessage));
                }
            }
        }

        public bool HasError => _validationStatus == RevisorValidationStatus.Error;

        public void Validate()
        {
            bool familyChanged = !string.IsNullOrEmpty(NewFamilyName) && NewFamilyName != OriginalFamilyName;
            bool typeChanged = !string.IsNullOrEmpty(NewTypeName) && NewTypeName != OriginalTypeName;

            // 1. Validar caracteres ilegales en nombre de familia
            if (familyChanged && IsLoadableFamily)
            {
                if (string.IsNullOrWhiteSpace(NewFamilyName))
                {
                    SetError("El nombre de familia no puede estar vacío");
                    return;
                }

                char badChar = NewFamilyName.FirstOrDefault(c => RevitForbiddenChars.Contains(c));
                if (badChar != '\0')
                {
                    SetError($"Carácter prohibido '{badChar}' en nombre de familia");
                    return;
                }
            }

            // 2. Validar caracteres ilegales en nombre de tipo
            if (typeChanged && TypeId != null)
            {
                if (string.IsNullOrWhiteSpace(NewTypeName))
                {
                    SetError("El nombre de tipo no puede estar vacío");
                    return;
                }

                char badChar = NewTypeName.FirstOrDefault(c => RevitForbiddenChars.Contains(c));
                if (badChar != '\0')
                {
                    SetError($"Carácter prohibido '{badChar}' en nombre de tipo");
                    return;
                }
            }

            // Si pasa validación básica
            ErrorMessage = string.Empty;

            if (familyChanged && typeChanged)
            {
                ValidationStatus = RevisorValidationStatus.Modified;
                StatusText = "✏️ Familia y Tipo modificados";
            }
            else if (familyChanged)
            {
                ValidationStatus = RevisorValidationStatus.Modified;
                StatusText = "✏️ Familia modificada";
            }
            else if (typeChanged)
            {
                ValidationStatus = RevisorValidationStatus.Modified;
                StatusText = "✏️ Tipo modificado";
            }
            else
            {
                ValidationStatus = RevisorValidationStatus.Unchanged;
                StatusText = "Sin cambios";
            }
        }

        public void SetError(string message)
        {
            ValidationStatus = RevisorValidationStatus.Error;
            ErrorMessage = message;
            StatusText = $"❌ {message}";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    public partial class FamilyRevisorWindow : Window
    {
        private readonly UIDocument _uidoc;
        private readonly Document _doc;
        private readonly List<FamilyRevisorItem> _allItems = new();
        private readonly ObservableCollection<FamilyRevisorItem> _displayedItems = new();
        private bool _isInitialized = false;

        // Pila para Deshacer (Undo) inspirado en DiRoots
        private readonly Stack<List<(ElementId? famId, ElementId? typeId, string prevFamName, string prevTypeName)>> _undoHistory = new();

        public FamilyRevisorWindow(UIDocument uidoc)
        {
            _uidoc = uidoc ?? throw new ArgumentNullException(nameof(uidoc));
            _doc = uidoc.Document;

            InitializeComponent();

            _isInitialized = true;

            if (dgElements != null)
                dgElements.ItemsSource = _displayedItems;

            LoadModelFamilies();
        }

        private void ScopeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            LoadModelFamilies();
        }

        private void BtnReload_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            LoadModelFamilies();
        }

        private void LoadModelFamilies()
        {
            if (!_isInitialized || _doc == null || cmbCategories == null) return;

            _allItems.Clear();
            var categories = new HashSet<string>();

            bool scopeActiveView = rbScopeActiveView?.IsChecked == true;
            bool scopeSelection = rbScopeSelection?.IsChecked == true;

            HashSet<ElementId>? validFamilyIds = null;
            HashSet<ElementId>? validTypeIds = null;

            // Filtrado por Ámbito (DiRoots Scope)
            if (scopeSelection)
            {
                var selIds = _uidoc.Selection.GetElementIds();
                validFamilyIds = new HashSet<ElementId>();
                validTypeIds = new HashSet<ElementId>();

                foreach (var id in selIds)
                {
                    var elem = _doc.GetElement(id);
                    if (elem == null) continue;

                    ElementId tId = elem.GetTypeId();
                    if (tId != null && tId != ElementId.InvalidElementId)
                    {
                        validTypeIds.Add(tId);
                        if (_doc.GetElement(tId) is FamilySymbol fs && fs.Family != null)
                        {
                            validFamilyIds.Add(fs.Family.Id);
                        }
                    }
                }
            }
            else if (scopeActiveView && _doc.ActiveView != null)
            {
                validFamilyIds = new HashSet<ElementId>();
                validTypeIds = new HashSet<ElementId>();

                var elementsInView = new FilteredElementCollector(_doc, _doc.ActiveView.Id)
                    .WhereElementIsNotElementType()
                    .ToElements();

                foreach (var elem in elementsInView)
                {
                    ElementId tId = elem.GetTypeId();
                    if (tId != null && tId != ElementId.InvalidElementId)
                    {
                        validTypeIds.Add(tId);
                        if (_doc.GetElement(tId) is FamilySymbol fs && fs.Family != null)
                        {
                            validFamilyIds.Add(fs.Family.Id);
                        }
                    }
                }
            }

            // 1. Familias Cargables y sus Tipos
            try
            {
                var families = new FilteredElementCollector(_doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .ToList();

                foreach (var fam in families)
                {
                    try
                    {
                        if (fam == null) continue;
                        if (validFamilyIds != null && !validFamilyIds.Contains(fam.Id))
                            continue;

                        string catName = "General";
                        try
                        {
                            if (fam.FamilyCategory != null && !string.IsNullOrEmpty(fam.FamilyCategory.Name))
                                catName = fam.FamilyCategory.Name;
                        }
                        catch { }

                        categories.Add(catName);

                        string famName = fam.Name ?? "(Sin nombre)";
                        ICollection<ElementId>? symbolIds = null;
                        try { symbolIds = fam.GetFamilySymbolIds(); } catch { }

                        if (symbolIds == null || symbolIds.Count == 0)
                        {
                            _allItems.Add(new FamilyRevisorItem
                            {
                                FamilyId = fam.Id,
                                TypeId = null,
                                IsLoadableFamily = true,
                                CategoryName = catName,
                                OriginalFamilyName = famName,
                                NewFamilyName = famName,
                                OriginalTypeName = "(Sin tipo)",
                                NewTypeName = "(Sin tipo)"
                            });
                        }
                        else
                        {
                            foreach (ElementId sId in symbolIds)
                            {
                                if (validTypeIds != null && !validTypeIds.Contains(sId))
                                    continue;

                                try
                                {
                                    if (_doc.GetElement(sId) is FamilySymbol sym)
                                    {
                                        string symName = sym.Name ?? "(Sin tipo)";
                                        _allItems.Add(new FamilyRevisorItem
                                        {
                                            FamilyId = fam.Id,
                                            TypeId = sym.Id,
                                            IsLoadableFamily = true,
                                            CategoryName = catName,
                                            OriginalFamilyName = famName,
                                            NewFamilyName = famName,
                                            OriginalTypeName = symName,
                                            NewTypeName = symName
                                        });
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            // 2. Familias de Sistema (Muros, Suelos, Techos, etc.)
            try
            {
                var elementTypes = new FilteredElementCollector(_doc)
                    .WhereElementIsElementType()
                    .Cast<ElementType>()
                    .ToList();

                foreach (var type in elementTypes)
                {
                    try
                    {
                        if (type == null) continue;
                        if (type is FamilySymbol) continue; // Ya procesado como cargable

                        Category? cat = null;
                        try { cat = type.Category; } catch { }
                        if (cat == null) continue;

                        try
                        {
                            if (cat.CategoryType != CategoryType.Model) continue;
                        }
                        catch { continue; }

                        if (validTypeIds != null && !validTypeIds.Contains(type.Id)) continue;

                        string catName = "Sistema";
                        try { if (!string.IsNullOrEmpty(cat.Name)) catName = cat.Name; } catch { }
                        categories.Add(catName);

                        string famName = string.Empty;
                        try
                        {
                            famName = type.get_Parameter(BuiltInParameter.ALL_MODEL_FAMILY_NAME)?.AsString() ?? string.Empty;
                        }
                        catch { }

                        if (string.IsNullOrWhiteSpace(famName))
                        {
                            try { famName = type.FamilyName; } catch { }
                        }
                        if (string.IsNullOrWhiteSpace(famName))
                        {
                            try { famName = type.GetType().Name.Replace("Type", ""); } catch { }
                        }

                        string typeName = "(Sin nombre)";
                        try { if (!string.IsNullOrEmpty(type.Name)) typeName = type.Name; } catch { }

                        _allItems.Add(new FamilyRevisorItem
                        {
                            FamilyId = null,
                            TypeId = type.Id,
                            IsLoadableFamily = false,
                            CategoryName = catName,
                            OriginalFamilyName = famName,
                            NewFamilyName = famName,
                            OriginalTypeName = typeName,
                            NewTypeName = typeName
                        });
                    }
                    catch { }
                }
            }
            catch { }

            // Poblar ComboBox de Categorías
            string prevSelectedCat = cmbCategories.SelectedItem?.ToString() ?? "📌 Todas las Categorías";
            cmbCategories.Items.Clear();
            cmbCategories.Items.Add("📌 Todas las Categorías");
            foreach (var c in categories.OrderBy(c => c))
            {
                cmbCategories.Items.Add(c);
            }

            if (cmbCategories.Items.Contains(prevSelectedCat))
                cmbCategories.SelectedItem = prevSelectedCat;
            else
                cmbCategories.SelectedIndex = 0;

            FilterAndRefreshGrid();
        }

        private void FilterAndRefreshGrid()
        {
            if (!_isInitialized || cmbCategories == null || txtSearchFilter == null) return;

            string selectedCat = cmbCategories.SelectedItem?.ToString() ?? "📌 Todas las Categorías";
            string search = txtSearchFilter?.Text?.Trim().ToLowerInvariant() ?? string.Empty;

            var filtered = _allItems.AsEnumerable();

            if (selectedCat != "📌 Todas las Categorías" && !string.IsNullOrEmpty(selectedCat))
            {
                filtered = filtered.Where(i => i.CategoryName == selectedCat);
            }

            if (!string.IsNullOrEmpty(search))
            {
                filtered = filtered.Where(i =>
                    i.CategoryName.ToLowerInvariant().Contains(search) ||
                    i.OriginalFamilyName.ToLowerInvariant().Contains(search) ||
                    i.OriginalTypeName.ToLowerInvariant().Contains(search) ||
                    i.NewFamilyName.ToLowerInvariant().Contains(search) ||
                    i.NewTypeName.ToLowerInvariant().Contains(search));
            }

            _displayedItems.Clear();
            foreach (var item in filtered)
            {
                _displayedItems.Add(item);
            }

            ValidateUniqueness();
            UpdateSummaryLabel();
        }

        private void ValidateUniqueness()
        {
            // Validar unicidad preventiva (como DiRoots)
            // 1. Unicidad de nombres de familias cargables
            var familyGroups = _allItems
                .Where(i => i.IsLoadableFamily && !string.IsNullOrEmpty(i.NewFamilyName))
                .GroupBy(i => i.NewFamilyName.Trim().ToLowerInvariant());

            foreach (var grp in familyGroups)
            {
                var distinctFamIds = grp.Select(i => i.FamilyId).Where(id => id != null).Distinct().ToList();
                if (distinctFamIds.Count > 1)
                {
                    foreach (var item in grp)
                    {
                        item.SetError($"Colisión: Dos familias distintas terminarían con el nombre '{item.NewFamilyName}'");
                    }
                }
            }

            // 2. Unicidad de tipos dentro de una misma familia
            var typeGroups = _allItems
                .Where(i => i.TypeId != null && !string.IsNullOrEmpty(i.NewTypeName))
                .GroupBy(i => $"{i.NewFamilyName.Trim().ToLowerInvariant()}|||{i.NewTypeName.Trim().ToLowerInvariant()}");

            foreach (var grp in typeGroups)
            {
                var distinctTypeIds = grp.Select(i => i.TypeId).Where(id => id != null).Distinct().ToList();
                if (distinctTypeIds.Count > 1)
                {
                    foreach (var item in grp)
                    {
                        item.SetError($"Colisión: Dos tipos con el mismo nombre '{item.NewTypeName}' en la familia");
                    }
                }
            }
        }

        private void ApplyRulesToDisplayedItems()
        {
            if (!_isInitialized || txtFind == null || txtReplace == null || cmbScope == null || cmbCaseFormat == null) return;

            string find = txtFind?.Text ?? string.Empty;
            string replace = txtReplace?.Text ?? string.Empty;
            bool matchCase = chkMatchCase?.IsChecked == true;
            string prefix = txtPrefix?.Text ?? string.Empty;
            string suffix = txtSuffix?.Text ?? string.Empty;
            string caseFormat = (cmbCaseFormat?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Mantener Original";
            string scope = (cmbScope?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Familias y Tipos";

            bool modifyFamilies = scope == "Familias y Tipos" || scope == "Solo Familias";
            bool modifyTypes = scope == "Familias y Tipos" || scope == "Solo Tipos";

            foreach (var item in _displayedItems)
            {
                if (modifyFamilies && item.IsLoadableFamily)
                {
                    item.NewFamilyName = TransformText(item.OriginalFamilyName, find, replace, matchCase, prefix, suffix, caseFormat);
                }
                else
                {
                    item.NewFamilyName = item.OriginalFamilyName;
                }

                if (modifyTypes && item.TypeId != null)
                {
                    item.NewTypeName = TransformText(item.OriginalTypeName, find, replace, matchCase, prefix, suffix, caseFormat);
                }
                else
                {
                    item.NewTypeName = item.OriginalTypeName;
                }
            }

            ValidateUniqueness();
            UpdateSummaryLabel();
        }

        private static string TransformText(string original, string find, string replace, bool matchCase, string prefix, string suffix, string caseFormat)
        {
            if (string.IsNullOrEmpty(original)) return string.Empty;
            string result = original;

            // 1. Buscar y reemplazar
            if (!string.IsNullOrEmpty(find))
            {
                var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                int pos = result.IndexOf(find, comparison);
                while (pos >= 0)
                {
                    result = result.Substring(0, pos) + replace + result.Substring(pos + find.Length);
                    pos = result.IndexOf(find, pos + replace.Length, comparison);
                }
            }

            // 2. Prefijo y Sufijo
            if (!string.IsNullOrEmpty(prefix)) result = prefix + result;
            if (!string.IsNullOrEmpty(suffix)) result = result + suffix;

            // 3. Formato de Texto
            switch (caseFormat)
            {
                case "MAYÚSCULAS":
                    result = result.ToUpperInvariant();
                    break;
                case "minúsculas":
                    result = result.ToLowerInvariant();
                    break;
                case "Formato Título":
                    result = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(result.ToLowerInvariant());
                    break;
            }

            return result;
        }

        private void UpdateSummaryLabel()
        {
            if (lblStatusSummary == null) return;

            int total = _displayedItems.Count;
            int changed = _displayedItems.Count(i => i.IsSelected && (i.NewFamilyName != i.OriginalFamilyName || i.NewTypeName != i.OriginalTypeName));
            int errors = _displayedItems.Count(i => i.IsSelected && i.HasError);

            if (errors > 0)
            {
                lblStatusSummary.Text = $"Total: {total} | {changed} con cambios | ⚠️ {errors} CON ERRORES O COLISIÓN";
            }
            else
            {
                lblStatusSummary.Text = $"Total: {total} | {changed} seleccionados con cambios listos para aplicar";
            }
        }

        private void RuleInputs_Changed(object sender, EventArgs e)
        {
            if (!_isInitialized) return;
            ApplyRulesToDisplayedItems();
        }

        private void CmbCategories_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            FilterAndRefreshGrid();
            ApplyRulesToDisplayedItems();
        }

        private void CmbScope_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            ApplyRulesToDisplayedItems();
        }

        private void TxtSearchFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized) return;
            FilterAndRefreshGrid();
        }

        private void DgElements_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (!_isInitialized) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ValidateUniqueness();
                UpdateSummaryLabel();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void BtnResetRules_Click(object sender, RoutedEventArgs e)
        {
            txtFind.Text = string.Empty;
            txtReplace.Text = string.Empty;
            txtPrefix.Text = string.Empty;
            txtSuffix.Text = string.Empty;
            chkMatchCase.IsChecked = false;
            cmbCaseFormat.SelectedIndex = 0;

            foreach (var item in _allItems)
            {
                item.NewFamilyName = item.OriginalFamilyName;
                item.NewTypeName = item.OriginalTypeName;
                item.Validate();
            }

            ValidateUniqueness();
            UpdateSummaryLabel();
        }

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _displayedItems) item.IsSelected = true;
            UpdateSummaryLabel();
        }

        private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _displayedItems) item.IsSelected = false;
            UpdateSummaryLabel();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

        // ══════════════════════════════════════════════════════════════════════════
        // EXPORTADOR MASIVO DE FAMILIAS .RFA A CARPETA (Inspirado en DiRoots tabExport)
        // ══════════════════════════════════════════════════════════════════════════
        private void BtnExportRfa_Click(object sender, RoutedEventArgs e)
        {
            var loadableItems = _allItems
                .Where(i => i.IsSelected && i.IsLoadableFamily && i.FamilyId != null)
                .GroupBy(i => i.FamilyId)
                .Select(g => g.First())
                .ToList();

            if (loadableItems.Count == 0)
            {
                TaskDialog.Show("Family Revisor", "No hay familias cargables (.rfa) seleccionadas en la lista para exportar.");
                return;
            }

            var dialog = new OpenFolderDialog
            {
                Title = $"Seleccionar carpeta para exportar {loadableItems.Count} familias (.rfa)",
                Multiselect = false
            };

            if (dialog.ShowDialog() != true) return;
            string targetFolder = dialog.FolderName;

            int exported = 0;
            var errors = new List<string>();

            foreach (var item in loadableItems)
            {
                try
                {
                    if (_doc.GetElement(item.FamilyId!) is Family fam && fam.IsEditable)
                    {
                        string safeCat = SanitizeFolderName(item.CategoryName);
                        string catFolder = Path.Combine(targetFolder, safeCat);
                        Directory.CreateDirectory(catFolder);

                        Document famDoc = _doc.EditFamily(fam);
                        string fileName = SanitizeFileName(fam.Name) + ".rfa";
                        string fullPath = Path.Combine(catFolder, fileName);

                        SaveAsOptions saveOptions = new SaveAsOptions
                        {
                            OverwriteExistingFile = true,
                            Compact = true
                        };

                        famDoc.SaveAs(fullPath, saveOptions);
                        famDoc.Close(false);
                        exported++;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Familia '{item.OriginalFamilyName}': {ex.Message}");
                }
            }

            string resultMsg = $"💾 Exportación Finalizada:\n\n• {exported} familias guardadas en:\n{targetFolder}\n(Clasificadas en subcarpetas por categoría)";
            if (errors.Count > 0)
            {
                resultMsg += $"\n\n⚠️ {errors.Count} incidencias:\n" + string.Join("\n", errors.Take(5));
            }

            TaskDialog.Show("Exportador de Familias", resultMsg);
        }

        // ══════════════════════════════════════════════════════════════════════════
        // EXPORTAR E IMPORTAR CSV (Inspirado en DiRoots SheetLink / Excel Sync)
        // ══════════════════════════════════════════════════════════════════════════
        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Archivo CSV (*.csv)|*.csv",
                Title = "Exportar Nomenclatura a CSV",
                FileName = $"FamilyRevisor_{DateTime.Now:yyyyMMdd_HHmm}.csv"
            };

            if (saveDialog.ShowDialog() != true) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("FamilyId;TypeId;IsLoadable;Category;OriginalFamily;NewFamily;OriginalType;NewType");

                foreach (var item in _allItems)
                {
                    string fId = item.FamilyId?.ToString() ?? "";
                    string tId = item.TypeId?.ToString() ?? "";
                    string isLoad = item.IsLoadableFamily ? "1" : "0";
                    string cat = EscapeCsv(item.CategoryName);
                    string origFam = EscapeCsv(item.OriginalFamilyName);
                    string newFam = EscapeCsv(item.NewFamilyName);
                    string origType = EscapeCsv(item.OriginalTypeName);
                    string newType = EscapeCsv(item.NewTypeName);

                    sb.AppendLine($"{fId};{tId};{isLoad};{cat};{origFam};{newFam};{origType};{newType}");
                }

                File.WriteAllText(saveDialog.FileName, sb.ToString(), Encoding.UTF8);
                TaskDialog.Show("Family Revisor", $"✅ Archivo exportado con éxito ({_allItems.Count} registros):\n{saveDialog.FileName}");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", $"No se pudo exportar el CSV: {ex.Message}");
            }
        }

        private void BtnImportCsv_Click(object sender, RoutedEventArgs e)
        {
            var openDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Archivo CSV (*.csv)|*.csv",
                Title = "Importar Nombres desde CSV"
            };

            if (openDialog.ShowDialog() != true) return;

            try
            {
                var lines = File.ReadAllLines(openDialog.FileName, Encoding.UTF8);
                if (lines.Length <= 1)
                {
                    TaskDialog.Show("Family Revisor", "El archivo CSV está vacío o solo contiene encabezados.");
                    return;
                }

                int updatedCount = 0;
                char separator = lines[0].Contains(';') ? ';' : ',';

                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var parts = line.Split(separator);
                    if (parts.Length < 8) continue;

                    string fIdStr = parts[0].Trim();
                    string tIdStr = parts[1].Trim();
                    string newFam = parts[5].Trim().Trim('"');
                    string newType = parts[7].Trim().Trim('"');

                    // Buscar el item correspondiente
                    FamilyRevisorItem? match = null;
                    if (!string.IsNullOrEmpty(tIdStr) && int.TryParse(tIdStr, out int tIntId))
                    {
                        match = _allItems.FirstOrDefault(x => x.TypeId != null && x.TypeId.Value == tIntId);
                    }
                    else if (!string.IsNullOrEmpty(fIdStr) && int.TryParse(fIdStr, out int fIntId))
                    {
                        match = _allItems.FirstOrDefault(x => x.FamilyId != null && x.FamilyId.Value == fIntId);
                    }

                    if (match != null)
                    {
                        if (!string.IsNullOrEmpty(newFam) && match.IsLoadableFamily) match.NewFamilyName = newFam;
                        if (!string.IsNullOrEmpty(newType)) match.NewTypeName = newType;
                        updatedCount++;
                    }
                }

                ValidateUniqueness();
                UpdateSummaryLabel();
                TaskDialog.Show("Family Revisor", $"✅ Importación completada.\nSe actualizaron {updatedCount} elementos con los nombres del CSV.");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", $"Error al leer el archivo CSV: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        // ASISTENTE DE ESTANDARIZACIÓN BIM (ISO 19650)
        // ══════════════════════════════════════════════════════════════════════════
        private void BtnStandardizeBim_Click(object sender, RoutedEventArgs e)
        {
            int standardized = 0;

            foreach (var item in _displayedItems.Where(i => i.IsSelected))
            {
                // Limpiar sufijos basura típicos de Revit (.0001, Copy (1), _copia, etc.)
                string fam = CleanBasuraNombres(item.OriginalFamilyName);
                string typ = CleanBasuraNombres(item.OriginalTypeName);

                // Normalizar espacios dobles a guión bajo
                fam = string.Join("_", fam.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
                typ = string.Join("_", typ.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

                if (item.IsLoadableFamily) item.NewFamilyName = fam;
                if (item.TypeId != null) item.NewTypeName = typ;

                standardized++;
            }

            ValidateUniqueness();
            UpdateSummaryLabel();
            TaskDialog.Show("Estandarización BIM", $"🤖 Se han estandarizado {standardized} elementos.\nSe han limpiado sufijos repetidos, copias y espacios.");
        }

        private static string CleanBasuraNombres(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            string res = input;

            string[] basuras = new[] { ".0001", ".0002", ".0003", "Copy (1)", "Copy (2)", "Copia (1)", "Copia (2)", "_copia", "_Copia", "(1)", "(2)" };
            foreach (var b in basuras)
            {
                res = res.Replace(b, "");
            }

            return res.Trim();
        }

        // ══════════════════════════════════════════════════════════════════════════
        // APLICAR RENOMBRAMIENTO Y DESHACER (UNDO)
        // ══════════════════════════════════════════════════════════════════════════
        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            var itemsToChange = _allItems
                .Where(i => i.IsSelected && (i.NewFamilyName != i.OriginalFamilyName || i.NewTypeName != i.OriginalTypeName))
                .ToList();

            if (itemsToChange.Count == 0)
            {
                TaskDialog.Show("Family Revisor", "No hay elementos seleccionados con cambios proyectados.");
                return;
            }

            var errorsInSelection = itemsToChange.Where(i => i.HasError).ToList();
            if (errorsInSelection.Count > 0)
            {
                TaskDialog.Show("Advertencia de Validación",
                    $"⚠️ Existen {errorsInSelection.Count} elementos con errores de validación o colisión de nombres.\n\nPor favor corrige los nombres marcados en rojo antes de aplicar la transacción.");
                return;
            }

            var confirm = TaskDialog.Show("Confirmar Renombramiento",
                $"¿Deseas aplicar el renombramiento de {itemsToChange.Count} elementos en el proyecto activo?",
                TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);

            if (confirm != TaskDialogResult.Yes) return;

            // Guardar snapshot para Deshacer (Undo)
            var undoSnapshot = itemsToChange.Select(i => (i.FamilyId, i.TypeId, i.OriginalFamilyName, i.OriginalTypeName)).ToList();

            int familiesRenamed = 0;
            int typesRenamed = 0;
            var errors = new List<string>();

            var familyGroups = itemsToChange
                .Where(i => i.IsLoadableFamily && i.FamilyId != null && i.NewFamilyName != i.OriginalFamilyName)
                .GroupBy(i => i.FamilyId)
                .ToList();

            using (Transaction trans = new Transaction(_doc, "Family Revisor: Renombramiento Masivo"))
            {
                trans.Start();

                // 1. Renombrar Familias
                foreach (var grp in familyGroups)
                {
                    try
                    {
                        var item = grp.First();
                        if (_doc.GetElement(item.FamilyId!) is Family fam)
                        {
                            fam.Name = item.NewFamilyName;
                            familiesRenamed++;
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Familia '{grp.First().OriginalFamilyName}': {ex.Message}");
                    }
                }

                // 2. Renombrar Tipos
                foreach (var item in itemsToChange)
                {
                    if (item.TypeId == null || item.NewTypeName == item.OriginalTypeName) continue;

                    try
                    {
                        if (_doc.GetElement(item.TypeId) is ElementType type)
                        {
                            type.Name = item.NewTypeName;
                            typesRenamed++;
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Tipo '{item.OriginalTypeName}': {ex.Message}");
                    }
                }

                trans.Commit();
            }

            _undoHistory.Push(undoSnapshot);
            btnUndo.IsEnabled = true;

            string resultMsg = $"✅ Proceso completado exitosamente.\n\n• Familias renombradas: {familiesRenamed}\n• Tipos renombrados: {typesRenamed}";
            if (errors.Count > 0)
            {
                resultMsg += $"\n\n⚠️ Ocurrieron {errors.Count} advertencias/errores:\n" + string.Join("\n", errors.Take(5));
            }

            TaskDialog.Show("Family Revisor", resultMsg);
            LoadModelFamilies();
        }

        private void BtnUndo_Click(object sender, RoutedEventArgs e)
        {
            if (_undoHistory.Count == 0) return;

            var snapshot = _undoHistory.Pop();
            btnUndo.IsEnabled = _undoHistory.Count > 0;

            var confirm = TaskDialog.Show("Deshacer Renombramiento",
                $"¿Deseas revertir los nombres de {snapshot.Count} elementos a su estado anterior?",
                TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);

            if (confirm != TaskDialogResult.Yes) return;

            int reverted = 0;
            var errors = new List<string>();

            using (Transaction trans = new Transaction(_doc, "Family Revisor: Deshacer Renombramiento"))
            {
                trans.Start();

                // Revertir tipos
                foreach (var s in snapshot)
                {
                    if (s.typeId != null)
                    {
                        try
                        {
                            if (_doc.GetElement(s.typeId) is ElementType type)
                            {
                                type.Name = s.prevTypeName;
                                reverted++;
                            }
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"Error al revertir tipo {s.prevTypeName}: {ex.Message}");
                        }
                    }
                }

                // Revertir familias
                var famSnapshots = snapshot.Where(s => s.famId != null).GroupBy(s => s.famId).Select(g => g.First());
                foreach (var s in famSnapshots)
                {
                    try
                    {
                        if (_doc.GetElement(s.famId!) is Family fam)
                        {
                            fam.Name = s.prevFamName;
                            reverted++;
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Error al revertir familia {s.prevFamName}: {ex.Message}");
                    }
                }

                trans.Commit();
            }

            TaskDialog.Show("Deshacer", $"↩️ Se han revertido {reverted} nombres con éxito.");
            LoadModelFamilies();
        }

        // Helpers de nombres de archivo y CSV
        private static string SanitizeFolderName(string folder)
        {
            char[] invalids = Path.GetInvalidPathChars().Concat(Path.GetInvalidFileNameChars()).ToArray();
            return string.Join("_", folder.Split(invalids, StringSplitOptions.RemoveEmptyEntries)).Trim();
        }

        private static string SanitizeFileName(string name)
        {
            char[] invalids = Path.GetInvalidFileNameChars();
            return string.Join("_", name.Split(invalids, StringSplitOptions.RemoveEmptyEntries)).Trim();
        }

        private static string EscapeCsv(string val)
        {
            if (string.IsNullOrEmpty(val)) return "";
            if (val.Contains(';') || val.Contains('"') || val.Contains('\n'))
            {
                return $"\"{val.Replace("\"", "\"\"")}\"";
            }
            return val;
        }
    }
}
