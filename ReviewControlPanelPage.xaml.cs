using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.UI;
using WpfMessageBox = System.Windows.MessageBox;

namespace RevitToSupabasePlugin
{
    public partial class ReviewControlPanelPage : Page, IDockablePaneProvider
    {
        public static readonly DockablePaneId PaneId = new DockablePaneId(new Guid("D0C9A51B-7C0B-4D8E-9801-D9F438682F81"));
        public static ReviewControlPanelPage? Instance { get; internal set; }

        private readonly ReviewControlPanelEventHandler _handler;
        private readonly ExternalEvent _externalEvent;
        private List<SupabaseProjectItem> _projectsList = new();
        private BimModelMetrics? _lastMetrics;

        public ReviewControlPanelPage()
        {
            InitializeComponent();
            Instance = this;

            _handler = new ReviewControlPanelEventHandler();
            _externalEvent = ExternalEvent.Create(_handler);

            _handler.OnMetricsRefreshed = OnMetricsReceived;
            _handler.OnSyncCompleted = OnSyncFinished;
            _handler.OnLogMessage = AddLog;
            _handler.OnPreviewGenerated = OnExportPreviewReceived;
            _handler.OnExportCompleted = OnFileExportCompleted;

            this.Loaded += ReviewControlPanelPage_Loaded;
            AuthenticationService.SessionStateChanged += OnSessionStateChanged;
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = this;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Right
            };
        }

        private async void ReviewControlPanelPage_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateUserSessionDisplay();
            UpdateBiMcpDisplay();
            await LoadProjectsAsync();

            AddLog("🚀 Panel de Control Review inicializado correctamente.");
            AddLog($"📡 Bi-MCP Local escuchando en http://localhost:{App.McpServerInstance?.Port ?? 8080}/mcp/");
            
            TriggerMetricsRefresh();
        }

        public void SelectBiMcpTab()
        {
            Dispatcher.Invoke(() =>
            {
                mainTabControl.SelectedItem = tabBiMcp;
                UpdateBiMcpDisplay();
            });
        }

        public void SelectExportTab()
        {
            Dispatcher.Invoke(() =>
            {
                mainTabControl.SelectedItem = tabExportar;
                txtExportUserEmail.Text = AuthenticationService.IsLoggedIn ? AuthenticationService.UserEmail : "Modo Invitado (Sin sesión activa)";
            });
        }

        private void OnSessionStateChanged()
        {
            Dispatcher.Invoke(() =>
            {
                UpdateUserSessionDisplay();
                UpdateBiMcpDisplay();
            });
        }

        private void UpdateUserSessionDisplay()
        {
            if (AuthenticationService.IsLoggedIn)
            {
                txtUserName.Text = string.IsNullOrEmpty(AuthenticationService.UserName) ? "Usuario Review" : AuthenticationService.UserName;
                txtUserEmail.Text = AuthenticationService.UserEmail;
                txtAvatarInitials.Text = txtUserName.Text.Length >= 2 ? txtUserName.Text.Substring(0, 2).ToUpper() : "RV";

                if (AuthenticationService.IsAdmin)
                {
                    txtUserRole.Text = "ADMIN";
                    badgeRole.Visibility = Visibility.Visible;
                }
                else
                {
                    txtUserRole.Text = "EDITOR";
                    badgeRole.Visibility = Visibility.Visible;
                }

                btnToggleAuth.Content = "Cerrar Sesión";
            }
            else
            {
                txtUserName.Text = "Modo Invitado (AnonKey)";
                txtUserEmail.Text = "Sin sesión activa en Supabase";
                txtAvatarInitials.Text = "??";
                badgeRole.Visibility = Visibility.Collapsed;
                btnToggleAuth.Content = "🔐 Iniciar Sesión";
            }

            txtActiveProjectId.Text = AuthenticationService.ActiveProjectId;
            txtPromptProjectId.Text = AuthenticationService.ActiveProjectId;
            txtPromptProjectName.Text = AuthenticationService.ActiveProjectName;
            txtExportUserEmail.Text = AuthenticationService.IsLoggedIn ? AuthenticationService.UserEmail : "Modo Invitado (Sin sesión activa)";

            SelectActiveProjectInCombo();
        }

        private void UpdateBiMcpDisplay()
        {
            txtBadgeMcpPort.Text = "CLOUD:HTTPS";
            txtMcpPort.Text = "HTTPS Seguro (TLS)";
            txtMcpActiveUser.Text = AuthenticationService.IsLoggedIn ? AuthenticationService.UserEmail : "Sesión AnonKey";

            txtMcpRunningState.Text = "🟢 EN LÍNEA (CLOUD)";
            txtMcpRunningState.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(6, 95, 70));
            boxMcpRunning.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(209, 250, 229));
            boxMcpRunning.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(5, 150, 105));
            badgeMcpStatus.Background = boxMcpRunning.Background;
            badgeMcpStatus.BorderBrush = boxMcpRunning.BorderBrush;
            dotMcpStatus.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129));

            txtMcpConfigJson.Text = "{\n" +
                                   "  \"reviewEcosystem\": {\n" +
                                   "    \"cloudEndpoint\": \"https://arca-review.vercel.app\",\n" +
                                   "    \"mcpCloud\": \"https://arca-review.vercel.app/mcp\",\n" +
                                   "    \"changeQueue\": \"Supabase REST (revit_change_queue)\",\n" +
                                   "    \"directActions\": \"Ribbon ➔ Acciones IA ➔ Acciones Directas IA\"\n" +
                                   "  }\n" +
                                   "}";
        }

        private async Task LoadProjectsAsync()
        {
            try
            {
                AddLog("🌐 Consultando proyectos registrados en Supabase...");
                _projectsList = await AuthenticationService.FetchProjectsListAsync();

                cmbSupabaseProjects.Items.Clear();
                foreach (var proj in _projectsList)
                {
                    cmbSupabaseProjects.Items.Add(proj);
                }

                SelectActiveProjectInCombo();
                AddLog($"📁 {_projectsList.Count} proyectos listados desde Supabase.");
            }
            catch (Exception ex)
            {
                AddLog($"⚠️ Error listando proyectos: {ex.Message}");
            }
        }

        private void SelectActiveProjectInCombo()
        {
            if (_projectsList.Count == 0 || cmbSupabaseProjects.Items.Count == 0) return;

            string currentId = AuthenticationService.ActiveProjectId;
            string currentName = AuthenticationService.ActiveProjectName;

            for (int i = 0; i < cmbSupabaseProjects.Items.Count; i++)
            {
                if (cmbSupabaseProjects.Items[i] is SupabaseProjectItem p)
                {
                    if (p.Id.Equals(currentId, StringComparison.OrdinalIgnoreCase) ||
                        p.Name.Equals(currentName, StringComparison.OrdinalIgnoreCase))
                    {
                        cmbSupabaseProjects.SelectedIndex = i;
                        txtActiveProjectId.Text = p.Id;
                        txtPromptProjectId.Text = p.Id;
                        txtPromptProjectName.Text = p.Name;
                        return;
                    }
                }
            }

            if (cmbSupabaseProjects.SelectedIndex == -1 && cmbSupabaseProjects.Items.Count > 0)
            {
                cmbSupabaseProjects.SelectedIndex = 0;
            }
        }

        private void CmbSupabaseProjects_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbSupabaseProjects.SelectedItem is SupabaseProjectItem selected)
            {
                txtActiveProjectId.Text = selected.Id;
                txtPromptProjectId.Text = selected.Id;
                txtPromptProjectName.Text = selected.Name;
            }
        }

        private void BtnSaveProjectSelection_Click(object sender, RoutedEventArgs e)
        {
            if (cmbSupabaseProjects.SelectedItem is SupabaseProjectItem selected)
            {
                AuthenticationService.SetActiveProject(selected.Id, selected.Name);
                txtPromptProjectId.Text = selected.Id;
                txtPromptProjectName.Text = selected.Name;
                AddLog($"💾 Proyecto activo configurado: '{selected.Name}' (ID: {selected.Id})");
                WpfMessageBox.Show($"Proyecto activo vinculado:\n\n• Nombre: {selected.Name}\n• ID: {selected.Id}", "Review - Proyecto Configurado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void BtnRefreshProjects_Click(object sender, RoutedEventArgs e)
        {
            await LoadProjectsAsync();
        }

        // --- Inicio de Sesión 100% In-App (Sin navegador externo) ---
        private async void BtnToggleAuth_Click(object sender, RoutedEventArgs e)
        {
            if (AuthenticationService.IsLoggedIn)
            {
                var confirm = WpfMessageBox.Show("¿Deseas cerrar tu sesión actual de Supabase?", "Cerrar Sesión", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm == MessageBoxResult.Yes)
                {
                    AuthenticationService.Logout();
                    AddLog("🔒 Sesión cerrada en la aplicación.");
                }
            }
            else
            {
                AddLog("🔐 Abriendo ventana de inicio de sesión nativa...");
                var loginWin = new LoginWindow();
                try
                {
                    var revitHandle = Process.GetCurrentProcess().MainWindowHandle;
                    if (revitHandle != IntPtr.Zero)
                    {
                        new System.Windows.Interop.WindowInteropHelper(loginWin).Owner = revitHandle;
                    }
                }
                catch { }

                if (loginWin.ShowDialog() == true)
                {
                    AddLog($"✅ Sesión iniciada con éxito para {AuthenticationService.UserEmail}");
                    await LoadProjectsAsync();
                }
            }
        }

        // --- Métricas del Modelo ---
        private void BtnRefreshMetrics_Click(object sender, RoutedEventArgs e)
        {
            TriggerMetricsRefresh();
        }

        public void TriggerMetricsRefresh()
        {
            try
            {
                _handler.CurrentAction = ControlPanelAction.RefreshMetrics;
                _externalEvent.Raise();
            }
            catch (Exception ex)
            {
                AddLog($"⚠️ Error al solicitar escaneo: {ex.Message}");
            }
        }

        private void OnMetricsReceived(BimModelMetrics metrics)
        {
            _lastMetrics = metrics;
            Dispatcher.Invoke(() =>
            {
                txtRevitDocTitle.Text = metrics.ProjectName;
                txtRevitDocNumber.Text = $"N°: {metrics.ProjectNumber}  |  Doc: {metrics.DocumentTitle}";
                txtLastScan.Text = $"Último escaneo: {metrics.LastRefreshed:HH:mm:ss}";

                txtKpiWalls.Text = metrics.WallsCount.ToString();
                txtKpiWallArea.Text = $"{metrics.TotalWallAreaM2:N1} m² sup.";

                txtKpiFloors.Text = metrics.FloorsCount.ToString();
                txtKpiFloorArea.Text = $"{metrics.TotalFloorAreaM2:N1} m² sup.";

                txtKpiStructural.Text = metrics.StructuralCount.ToString();
                txtKpiPlumbing.Text = metrics.PlumbingCount.ToString();
                txtKpiRooms.Text = metrics.RoomsCount.ToString();
                txtKpiSheets.Text = metrics.SheetsCount.ToString();
                txtKpiLevels.Text = $"{metrics.LevelsCount} Niveles";
            });
        }

        // --- Centro de Sincronización Solidificado ---
        private void BtnSyncSupabaseNow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                progSync.Visibility = Visibility.Visible;
                btnSyncSupabaseNow.IsEnabled = false;

                _handler.CurrentAction = ControlPanelAction.SyncSupabase;
                _externalEvent.Raise();
            }
            catch (Exception ex)
            {
                AddLog($"❌ Error lanzando sincronización: {ex.Message}");
                progSync.Visibility = Visibility.Collapsed;
                btnSyncSupabaseNow.IsEnabled = true;
            }
        }

        private void BtnSyncMaterialsNow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AddLog("🎨 Iniciando sincronización de materiales desde Review Cloud...");
                _handler.CurrentAction = ControlPanelAction.SyncMaterials;
                _externalEvent.Raise();
            }
            catch (Exception ex)
            {
                AddLog($"❌ Error al solicitar sincronización de materiales: {ex.Message}");
            }
        }

        private void OnSyncFinished(bool success, string message)
        {
            Dispatcher.Invoke(() =>
            {
                progSync.Visibility = Visibility.Collapsed;
                btnSyncSupabaseNow.IsEnabled = true;

                if (success)
                {
                    WpfMessageBox.Show(message, "Sincronización Supabase", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    WpfMessageBox.Show(message, "Error de Sincronización", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            });
        }

        // --- Compilador de Plantillas & Mega-Prompt ---
        private void BtnSelectAllBmTables_Click(object sender, RoutedEventArgs e)
        {
            chkBmPluginData.IsChecked = true;
            chkBmProjects.IsChecked = true;
            chkBmComponentes.IsChecked = true;
            chkBmMateriales.IsChecked = true;
            chkBmTeam.IsChecked = true;
            chkBmDict.IsChecked = true;
            chkBmActions.IsChecked = true;
        }

        private void BtnDeselectAllBmTables_Click(object sender, RoutedEventArgs e)
        {
            chkBmPluginData.IsChecked = false;
            chkBmProjects.IsChecked = false;
            chkBmComponentes.IsChecked = false;
            chkBmMateriales.IsChecked = false;
            chkBmTeam.IsChecked = false;
            chkBmDict.IsChecked = false;
            chkBmActions.IsChecked = false;
        }

        private async void BtnCompileMegaPrompt_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                btnCompileMegaPrompt.IsEnabled = false;
                AddLog("⚡ Compilando Mega-Prompt con ID de plantilla y tablas seleccionadas...");

                string projectId = AuthenticationService.ActiveProjectId;
                string projectName = AuthenticationService.ActiveProjectName;

                var selectedTables = new List<string>();
                if (chkBmPluginData.IsChecked == true) selectedTables.Add("plugin_project_data");
                if (chkBmProjects.IsChecked == true) selectedTables.Add("projects");
                if (chkBmComponentes.IsChecked == true) selectedTables.Add("Catalogo_Componentes");
                if (chkBmMateriales.IsChecked == true) selectedTables.Add("Materiales");
                if (chkBmTeam.IsChecked == true) selectedTables.Add("bep_team");
                if (chkBmDict.IsChecked == true) selectedTables.Add("bim_dictionary");
                if (chkBmActions.IsChecked == true) selectedTables.Add("actions");

                var sb = new StringBuilder();
                sb.AppendLine("=======================================================");
                sb.AppendLine("  MEGA-PROMPT DE CONFIGURACIÓN DE PLANTILLA REVIT BIM");
                sb.AppendLine("  SISTEMA REVIEW / ARCA");
                sb.AppendLine("=======================================================");
                sb.AppendLine();
                sb.AppendLine($"[ID DE PROYECTO SUPABASE: {projectId}]");
                sb.AppendLine($"[NOMBRE DEL PROYECTO: {projectName}]");
                sb.AppendLine($"[USUARIO REVIEW: {(AuthenticationService.IsLoggedIn ? AuthenticationService.UserEmail : "AnonKey")}]");
                sb.AppendLine($"[FECHA DE COMPILACIÓN: {DateTime.Now:yyyy-MM-dd HH:mm:ss}]");
                sb.AppendLine();
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                sb.AppendLine("SECCIÓN A: ESTADO DEL MODELO ACTIVO EN REVIT");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                if (_lastMetrics != null)
                {
                    sb.AppendLine($"• Documento Revit: {_lastMetrics.DocumentTitle}");
                    sb.AppendLine($"• Proyecto: {_lastMetrics.ProjectName} (N° {_lastMetrics.ProjectNumber})");
                    sb.AppendLine($"• Niveles: {_lastMetrics.LevelsCount}");
                    sb.AppendLine($"• Muros: {_lastMetrics.WallsCount} ({_lastMetrics.TotalWallAreaM2} m²)");
                    sb.AppendLine($"• Pisos: {_lastMetrics.FloorsCount} ({_lastMetrics.TotalFloorAreaM2} m²)");
                    sb.AppendLine($"• Estructuras: {_lastMetrics.StructuralCount}");
                    sb.AppendLine($"• Riego/Fontanería: {_lastMetrics.PlumbingCount}");
                    sb.AppendLine($"• Habitaciones: {_lastMetrics.RoomsCount}");
                    sb.AppendLine($"• Láminas: {_lastMetrics.SheetsCount}");
                }
                else
                {
                    sb.AppendLine("• El modelo está conectado. Ejecuta 'Escanear' en Métricas para actualizar conteos.");
                }
                sb.AppendLine();

                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                sb.AppendLine("SECCIÓN B: TABLAS DE BASE DE DATOS DE REVIEW (SUPABASE)");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                sb.AppendLine($"Tablas seleccionadas para el contexto: {string.Join(", ", selectedTables)}");
                sb.AppendLine();

                if (selectedTables.Count > 0)
                {
                    var supabase = new SupabaseService();
                    var fetched = await supabase.FetchTablesAsync(selectedTables);
                    foreach (var kv in fetched)
                    {
                        sb.AppendLine($"=== TABLA: {kv.Key} ===");
                        sb.AppendLine(kv.Value);
                        sb.AppendLine();
                    }
                }

                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                sb.AppendLine("INSTRUCCIÓN FINAL PARA EL ASISTENTE IA:");
                sb.AppendLine("Utiliza los IDs de proyecto, parámetros y esquemas anteriores para estructurar y aplicar la plantilla BIM.");

                txtMegaPromptResult.Text = sb.ToString();
                AddLog("✅ Mega-Prompt compilado exitosamente. Listo para copiar.");
            }
            catch (Exception ex)
            {
                AddLog($"❌ Error compilando Mega-Prompt: {ex.Message}");
                txtMegaPromptResult.Text = $"Error: {ex.Message}";
            }
            finally
            {
                btnCompileMegaPrompt.IsEnabled = true;
            }
        }

        private void BtnCopyMegaPrompt_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtMegaPromptResult.Text))
            {
                WpfMessageBox.Show("Primero debes compilar el Mega-Prompt haciendo clic en '⚡ 1. Compilar Prompt'.", "Mega-Prompt Vacío", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                System.Windows.Clipboard.SetText(txtMegaPromptResult.Text);
                AddLog("📋 Mega-Prompt copiado al portapapeles.");
                WpfMessageBox.Show("✅ Mega-Prompt copiado al portapapeles.\n\nPégalo en Claude, Gemini, ChatGPT o tu asistente de IA para proceder con la configuración.", "Mega-Prompt Copiado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AddLog($"Error al copiar al portapapeles: {ex.Message}");
            }
        }

        private void BtnOpenPlantillasWin_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AddLog("🚀 Abriendo Asistente de Plantillas...");
                _handler.CurrentAction = ControlPanelAction.OpenPlantillas;
                _externalEvent.Raise();
            }
            catch (Exception ex)
            {
                AddLog($"Error abriendo plantillas: {ex.Message}");
            }
        }

        // --- Review Cloud & IA Actions ---
        private async void BtnPingMcp_Click(object sender, RoutedEventArgs e)
        {
            string url = "https://arca-review.vercel.app";
            AddLog($"☁️ Verificando conectividad segura con Review Cloud ({url})...");

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var res = await client.GetAsync(url);
                AddLog($"✅ Review Cloud HTTPS respondiendo correctamente: {(int)res.StatusCode} {res.StatusCode}");
                WpfMessageBox.Show($"Conexión segura con Review Cloud establecida con éxito.\n\n• Endpoint: {url}\n• Protocolo: HTTPS / TLS Seguro\n• Código: {(int)res.StatusCode} {res.StatusCode}", "Conexión Cloud Exitosa", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AddLog($"⚠️ Error conectando a Review Cloud: {ex.Message}");
                WpfMessageBox.Show($"No se pudo conectar a Review Cloud:\n{ex.Message}\n\nVerifique su conexión a Internet.", "Error de Conexión Cloud", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnCopyMcpConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Windows.Clipboard.SetText(txtMcpConfigJson.Text);
                AddLog("📋 Configuración Review Cloud copiada al portapapeles.");
                WpfMessageBox.Show("Configuración del ecosistema copiada al portapapeles.\n\nPuedes utilizarla para configurar clientes o asistentes externos.", "Configuración Copiada", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AddLog($"Error al copiar: {ex.Message}");
            }
        }

        // --- Logs ---
        public void AddLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                string time = DateTime.Now.ToString("HH:mm:ss");
                txtLogConsole.AppendText($"[{time}] {message}\n");
                scrollLog.ScrollToEnd();
            });
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            txtLogConsole.Clear();
        }

        // --- Acciones Rápidas ---
        private void BtnOpenSheetsQueue_Click(object sender, RoutedEventArgs e)
        {
            AddLog("📋 Abriendo Gestor de Cambios (Revisión e Implementación desde App Review)...");
            try
            {
                _handler.CurrentAction = ControlPanelAction.OpenSheetsQueue;
                _externalEvent.Raise();
            }
            catch (Exception ex)
            {
                AddLog($"Error abriendo Gestor de Cambios: {ex.Message}");
            }
        }

        private void BtnOpenLadrillos_Click(object sender, RoutedEventArgs e)
        {
            AddLog("🧱 Abriendo Gestor de Familias de Ladrillos...");
            try
            {
                _handler.CurrentAction = ControlPanelAction.OpenLadrillos;
                _externalEvent.Raise();
            }
            catch (Exception ex)
            {
                AddLog($"Error abriendo ventana: {ex.Message}");
            }
        }

        private void BtnOpenPisos_Click(object sender, RoutedEventArgs e)
        {
            AddLog("🪵 Abriendo Gestor de Piezas y Baldosas...");
            try
            {
                _handler.CurrentAction = ControlPanelAction.OpenPisos;
                _externalEvent.Raise();
            }
            catch (Exception ex)
            {
                AddLog($"Error abriendo ventana: {ex.Message}");
            }
        }

        private void BtnOpenSteelDeck_Click(object sender, RoutedEventArgs e)
        {
            AddLog("🔩 Abriendo Gestor Steel Deck...");
            try
            {
                _handler.CurrentAction = ControlPanelAction.OpenSteelDeck;
                _externalEvent.Raise();
            }
            catch (Exception ex)
            {
                AddLog($"Error abriendo ventana: {ex.Message}");
            }
        }

        private void BtnOpenSchedulesIA_Click(object sender, RoutedEventArgs e)
        {
            AddLog("📝 Abriendo Asistente Tablas IA...");
            try
            {
                _handler.CurrentAction = ControlPanelAction.OpenSchedulesIA;
                _externalEvent.Raise();
            }
            catch (Exception ex)
            {
                AddLog($"Error abriendo ventana: {ex.Message}");
            }
        }

        private void BtnOpenGestorCategorias_Click(object sender, RoutedEventArgs e)
        {
            AddLog("🏷️ Abriendo Gestor de Categorías BIM...");
            try
            {
                var doc = ReviewControlPanelEventHandler_Doc();
                WpfMessageBox.Show("Puedes abrir el Gestor de Categorías directamente desde la nueva pestaña '🏷️ Categorías & Familias' en la cinta de Revit.", "Gestor de Categorías BIM", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AddLog($"Error: {ex.Message}");
            }
        }

        private static Autodesk.Revit.DB.Document? ReviewControlPanelEventHandler_Doc() => null;

        private void BtnOpenWebApp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string url = "https://arca-review.vercel.app";
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                AddLog($"🌐 Abriendo Web App: {url}");
            }
            catch (Exception ex)
            {
                AddLog($"Error abriendo navegador: {ex.Message}");
            }
        }

        private void BtnOpenAsistenteAnotacion_Click(object sender, RoutedEventArgs e)
        {
            AddLog("📐 Abriendo Asistente de Anotación & Gráficos...");
        }

        private void BtnOpenFamilyRevisor_Click(object sender, RoutedEventArgs e)
        {
            AddLog("🏷️ Abriendo Renombrador de Familias...");
        }

        private void BtnOpenSheetRevisor_Click(object sender, RoutedEventArgs e)
        {
            AddLog("📑 Abriendo Renombrador de Láminas...");
        }

        // =========================================================================
        // EXPORTADOR DE METADATA Y ARCHIVOS (SIN BD)
        // =========================================================================

        private void BtnExportIfcJson_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Archivos ifcJSON (*.ifcjson;*.json)|*.ifcjson;*.json|Todos los archivos (*.*)|*.*",
                    DefaultExt = "ifcjson",
                    FileName = $"RevitModel_{DateTime.Now:yyyyMMdd_HHmm}.ifcjson"
                };

                if (sfd.ShowDialog() == true)
                {
                    _handler.ExportTargetFilePath = sfd.FileName;
                    _handler.CurrentAction = ControlPanelAction.ExportIfcJson;
                    _externalEvent.Raise();
                    AddLog($"🏗️ Iniciando exportación ifcJSON a: {Path.GetFileName(sfd.FileName)}");
                }
            }
            catch (Exception ex)
            {
                AddLog($"❌ Error al solicitar exportación ifcJSON: {ex.Message}");
            }
        }

        private void BtnExport3dModel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Modelo 3D Web JSON (*.json)|*.json|Todos los archivos (*.*)|*.*",
                    DefaultExt = "json",
                    FileName = "Web3D_Model_Stylized.json"
                };

                if (sfd.ShowDialog() == true)
                {
                    _handler.ExportTargetFilePath = sfd.FileName;
                    _handler.CurrentAction = ControlPanelAction.Export3dModel;
                    _externalEvent.Raise();
                    AddLog($"🎨 Iniciando exportación Modelo 3D Web a: {Path.GetFileName(sfd.FileName)}");
                }
            }
            catch (Exception ex)
            {
                AddLog($"❌ Error al exportar Modelo 3D: {ex.Message}");
            }
        }

        private void SyncExportCheckboxesToHandler()
        {
            _handler.ExportIncludeProjectInfo = chkExpProjectInfo.IsChecked == true;
            _handler.ExportIncludeSiteLocation = chkExpSiteLocation.IsChecked == true;
            _handler.ExportIncludeLevels = chkExpLevels.IsChecked == true;
            _handler.ExportIncludeCategorySummary = chkExpCategorySummary.IsChecked == true;
            _handler.ExportIncludeElementDetails = chkExpElementDetails.IsChecked == true;
            _handler.ExportIncludeRooms = chkExpRooms.IsChecked == true;
            _handler.ExportIncludeSchedules = chkExpSchedules.IsChecked == true;
            _handler.ExportIncludeSheetsAndViews = chkExpSheets.IsChecked == true;
            _handler.ExportIncludeMaterials = chkExpMaterials.IsChecked == true;
            _handler.ExportIncludeWeb3dModel = chkExpWeb3d.IsChecked == true;
        }

        private void BtnExportFullJson_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Archivos JSON (*.json)|*.json|Todos los archivos (*.*)|*.*",
                    DefaultExt = "json",
                    FileName = $"RevitMetadata_{DateTime.Now:yyyyMMdd_HHmm}.json"
                };

                if (sfd.ShowDialog() == true)
                {
                    SyncExportCheckboxesToHandler();
                    _handler.ExportTargetFilePath = sfd.FileName;
                    _handler.CurrentAction = ControlPanelAction.ExportFullMetadataJson;
                    _externalEvent.Raise();
                    AddLog($"💾 Iniciando exportación de metadata completa a: {Path.GetFileName(sfd.FileName)}");
                }
            }
            catch (Exception ex)
            {
                AddLog($"❌ Error al exportar JSON completo: {ex.Message}");
            }
        }

        private void BtnPreviewExport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SyncExportCheckboxesToHandler();
                _handler.CurrentAction = ControlPanelAction.PreviewMetadataJson;
                _externalEvent.Raise();
                AddLog("🔍 Solicitando vista previa de metadata...");
            }
            catch (Exception ex)
            {
                AddLog($"❌ Error generando vista previa: {ex.Message}");
            }
        }

        private void BtnCopyExportPreview_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(txtExportPreview.Text))
                {
                    System.Windows.Clipboard.SetText(txtExportPreview.Text);
                    AddLog("📋 Vista previa copiada al portapapeles.");
                    WpfMessageBox.Show("¡Estructura JSON copiada al portapapeles con éxito!", "Review Exportador", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                AddLog($"⚠️ Error copiando al portapapeles: {ex.Message}");
            }
        }

        private void BtnSelectAllExportChecks_Click(object sender, RoutedEventArgs e)
        {
            chkExpProjectInfo.IsChecked = true;
            chkExpSiteLocation.IsChecked = true;
            chkExpLevels.IsChecked = true;
            chkExpCategorySummary.IsChecked = true;
            chkExpElementDetails.IsChecked = true;
            chkExpRooms.IsChecked = true;
            chkExpSchedules.IsChecked = true;
            chkExpSheets.IsChecked = true;
            chkExpMaterials.IsChecked = true;
            chkExpWeb3d.IsChecked = true;
        }

        private void BtnDeselectAllExportChecks_Click(object sender, RoutedEventArgs e)
        {
            chkExpProjectInfo.IsChecked = false;
            chkExpSiteLocation.IsChecked = false;
            chkExpLevels.IsChecked = false;
            chkExpCategorySummary.IsChecked = false;
            chkExpElementDetails.IsChecked = false;
            chkExpRooms.IsChecked = false;
            chkExpSchedules.IsChecked = false;
            chkExpSheets.IsChecked = false;
            chkExpMaterials.IsChecked = false;
            chkExpWeb3d.IsChecked = false;
        }

        private void OnExportPreviewReceived(string json)
        {
            Dispatcher.Invoke(() =>
            {
                txtExportPreview.Text = json;
            });
        }

        private void OnFileExportCompleted(bool success, string message)
        {
            Dispatcher.Invoke(() =>
            {
                WpfMessageBox.Show(message, "Exportación de Archivos Review", MessageBoxButton.OK, success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            });
        }
    }
}
