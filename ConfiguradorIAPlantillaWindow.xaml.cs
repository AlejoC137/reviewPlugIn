using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WpfCheckBox  = System.Windows.Controls.CheckBox;
using WpfVisibility = System.Windows.Visibility;
using RevitView     = Autodesk.Revit.DB.View;

namespace RevitToSupabasePlugin
{
    public partial class ConfiguradorIAPlantillaWindow : Window
    {
        // ── State ────────────────────────────────────────────────────────────────
        private readonly Document _doc;
        private readonly SupabaseService _supabase = new SupabaseService();

        /// <summary>tableName → fetched rows (pretty JSON string)</summary>
        private Dictionary<string, string> _fetchedData = new();

        // ── Constructor ──────────────────────────────────────────────────────────

        public ConfiguradorIAPlantillaWindow(Document doc)
        {
            InitializeComponent();
            _doc = doc;
        }

        // ══════════════════════════════════════════════════════════════════════════
        // PASO 1 — LOGIN
        // ══════════════════════════════════════════════════════════════════════════

        private async void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            string email = TxtEmail.Text.Trim();
            string password = TxtPassword.Password;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                TxtLoginStatus.Text = "Por favor ingresa email y contraseña.";
                TxtLoginStatus.Foreground = System.Windows.Media.Brushes.OrangeRed;
                return;
            }

            SetBusy("Conectando a Supabase…");
            BtnLogin.IsEnabled = false;

            var (ok, error) = await _supabase.SignInAsync(email, password);

            BtnLogin.IsEnabled = true;
            SetIdle();

            if (!ok)
            {
                TxtLoginStatus.Foreground = System.Windows.Media.Brushes.OrangeRed;
                TxtLoginStatus.Text = $"❌ {error}";
                return;
            }

            TxtLoginStatus.Foreground = System.Windows.Media.Brushes.MediumSpringGreen;
            TxtLoginStatus.Text = $"✅ Conectado como {email}";
            TxtBadgeAuth.Text = $"✓ {email}";
            BadgeAuth.Visibility = WpfVisibility.Visible;

            // Populate tables tab
            PopulateTables();
            TabTablas.IsEnabled = true;
            TabPrompt.IsEnabled = true;
            TabApply.IsEnabled = true;
            MainTabControl.SelectedItem = TabTablas;
            SetStatus($"Conectado. Selecciona las tablas a incluir.");
        }

        // ══════════════════════════════════════════════════════════════════════════
        // PASO 2 — TABLAS
        // ══════════════════════════════════════════════════════════════════════════

        private void PopulateTables()
        {
            PanelTablas.Children.Clear();
            var tables = _supabase.GetKnownTables();
            foreach (var t in tables)
            {
                var cb = new WpfCheckBox
                {
                    Content = t,
                    IsChecked = true,
                    Tag = t
                };
                if (t == "plugin_project_data")
                {
                    cb.Foreground = System.Windows.Media.Brushes.CornflowerBlue;
                    cb.ToolTip = "Datos exportados por la Conexión Supabase";
                }
                PanelTablas.Children.Add(cb);
            }
        }

        private void BtnSelAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (WpfCheckBox cb in PanelTablas.Children.OfType<WpfCheckBox>())
                cb.IsChecked = true;
        }

        private void BtnSelNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (WpfCheckBox cb in PanelTablas.Children.OfType<WpfCheckBox>())
                cb.IsChecked = false;
        }

        private async void BtnFetchAndCopy_Click(object sender, RoutedEventArgs e)
        {
            var selected = PanelTablas.Children.OfType<WpfCheckBox>()
                .Where(cb => cb.IsChecked == true)
                .Select(cb => cb.Tag?.ToString() ?? "")
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();

            if (selected.Count == 0)
            {
                SetStatus("Selecciona al menos una tabla.");
                return;
            }

            SetBusy($"Descargando {selected.Count} tabla(s)…");
            BtnFetchAndCopy.IsEnabled = false;

            _fetchedData = await _supabase.FetchTablesAsync(selected);

            BtnFetchAndCopy.IsEnabled = true;
            SetIdle();

            int errors = _fetchedData.Values.Count(v => v.StartsWith("[ERROR:"));
            SetStatus($"Datos descargados: {_fetchedData.Count} tablas ({errors} con error). Revisa el prompt.");

            // Build prompt automatically and switch tab
            BuildPrompt();
            MainTabControl.SelectedItem = TabPrompt;
        }

        // ══════════════════════════════════════════════════════════════════════════
        // PASO 3 — MEGA-PROMPT
        // ══════════════════════════════════════════════════════════════════════════

        private void BtnGeneratePrompt_Click(object sender, RoutedEventArgs e) => BuildPrompt();

        private void BtnCopyPrompt_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(TxtPrompt.Text)) return;
            System.Windows.Clipboard.SetText(TxtPrompt.Text);
            SetStatus("✅ Prompt copiado al portapapeles — pégalo en tu IA junto con el PDF del proyecto.");
        }

        private void BuildPrompt()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=======================================================");
            sb.AppendLine("  PROMPT PARA CONFIGURACIÓN DE PLANTILLA REVIT BIM");
            sb.AppendLine("=======================================================");
            sb.AppendLine();
            sb.AppendLine("Eres un experto en configuración de plantillas Revit BIM y en interpretación");
            sb.AppendLine("de documentos de diseño arquitectónico.");
            sb.AppendLine();

            // ── Supabase data ─────────────────────────────────────────────────────
            if (_fetchedData.Count > 0)
            {
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                sb.AppendLine("SECCIÓN A: DATOS DE LA BASE DE DATOS DEL PROYECTO (Supabase)");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                sb.AppendLine();
                foreach (var kv in _fetchedData)
                {
                    sb.AppendLine($"=== Tabla: {kv.Key} ===");
                    sb.AppendLine(kv.Value);
                    sb.AppendLine();
                }
            }

            // ── Revit model data ──────────────────────────────────────────────────
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine("SECCIÓN B: ESTADO ACTUAL DE LA PLANTILLA REVIT");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine();

            try
            {
                if (ChkRevitInfo.IsChecked == true)
                {
                    sb.AppendLine("=== Información del Proyecto ===");
                    var pi = _doc.ProjectInformation;
                    sb.AppendLine($"  Nombre:       {pi.Name}");
                    sb.AppendLine($"  Número:       {pi.Number}");
                    sb.AppendLine($"  Dirección:    {pi.Address}");
                    sb.AppendLine($"  Autor:        {pi.Author}");
                    sb.AppendLine($"  Edificio:     {pi.BuildingName}");
                    sb.AppendLine($"  Organización: {pi.OrganizationName}");
                    sb.AppendLine();
                }

                if (ChkRevitLevels.IsChecked == true)
                {
                    sb.AppendLine("=== Niveles actuales ===");
                    using var col = new FilteredElementCollector(_doc);
                    col.OfClass(typeof(Level));
                    foreach (Element e in col.OrderBy(x => ((Level)x).Elevation))
                    {
                        var lv = (Level)e;
                        double mm = lv.Elevation * 304.8;
                        sb.AppendLine($"  {lv.Name}  (elevación: {mm:F0} mm)");
                    }
                    sb.AppendLine();
                }

                if (ChkRevitMaterials.IsChecked == true)
                {
                    sb.AppendLine("=== Materiales actuales ===");
                    using var col = new FilteredElementCollector(_doc);
                    col.OfClass(typeof(Material));
                    int n = 0;
                    foreach (Element e in col.Take(60))
                    {
                        var m = (Material)e;
                        sb.AppendLine($"  {m.Name}  RGB({m.Color.Red},{m.Color.Green},{m.Color.Blue})");
                        n++;
                    }
                    if (n == 60) sb.AppendLine("  ... (truncado a 60 materiales)");
                    sb.AppendLine();
                }

                if (ChkRevitSchedules.IsChecked == true)
                {
                    sb.AppendLine("=== Schedules / Tablas actuales ===");
                    using var col = new FilteredElementCollector(_doc);
                    col.OfClass(typeof(ViewSchedule));
                    foreach (Element e in col)
                    {
                        if (e is ViewSchedule vs && !vs.IsTemplate)
                            sb.AppendLine($"  {vs.Name}");
                    }
                    sb.AppendLine();
                }

                if (ChkRevitViews.IsChecked == true)
                {
                    sb.AppendLine("=== Vistas actuales ===");
                    using var colV = new FilteredElementCollector(_doc);
                    colV.OfClass(typeof(RevitView));
                    int nv = 0;
                    foreach (Element e in colV)
                    {
                        if (e is RevitView v && !v.IsTemplate && !(v is ViewSheet))
                        {
                            sb.AppendLine($"  [{v.ViewType}] {v.Name}  (escala 1:{v.Scale})");
                            if (++nv >= 50) { sb.AppendLine("  ... (truncado)"); break; }
                        }
                    }
                    sb.AppendLine();

                    sb.AppendLine("=== Planos (Sheets) actuales ===");
                    using var colS = new FilteredElementCollector(_doc);
                    colS.OfClass(typeof(ViewSheet));
                    foreach (Element e in colS)
                    {
                        if (e is ViewSheet vs)
                            sb.AppendLine($"  Nº {vs.SheetNumber}  —  {vs.Name}");
                    }
                    sb.AppendLine();
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[Error leyendo el modelo: {ex.Message}]");
            }

            // ── Instructions ──────────────────────────────────────────────────────
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine("INSTRUCCIÓN PRINCIPAL");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine();
            sb.AppendLine("Analiza el PDF adjunto (documento de diseño del proyecto) y TODA la");
            sb.AppendLine("información anterior. Genera un JSON que configure esta plantilla Revit");
            sb.AppendLine("para que se parezca lo más posible al proyecto descrito.");
            sb.AppendLine();
            sb.AppendLine("El JSON DEBE seguir exactamente esta estructura:");
            sb.AppendLine();
            sb.AppendLine(@"{
  ""project_info"": {
    ""name"": ""Nombre del proyecto"",
    ""number"": ""001"",
    ""client"": ""Cliente"",
    ""author"": ""Arquitecto"",
    ""address"": ""Dirección"",
    ""building_name"": ""Edificio"",
    ""organization_name"": ""Empresa"",
    ""status"": ""Proyecto"",
    ""issue_date"": ""2024-01-01""
  },
  ""levels"": [
    { ""current_name"": ""Level 1"", ""new_name"": ""N0 - Planta Baja"", ""elevation_mm"": 0 }
  ],
  ""materials"": [
    { ""current_name"": ""Default Wall"", ""new_name"": ""Concreto Estructural"", ""color_r"": 180, ""color_g"": 170, ""color_b"": 160 }
  ],
  ""text_styles"": [
    { ""type_name"": ""2.5mm Arial"", ""font_size_mm"": 2.5, ""bold"": false, ""italic"": false }
  ],
  ""views"": [
    { ""current_name"": ""Floor Plan: Level 1"", ""new_name"": ""Planta Nivel 0"", ""scale"": 100 }
  ],
  ""sheets"": [
    { ""current_number"": ""A101"", ""new_number"": ""ARQ-01"", ""new_name"": ""Planta General Nivel 0"", ""drawn_by"": ""A. Lopez"" }
  ],
  ""schedules_to_create"": [
    { ""name"": ""Tabla de Acabados"", ""category"": ""Rooms"", ""fields"": [""Name"", ""Area"", ""Comments""] }
  ],
  ""project_parameters"": [
    { ""name"": ""Project Status"", ""value"": ""Fase 2 - Diseño Básico"" }
  ]
}");
            sb.AppendLine();
            sb.AppendLine("REGLAS IMPORTANTES:");
            sb.AppendLine("• Devuelve ÚNICAMENTE el bloque JSON. Sin explicaciones, sin código Python, sin markdown.");
            sb.AppendLine("• Usa los nombres EXACTOS de los niveles/vistas/planos tal como aparecen en la Sección B.");
            sb.AppendLine("• Si no tienes información para un campo, omítelo (no uses null ni strings vacíos).");
            sb.AppendLine("• Para 'schedules_to_create', la categoría puede ser: Rooms, Walls, Doors, Windows, Floors, Furniture.");

            TxtPrompt.Text = sb.ToString();
        }

        // ══════════════════════════════════════════════════════════════════════════
        // PASO 4 — APLICAR JSON
        // ══════════════════════════════════════════════════════════════════════════

        private const string ExampleJson = @"{
  ""project_info"": {
    ""name"": ""Mi Proyecto"",
    ""number"": ""001"",
    ""client"": ""Cliente Ejemplo"",
    ""author"": ""Arq. González"",
    ""address"": ""Bogotá, Colombia"",
    ""status"": ""Proyecto""
  },
  ""levels"": [
    { ""current_name"": ""Level 1"", ""new_name"": ""N0 - Planta Baja"", ""elevation_mm"": 0 },
    { ""current_name"": ""Level 2"", ""new_name"": ""N1 - Primer Piso"", ""elevation_mm"": 3200 }
  ],
  ""project_parameters"": [
    { ""name"": ""Project Status"", ""value"": ""En Diseño"" }
  ]
}";

        private void BtnInsertExample_Click(object sender, RoutedEventArgs e)
        {
            TxtJsonInput.Text = ExampleJson;
        }

        private void BtnClearJson_Click(object sender, RoutedEventArgs e)
        {
            TxtJsonInput.Text = "";
            TxtLog.Text = "";
            BorderLog.Visibility = WpfVisibility.Collapsed;
        }

        private void BtnApplyJson_Click(object sender, RoutedEventArgs e)
        {
            string jsonText = TxtJsonInput.Text.Trim();
            if (string.IsNullOrEmpty(jsonText))
            {
                SetStatus("❌ El campo JSON está vacío.");
                return;
            }

            SetBusy("Aplicando cambios en Revit…");
            BtnApplyJson.IsEnabled = false;

            ApplicatorResult result;
            try
            {
                result = PlantillaApplicatorService.ApplyJson(_doc, jsonText);
            }
            catch (Exception ex)
            {
                result = new ApplicatorResult();
                result.Success = false;
                result.Errors.Add(ex.Message);
            }

            BtnApplyJson.IsEnabled = true;
            SetIdle();

            // Show log
            var sb = new StringBuilder();
            if (result.Success)
            {
                sb.AppendLine($"✅ {result.Summary}");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine($"⚠️  La operación terminó con errores. {result.Summary}");
                sb.AppendLine();
            }

            if (result.Log.Count > 0)
            {
                sb.AppendLine("── Cambios aplicados ──");
                foreach (var entry in result.Log) sb.AppendLine($"  ✓ {entry}");
                sb.AppendLine();
            }

            if (result.Errors.Count > 0)
            {
                sb.AppendLine("── Errores ──");
                foreach (var err in result.Errors) sb.AppendLine($"  ✗ {err}");
            }

            TxtLog.Text = sb.ToString();
            TxtLog.Foreground = result.Errors.Count == 0
                ? System.Windows.Media.Brushes.MediumSpringGreen
                : System.Windows.Media.Brushes.OrangeRed;
            BorderLog.Visibility = WpfVisibility.Visible;

            SetStatus(result.Success
                ? $"✅ {result.Summary} (reversible con Ctrl+Z)"
                : $"⚠️ {result.Summary}");
        }

        // ══════════════════════════════════════════════════════════════════════════
        // HELPERS
        // ══════════════════════════════════════════════════════════════════════════

        private void SetStatus(string msg)
        {
            TxtStatus.Text = msg;
        }

        private void SetBusy(string msg)
        {
            TxtStatus.Text = msg;
            TxtLoadingDots.Text = "⟳";
        }

        private void SetIdle()
        {
            TxtLoadingDots.Text = "";
        }
    }
}
