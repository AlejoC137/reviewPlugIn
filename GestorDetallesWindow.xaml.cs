using System;
using System.Collections.Generic;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Newtonsoft.Json;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using View = Autodesk.Revit.DB.View;
using MessageBox = System.Windows.MessageBox;

namespace RevitToSupabasePlugin
{
    public partial class GestorDetallesWindow : Window
    {
        private readonly UIDocument _uiDoc;
        private readonly Document _doc;
        private DetailMetaPrompt? _currentPrompt;

        public GestorDetallesWindow(UIDocument uiDoc)
        {
            InitializeComponent();
            _uiDoc = uiDoc;
            _doc = uiDoc.Document;

            View activeView = _doc.ActiveView;
            TxtActiveViewInfo.Text = $"Vista Activa: {activeView.Name} | Tipo: {activeView.ViewType} | Escala 1:{activeView.Scale}";

            // Pre-cargar una plantilla de respuesta de ejemplo en el tab JSON para facilitar pruebas
            LoadSampleJsonResponse();
        }

        private void BtnGeneratePrompt_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var optionalInputs = new UserOptionalInputs
                {
                    ClimateExposure = TxtClimate.Text.Trim(),
                    PerformanceTarget = TxtPerformance.Text.Trim(),
                    CustomNotes = TxtNotes.Text.Trim()
                };

                _currentPrompt = DetailViewExtractor.ExtractCurrentView(_doc, _doc.ActiveView, optionalInputs);

                string jsonPrompt = JsonConvert.SerializeObject(_currentPrompt, Formatting.Indented);
                TxtPromptPreview.Text = jsonPrompt;

                MessageBox.Show(
                    $"Se analizaron {_currentPrompt.CutElements.Count} elementos en la vista.\nMeta-Prompt generado y listo para enviar al motor de IA o Notebook.",
                    "Análisis de Detalle Completado",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al analizar la vista: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            string rawJson = TxtJsonResponse.Text.Trim();
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                MessageBox.Show("Por favor ingresa o genera una respuesta JSON en la pestaña correspondiente antes de ejecutar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                DetailAiResponse? response = JsonConvert.DeserializeObject<DetailAiResponse>(rawJson);
                if (response == null)
                {
                    MessageBox.Show("No se pudo interpretar el JSON ingresado. Verifica su sintaxis.", "Error de Formato", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Ejecutar modificaciones y colocación paramétrica
                DetailExecutionReport report = DetailExecutionEngine.ApplyJsonActions(_doc, _doc.ActiveView, response);

                // Actualizar pestaña de reporte técnico
                if (response.TechnicalCompliance != null)
                {
                    TxtReportSummary.Text = string.IsNullOrWhiteSpace(response.TechnicalCompliance.Summary)
                        ? "Evaluación constructiva sin resumen formal."
                        : response.TechnicalCompliance.Summary;

                    TxtReportThermal.Text = string.IsNullOrWhiteSpace(response.TechnicalCompliance.ThermalBridgeStatus)
                        ? "No se especificaron medidas térmicas."
                        : response.TechnicalCompliance.ThermalBridgeStatus;

                    TxtReportWaterproofing.Text = string.IsNullOrWhiteSpace(response.TechnicalCompliance.WaterproofingStrategy)
                        ? "No se especificó estrategia hidrófuga."
                        : response.TechnicalCompliance.WaterproofingStrategy;

                    if (response.TechnicalCompliance.SiteWarnings != null && response.TechnicalCompliance.SiteWarnings.Count > 0)
                    {
                        TxtReportWarnings.Text = string.Join("\n• ", response.TechnicalCompliance.SiteWarnings);
                    }
                    else
                    {
                        TxtReportWarnings.Text = "Ninguna advertencia de constructibilidad registrada.";
                    }
                }

                string msg = $"Detalle estandarizado con éxito:\n" +
                             $"- Tipos de elementos ajustados: {report.TypesAdjusted}\n" +
                             $"- Componentes 2D inyectados: {report.ComponentsPlaced}";

                if (report.Warnings.Count > 0)
                {
                    msg += $"\n\nObservaciones ({report.Warnings.Count}):\n" + string.Join("\n", report.Warnings);
                }

                MessageBox.Show(msg, "Estandarización Aplicada", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al ejecutar las acciones en Revit: " + ex.Message, "Error de Ejecución", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnIngestCad_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Reference pickedRef = _uiDoc.Selection.PickObject(ObjectType.Element, "Selecciona el dibujo de AutoCAD (ImportInstance) en pantalla");
                Element elem = _doc.GetElement(pickedRef);

                if (elem is ImportInstance cadInstance)
                {
                    Level? level = _doc.ActiveView.GenLevel ?? new FilteredElementCollector(_doc).OfClass(typeof(Level)).FirstElement() as Level;
                    if (level == null)
                    {
                        MessageBox.Show("No se encontró ningún nivel asociado para posicionar los elementos base.", "Nivel Requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var (wallsCreated, linesSkipped) = CadToBimImporter.ConvertDwgToNative3D(_doc, cadInstance, level);

                    MessageBox.Show(
                        $"Ingesta CAD completada:\n- Muros base nativos creados: {wallsCreated}\n- Líneas filtradas/omitidas: {linesSkipped}\n\nLos muros han sido creados bajo la categoría nativa OST_Walls lista para tu plugin de detallado fino.",
                        "CAD a BIM",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                }
                else
                {
                    MessageBox.Show("El elemento seleccionado no es un archivo CAD importado/vinculado.", "Tipo Inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // Usuario canceló la selección con Escape
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al procesar el CAD: " + ex.Message, "Error CAD", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadSampleJsonResponse()
        {
            var sample = new DetailAiResponse
            {
                NodeIdentification = new NodeIdentification
                {
                    NodeType = "WALL_FLOOR_INTERSECTION",
                    StandardArchetypeId = "STD-EXT-WALL-SLAB-001",
                    ConfidenceScore = 0.98
                },
                TechnicalCompliance = new TechnicalCompliance
                {
                    Summary = "Encuentro de losa de entrepiso con muro exterior resuelto con rotura de puente térmico y lámina impermeable perimetral.",
                    ThermalBridgeStatus = "Aislamiento continuo exterior de 50mm con banda perimetral elástica contra puente acústico e impacto.",
                    WaterproofingStrategy = "Solape mínimo de 150mm en plano vertical con remate de goterón metálico.",
                    CodeReferences = new List<string> { "CTE-DB-HE", "ASTM C1193 - Sellados de junta" },
                    SiteWarnings = new List<string> { "Comprobar curado del sellante antes de fijar chapistería exterior." }
                },
                ParametricActions = new ParametricActions
                {
                    NativeAdjustments = new List<NativeElementAdjustment>(),
                    DetailComponents = new List<DetailComponentInjection>()
                }
            };

            TxtJsonResponse.Text = JsonConvert.SerializeObject(sample, Formatting.Indented);
        }
    }
}
