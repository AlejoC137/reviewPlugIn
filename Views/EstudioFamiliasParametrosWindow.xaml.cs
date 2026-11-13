using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitToSupabasePlugin.Models;
using RevitToSupabasePlugin.Services;
using Application = Autodesk.Revit.ApplicationServices.Application;
using ComboBox = System.Windows.Controls.ComboBox;
using MessageBox = System.Windows.MessageBox;

namespace RevitToSupabasePlugin.Views
{
    public class StudioExternalEventHandler : IExternalEventHandler
    {
        public Action<UIApplication>? PendingAction { get; set; }

        public void Execute(UIApplication app)
        {
            var action = PendingAction;
            PendingAction = null;
            if (action == null) return;

            try
            {
                action(app);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EstudioFamiliasParametros] Error ExternalEvent: {ex.Message}");
            }
        }

        public string GetName() => "ARCA_FamilyParameterStudio_Event";
    }

    public partial class EstudioFamiliasParametrosWindow : Window
    {
        private readonly UIApplication _uiApp;
        private readonly StudioExternalEventHandler _extHandler;
        private readonly ExternalEvent _extEvent;

        private List<LoadableFamilySummaryItem> _allProjectFamilies = new List<LoadableFamilySummaryItem>();
        private readonly ObservableCollection<LoadableFamilySummaryItem> _filteredProjectFamilies = new ObservableCollection<LoadableFamilySummaryItem>();
        private readonly ObservableCollection<NestedFamilyNode> _treeRoots = new ObservableCollection<NestedFamilyNode>();

        private List<NativeParameterItem> _allNativeParams = new List<NativeParameterItem>();
        private readonly ObservableCollection<NativeParameterItem> _filteredNativeParams = new ObservableCollection<NativeParameterItem>();

        private readonly List<RevitForgeOption> _revitGroups;
        private readonly List<RevitForgeOption> _revitDataTypes;

        private long _currentRootFamilyId = -1;
        private NestedFamilyNode? _selectedTreeNode;
        private NativeParameterItem? _selectedNativeParam;
        private ElementId? _inspectedElementId;
        private bool _isPopulatingUi;

        public EstudioFamiliasParametrosWindow(UIApplication uiApp)
        {
            InitializeComponent();
            _uiApp = uiApp;
            _extHandler = new StudioExternalEventHandler();
            _extEvent = ExternalEvent.Create(_extHandler);

            _revitGroups = NativeParameterService.GetCommonRevitGroups();
            _revitDataTypes = NativeParameterService.GetCommonRevitDataTypes();

            lstLoadableFamilies.ItemsSource = _filteredProjectFamilies;
            tvNestedTree.ItemsSource = _treeRoots;
            dgNativeParameters.ItemsSource = _filteredNativeParams;

            cmbCreateParamDataType.ItemsSource = _revitDataTypes;
            cmbCreateParamDataType.SelectedIndex = 0;

            cmbCreateParamGroup.ItemsSource = _revitGroups;
            cmbCreateParamGroup.SelectedIndex = 0;

            cmbEditGroup.ItemsSource = _revitGroups;
            cmbEditGroup.SelectedIndex = 0;

            RunInRevitContext(RefreshContextFromActiveDoc);
        }

        private void RunInRevitContext(Action<UIApplication> action)
        {
            try
            {
                // Si estamos dentro de un comando modal o contexto válido, ejecutar directo; si no, encolar en ExternalEvent.
                if (_uiApp.ActiveUIDocument?.Document != null)
                {
                    _extHandler.PendingAction = action;
                    _extEvent.Raise();
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Error solicitando ejecución en Revit: {ex.Message}", isError: true);
            }
        }

        private void SetStatus(string msg, bool isError = false, string? guidInfo = null)
        {
            Dispatcher.Invoke(() =>
            {
                txtStatusBar.Text = msg;
                txtStatusBar.Foreground = isError
                    ? (System.Windows.Media.Brush)FindResource("ArcaError")
                    : (System.Windows.Media.Brush)FindResource("ArcaInk");
                txtStatusGuid.Text = guidInfo ?? string.Empty;
            });
        }

        // =========================================================================
        // SINCRONIZACIÓN DE CONTEXTO CON REVIT (.RVT O .RFA)
        // =========================================================================

        private void RefreshContextFromActiveDoc(UIApplication app)
        {
            UIDocument? uidoc = app.ActiveUIDocument;
            Document? doc = uidoc?.Document;
            if (doc == null)
            {
                SetStatus("Abre un proyecto (.rvt) o familia (.rfa) en Revit.", isError: true);
                return;
            }

            if (doc.IsFamilyDocument)
            {
                NestedFamilyNode? rootTree = NestedFamilyService.InspectFamilyTree(app.Application, doc, null);
                var docParams = NativeParameterService.InspectDocumentParameters(app.Application, doc);

                Dispatcher.Invoke(() =>
                {
                    _isPopulatingUi = true;
                    txtDocContextSubtitle.Text = $"🧩 Editor de Familias (.RFA): {doc.Title}";
                    cardProjectFamilies.Visibility = System.Windows.Visibility.Collapsed;
                    rowProjectFamilies.Height = new GridLength(0);
                    txtTreeHint.Text = "Árbol en vivo del .RFA activo";

                    _currentRootFamilyId = -1;
                    _treeRoots.Clear();
                    if (rootTree != null)
                    {
                        _treeRoots.Add(rootTree);
                        rootTree.IsSelected = true;
                        DisplaySelectedTreeNode(rootTree);
                    }

                    _allNativeParams = docParams;
                    ApplyNativeParamFilter();
                    _isPopulatingUi = false;
                    SetStatus($"Familia activa '{doc.Title}' cargada ({docParams.Count} parámetros).");
                });
            }
            else
            {
                var families = NestedFamilyService.GetLoadableFamiliesInProject(doc);
                Element? selectedElem = GetFirstSelectedElement(uidoc);
                NestedFamilyNode? autoTree = null;
                long autoFamId = -1;

                if (selectedElem is FamilyInstance fi && fi.Symbol?.Family != null && !fi.Symbol.Family.IsInPlace && fi.Symbol.Family.IsEditable)
                {
                    autoFamId = fi.Symbol.Family.Id.Value;
                    autoTree = NestedFamilyService.InspectFamilyTree(app.Application, doc, fi.Symbol.Family);
                }

                List<NativeParameterItem> paramsToShow = (rbParamScopeSelection.IsChecked == true && selectedElem != null)
                    ? NativeParameterService.InspectElementParameters(doc, selectedElem)
                    : NativeParameterService.InspectDocumentParameters(app.Application, doc);

                Dispatcher.Invoke(() =>
                {
                    _isPopulatingUi = true;
                    txtDocContextSubtitle.Text = $"📄 Proyecto (.RVT): {doc.Title}";
                    cardProjectFamilies.Visibility = System.Windows.Visibility.Visible;
                    rowProjectFamilies.Height = new GridLength(1.05, GridUnitType.Star);
                    txtTreeHint.Text = "Toca una familia para ver su árbol";

                    _allProjectFamilies = families;
                    ApplyProjectFamiliesFilter();

                    if (autoTree != null)
                    {
                        _currentRootFamilyId = autoFamId;
                        _treeRoots.Clear();
                        _treeRoots.Add(autoTree);
                        autoTree.IsSelected = true;
                        DisplaySelectedTreeNode(autoTree);

                        var match = _filteredProjectFamilies.FirstOrDefault(f => f.FamilyIdValue == autoFamId);
                        if (match != null)
                        {
                            match.NestedCount = autoTree.Children.Count;
                            lstLoadableFamilies.SelectedItem = match;
                            lstLoadableFamilies.ScrollIntoView(match);
                        }
                    }

                    _inspectedElementId = selectedElem?.Id;
                    _allNativeParams = paramsToShow;
                    ApplyNativeParamFilter();
                    _isPopulatingUi = false;
                    SetStatus($"Proyecto '{doc.Title}': {families.Count} familias cargables.");
                });
            }
        }

        private static Element? GetFirstSelectedElement(UIDocument? uidoc)
        {
            if (uidoc == null) return null;
            var ids = uidoc.Selection.GetElementIds();
            if (ids == null || ids.Count == 0) return null;
            return uidoc.Document.GetElement(ids.First());
        }

        // =========================================================================
        // EVENTOS DE BARRA SUPERIOR Y MODOS
        // =========================================================================

        private void MainMode_Changed(object sender, RoutedEventArgs e)
        {
            if (panelFamiliesMode == null || panelParametersMode == null) return;
            bool showFamilies = rbModeFamilies.IsChecked == true;
            panelFamiliesMode.Visibility = showFamilies ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            panelParametersMode.Visibility = showFamilies ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        }

        private void BtnRefreshContext_Click(object sender, RoutedEventArgs e)
        {
            RunInRevitContext(RefreshContextFromActiveDoc);
        }

        private void BtnInspectSelection_Click(object sender, RoutedEventArgs e)
        {
            RunInRevitContext(app =>
            {
                UIDocument? uidoc = app.ActiveUIDocument;
                Document? doc = uidoc?.Document;
                if (doc == null) return;

                Element? elem = GetFirstSelectedElement(uidoc);
                if (elem == null)
                {
                    SetStatus("No hay ningún elemento seleccionado en Revit.", isError: true);
                    return;
                }

                // Cargar parámetros nativos del elemento seleccionado
                var elemParams = NativeParameterService.InspectElementParameters(doc, elem);
                NestedFamilyNode? tree = null;
                long rootFamId = -1;

                if (elem is FamilyInstance fi && fi.Symbol?.Family != null && !fi.Symbol.Family.IsInPlace && fi.Symbol.Family.IsEditable)
                {
                    rootFamId = fi.Symbol.Family.Id.Value;
                    tree = NestedFamilyService.InspectFamilyTree(app.Application, doc, fi.Symbol.Family);
                }

                Dispatcher.Invoke(() =>
                {
                    _isPopulatingUi = true;
                    _inspectedElementId = elem.Id;

                    if (tree != null)
                    {
                        _currentRootFamilyId = rootFamId;
                        _treeRoots.Clear();
                        _treeRoots.Add(tree);
                        tree.IsSelected = true;
                        DisplaySelectedTreeNode(tree);

                        var match = _filteredProjectFamilies.FirstOrDefault(f => f.FamilyIdValue == rootFamId);
                        if (match != null)
                        {
                            match.NestedCount = tree.Children.Count;
                            lstLoadableFamilies.SelectedItem = match;
                            lstLoadableFamilies.ScrollIntoView(match);
                        }
                    }

                    rbParamScopeSelection.IsChecked = true;
                    _allNativeParams = elemParams;
                    ApplyNativeParamFilter();
                    _isPopulatingUi = false;

                    SetStatus($"Selección leída: '{elem.Name}' ({elemParams.Count} parámetros).");
                });
            });
        }

        private void BtnOpenAdvancedGestor_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new GestorParametrosWindow(_uiApp);
                win.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudo abrir el Gestor Avanzado:\n{ex.Message}", "ARCA Review", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // =========================================================================
        // MODO 1: EXPLORADOR DE FAMILIAS CARGABLES & ANIDADAS
        // =========================================================================

        private void TxtSearchFamilies_TextChanged(object sender, TextChangedEventArgs e) => ApplyProjectFamiliesFilter();
        private void ChkOnlyWithNested_Changed(object sender, RoutedEventArgs e) => ApplyProjectFamiliesFilter();

        private void ApplyProjectFamiliesFilter()
        {
            string q = (txtSearchFamilies?.Text ?? string.Empty).Trim();
            bool onlyNested = chkOnlyWithNested?.IsChecked == true;

            _filteredProjectFamilies.Clear();
            foreach (var item in _allProjectFamilies)
            {
                if (onlyNested && item.NestedCount <= 0) continue;
                if (!string.IsNullOrEmpty(q) &&
                    item.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 &&
                    item.CategoryName.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                _filteredProjectFamilies.Add(item);
            }
        }

        private void BtnScanAllNested_Click(object sender, RoutedEventArgs e)
        {
            SetStatus("Escaneando familias cargables para detectar familias anidadas...");
            RunInRevitContext(app =>
            {
                Document? doc = app.ActiveUIDocument?.Document;
                if (doc == null || doc.IsFamilyDocument) return;

                int withNested = 0;
                foreach (var item in _allProjectFamilies)
                {
                    if (doc.GetElement(new ElementId(item.FamilyIdValue)) is Family fam)
                    {
                        int cnt = NestedFamilyService.CountDirectNestedFamilies(doc, fam);
                        if (cnt > 0) withNested++;
                        Dispatcher.Invoke(() => item.NestedCount = cnt);
                    }
                }

                Dispatcher.Invoke(() =>
                {
                    ApplyProjectFamiliesFilter();
                    SetStatus($"Escaneo completado: {withNested} familia(s) con familias anidadas.");
                });
            });
        }

        private void LstLoadableFamilies_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isPopulatingUi) return;
            if (lstLoadableFamilies.SelectedItem is not LoadableFamilySummaryItem item) return;

            SetStatus($"Abriendo estructura de '{item.Name}'...");
            RunInRevitContext(app =>
            {
                Document? doc = app.ActiveUIDocument?.Document;
                if (doc == null) return;
                if (doc.GetElement(new ElementId(item.FamilyIdValue)) is not Family fam) return;

                NestedFamilyNode? root = NestedFamilyService.InspectFamilyTree(app.Application, doc, fam);
                Dispatcher.Invoke(() =>
                {
                    _isPopulatingUi = true;
                    _currentRootFamilyId = item.FamilyIdValue;
                    _treeRoots.Clear();
                    if (root != null)
                    {
                        item.NestedCount = root.Children.Count;
                        _treeRoots.Add(root);
                        root.IsSelected = true;
                        DisplaySelectedTreeNode(root);
                        SetStatus($"Estructura de '{item.Name}' cargada ({root.Children.Count} anidadas directas).");
                    }
                    _isPopulatingUi = false;
                });
            });
        }

        private void TvNestedTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_isPopulatingUi) return;
            if (e.NewValue is NestedFamilyNode node)
                DisplaySelectedTreeNode(node);
        }

        private void DisplaySelectedTreeNode(NestedFamilyNode node)
        {
            _isPopulatingUi = true;
            _selectedTreeNode = node;

            txtNodeTitle.Text = $"{node.NodeIcon} {node.Name}";
            txtNodeBreadcrumb.Text = $"{node.PathBreadcrumb}  ·  {node.NodeBadgeText}";
            txtNodeParamsCount.Text = $"{node.Parameters.Count} parámetro(s)";

            dgNodeParameters.ItemsSource = node.Parameters;
            dgNodeAssociations.ItemsSource = node.Associations;
            dgNodeTypes.ItemsSource = node.Types;

            bool canSelectInRevit = node.SampleInstanceIds.Count > 0 || (node.IsRoot && _currentRootFamilyId > 0);
            btnSelectNodeInRevit.IsEnabled = canSelectInRevit;
            btnOpenNodeInEditor.IsEnabled = string.IsNullOrEmpty(node.CycleWarning);

            _isPopulatingUi = false;
        }

        private void BtnSelectNodeInRevit_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTreeNode == null) return;
            var node = _selectedTreeNode;

            RunInRevitContext(app =>
            {
                UIDocument? uidoc = app.ActiveUIDocument;
                Document? doc = uidoc?.Document;
                if (uidoc == null || doc == null) return;

                var idsToSelect = new List<ElementId>();
                if (node.SampleInstanceIds.Count > 0)
                {
                    idsToSelect.AddRange(node.SampleInstanceIds.Select(id => new ElementId(id)));
                }
                else if (node.IsRoot && _currentRootFamilyId > 0)
                {
                    var instances = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilyInstance))
                        .WhereElementIsNotElementType()
                        .Cast<FamilyInstance>()
                        .Where(fi => fi.Symbol?.Family?.Id.Value == _currentRootFamilyId)
                        .Select(fi => fi.Id);
                    idsToSelect.AddRange(instances);
                }

                if (idsToSelect.Count == 0)
                {
                    SetStatus($"No hay ejemplares colocados de '{node.Name}' en el documento activo.", isError: true);
                    return;
                }

                uidoc.Selection.SetElementIds(idsToSelect);
                uidoc.ShowElements(idsToSelect);
                SetStatus($"Seleccionados {idsToSelect.Count} ejemplar(es) de '{node.Name}' en Revit.");
            });
        }

        private void BtnOpenNodeInEditor_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTreeNode == null) return;
            string targetName = _selectedTreeNode.Name;
            long rootId = _currentRootFamilyId;

            SetStatus($"Abriendo '{targetName}' en el Editor de Familias...");
            RunInRevitContext(app =>
            {
                Document? doc = app.ActiveUIDocument?.Document;
                if (doc == null) return;

                var (ok, msg) = NestedFamilyService.OpenFamilyNodeInEditor(app, doc, rootId, targetName);
                SetStatus(msg, isError: !ok);
                if (ok)
                    RefreshContextFromActiveDoc(app);
            });
        }

        private void CmbAssociationHost_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isPopulatingUi) return;
            if (sender is not ComboBox cmb || cmb.DataContext is not NestedAssociationItem assoc) return;
            if (cmb.SelectedItem is not string chosenHostParam) return;

            RunInRevitContext(app =>
            {
                Document? doc = app.ActiveUIDocument?.Document;
                if (doc == null) return;

                var (ok, msg) = NestedFamilyService.AssociateNestedParameter(
                    doc, assoc.NestedInstanceIdValue, assoc.NestedParamName, assoc.IsNestedTypeParam, chosenHostParam);

                SetStatus(msg, isError: !ok);
            });
        }

        private void BtnJumpToParamEditor_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTreeNode != null)
            {
                _allNativeParams = _selectedTreeNode.Parameters.ToList();
                ApplyNativeParamFilter();
            }
            rbModeParameters.IsChecked = true;
        }

        // =========================================================================
        // MODO 2: EDITOR NATIVO DE PARÁMETROS
        // =========================================================================

        private void ParamScope_Changed(object sender, RoutedEventArgs e)
        {
            if (_isPopulatingUi || !IsLoaded) return;

            RunInRevitContext(app =>
            {
                UIDocument? uidoc = app.ActiveUIDocument;
                Document? doc = uidoc?.Document;
                if (doc == null) return;

                bool useSel = rbParamScopeSelection.IsChecked == true;
                List<NativeParameterItem> items;

                if (useSel)
                {
                    Element? sel = GetFirstSelectedElement(uidoc);
                    if (sel == null)
                    {
                        SetStatus("Selecciona un elemento en Revit para ver sus parámetros.", isError: true);
                        Dispatcher.Invoke(() =>
                        {
                            _allNativeParams.Clear();
                            ApplyNativeParamFilter();
                        });
                        return;
                    }
                    _inspectedElementId = sel.Id;
                    items = NativeParameterService.InspectElementParameters(doc, sel);
                }
                else
                {
                    _inspectedElementId = null;
                    items = NativeParameterService.InspectDocumentParameters(app.Application, doc);
                }

                Dispatcher.Invoke(() =>
                {
                    _allNativeParams = items;
                    ApplyNativeParamFilter();
                    SetStatus($"Mostrando {items.Count} parámetro(s).");
                });
            });
        }

        private void CmbParamFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyNativeParamFilter();
        private void TxtSearchNativeParams_TextChanged(object sender, TextChangedEventArgs e) => ApplyNativeParamFilter();

        private void ApplyNativeParamFilter()
        {
            if (_filteredNativeParams == null) return;

            string tag = (cmbParamFilter?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "ALL";
            string q = (txtSearchNativeParams?.Text ?? string.Empty).Trim();

            _filteredNativeParams.Clear();
            foreach (var p in _allNativeParams)
            {
                bool matchFilter = tag switch
                {
                    "BUILTIN" => p.Origin == ParameterOriginKind.BuiltIn,
                    "LOCAL" => p.Origin == ParameterOriginKind.Family || p.Origin == ParameterOriginKind.Project,
                    "SHARED" => p.Origin == ParameterOriginKind.Shared,
                    "FORMULA" => p.HasFormula,
                    "INSTANCE" => p.IsInstance,
                    "TYPE" => !p.IsInstance,
                    _ => true
                };

                if (!matchFilter) continue;

                if (!string.IsNullOrEmpty(q) &&
                    p.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 &&
                    p.GroupLabel.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                _filteredNativeParams.Add(p);
            }
        }

        private void DgNativeParameters_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedNativeParam = dgNativeParameters.SelectedItem as NativeParameterItem;
            UpdateNativeInspector(_selectedNativeParam);
        }

        private void UpdateNativeInspector(NativeParameterItem? p)
        {
            _isPopulatingUi = true;
            if (p == null)
            {
                txtInspectorParamName.Text = "Selecciona un parámetro";
                txtInspectorParamMeta.Text = "Haz clic en cualquier fila para ver sus reglas nativas y editarlo.";
                txtInspectorHint.Text = "Revit distingue entre parámetros nativos (Built-In), de Familia, de Proyecto y Compartidos.";
                txtEditValue.Text = "";
                txtEditFormula.Text = "";
                txtEditName.Text = "";
                chkEditReporting.IsChecked = false;

                txtEditValue.IsEnabled = btnApplyValue.IsEnabled = false;
                txtEditFormula.IsEnabled = btnApplyFormula.IsEnabled = false;
                txtEditName.IsEnabled = btnApplyRename.IsEnabled = false;
                cmbEditGroup.IsEnabled = btnApplyGroup.IsEnabled = false;
                chkEditReporting.IsEnabled = false;
                btnToggleScope.IsEnabled = false;
                btnConvertToShared.IsEnabled = false;
                btnDeleteParam.IsEnabled = false;
                _isPopulatingUi = false;
                return;
            }

            txtInspectorParamName.Text = p.Name;
            txtInspectorParamMeta.Text = $"{p.OriginBadge}  ·  {p.ScopeBadge}  ·  {p.DataTypeLabel}";
            txtInspectorHint.Text = p.EditabilityHint;

            txtEditValue.Text = p.CurrentValueDisplay == "—" ? "" : p.CurrentValueDisplay;
            txtEditFormula.Text = p.Formula;
            txtEditName.Text = p.Name;
            chkEditReporting.IsChecked = p.IsReporting;

            var matchGroup = _revitGroups.FirstOrDefault(g => g.RawId == p.GroupTypeIdRaw);
            if (matchGroup != null) cmbEditGroup.SelectedItem = matchGroup;

            txtEditValue.IsEnabled = btnApplyValue.IsEnabled = p.CanEditValue;
            txtEditFormula.IsEnabled = btnApplyFormula.IsEnabled = p.CanEditFormula;
            txtEditName.IsEnabled = btnApplyRename.IsEnabled = p.CanRename;
            cmbEditGroup.IsEnabled = btnApplyGroup.IsEnabled = p.CanEditGroup;
            chkEditReporting.IsEnabled = p.CanToggleReporting;
            btnToggleScope.IsEnabled = p.CanToggleScope;
            btnConvertToShared.IsEnabled = p.CanConvertToShared;
            btnDeleteParam.IsEnabled = p.CanDelete;

            txtStatusGuid.Text = p.IsShared ? $"GUID: {p.GuidText}" : string.Empty;
            _isPopulatingUi = false;
        }

        private void BtnApplyValue_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedNativeParam == null) return;
            var p = _selectedNativeParam;
            string newVal = txtEditValue.Text;

            RunInRevitContext(app =>
            {
                Document? doc = app.ActiveUIDocument?.Document;
                if (doc == null) return;
                Element? targetElem = _inspectedElementId != null ? doc.GetElement(_inspectedElementId) : null;

                var (ok, msg) = NativeParameterService.SetParameterValueNative(doc, targetElem, p, newVal);
                SetStatus(msg, isError: !ok);
                if (ok) RefreshParametersAfterEdit(app);
            });
        }

        private void BtnApplyFormula_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedNativeParam == null) return;
            string paramName = _selectedNativeParam.Name;
            string formula = txtEditFormula.Text;

            RunInRevitContext(app =>
            {
                Document? doc = app.ActiveUIDocument?.Document;
                if (doc == null) return;

                var (ok, msg) = NativeParameterService.SetFamilyFormulaNative(doc, paramName, formula);
                SetStatus(msg, isError: !ok);
                if (ok) RefreshParametersAfterEdit(app);
            });
        }

        private void BtnApplyRename_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedNativeParam == null) return;
            string oldName = _selectedNativeParam.Name;
            string newName = txtEditName.Text;

            RunInRevitContext(app =>
            {
                Document? doc = app.ActiveUIDocument?.Document;
                if (doc == null) return;

                var (ok, msg) = NativeParameterService.RenameFamilyParameterNative(doc, oldName, newName);
                SetStatus(msg, isError: !ok);
                if (ok) RefreshParametersAfterEdit(app);
            });
        }

        private void BtnApplyGroup_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedNativeParam == null || cmbEditGroup.SelectedItem is not RevitForgeOption grp) return;
            string paramName = _selectedNativeParam.Name;

            RunInRevitContext(app =>
            {
                Document? doc = app.ActiveUIDocument?.Document;
                if (doc == null) return;

                var (ok, msg) = NativeParameterService.SetParameterGroupNative(doc, paramName, grp.TypeId);
                SetStatus(msg, isError: !ok);
                if (ok) RefreshParametersAfterEdit(app);
            });
        }

        private void ChkEditReporting_Click(object sender, RoutedEventArgs e)
        {
            if (_isPopulatingUi || _selectedNativeParam == null) return;
            string paramName = _selectedNativeParam.Name;
            bool makeRep = chkEditReporting.IsChecked == true;

            RunInRevitContext(app =>
            {
                Document? doc = app.ActiveUIDocument?.Document;
                if (doc == null) return;

                var (ok, msg) = NativeParameterService.ToggleReportingNative(doc, paramName, makeRep);
                SetStatus(msg, isError: !ok);
                RefreshParametersAfterEdit(app);
            });
        }

        private void BtnToggleScope_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedNativeParam == null) return;
            string paramName = _selectedNativeParam.Name;

            RunInRevitContext(app =>
            {
                Document? doc = app.ActiveUIDocument?.Document;
                if (doc == null) return;

                var (ok, msg) = NativeParameterService.ToggleScopeNative(app.Application, doc, paramName);
                SetStatus(msg, isError: !ok);
                if (ok) RefreshParametersAfterEdit(app);
            });
        }

        private void BtnConvertToShared_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedNativeParam == null) return;
            string paramName = _selectedNativeParam.Name;

            RunInRevitContext(app =>
            {
                Document? doc = app.ActiveUIDocument?.Document;
                if (doc == null) return;

                var (ok, msg) = NativeParameterService.ConvertToSharedNative(app.Application, doc, paramName);
                SetStatus(msg, isError: !ok);
                if (ok) RefreshParametersAfterEdit(app);
            });
        }

        private void BtnDeleteParam_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedNativeParam == null) return;
            string paramName = _selectedNativeParam.Name;

            if (MessageBox.Show($"¿Eliminar el parámetro '{paramName}' del documento activo?",
                "ARCA Review", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            RunInRevitContext(app =>
            {
                Document? doc = app.ActiveUIDocument?.Document;
                if (doc == null) return;

                var (ok, msg) = NativeParameterService.DeleteParameterNative(doc, paramName);
                SetStatus(msg, isError: !ok);
                if (ok) RefreshParametersAfterEdit(app);
            });
        }

        private void BtnToggleCreateParam_Click(object sender, RoutedEventArgs e)
        {
            panelCreateParameter.Visibility = panelCreateParameter.Visibility == System.Windows.Visibility.Visible
                ? System.Windows.Visibility.Collapsed
                : System.Windows.Visibility.Visible;
        }

        private void BtnConfirmCreateParam_Click(object sender, RoutedEventArgs e)
        {
            string name = txtCreateParamName.Text;
            var dt = (cmbCreateParamDataType.SelectedItem as RevitForgeOption)?.TypeId ?? SpecTypeId.String.Text;
            var grp = (cmbCreateParamGroup.SelectedItem as RevitForgeOption)?.TypeId ?? GroupTypeId.Data;
            bool isInst = (cmbCreateParamScope.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "INSTANCE";
            bool asShared = chkCreateParamShared.IsChecked == true;

            RunInRevitContext(app =>
            {
                Document? doc = app.ActiveUIDocument?.Document;
                if (doc == null) return;

                var (ok, msg) = NativeParameterService.CreateNativeParameter(app.Application, doc, name, dt, grp, isInst, asShared);
                SetStatus(msg, isError: !ok);
                if (ok)
                {
                    Dispatcher.Invoke(() => panelCreateParameter.Visibility = System.Windows.Visibility.Collapsed);
                    RefreshParametersAfterEdit(app);
                }
            });
        }

        private void RefreshParametersAfterEdit(UIApplication app)
        {
            Document? doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;

            Element? sel = _inspectedElementId != null ? doc.GetElement(_inspectedElementId) : null;
            List<NativeParameterItem> refreshed = (rbParamScopeSelection.IsChecked == true && sel != null)
                ? NativeParameterService.InspectElementParameters(doc, sel)
                : NativeParameterService.InspectDocumentParameters(app.Application, doc);

            string? prevSelectedName = _selectedNativeParam?.Name;

            Dispatcher.Invoke(() =>
            {
                _allNativeParams = refreshed;
                ApplyNativeParamFilter();

                if (!string.IsNullOrEmpty(prevSelectedName))
                {
                    var match = _filteredNativeParams.FirstOrDefault(x => x.Name == prevSelectedName);
                    if (match != null)
                        dgNativeParameters.SelectedItem = match;
                }
            });
        }
    }
}
