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
  ""name"": ""Esquema BIM: Normalización y Cómputos en Revit"",
  ""description"": ""Esquema jerárquico con capa de acciones ejecutables en Revit"",
  ""blockType"": ""text"",
  ""children"": [
    {
      ""name"": ""1. AUDITORÍA Y CODIFICACIÓN DE MUROS"",
      ""blockType"": ""text"",
      ""children"": [
        {
          ""name"": ""Codificar Muros del Modelo"",
          ""description"": ""Actualiza Comentarios en todos los muros"",
          ""blockType"": ""revit_action"",
          ""revitAction"": {
            ""enabled"": true,
            ""order"": 1,
            ""phase"": ""Fase 1: Normalización"",
            ""category"": ""Walls"",
            ""tool"": ""modify_parameters"",
            ""filter"": {
              ""type_name"": """",
              ""family_name"": """",
              ""level_name"": """",
              ""search_text"": """"
            },
            ""element_ids"": [],
            ""parameters"": {
              ""Comments"": ""NORMALIZADO DESDE ESQUEMA ARCA REVIEW""
            }
          },
          ""children"": []
        }
      ]
    },
    {
      ""name"": ""2. DOCUMENTACIÓN Y TABLAS BIM"",
      ""blockType"": ""text"",
      ""children"": [
        {
          ""name"": ""Crear Tabla de Cuantificación de Muros"",
          ""description"": ""Crea ViewSchedule automáticamente"",
          ""blockType"": ""revit_action"",
          ""revitAction"": {
            ""enabled"": true,
            ""order"": 2,
            ""phase"": ""Fase 2: Documentación"",
            ""category"": ""Schedules"",
            ""tool"": ""create_schedule"",
            ""filter"": { ""type_name"": """", ""family_name"": """", ""level_name"": """", ""search_text"": """" },
            ""element_ids"": [],
            ""parameters"": {
              ""ScheduleName"": ""ARCA_COMPUTO_MUROS_BIM"",
              ""TargetCategory"": ""Walls"",
              ""Fields"": ""Family and Type,Base Constraint,Length,Area,Volume,Comments""
            }
          },
          ""children"": []
        }
      ]
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
                txtFooterStatus.Text = "✅ Mega-Prompt de Esquemas Revit copiado al portapapeles. Pégalo en tu IA o en ARCA Review.";
                MessageBox.Show(
                    "¡Prompt Maestro de Esquemas y Acciones de Revit copiado al portapapeles!\n\n" +
                    "1. Pégalo en Gemini, ChatGPT o Claude y describe el proceso BIM que necesitas.\n" +
                    "2. El JSON generado sirve tanto en el Mapa de Esquemas de ARCA Review Web como en este Receptor del Plugin.",
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
