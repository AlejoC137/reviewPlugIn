using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using reviewPlugIn.Services;
using MessageBox = System.Windows.MessageBox;

namespace reviewPlugIn.Views
{
    public partial class SyncSheetsQueueWindow : Window
    {
        private readonly Document _doc;
        private List<SheetQueueDocument> _queues = new List<SheetQueueDocument>();
        private SheetQueueDocument? _selectedQueue;
        private bool _allChecked = true;

        public SyncSheetsQueueWindow(Document doc)
        {
            InitializeComponent();
            _doc = doc;

            string projName = _doc.ProjectInformation?.Name;
            if (string.IsNullOrWhiteSpace(projName) || projName == "Project Name")
                projName = _doc.Title;

            TxtActiveDocument.Text = $"Documento Revit activo: {_doc.Title} (Proyecto: {projName})";
            Loaded += SyncSheetsQueueWindow_Loaded;
        }

        private async void SyncSheetsQueueWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadQueuesFromSupabaseAsync();
        }

        private async System.Threading.Tasks.Task LoadQueuesFromSupabaseAsync()
        {
            try
            {
                BtnRefreshQueue.IsEnabled = false;
                BtnApplyChanges.IsEnabled = false;
                TxtStatusLog.Text = "⏳ Conectando con Supabase para consultar colas de Sheets pendientes...";

                string projName = _doc.ProjectInformation?.Name;
                if (string.IsNullOrWhiteSpace(projName) || projName == "Project Name")
                    projName = _doc.Title;

                _queues = await SheetsQueueService.FetchQueuesAsync(projName);
                CmbQueues.ItemsSource = _queues;

                if (_queues.Count > 0)
                {
                    CmbQueues.SelectedIndex = 0;
                    TxtStatusLog.Text = $"✔ Se encontraron {_queues.Count} cola(s) en Supabase. Revisa los cambios propuestos y haz clic en 'Confirmar y Aplicar Cambios en Revit'.";
                    BtnApplyChanges.IsEnabled = true;
                }
                else
                {
                    GridChanges.ItemsSource = null;
                    GridNewSheets.ItemsSource = null;
                    BorderNewSheets.Visibility = System.Windows.Visibility.Collapsed;
                    TxtQueueSummaryBadge.Text = "Sin colas";
                    TxtStatusLog.Text = "ℹ No hay colas de cambios de Sheets registradas en Supabase. Envía cambios desde Review Web (Documentos -> Gestión de Sheets) o desde el MCP de Gemini.";
                }
            }
            catch (Exception ex)
            {
                TxtStatusLog.Text = $"❌ Error al cargar la cola desde Supabase: {ex.Message}";
            }
            finally
            {
                BtnRefreshQueue.IsEnabled = true;
            }
        }

        private void CmbQueues_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedQueue = CmbQueues.SelectedItem as SheetQueueDocument;
            if (_selectedQueue == null)
            {
                GridChanges.ItemsSource = null;
                GridNewSheets.ItemsSource = null;
                BorderNewSheets.Visibility = System.Windows.Visibility.Collapsed;
                TxtQueueSummaryBadge.Text = "0 cambios";
                return;
            }

            var changes = _selectedQueue.Payload.Changes ?? new List<SheetChangeItem>();
            var newSheets = _selectedQueue.Payload.NewSheets ?? new List<NewSheetQueueItem>();

            GridChanges.ItemsSource = changes;
            GridNewSheets.ItemsSource = newSheets;
            BorderNewSheets.Visibility = newSheets.Count > 0
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;

            TxtQueueSummaryBadge.Text = $"{changes.Count} mod. / {newSheets.Count} nuevos [{_selectedQueue.Payload.Status?.ToUpperInvariant()}]";
            BtnApplyChanges.IsEnabled = (changes.Count + newSheets.Count) > 0;
        }

        private async void BtnRefreshQueue_Click(object sender, RoutedEventArgs e)
        {
            await LoadQueuesFromSupabaseAsync();
        }

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedQueue == null) return;
            _allChecked = !_allChecked;

            foreach (var ch in _selectedQueue.Payload.Changes)
                ch.IsSelected = _allChecked;

            foreach (var ns in _selectedQueue.Payload.NewSheets)
                ns.IsSelected = _allChecked;

            GridChanges.Items.Refresh();
            GridNewSheets.Items.Refresh();
        }

        private async void BtnApplyChanges_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedQueue == null)
            {
                MessageBox.Show("Selecciona una cola de cambios primero.", "Review BIM", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selectedChanges = (_selectedQueue.Payload.Changes ?? new List<SheetChangeItem>())
                .Where(c => c.IsSelected)
                .ToList();

            var selectedNewSheets = (_selectedQueue.Payload.NewSheets ?? new List<NewSheetQueueItem>())
                .Where(n => n.IsSelected)
                .ToList();

            if (selectedChanges.Count == 0 && selectedNewSheets.Count == 0)
            {
                MessageBox.Show("No hay ningún cambio marcado para aplicar.", "Review BIM", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"¿Confirmas aplicar automáticamente en el modelo Revit:\n\n" +
                $"• {selectedChanges.Count} plano(s) modificado(s) (numeración, nombre, categoría y propiedades)\n" +
                $"• {selectedNewSheets.Count} nuevo(s) plano(s) por crear\n\n" +
                $"Esta operación se ejecutará en una transacción de Revit.",
                "Confirmar Aplicación de Cola de Sheets",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                BtnApplyChanges.IsEnabled = false;
                TxtStatusLog.Text = "⚙ Aplicando cambios en el modelo de Revit...";

                var applyResult = SheetsQueueService.ApplyQueueToRevitDocument(_doc, selectedChanges, selectedNewSheets);

                if (applyResult.Success)
                {
                    await SheetsQueueService.MarkQueueAsAppliedAsync(_selectedQueue, applyResult);

                    string errInfo = applyResult.Errors.Count > 0
                        ? $" (Advertencias: {applyResult.Errors.Count} — {string.Join("; ", applyResult.Errors.Take(3))})"
                        : "";

                    TxtStatusLog.Text = $"✅ APLICACIÓN COMPLETADA: {applyResult.ModifiedCount} planos actualizados, {applyResult.CreatedCount} nuevos creados, {applyResult.CollectionsAssignedCount} categorías asignadas.{errInfo}";

                    MessageBox.Show(
                        $"¡Cambios aplicados exitosamente en Revit!\n\n" +
                        $"✔ Planos modificados: {applyResult.ModifiedCount}\n" +
                        $"✔ Nuevos planos creados: {applyResult.CreatedCount}\n" +
                        $"✔ Categorías / Colecciones asignadas: {applyResult.CollectionsAssignedCount}\n" +
                        (applyResult.Errors.Count > 0 ? $"\nAdvertencias ({applyResult.Errors.Count}):\n" + string.Join("\n", applyResult.Errors.Take(5)) : ""),
                        "Review BIM — Sheets Actualizados",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    await LoadQueuesFromSupabaseAsync();
                }
                else
                {
                    string errorsJoined = string.Join("\n", applyResult.Errors);
                    TxtStatusLog.Text = $"❌ Error al aplicar cambios: {errorsJoined}";
                    MessageBox.Show($"No se pudieron aplicar los cambios:\n{errorsJoined}", "Error en Revit", MessageBoxButton.OK, MessageBoxImage.Error);
                    BtnApplyChanges.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                TxtStatusLog.Text = $"❌ Excepción aplicando cola: {ex.Message}";
                MessageBox.Show($"Error inesperado: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                BtnApplyChanges.IsEnabled = true;
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
