using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WpfMessageBox = System.Windows.MessageBox;

namespace RevitToSupabasePlugin.Views
{
    public class McpToolDisplayItem
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string DefaultParamsJson { get; set; } = "{}";
    }

    public partial class McpControlPanelWindow : Window
    {
        private readonly UIApplication? _uiApp;
        private readonly List<McpToolDisplayItem> _tools = new();

        public McpControlPanelWindow(UIApplication? uiApp = null)
        {
            _uiApp = uiApp;
            InitializeComponent();

            Loaded += McpControlPanelWindow_Loaded;
            Closed += McpControlPanelWindow_Closed;
        }

        private void McpControlPanelWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RevitMcpServer.OnLogMessage += OnServerLogMessage;
            PopulateToolsList();
            UpdateServerStatusUi();
            GenerateClientConfigSnippet();

            AddLog($"[{DateTime.Now:HH:mm:ss}] 🚀 Centro de Control Bi-MCP abierto.");
            if (App.McpServerInstance?.IsRunning == true)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 🟢 Servidor MCP en línea en loopback port {App.McpServerInstance.Port}.");
            }
            else
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 🔴 Servidor MCP actualmente detenido.");
            }
        }

        private void McpControlPanelWindow_Closed(object? sender, EventArgs e)
        {
            RevitMcpServer.OnLogMessage -= OnServerLogMessage;
        }

        private void OnServerLogMessage(string msg)
        {
            Dispatcher.Invoke(() =>
            {
                AddLog(msg);
                txtKpiRequests.Text = (App.McpServerInstance?.RequestCount ?? 0).ToString();
            });
        }

        public void AddLog(string line)
        {
            txtConsoleLog.AppendText(line + Environment.NewLine);
            txtConsoleLog.ScrollToEnd();
        }

        private void UpdateServerStatusUi()
        {
            bool running = App.McpServerInstance?.IsRunning ?? false;
            int port = App.McpServerInstance?.Port ?? 8080;
            int requests = App.McpServerInstance?.RequestCount ?? 0;

            if (running)
            {
                txtStatus.Text = $"🟢 EN LÍNEA: 127.0.0.1:{port}";
                txtStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(6, 95, 70));
                borderStatus.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(209, 250, 229));
                borderStatus.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(5, 150, 105));
                dotStatus.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129));

                txtKpiState.Text = "ACTIVO";
                txtKpiState.Foreground = txtStatus.Foreground;
                txtKpiPort.Text = port.ToString();
            }
            else
            {
                txtStatus.Text = "🔴 MCP DETENIDO";
                txtStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(153, 27, 27));
                borderStatus.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(254, 226, 226));
                borderStatus.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38));
                dotStatus.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));

                txtKpiState.Text = "DETENIDO";
                txtKpiState.Foreground = txtStatus.Foreground;
                txtKpiPort.Text = "OFF";
            }

            txtKpiRequests.Text = requests.ToString();
        }

        private void PopulateToolsList()
        {
            _tools.Clear();
            _tools.Add(new McpToolDisplayItem
            {
                Name = "revit_get_active_session",
                Description = "Obtiene el estado en vivo del documento abierto, vista activa, ruta y usuario.",
                DefaultParamsJson = "{}"
            });
            _tools.Add(new McpToolDisplayItem
            {
                Name = "revit_get_selection",
                Description = "Obtiene los elementos actualmente seleccionados en la pantalla de Revit.",
                DefaultParamsJson = "{}"
            });
            _tools.Add(new McpToolDisplayItem
            {
                Name = "revit_select_elements",
                Description = "Selecciona elementos en pantalla por sus ElementIds y enfoca la vista.",
                DefaultParamsJson = "{\n  \"element_ids\": [],\n  \"zoom_to_elements\": true\n}"
            });
            _tools.Add(new McpToolDisplayItem
            {
                Name = "revit_query_elements",
                Description = "Consulta y filtra elementos del modelo por categoría, nivel o texto.",
                DefaultParamsJson = "{\n  \"category\": \"Doors\",\n  \"limit\": 10\n}"
            });
            _tools.Add(new McpToolDisplayItem
            {
                Name = "revit_create_door_assemblies",
                Description = "Crea ensamblajes (Assemblies) en lote para puertas/ventanas y genera vistas técnicas.",
                DefaultParamsJson = "{\n  \"group_by_instance\": false,\n  \"single_instance_per_type\": true,\n  \"initial_name\": \"P-01\",\n  \"create_views\": true,\n  \"category\": \"Doors\"\n}"
            });
            _tools.Add(new McpToolDisplayItem
            {
                Name = "revit_get_levels_and_rooms",
                Description = "Lista todos los niveles y habitaciones creadas en el proyecto activo.",
                DefaultParamsJson = "{}"
            });
            _tools.Add(new McpToolDisplayItem
            {
                Name = "revit_get_materials",
                Description = "Lista los materiales cargados en el proyecto activo de Revit.",
                DefaultParamsJson = "{\n  \"limit\": 25\n}"
            });
            _tools.Add(new McpToolDisplayItem
            {
                Name = "revit_get_element_parameters",
                Description = "Inspecciona en profundidad todos los parámetros de instancia y tipo de un elemento.",
                DefaultParamsJson = "{\n  \"element_id\": 0\n}"
            });
            _tools.Add(new McpToolDisplayItem
            {
                Name = "revit_modify_parameters",
                Description = "Modifica parámetros de instancia o comentarios en uno o varios elementos.",
                DefaultParamsJson = "{\n  \"element_ids\": [],\n  \"parameters\": {\n    \"Comments\": \"Revisado por IA\"\n  }\n}"
            });
            _tools.Add(new McpToolDisplayItem
            {
                Name = "revit_manage_sheets",
                Description = "Gestiona láminas (Sheets): listar, crear con cajetín clonado o renumerar en lote.",
                DefaultParamsJson = "{\n  \"action\": \"list\"\n}"
            });
            _tools.Add(new McpToolDisplayItem
            {
                Name = "revit_manage_views_and_viewports",
                Description = "Gestiona vistas y su colocación en láminas (Viewports).",
                DefaultParamsJson = "{\n  \"action\": \"list\"\n}"
            });
            _tools.Add(new McpToolDisplayItem
            {
                Name = "revit_delete_or_change_type",
                Description = "Elimina elementos o modifica su familia/tipo de forma segura.",
                DefaultParamsJson = "{\n  \"action\": \"change_type\",\n  \"element_ids\": []\n}"
            });
            _tools.Add(new McpToolDisplayItem
            {
                Name = "revit_convertir_pisos_3d",
                Description = "Modela y parametriza el despiece físico de baldosas sobre un piso.",
                DefaultParamsJson = "{\n  \"material_baldosa\": \"Porcelanato\"\n}"
            });
            _tools.Add(new McpToolDisplayItem
            {
                Name = "revit_crear_tablas_computos",
                Description = "Crea automáticamente tablas de planificación de cómputos métricos.",
                DefaultParamsJson = "{\n  \"especialidad\": \"Albanileria\"\n}"
            });
            _tools.Add(new McpToolDisplayItem
            {
                Name = "revit_sync_to_supabase",
                Description = "Extrae toda la metadata BIM del documento y la sincroniza con Supabase.",
                DefaultParamsJson = "{\n  \"include_3d_scene\": false\n}"
            });
            _tools.Add(new McpToolDisplayItem
            {
                Name = "revit_apply_template_config",
                Description = "Aplica configuraciones masivas mediante PlantillaApplicatorService.",
                DefaultParamsJson = "{\n  \"config_json\": \"{}\"\n}"
            });

            lstTools.ItemsSource = _tools;
            if (_tools.Count > 0)
            {
                lstTools.SelectedIndex = 0;
            }
        }

        private void LstTools_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstTools.SelectedItem is McpToolDisplayItem item)
            {
                txtSelectedToolName.Text = item.Name;
                txtSelectedToolDesc.Text = item.Description;
                txtToolInputJson.Text = item.DefaultParamsJson;
                txtToolOutputJson.Text = "// Presiona 'Ejecutar Herramienta' para ver la respuesta en vivo...";
            }
        }

        private async void BtnExecuteToolDirect_Click(object sender, RoutedEventArgs e)
        {
            if (lstTools.SelectedItem is not McpToolDisplayItem item) return;

            btnExecuteToolDirect.IsEnabled = false;
            txtToolOutputJson.Text = "⏳ Ejecutando en el hilo principal de Revit mediante ExternalEvent...";

            try
            {
                JObject args = new();
                if (!string.IsNullOrWhiteSpace(txtToolInputJson.Text))
                {
                    try
                    {
                        args = JObject.Parse(txtToolInputJson.Text);
                    }
                    catch (Exception ex)
                    {
                        WpfMessageBox.Show($"JSON de argumentos inválido:\n{ex.Message}", "Error de JSON", MessageBoxButton.OK, MessageBoxImage.Warning);
                        btnExecuteToolDirect.IsEnabled = true;
                        return;
                    }
                }

                if (App.McpServerInstance == null || !App.McpServerInstance.IsRunning)
                {
                    txtToolOutputJson.Text = "❌ El servidor MCP está detenido. Haz clic en 'Reiniciar Servidor MCP' primero.";
                    btnExecuteToolDirect.IsEnabled = true;
                    return;
                }

                var result = await App.McpServerInstance.ExecuteToolDirectAsync(item.Name, args);
                txtToolOutputJson.Text = result.ToString(Formatting.Indented);
            }
            catch (Exception ex)
            {
                txtToolOutputJson.Text = $"❌ Excepción al ejecutar: {ex.Message}";
            }
            finally
            {
                btnExecuteToolDirect.IsEnabled = true;
                UpdateServerStatusUi();
            }
        }

        private void GenerateClientConfigSnippet()
        {
            string bridgePath = @"G:/Mi unidad/git/reviewPlugIn/Antigravity_MCP/bridge/revit-mcp-bridge.mjs";
            txtConfigSnippet.Text = "{\n" +
                                    "  \"mcpServers\": {\n" +
                                    "    \"reviewplugin\": {\n" +
                                    "      \"command\": \"node\",\n" +
                                    $"      \"args\": [\n        \"{bridgePath}\"\n      ]\n" +
                                    "    }\n" +
                                    "  }\n" +
                                    "}";
        }

        private void BtnRestart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 🔄 Reiniciando Servidor MCP...");
                App.McpServerInstance?.Stop();
                var newServer = new RevitMcpServer();
                newServer.Start();
                App.McpServerInstance = newServer;

                UpdateServerStatusUi();
                AddLog($"[{DateTime.Now:HH:mm:ss}] 🟢 Servidor MCP reiniciado con éxito en puerto {newServer.Port}.");
                WpfMessageBox.Show($"Servidor MCP reiniciado con éxito en puerto {newServer.Port}.", "MCP Reiniciado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] ❌ Error reiniciando MCP: {ex.Message}");
                WpfMessageBox.Show($"Error al reiniciar: {ex.Message}", "Error MCP", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnTestSession_Click(object sender, RoutedEventArgs e)
        {
            if (App.McpServerInstance == null) return;
            var res = await App.McpServerInstance.ExecuteToolDirectAsync("revit_get_active_session", new JObject());
            AddLog($"[{DateTime.Now:HH:mm:ss}] 📊 Proyecto Activo: {res["project_name"]} | Vista: {res["active_view"]}");
            WpfMessageBox.Show($"Sesión Activa Consultada con Éxito:\n\n• Proyecto: {res["project_name"]}\n• Vista Activa: {res["active_view"]}\n• Elementos Seleccionados: {res["selection_count"]}", "Test Exitoso", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnTestDoorAssemblies_Click(object sender, RoutedEventArgs e)
        {
            if (App.McpServerInstance == null) return;
            AddLog($"[{DateTime.Now:HH:mm:ss}] 🚪 Ejecutando creación batch de ensamblajes de puertas...");
            var args = new JObject
            {
                ["group_by_instance"] = false,
                ["single_instance_per_type"] = true,
                ["initial_name"] = "P-01",
                ["create_views"] = true,
                ["category"] = "Doors"
            };
            var res = await App.McpServerInstance.ExecuteToolDirectAsync("revit_create_door_assemblies", args);
            AddLog($"[{DateTime.Now:HH:mm:ss}] 🚪 Ensamblajes creados: {res["assemblies_created"]} | Vistas: {res["views_created"]}");
            WpfMessageBox.Show($"Resultado de Creación de Ensamblajes:\n\n• Ensamblajes creados: {res["assemblies_created"]}\n• Vistas técnicas generadas: {res["views_created"]}", "Batch Completado", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string folder = @"G:\Mi unidad\git\reviewPlugIn\Antigravity_MCP";
                Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AddLog($"Error abriendo carpeta: {ex.Message}");
            }
        }

        private void BtnLaunchInstaller_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string script = @"G:\Mi unidad\git\reviewPlugIn\Antigravity_MCP\deploy\install_mcp.cmd";
                Process.Start(new ProcessStartInfo { FileName = script, UseShellExecute = true });
                AddLog($"[{DateTime.Now:HH:mm:ss}] ⚡ Autoinstalador ejecutado.");
            }
            catch (Exception ex)
            {
                AddLog($"Error al ejecutar instalador: {ex.Message}");
            }
        }

        private void BtnCopyLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Windows.Clipboard.SetText(txtConsoleLog.Text);
                WpfMessageBox.Show("Registro de consola copiado al portapapeles.", "Log Copiado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            txtConsoleLog.Clear();
        }

        private void BtnCopyJsonSnippet_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Windows.Clipboard.SetText(txtConfigSnippet.Text);
                WpfMessageBox.Show("Configuración JSON copiada al portapapeles.", "Copiado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
        }

        private void BtnConfigureGemini_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "config", "mcp_config.json");
                string dir = Path.GetDirectoryName(path)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                File.WriteAllText(path, txtConfigSnippet.Text, Encoding.UTF8);
                AddLog($"[{DateTime.Now:HH:mm:ss}] ✅ Antigravity configurado automáticamente en {path}");
                WpfMessageBox.Show($"Antigravity ha sido configurado con éxito en:\n{path}\n\nReinicia Antigravity para aplicar los cambios.", "Configuración Exitosa", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"Error al configurar Antigravity:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnConfigureClaude_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude_desktop_config.json");
                string dir = Path.GetDirectoryName(path)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                File.WriteAllText(path, txtConfigSnippet.Text, Encoding.UTF8);
                AddLog($"[{DateTime.Now:HH:mm:ss}] ✅ Claude Desktop configurado automáticamente en {path}");
                WpfMessageBox.Show($"Claude Desktop ha sido configurado con éxito en:\n{path}\n\nReinicia Claude Desktop para aplicar los cambios.", "Configuración Exitosa", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"Error al configurar Claude:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
