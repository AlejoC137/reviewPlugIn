using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using reviewPlugIn.Services;
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;

namespace reviewPlugIn.Views
{
    public partial class AccionesDirectasIAWindow : Window
    {
        private readonly Document _doc;
        private List<DirectAiActionItem> _currentActions = new List<DirectAiActionItem>();
        private List<CloudSchemaSummary> _cloudSchemas = new List<CloudSchemaSummary>();

        public AccionesDirectasIAWindow(UIDocument uidoc) : this(uidoc?.Document)
        {
        }

        public AccionesDirectasIAWindow(Document? doc, string? initialJson = null)
        {
            InitializeComponent();
            _doc = doc;

            if (!string.IsNullOrWhiteSpace(initialJson))
            {
                txtJsonInput.Text = initialJson;
                RunSimulation();
            }
            else
            {
                LoadExampleJson();
            }

            _ = LoadCloudSchemasAsync();
        }

        private async System.Threading.Tasks.Task LoadCloudSchemasAsync()
        {
            try
            {
                btnRefreshCloudSchemas.IsEnabled = false;
                txtFooterStatus.Text = "☁️ Consultando esquemas disponibles en ARCA Review Cloud...";
                _cloudSchemas = await DirectAiActionService.FetchSchemasFromSupabaseAsync();
                cmbCloudSchemas.ItemsSource = _cloudSchemas;
                if (_cloudSchemas.Count > 0)
                {
                    cmbCloudSchemas.SelectedIndex = 0;
                    txtFooterStatus.Text = $"☁️ {_cloudSchemas.Count} esquema(s) encontrados en ARCA Review Cloud.";
                }
                else
                {
                    txtFooterStatus.Text = "ℹ️ No se encontraron esquemas remotos o sin conexión.";
                }
            }
            catch (Exception ex)
            {
                txtFooterStatus.Text = $"⚠️ No se pudo listar esquemas Cloud: {ex.Message}";
            }
            finally
            {
                btnRefreshCloudSchemas.IsEnabled = true;
            }
        }

        private void LoadExampleJson()
        {
            txtJsonInput.Text = @"{
  ""schema_version"": ""2.0-revit-schema"",
  ""schema_title"": ""FLUJO DE TRABAJO BIM: ENSAMBLAJES Y DOCUMENTACIÓN DE PUERTAS"",
  ""generated_at"": ""2026-10-27T14:32:00.000Z"",
  ""total_actions"": 3,
  ""enabled_actions"": 3,
  ""actions"": [
    {
      ""id"": ""act_001"",
      ""order"": 1,
      ""enabled"": true,
      ""step_name"": ""➕ CREAR: Ensamblajes de Puertas del Proyecto"",
      ""process"": ""Carpintería Metálica y Madera"",
      ""phase"": ""Fase 1: Ensamblajes"",
      ""path"": ""CARPINTERÍAS › PUERTAS › ENSAMBLAJES"",
      ""description"": ""Empaqueta las puertas del modelo en ensamblajes (AssemblyInstances) agrupadas por Tipo"",
      ""action"": ""create_assemblies"",
      ""tool"": ""create_assemblies"",
      ""category"": ""Doors"",
      ""filter"": {
        ""type_name"": """",
        ""family_name"": """",
        ""level_name"": """",
        ""search_text"": """"
      },
      ""element_ids"": [],
      ""parameters"": {
        ""ScheduleName"": ""Tabla de planificación de puertas CIC - Trabajo"",
        ""Fields"": ""Family, Type, Recuento, Nivel"",
        ""TargetCategory"": ""Doors"",
        ""GroupBy"": ""Type""
      },
      ""operation"": ""CREATE"",
      ""iteration_mode"": ""BY_TYPE""
    },
    {
      ""id"": ""act_002"",
      ""order"": 2,
      ""enabled"": true,
      ""step_name"": ""➕ GENERAR: Vistas y Tablas de Ensamblaje"",
      ""process"": ""Carpintería Metálica y Madera"",
      ""phase"": ""Fase 2: Documentación"",
      ""path"": ""CARPINTERÍAS › PUERTAS › VISTAS"",
      ""description"": ""Genera vista 3D, planta, elevación frontal y tabla de despiece para cada ensamblaje de puerta"",
      ""action"": ""create_assembly_views"",
      ""tool"": ""create_assembly_views"",
      ""category"": ""Doors"",
      ""filter"": {
        ""type_name"": """",
        ""family_name"": """",
        ""level_name"": """",
        ""search_text"": """"
      },
      ""element_ids"": [],
      ""parameters"": {
        ""ScheduleName"": ""NUEVA_Tabla de despiece de puertas"",
        ""Fields"": ""Nivel, Familia y tipo, Área, Comentarios"",
        ""ViewsToCreate"": ""Schedule,3D,Plan,Section""
      },
      ""operation"": ""CREATE"",
      ""iteration_mode"": ""ALL_ELEMENTS""
    },
    {
      ""id"": ""act_003"",
      ""order"": 3,
      ""enabled"": true,
      ""step_name"": ""🏷️ NORMALIZAR: Parámetro Comments ISO 19650"",
      ""process"": ""Control de Calidad BIM"",
      ""phase"": ""Fase 3: Auditoría"",
      ""path"": ""CARPINTERÍAS › PUERTAS › AUDITORÍA"",
      ""description"": ""Asigna el sello de normalización en Comentarios de todas las puertas"",
      ""action"": ""modify_parameters"",
      ""tool"": ""modify_parameters"",
      ""category"": ""Doors"",
      ""filter"": {
        ""type_name"": """",
        ""family_name"": """",
        ""level_name"": """",
        ""search_text"": """"
      },
      ""element_ids"": [],
      ""parameters"": {
        ""Comments"": ""AUDITADO ARCA REVIEW""
      },
      ""operation"": ""UPDATE"",
      ""iteration_mode"": ""ALL_ELEMENTS""
    }
  ]
}";
        }

        private async void BtnRefreshCloudSchemas_Click(object sender, RoutedEventArgs e)
        {
            await LoadCloudSchemasAsync();
        }

        private async void BtnLoadSelectedCloudSchema_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!(cmbCloudSchemas.SelectedItem is CloudSchemaSummary selected))
                {
                    MessageBox.Show("Selecciona primero un esquema de la lista Cloud.", "ARCA Review", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                btnLoadSelectedCloudSchema.IsEnabled = false;
                txtFooterStatus.Text = $"📥 Descargando esquema '{selected.Title}' desde Supabase...";

                string json = await DirectAiActionService.FetchSchemaJsonByIdAsync(selected);
                if (string.IsNullOrWhiteSpace(json))
                {
                    MessageBox.Show("El esquema seleccionado no contiene nodos o datos JSON.", "ARCA Review", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                txtJsonInput.Text = json;
                RunSimulation();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar esquema desde Cloud:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnLoadSelectedCloudSchema.IsEnabled = true;
            }
        }

        private void BtnCopyPrompt_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string prompt = DirectAiActionService.BuildSystemPrompt(_doc);
                Clipboard.SetText(prompt);
                txtFooterStatus.Text = "✅ Mega-Prompt de Alta Rigurosidad (Zero-Laxity) copiado al portapapeles con contexto real de Revit.";
                MessageBox.Show(
                    "¡Mega-Prompt de Automatización Revit 2.0 copiado al portapapeles!\n\n" +
                    "• Contiene directivas estrictas de CERO LAXITUD y Cero Alucinaciones para que la IA responda únicamente con JSON 2.0 válido sin charla conversacional.\n" +
                    "• Incluye el contexto real extraído de tu modelo activo (niveles, tablas existentes, categorías y tipos).\n" +
                    "• Soporta ingesta de flujos de trabajo en Markdown, creación de tablas, ensamblajes de puertas/familias, vistas de detalle y auditoría.\n\n" +
                    "Pégalo en Gemini, ChatGPT o Claude y adjunta tu instrucción o flujo de trabajo.",
                    "Prompt IA Copiado",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al copiar al portapapeles: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnLoadExample_Click(object sender, RoutedEventArgs e)
        {
            LoadExampleJson();
            txtFooterStatus.Text = "📄 Esquema JSON de ejemplo cargado. Presiona '🔍 2. Analizar Esquema y Simular en Modelo'.";
        }

        private void BtnPasteClipboard_Click(object sender, RoutedEventArgs e)
        {
            if (Clipboard.ContainsText())
            {
                txtJsonInput.Text = Clipboard.GetText();
                txtFooterStatus.Text = "📋 JSON pegado desde el portapapeles. Ejecutando simulación...";
                RunSimulation();
            }
        }

        private void BtnClearJson_Click(object sender, RoutedEventArgs e)
        {
            txtJsonInput.Clear();
            _currentActions.Clear();
            gridActions.ItemsSource = null;
            btnExecute.IsEnabled = false;
            txtSimSummary.Text = "Sin acciones cargadas.";
            txtFooterStatus.Text = "🗑️ Entrada JSON limpiada.";
        }

        private void BtnSimulate_Click(object sender, RoutedEventArgs e)
        {
            RunSimulation();
        }

        private void RunSimulation()
        {
            try
            {
                _currentActions = DirectAiActionService.ParseActionsJson(txtJsonInput.Text, out string schemaTitle);
                DirectAiActionService.SimulateActions(_doc, _currentActions);

                txtSchemaHeaderLabel.Text = $"1. ESQUEMA CARGADO: {schemaTitle.ToUpperInvariant()} ({_currentActions.Count} ACCIONES REVIT)";
                gridActions.ItemsSource = null;
                gridActions.ItemsSource = _currentActions;

                int totalMatched = _currentActions.Sum(a => a.MatchedCount);
                int actionableRules = _currentActions.Count(a => a.MatchedCount > 0 && a.IsSelected);

                txtSimSummary.Text = $"{_currentActions.Count} acciones extraídas | {totalMatched} elementos coincidentes en '{_doc?.Title}'";
                btnExecute.IsEnabled = actionableRules > 0;

                txtFooterStatus.Text = actionableRules > 0
                    ? $"✅ Simulación exitosa: {actionableRules} acción(es) listas para afectar {totalMatched} elementos en el modelo."
                    : "⚠️ El esquema no contiene acciones con elementos coincidentes o están desmarcadas.";
            }
            catch (Exception ex)
            {
                btnExecute.IsEnabled = false;
                txtSimSummary.Text = "Error en formato JSON";
                txtFooterStatus.Text = $"❌ Error al analizar JSON: {ex.Message}";
                MessageBox.Show(
                    $"No se pudo interpretar el JSON:\n\n{ex.Message}\n\n" +
                    "Puedes pegar tanto el JSON del árbol de Esquemas de ARCA Review como una cola de acciones JSON.",
                    "Error de Formato JSON",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void BtnExecute_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_doc == null)
                {
                    MessageBox.Show("No hay un documento de Revit activo.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var selectedActions = _currentActions.Where(a => a.IsSelected && a.MatchedCount > 0).ToList();
                if (selectedActions.Count == 0)
                {
                    MessageBox.Show("No hay acciones seleccionadas con elementos válidos para ejecutar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var result = DirectAiActionService.ExecuteActions(_doc, _currentActions);

                gridActions.Items.Refresh();
                if (result.logs != null && result.logs.Count > 0)
                {
                    txtExecutionLogs.Text = string.Join(Environment.NewLine, result.logs);
                }

                txtFooterStatus.Text = $"🎉 Ejecución completada: {result.updatedElements} elementos afectados y {result.updatedParams} operaciones realizadas.";

                MessageBox.Show(
                    $"¡Esquema de Acciones ejecutado con éxito en '{_doc.Title}'!\n\n" +
                    $"• Elementos modificados / creados: {result.updatedElements}\n" +
                    $"• Operaciones aplicadas: {result.updatedParams}",
                    "Ejecución Completada",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                txtFooterStatus.Text = $"❌ Error en ejecución: {ex.Message}";
                MessageBox.Show($"Error al aplicar cambios en Revit:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
