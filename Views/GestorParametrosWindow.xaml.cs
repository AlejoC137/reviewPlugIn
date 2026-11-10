using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using RevitToSupabasePlugin.Models;
using RevitToSupabasePlugin.Services;
using Application = Autodesk.Revit.ApplicationServices.Application;
using Document = Autodesk.Revit.DB.Document;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace RevitToSupabasePlugin.Views
{
    public class ParameterManagerActionHandler : IExternalEventHandler
    {
        public Action<UIApplication>? PendingAction { get; set; }
        public Action? OnCompletedCallback { get; set; }

        public void Execute(UIApplication app)
        {
            try
            {
                PendingAction?.Invoke(app);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GestorParametros] Error en ExternalEvent: {ex.Message}");
            }
            finally
            {
                PendingAction = null;
                OnCompletedCallback?.Invoke();
            }
        }

        public string GetName() => "ARCA_ParameterManagerExternalEvent";
    }

    public partial class GestorParametrosWindow : Window
    {
        private readonly UIApplication _uiApp;
        private readonly Application _app;
        private Document _currentDoc;

        private readonly ParameterManagerActionHandler _extHandler;
        private readonly ExternalEvent _extEvent;

        private List<ProjectParameterItem> _allDocParameters = new List<ProjectParameterItem>();
        private ObservableCollection<ProjectParameterItem> _filteredDocParameters = new ObservableCollection<ProjectParameterItem>();
        private ObservableCollection<SharedParamTxtItem> _txtParameters = new ObservableCollection<SharedParamTxtItem>();
        private ObservableCollection<CloudSharedParamFileRecord> _cloudProfiles = new ObservableCollection<CloudSharedParamFileRecord>();
        private ObservableCollection<ElementParameterValueRow> _elementMatrixRows = new ObservableCollection<ElementParameterValueRow>();
        private ObservableCollection<OpenRevitDocItem> _openDocsList = new ObservableCollection<OpenRevitDocItem>();
        private ObservableCollection<CategoryChoiceItem> _availableCategories = new ObservableCollection<CategoryChoiceItem>();

        private bool _isInitializing = true;

        public GestorParametrosWindow(UIApplication uiApp)
        {
            InitializeComponent();
            _uiApp = uiApp;
            _app = uiApp.Application;
            _currentDoc = uiApp.ActiveUIDocument.Document;

            _extHandler = new ParameterManagerActionHandler();
            _extEvent = ExternalEvent.Create(_extHandler);

            dgProjectParameters.ItemsSource = _filteredDocParameters;
            dgSharedTxtItems.ItemsSource = _txtParameters;
            dgCloudProfiles.ItemsSource = _cloudProfiles;
            dgElementMatrix.ItemsSource = _elementMatrixRows;
            dgOpenDocsSync.ItemsSource = _openDocsList;
            lstNewParamCategories.ItemsSource = _availableCategories;

            InitializeWorkspace();
        }

        private void InitializeWorkspace()
        {
            _isInitializing = true;
            try
            {
                // Estado de sesión SSO
                txtSessionBadge.Text = AuthenticationService.IsLoggedIn
                    ? $"☁️ Cloud: {AuthenticationService.UserEmail}"
                    : $"☁️ Cloud: Modo Directo ({AuthenticationService.ActiveProjectName})";

                // Asegurar archivo .txt por defecto si Revit no tiene ninguno configurado
                string spPath = SharedParameterCloudService.EnsureDefaultSharedParamFile(_app, true);
                UpdateTxtStatusBadges(spPath);

                // Cargar lista de documentos abiertos (.rvt y .rfa)
                RefreshOpenDocumentsList();

                // Escanear documento activo y archivo .txt
                ReloadCurrentDocumentAndTxtData();

                // Cargar perfiles de la nube (Supabase + Local)
                _ = LoadCloudProfilesAsync();

                AppendLog($"✅ Gestor Integral de Parámetros iniciado sobre '{_currentDoc.Title}'.");
            }
            catch (Exception ex)
            {
                AppendLog($"❌ Error inicializando Gestor de Parámetros: {ex.Message}");
            }
            finally
            {
                _isInitializing = false;
            }
        }

        private void RunRevitAction(Action<UIApplication> action, Action? afterUiCallback = null)
        {
            try
            {
                action(_uiApp);
                afterUiCallback?.Invoke();
            }
            catch ( Autodesk.Revit.Exceptions.InvalidOperationException )
            {
                // Si la ventana fue abierta en modo Modeless fuera de contexto de API, ejecutar vía ExternalEvent
                _extHandler.PendingAction = action;
                _extHandler.OnCompletedCallback = () =>
                {
                    Dispatcher.Invoke(() => afterUiCallback?.Invoke());
                };
                _extEvent.Raise();
            }
            catch (Exception ex)
            {
                AppendLog($"❌ Error en ejecución: {ex.Message}");
            }
        }

        private void RefreshOpenDocumentsList()
        {
            _openDocsList.Clear();
            Document activeDoc = _uiApp.ActiveUIDocument?.Document ?? _currentDoc;

            foreach (Document d in _app.Documents)
            {
                if (d == null || d.IsLinked) continue;
                bool isCurrent = string.Equals(d.Title, _currentDoc?.Title, StringComparison.OrdinalIgnoreCase);

                _openDocsList.Add(new OpenRevitDocItem
                {
                    DocumentRef = d,
                    Title = d.Title,
                    PathName = string.IsNullOrWhiteSpace(d.PathName) ? "(Sin guardar en disco)" : d.PathName,
                    IsFamilyDocument = d.IsFamilyDocument,
                    IsActiveDoc = string.Equals(d.Title, activeDoc?.Title, StringComparison.OrdinalIgnoreCase),
                    IsSelectedForSync = !isCurrent
                });
            }

            cmbOpenDocuments.ItemsSource = _openDocsList;
            var currentItem = _openDocsList.FirstOrDefault(x => x.DocumentRef == _currentDoc) ?? _openDocsList.FirstOrDefault();
            if (currentItem != null)
            {
                cmbOpenDocuments.SelectedItem = currentItem;
            }
        }

        private void ReloadCurrentDocumentAndTxtData()
        {
            if (_currentDoc == null) return;

            // 1. Escanear todos los parámetros del documento seleccionado
            _allDocParameters = ParameterTransformationService.ScanDocumentParameters(_app, _currentDoc, true);
            ApplyParameterFilters();

            // 2. Escanear el archivo .txt activo
            string spPath = _app.SharedParametersFilename;
            UpdateTxtStatusBadges(spPath);

            _txtParameters.Clear();
            var txtItems = SharedParameterCloudService.ReadSharedParameterFileItems(_app, _currentDoc, spPath);
            foreach (var ti in txtItems)
            {
                _txtParameters.Add(ti);
            }

            // 3. Cargar categorías disponibles y combos de parámetros para Tab 2 y Tab 3
            _availableCategories.Clear();
            var cats = ParameterTransformationService.GetModelCategoriesWithParameters(_currentDoc);
            foreach (var c in cats)
            {
                _availableCategories.Add(c);
            }

            cmbMassCategory.ItemsSource = _availableCategories;
            if (_availableCategories.Count > 0 && cmbMassCategory.SelectedIndex < 0)
            {
                cmbMassCategory.SelectedIndex = 0;
            }

            PopulateParameterSuggestionsForMassTab();
            UpdateKpiCounters();
        }

        private void PopulateParameterSuggestionsForMassTab()
        {
            var names = new List<string> { "Marca", "Comentarios", "Comentarios de tipo", "Descripción" };
            foreach (var p in _allDocParameters)
            {
                if (!names.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
                    names.Add(p.Name);
            }

            string prevSrc = cmbMassSourceParam.Text;
            string prevTgt = cmbMassTargetParam.Text;

            cmbMassSourceParam.ItemsSource = names;
            cmbMassTargetParam.ItemsSource = names;

            cmbMassSourceParam.Text = string.IsNullOrWhiteSpace(prevSrc) ? "Comentarios" : prevSrc;
            cmbMassTargetParam.Text = string.IsNullOrWhiteSpace(prevTgt) ? (names.FirstOrDefault(n => n.StartsWith("ARCA_")) ?? "Marca") : prevTgt;
        }

        private void UpdateTxtStatusBadges(string? spPath)
        {
            string displayPath = string.IsNullOrWhiteSpace(spPath) ? "(Sin archivo .TXT asignado)" : spPath;
            txtActiveSharedFilePath.Text = displayPath;
            txtActiveTxtBadge.Text = $".TXT: {Path.GetFileName(displayPath)}";
        }

        private void UpdateKpiCounters()
        {
            int total = _allDocParameters.Count;
            int projCount = _allDocParameters.Count(x => x.Origin == ParameterOriginKind.Project);
            int sharedCount = _allDocParameters.Count(x => x.Origin == ParameterOriginKind.Shared);
            int orphanCount = _allDocParameters.Count(x => x.IsOrphanInTxt);
            int txtDefsCount = _txtParameters.Count;

            lblKpiTotal.Text = total.ToString();
            lblKpiProject.Text = projCount.ToString();
            lblKpiShared.Text = sharedCount.ToString();
            lblKpiOrphans.Text = orphanCount.ToString();
            lblKpiTxtDefs.Text = txtDefsCount.ToString();
        }

        private void ApplyParameterFilters()
        {
            if (_filteredDocParameters == null) return;
            _filteredDocParameters.Clear();

            string query = (txtSearchParams?.Text ?? "").Trim().ToLowerInvariant();

            IEnumerable<ProjectParameterItem> queryable = _allDocParameters;

            if (rbFilterProject?.IsChecked == true)
                queryable = queryable.Where(x => x.Origin == ParameterOriginKind.Project);
            else if (rbFilterShared?.IsChecked == true)
                queryable = queryable.Where(x => x.Origin == ParameterOriginKind.Shared);
            else if (rbFilterOrphan?.IsChecked == true)
                queryable = queryable.Where(x => x.IsOrphanInTxt);
            else if (rbFilterInstance?.IsChecked == true)
                queryable = queryable.Where(x => x.IsInstance);
            else if (rbFilterType?.IsChecked == true)
                queryable = queryable.Where(x => !x.IsInstance);

            if (!string.IsNullOrEmpty(query))
            {
                queryable = queryable.Where(x =>
                    x.Name.ToLowerInvariant().Contains(query) ||
                    x.CategoriesSummary.ToLowerInvariant().Contains(query) ||
                    x.DataTypeLabel.ToLowerInvariant().Contains(query) ||
                    x.GuidText.ToLowerInvariant().Contains(query));
            }

            foreach (var item in queryable)
            {
                _filteredDocParameters.Add(item);
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        // EVENTOS DE CABECERA Y PESTAÑA 1 (EXPLORADOR)
        // ══════════════════════════════════════════════════════════════════════════

        private void CmbOpenDocuments_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            if (cmbOpenDocuments.SelectedItem is OpenRevitDocItem docItem && docItem.DocumentRef != null)
            {
                _currentDoc = docItem.DocumentRef;
                ReloadCurrentDocumentAndTxtData();
                AppendLog($"📄 Documento activo cambiado a: '{_currentDoc.Title}' ({docItem.DocKindLabel}).");
            }
        }

        private void BtnRefreshAll_Click(object sender, RoutedEventArgs e)
        {
            RefreshOpenDocumentsList();
            ReloadCurrentDocumentAndTxtData();
            AppendLog("🔄 Escaneo de parámetros y archivo .TXT actualizado.");
        }

        private void FilterParameters_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            ApplyParameterFilters();
        }

        private void TxtSearchParams_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing) return;
            ApplyParameterFilters();
        }

        private void BtnSelectAllVisibleParams_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _filteredDocParameters) item.IsSelected = true;
        }

        private void BtnUnselectAllParams_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _allDocParameters) item.IsSelected = false;
        }

        private void BtnSelectOnlyNonShared_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _allDocParameters)
            {
                item.IsSelected = item.Origin == ParameterOriginKind.Project;
            }
            AppendLog($"🔶 Seleccionados {_allDocParameters.Count(x => x.IsSelected)} parámetros de Proyecto (No-Shared) listos para transformar.");
        }

        // ══════════════════════════════════════════════════════════════════════════
        // EVENTOS DE PESTAÑA 1 & 2 (MOTOR TRANSFORMATIVO Y RESCATE)
        // ══════════════════════════════════════════════════════════════════════════

        private void BtnQuickConvertToShared_Click(object sender, RoutedEventArgs e)
        {
            var selected = _allDocParameters.Where(x => x.IsSelected).ToList();
            if (selected.Count == 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("ARCA Review", "Selecciona al menos un parámetro en la tabla del Explorador (Pestaña 1) para transformarlo a Parámetro Compartido.");
                return;
            }

            string targetGroup = string.IsNullOrWhiteSpace(txtTransformTargetGroup.Text)
                ? "01_ARCA_Transformados"
                : txtTransformTargetGroup.Text.Trim();

            string scopeTag = (cmbTransformScopeMode.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "KEEP";

            RunRevitAction(_ =>
            {
                var res = ParameterTransformationService.ConvertProjectParametersToShared(
                    _app,
                    _currentDoc,
                    selected,
                    targetGroup,
                    scopeTag);

                foreach (string log in res.logs) AppendLog(log);
                AppendLog($"🎉 Resumen Transformación: {res.convertedCount} parámetros transformados a Shared | {res.preservedValuesCount} valores de elementos preservados.");
            }, () =>
            {
                ReloadCurrentDocumentAndTxtData();
            });
        }

        private void BtnQuickToggleScope_Click(object sender, RoutedEventArgs e)
        {
            var selected = _allDocParameters.Where(x => x.IsSelected).ToList();
            if (selected.Count == 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("ARCA Review", "Marca al menos un parámetro en la tabla para invertir su ámbito entre Instancia y Tipo.");
                return;
            }

            RunRevitAction(_ =>
            {
                var res = ParameterTransformationService.ToggleParameterScopeInstanceType(_app, _currentDoc, selected);
                foreach (string log in res.logs) AppendLog(log);
            }, () =>
            {
                ReloadCurrentDocumentAndTxtData();
            });
        }

        private void BtnRescueOrphans_Click(object sender, RoutedEventArgs e)
        {
            RunRevitAction(_ =>
            {
                int rescued = SharedParameterCloudService.RescueOrphanSharedParametersToTxt(_app, _currentDoc);
                if (rescued > 0)
                    AppendLog($"🛟 Se rescataron {rescued} parámetros compartidos huérfanos desde '{_currentDoc.Title}' y se escribieron con su GUID original en el archivo .TXT activo.");
                else
                    AppendLog("ℹ️ No se encontraron parámetros compartidos huérfanos pendientes de rescatar en este documento.");
            }, () =>
            {
                ReloadCurrentDocumentAndTxtData();
            });
        }

        private void BtnCreateAndBindNewSharedParam_Click(object sender, RoutedEventArgs e)
        {
            string pName = txtNewParamName.Text.Trim();
            string grpName = txtNewParamGroup.Text.Trim();
            string dtToken = (cmbNewParamDataType.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "TEXT";
            bool asInstance = ((cmbNewParamScope.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "INSTANCE") == "INSTANCE";

            if (string.IsNullOrWhiteSpace(pName))
            {
                Autodesk.Revit.UI.TaskDialog.Show("ARCA Review", "Ingresa un nombre válido para el nuevo parámetro compartido.");
                return;
            }

            var selectedCatIds = _availableCategories.Where(c => c.IsChecked).Select(c => c.CategoryId).ToList();
            if (!_currentDoc.IsFamilyDocument && selectedCatIds.Count == 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("ARCA Review", "Marca al menos una categoría de la lista para vincular el parámetro en el proyecto.");
                return;
            }

            RunRevitAction(_ =>
            {
                string txtPath = SharedParameterCloudService.EnsureDefaultSharedParamFile(_app, true);
                Guid newGuid = Guid.NewGuid();
                SharedParameterCloudService.AppendParametersWithKnownGuidsToTxtFile(
                    txtPath,
                    string.IsNullOrWhiteSpace(grpName) ? "01_ARCA_General" : grpName,
                    new List<(Guid guid, string name, string dataTypeToken)> { (newGuid, pName, dtToken) });

                _app.SharedParametersFilename = txtPath;

                var tempItem = new SharedParamTxtItem
                {
                    GroupName = grpName,
                    Name = pName,
                    Guid = newGuid,
                    DataTypeLabel = dtToken
                };

                var bindRes = ParameterTransformationService.BindTxtSharedParametersToDocument(
                    _app,
                    _currentDoc,
                    new List<SharedParamTxtItem> { tempItem },
                    selectedCatIds,
                    asInstance);

                foreach (string l in bindRes.logs) AppendLog(l);
            }, () =>
            {
                ReloadCurrentDocumentAndTxtData();
            });
        }

        // ══════════════════════════════════════════════════════════════════════════
        // EVENTOS DE PESTAÑA 3 (OPERACIONES MASIVAS DE VALORES EN ELEMENTOS)
        // ══════════════════════════════════════════════════════════════════════════

        private void BtnLoadElementMatrix_Click(object sender, RoutedEventArgs e)
        {
            if (cmbMassCategory.SelectedItem is not CategoryChoiceItem catChoice)
            {
                AppendLog("⚠️ Selecciona una categoría primero.");
                return;
            }

            string srcParam = cmbMassSourceParam.Text.Trim();
            string tgtParam = cmbMassTargetParam.Text.Trim();
            bool inspectTypes = chkMassInspectTypes.IsChecked == true;

            _elementMatrixRows.Clear();
            var rows = ParameterTransformationService.LoadElementParameterMatrix(
                _currentDoc,
                catChoice.CategoryId,
                srcParam,
                tgtParam,
                inspectTypes);

            foreach (var r in rows) _elementMatrixRows.Add(r);

            lblMatrixSummary.Text = $"Categoría '{catChoice.Name}': {_elementMatrixRows.Count} {(inspectTypes ? "Tipos" : "Ejemplares")} cargados | Origen: '{srcParam}' ➔ Destino: '{tgtParam}'";
            AppendLog($"🔍 Matriz cargada: {_elementMatrixRows.Count} elementos de '{catChoice.Name}'.");
        }

        private void BtnPreviewMassRule_Click(object sender, RoutedEventArgs e)
        {
            if (_elementMatrixRows.Count == 0)
            {
                BtnLoadElementMatrix_Click(sender, e);
            }

            string mode = (cmbMassRuleMode.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "COPY";
            string arg1 = txtMassArg1.Text;
            string arg2 = txtMassArg2.Text;
            bool onlyIfEmpty = chkMassOnlyIfEmpty.IsChecked == true;

            int modified = ParameterTransformationService.PreviewMassOperationOnRows(
                _elementMatrixRows.ToList(),
                mode,
                arg1,
                arg2,
                onlyIfEmpty);

            AppendLog($"👁️ Vista previa generada ({mode}): {modified} elementos listos para actualizar. Revisa la columna 'Nuevo Valor' y pulsa 'Aplicar Valores'.");
        }

        private void BtnApplyMassMatrix_Click(object sender, RoutedEventArgs e)
        {
            string tgtParam = cmbMassTargetParam.Text.Trim();
            if (string.IsNullOrWhiteSpace(tgtParam))
            {
                Autodesk.Revit.UI.TaskDialog.Show("ARCA Review", "Especifica el nombre del Parámetro Destino.");
                return;
            }

            RunRevitAction(_ =>
            {
                var res = ParameterTransformationService.ApplyElementParameterMatrixToDocument(
                    _currentDoc,
                    _elementMatrixRows.ToList(),
                    tgtParam);

                foreach (string l in res.logs) AppendLog(l);
            }, () =>
            {
                ReloadCurrentDocumentAndTxtData();
            });
        }

        private void BtnMatrixSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in _elementMatrixRows) r.IsSelected = true;
        }

        private void BtnMatrixUnselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in _elementMatrixRows) r.IsSelected = false;
        }

        // ══════════════════════════════════════════════════════════════════════════
        // EVENTOS DE PESTAÑA 4 (ARCHIVOS .TXT Y RESPALDO EN LA NUBE SUPABASE)
        // ══════════════════════════════════════════════════════════════════════════

        private void BtnBrowseLocalTxt_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Seleccionar archivo .TXT de Parámetros Compartidos de Revit",
                Filter = "Archivos de Parámetros Compartidos (*.txt)|*.txt|Todos los archivos (*.*)|*.*",
                CheckFileExists = true
            };

            if (dlg.ShowDialog() == true)
            {
                _app.SharedParametersFilename = dlg.FileName;
                ReloadCurrentDocumentAndTxtData();
                AppendLog($"📂 Archivo .TXT activo cambiado a: '{dlg.FileName}' ({_txtParameters.Count} definiciones).");
            }
        }

        private void BtnActivateDefaultArcaTxt_Click(object sender, RoutedEventArgs e)
        {
            string defaultPath = SharedParameterCloudService.EnsureDefaultSharedParamFile(_app, false);
            _app.SharedParametersFilename = defaultPath;
            ReloadCurrentDocumentAndTxtData();
            AppendLog($"🏛️ Archivo Estándar ARCA activado: '{defaultPath}'.");
        }

        private void BtnSelectAllTxtItems_Click(object sender, RoutedEventArgs e)
        {
            bool anyUnselected = _txtParameters.Any(x => !x.IsSelected);
            foreach (var item in _txtParameters) item.IsSelected = anyUnselected;
        }

        private void BtnBindSelectedTxtItemsToDoc_Click(object sender, RoutedEventArgs e)
        {
            var selectedTxt = _txtParameters.Where(x => x.IsSelected).ToList();
            if (selectedTxt.Count == 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("ARCA Review", "Selecciona uno o más parámetros de la tabla del archivo .TXT para vincularlos al documento activo.");
                return;
            }

            bool asInstance = ((cmbBindFromTxtScope.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "INSTANCE") == "INSTANCE";
            var catIds = _availableCategories.Where(c => c.IsChecked).Select(c => c.CategoryId).ToList();

            RunRevitAction(_ =>
            {
                var res = ParameterTransformationService.BindTxtSharedParametersToDocument(
                    _app,
                    _currentDoc,
                    selectedTxt,
                    catIds,
                    asInstance);

                foreach (string l in res.logs) AppendLog(l);
            }, () =>
            {
                ReloadCurrentDocumentAndTxtData();
            });
        }

        private async void BtnUploadTxtToCloud_Click(object sender, RoutedEventArgs e)
        {
            string localPath = _app.SharedParametersFilename;
            if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
            {
                localPath = SharedParameterCloudService.EnsureDefaultSharedParamFile(_app, true);
            }

            string title = txtCloudProfileTitle.Text.Trim();
            AppendLog($"☁️ Subiendo '{Path.GetFileName(localPath)}' a Supabase Cloud...");

            var res = await SharedParameterCloudService.UploadSharedParamFileToCloudAsync(
                localPath,
                title,
                _currentDoc?.Title ?? AuthenticationService.ActiveProjectName);

            AppendLog(res.ok ? $"✅ {res.message}" : $"❌ {res.message}");
            await LoadCloudProfilesAsync();
        }

        private async void BtnRefreshCloudProfiles_Click(object sender, RoutedEventArgs e)
        {
            AppendLog("🔄 Consultando perfiles de Parámetros Compartidos en Supabase Cloud...");
            await LoadCloudProfilesAsync();
        }

        private async System.Threading.Tasks.Task LoadCloudProfilesAsync()
        {
            var list = await SharedParameterCloudService.FetchCloudProfilesAsync();
            _cloudProfiles.Clear();
            foreach (var item in list)
            {
                _cloudProfiles.Add(item);
            }
            if (_cloudProfiles.Count > 0 && dgCloudProfiles.SelectedIndex < 0)
            {
                dgCloudProfiles.SelectedIndex = 0;
            }
        }

        private void BtnDownloadAndActivateCloudTxt_Click(object sender, RoutedEventArgs e)
        {
            if (dgCloudProfiles.SelectedItem is not CloudSharedParamFileRecord rec)
            {
                Autodesk.Revit.UI.TaskDialog.Show("ARCA Review", "Selecciona un perfil .TXT de la tabla de la nube.");
                return;
            }

            string localPath = SharedParameterCloudService.MaterializeCloudRecordToLocalFile(_app, rec, true);
            ReloadCurrentDocumentAndTxtData();
            AppendLog($"⬇️ Perfil '{rec.ProfileTitle}' descargado en '{localPath}' y activado como archivo .TXT principal en Revit.");
        }

        private void BtnMergeCloudTxtIntoLocal_Click(object sender, RoutedEventArgs e)
        {
            if (dgCloudProfiles.SelectedItem is not CloudSharedParamFileRecord rec)
            {
                Autodesk.Revit.UI.TaskDialog.Show("ARCA Review", "Selecciona un perfil .TXT de la nube para fusionarlo con tu archivo .TXT actual.");
                return;
            }

            int added = SharedParameterCloudService.MergeCloudRecordIntoActiveTxt(_app, _currentDoc, rec);
            ReloadCurrentDocumentAndTxtData();
            AppendLog($"🔀 Fusión completada con '{rec.ProfileTitle}': {added} nuevos parámetros/GUIDs incorporados al archivo .TXT activo sin alterar los existentes.");
        }

        // ══════════════════════════════════════════════════════════════════════════
        // EVENTOS DE PESTAÑA 5 (PROPAGACIÓN MULTI-DOCUMENTO .RVT / .RFA)
        // ══════════════════════════════════════════════════════════════════════════

        private void BtnPropagateMultiDoc_Click(object sender, RoutedEventArgs e)
        {
            var selectedParams = _allDocParameters.Where(x => x.IsSelected).ToList();
            if (selectedParams.Count == 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("ARCA Review", "Primero marca en la Pestaña 1 (Explorador) los parámetros que deseas propagar.");
                return;
            }

            var targetDocs = _openDocsList.Where(d => d.IsSelectedForSync && d.DocumentRef != null).ToList();
            if (targetDocs.Count == 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("ARCA Review", "Marca al menos un documento destino (.rvt o .rfa) en la tabla de sincronización.");
                return;
            }

            RunRevitAction(_ =>
            {
                var res = ParameterTransformationService.PropagateParametersToOpenDocuments(
                    _app,
                    _currentDoc,
                    selectedParams,
                    targetDocs);

                foreach (string l in res.logs) AppendLog(l);
                AppendLog($"🚀 Propagación Multi-Documento finalizada: {res.docsUpdated} documentos actualizados ({res.totalBindingsCreated} vínculos creados).");
            }, () =>
            {
                ReloadCurrentDocumentAndTxtData();
            });
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            txtTerminalLog.Clear();
        }

        private void AppendLog(string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            if (string.IsNullOrEmpty(txtTerminalLog.Text))
                txtTerminalLog.Text = line;
            else
                txtTerminalLog.AppendText(Environment.NewLine + line);

            txtTerminalLog.ScrollToEnd();
        }
    }
}
