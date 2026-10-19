using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using reviewPlugIn.Services;
using WpfClipboard = System.Windows.Clipboard;
using WpfMessageBox = System.Windows.MessageBox;

namespace reviewPlugIn.Views
{
    public partial class AccionesDirectasIAWindow : Window
    {
        private readonly UIDocument _uidoc;
        private readonly Document _doc;
        private List<DirectAiActionItem> _simulationItems = new List<DirectAiActionItem>();

        public AccionesDirectasIAWindow(UIDocument uidoc)
        {
            InitializeComponent();
            _uidoc = uidoc ?? throw new ArgumentNullException(nameof(uidoc));
            _doc = uidoc.Document;

            TxtActiveDoc.Text = $"Documento Revit: {_doc.Title} {(_doc.IsWorkshared ? "[Compartido]" : "[Local]")}";

            LoadTemplates();
        }

        private void LoadTemplates()
        {
            CmbTemplates.Items.Add(new ComboBoxItem { Content = "— Selecciona una plantilla para comenzar —", Tag = "" });

            CmbTemplates.Items.Add(new ComboBoxItem
            {
                Content = "🧱 Muros: Cambiar parámetro por Tipo (Ej: Muros Rojos a Verde / Acabados)",
                Tag = "{\n  \"action\": \"modify_parameters\",\n  \"category\": \"Walls\",\n  \"filter\": {\n    \"type_name\": \"Muro Ladrillo 15cm\"\n  },\n  \"parameters\": {\n    \"Comentarios\": \"Revisado por IA\",\n    \"Material de Acabado\": \"Pintura Verde\"\n  }\n}"
            });

            CmbTemplates.Items.Add(new ComboBoxItem
            {
                Content = "🔢 Elementos Seleccionados: Modificar por Element IDs",
                Tag = "{\n  \"action\": \"modify_parameters\",\n  \"element_ids\": [1024501, 1024502],\n  \"parameters\": {\n    \"Comentarios\": \"Aprobado por Dirección BIM\",\n    \"Marca\": \"M-01\"\n  }\n}"
            });

            CmbTemplates.Items.Add(new ComboBoxItem
            {
                Content = "🚪 Puertas: Modificar comentarios y marca en Nivel 1",
                Tag = "{\n  \"action\": \"modify_parameters\",\n  \"category\": \"Doors\",\n  \"filter\": {\n    \"level_name\": \"Nivel 1\"\n  },\n  \"parameters\": {\n    \"Comentarios\": \"Verificar cerrajería y sentido de apertura\"\n  }\n}"
            });

            CmbTemplates.Items.Add(new ComboBoxItem
            {
                Content = "📋 Láminas (Sheets): Renombrar y Renumerar",
                Tag = "{\n  \"action\": \"manage_sheets\",\n  \"sheet_number\": \"A101\",\n  \"new_name\": \"PLANTA ARQUITECTÓNICA - NIVEL 1\",\n  \"new_number\": \"ARQ-101\"\n}"
            });

            CmbTemplates.Items.Add(new ComboBoxItem
            {
                Content = "📦 Lote Múltiple: Modificar Muros y Puertas en una sola instrucción",
                Tag = "[\n  {\n    \"action\": \"modify_parameters\",\n    \"category\": \"Walls\",\n    \"filter\": { \"type_name\": \"Muro Exterior\" },\n    \"parameters\": { \"Comentarios\": \"Fachada Principal\" }\n  },\n  {\n    \"action\": \"modify_parameters\",\n    \"category\": \"Doors\",\n    \"parameters\": { \"Comentarios\": \"Puerta Cortafuego\" }\n  }\n]"
            });

            CmbTemplates.SelectedIndex = 0;
        }

        private void CmbTemplates_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbTemplates.SelectedItem is ComboBoxItem item && item.Tag is string rawJson && !string.IsNullOrWhiteSpace(rawJson))
            {
                TxtJsonEditor.Text = rawJson;
                TxtLogStatus.Text = $"Plantilla cargada: {item.Content}. Haz clic en 'Validar & Simular Cambios'.";
            }
        }

        private void BtnPasteClipboard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (WpfClipboard.ContainsText())
                {
                    string text = WpfClipboard.GetText();
                    TxtJsonEditor.Text = text;
                    TxtLogStatus.Text = "📋 Contenido pegado exitosamente desde el portapapeles. Listo para simular.";
                }
                else
                {
                    TxtLogStatus.Text = "⚠️ El portapapeles no contiene texto.";
                }
            }
            catch (Exception ex)
            {
                TxtLogStatus.Text = $"Error leyendo el portapapeles: {ex.Message}";
            }
        }

        private void BtnCopyPrompt_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string prompt = DirectAiActionService.GetGeminiSystemPrompt();
                WpfClipboard.SetText(prompt);
                TxtLogStatus.Text = "📘 Prompt de Sistema para Gemini copiado al portapapeles. Pégalo en tu chat con Gemini.";
                WpfMessageBox.Show(
                    "Se ha copiado al portapapeles la Guía de Esquemas JSON para Gemini.\n\n" +
                    "Instrucciones:\n" +
                    "1. Pega este texto en Gemini Desktop o Web como contexto de tu proyecto.\n" +
                    "2. Pídele lo que deseas cambiar (ej. 'Modifica todos los muros rojos a verde').\n" +
                    "3. Copia el JSON que Gemini te devuelva y pégalo en esta ventana.",
                    "Prompt para Gemini Copiado",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                TxtLogStatus.Text = $"Error copiando prompt: {ex.Message}";
            }
        }

        private void BtnClearEditor_Click(object sender, RoutedEventArgs e)
        {
            TxtJsonEditor.Text = "";
            _simulationItems.Clear();
            GridSimulation.ItemsSource = null;
            TxtSimulationSummary.Text = "Editor limpio. Pega un JSON o selecciona una plantilla.";
            TxtLogStatus.Text = "Listo.";
        }

        private void BtnSimulate_Click(object sender, RoutedEventArgs e)
        {
            string json = TxtJsonEditor.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(json))
            {
                WpfMessageBox.Show("Por favor escribe o pega una instrucción JSON antes de simular.", "JSON Vacío", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TxtLogStatus.Text = "🔍 Analizando JSON y buscando elementos en el modelo activo...";
            var simResult = DirectAiActionService.Simulate(_doc, json);

            if (!simResult.Success)
            {
                string errors = string.Join(Environment.NewLine, simResult.Errors);
                TxtLogStatus.Text = $"❌ Error en la simulación: {errors}";
                WpfMessageBox.Show($"Se detectaron errores en la instrucción JSON:\n\n{errors}", "Error de Validación", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _simulationItems = simResult.Items;
            GridSimulation.ItemsSource = null;
            GridSimulation.ItemsSource = _simulationItems;

            int readyCount = _simulationItems.Count(i => i.Status == "Listo");
            int warnCount = _simulationItems.Count(i => i.Status != "Listo");

            TxtSimulationSummary.Text = $"Simulación lista: {_simulationItems.Count} parámetro(s) evaluados. ({readyCount} listos para aplicar, {warnCount} advertencias/bloqueados).";
            TxtLogStatus.Text = "✅ Simulación exitosa. Revisa los elementos y desmarca los que no desees cambiar antes de confirmar.";
        }

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _simulationItems)
            {
                if (item.Status != "Bloqueado") item.IsSelected = true;
            }
            GridSimulation.Items.Refresh();
        }

        private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _simulationItems)
            {
                item.IsSelected = false;
            }
            GridSimulation.Items.Refresh();
        }

        private void BtnExecute_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = _simulationItems.Where(i => i.IsSelected).ToList();
            if (selectedItems.Count == 0)
            {
                // Si la tabla está vacía, intentar simular y ejecutar directamente
                if (_simulationItems.Count == 0)
                {
                    BtnSimulate_Click(sender, e);
                    selectedItems = _simulationItems.Where(i => i.IsSelected).ToList();
                    if (selectedItems.Count == 0) return;
                }
                else
                {
                    WpfMessageBox.Show("No hay ninguna modificación seleccionada para aplicar.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            var confirm = WpfMessageBox.Show(
                $"¿Deseas aplicar {selectedItems.Count} cambio(s) seleccionados en el modelo de Revit?\n\n" +
                "Esta operación se registrará en el historial de Revit y puede deshacerse con Ctrl+Z.",
                "Confirmar Acción Directa de IA",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            TxtLogStatus.Text = "⚡ Ejecutando cambios en transacción de Revit...";

            var execResult = DirectAiActionService.Execute(_doc, TxtJsonEditor.Text, selectedItems);

            if (execResult.Success)
            {
                TxtLogStatus.Text = $"✅ Éxito: {execResult.Summary}";
                WpfMessageBox.Show(
                    $"{execResult.Summary}\n\nPuedes verificar los cambios directamente en las vistas y tablas de Revit.",
                    "Acción Ejecutada con Éxito",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // Re-simular para refrescar los valores actuales
                BtnSimulate_Click(sender, e);
            }
            else
            {
                string errors = string.Join(Environment.NewLine, execResult.Errors);
                TxtLogStatus.Text = $"❌ Fallo en la ejecución: {errors}";
                WpfMessageBox.Show($"Ocurrió un error al aplicar los cambios en Revit:\n\n{errors}", "Error de Ejecución", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
