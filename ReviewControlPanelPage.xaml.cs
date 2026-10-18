using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

        public ReviewControlPanelPage()
        {
            InitializeComponent();
            Instance = this;

            _handler = new ReviewControlPanelEventHandler();
            _externalEvent = ExternalEvent.Create(_handler);

            _handler.OnMetricsRefreshed = OnMetricsReceived;
            _handler.OnSyncCompleted = OnSyncFinished;
            _handler.OnLogMessage = AddLog;

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
            await LoadProjectsAsync();

            AddLog("🚀 Panel de Control Review inicializado correctamente.");
            AddLog($"📡 Bi-MCP Local escuchando en http://localhost:{App.McpServerInstance?.Port ?? 8080}/mcp/");
            
            // Solicitar escaneo inicial del modelo abierto
            TriggerMetricsRefresh();
        }

        private void OnSessionStateChanged()
        {
            Dispatcher.Invoke(() =>
            {
                UpdateUserSessionDisplay();
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
                btnToggleAuth.Content = "Iniciar Sesión";
            }

            txtActiveProjectId.Text = AuthenticationService.ActiveProjectId;
            SelectActiveProjectInCombo();
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
            }
        }

        private void BtnSaveProjectSelection_Click(object sender, RoutedEventArgs e)
        {
            if (cmbSupabaseProjects.SelectedItem is SupabaseProjectItem selected)
            {
                AuthenticationService.SetActiveProject(selected.Id, selected.Name);
                AddLog($"💾 Proyecto activo configurado: '{selected.Name}' (ID: {selected.Id})");
                WpfMessageBox.Show($"Proyecto activo vinculado:\n\n• Nombre: {selected.Name}\n• ID: {selected.Id}", "Review - Proyecto Configurado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void BtnRefreshProjects_Click(object sender, RoutedEventArgs e)
        {
            await LoadProjectsAsync();
        }

        private void BtnToggleAuth_Click(object sender, RoutedEventArgs e)
        {
            if (AuthenticationService.IsLoggedIn)
            {
                var confirm = WpfMessageBox.Show("¿Deseas cerrar tu sesión actual de Supabase?", "Cerrar Sesión", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm == MessageBoxResult.Yes)
                {
                    AuthenticationService.Logout();
                    AddLog("🔒 Sesión cerrada.");
                }
            }
            else
            {
                AddLog("🔐 Abriendo ventana de login en navegador...");
                Task.Run(async () =>
                {
                    await AuthenticationService.LoginWithBrowserAsync();
                });
            }
        }

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
            AddLog("📐 Abriendo Cola de Cambios de Sheets (Sincronización Inversa desde Review / MCP)...");
            try
            {
                _handler.CurrentAction = ControlPanelAction.OpenSheetsQueue;
                _externalEvent.Raise();
            }
            catch (Exception ex)
            {
                AddLog($"Error abriendo Cola de Sheets: {ex.Message}");
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
            AddLog("🏷️ Indicando ubicación de Gestor de Categorías...");
            try
            {
                WpfMessageBox.Show("Puedes abrir el Gestor de Categorías desde la pestaña '🪵 Enchapes & Pisos' en la cinta de Revit.", "Gestor de Categorías", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AddLog($"Error: {ex.Message}");
            }
        }

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
    }
}
