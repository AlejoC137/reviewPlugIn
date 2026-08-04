using System;
using System.IO;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;

namespace RevitToSupabasePlugin
{
    public partial class ExportadorMetadataWindow : System.Windows.Window
    {
        private Document _doc;
        private ExternalEvent _extEvent;
        private ExportMetadataExternalEventHandler _handler;

        public ExportadorMetadataWindow(Document doc, ExternalEvent extEvent, ExportMetadataExternalEventHandler handler)
        {
            InitializeComponent();
            _doc = doc;
            _extEvent = extEvent;
            _handler = handler;
            _handler.SetWindowReference(this);
        }

        private void BtnPreview_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ProjectMetadataPackage package = MetadataExporterService.CollectMetadata(
                    _doc, 
                    chkElementDetails.IsChecked == true,
                    chkProjectInfo.IsChecked == true,
                    chkSiteLocation.IsChecked == true,
                    chkLevels.IsChecked == true,
                    chkCategorySummary.IsChecked == true,
                    chkRooms.IsChecked == true,
                    chkSchedules.IsChecked == true,
                    chkSheetsViews.IsChecked == true,
                    chkMaterials.IsChecked == true,
                    chkWeb3dModel.IsChecked == true,
                    null, 
                    null
                );

                package.UserEmail = txtEmail.Text.Trim();

                string jsonPreview = JsonConvert.SerializeObject(package, Formatting.Indented);
                txtPreview.Text = jsonPreview;
                txtStatus.Text = "Vista previa generada correctamente.";
            }
            catch (Exception ex)
            {
                txtPreview.Text = "Error al generar vista previa: " + ex.Message;
            }
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            string email = txtEmail.Text.Trim();
            if (string.IsNullOrEmpty(email))
            {
                System.Windows.MessageBox.Show("Por favor ingresa tu correo electrónico.", "Campo Requerido", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            // Configurar manejador Nonica
            _handler.UserEmail = email;
            _handler.UserPassword = txtPassword.Password;
            _handler.IncludeProjectInfo = chkProjectInfo.IsChecked == true;
            _handler.IncludeSiteLocation = chkSiteLocation.IsChecked == true;
            _handler.IncludeLevels = chkLevels.IsChecked == true;
            _handler.IncludeCategorySummary = chkCategorySummary.IsChecked == true;
            _handler.IncludeElementDetails = chkElementDetails.IsChecked == true;
            _handler.IncludeRooms = chkRooms.IsChecked == true;
            _handler.IncludeSchedules = chkSchedules.IsChecked == true;
            _handler.IncludeSheetsAndViews = chkSheetsViews.IsChecked == true;
            _handler.IncludeMaterials = chkMaterials.IsChecked == true;
            _handler.IncludeWeb3dModel = chkWeb3dModel.IsChecked == true;

            // Ocultar ventana principal
            this.Hide();

            // Disparar ExternalEvent
            _extEvent.Raise();
        }

        private void BtnSaveJson_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Si la vista previa está vacía, la generamos
                if (string.IsNullOrEmpty(txtPreview.Text))
                {
                    BtnPreview_Click(sender, e);
                }

                string jsonText = txtPreview.Text;
                if (string.IsNullOrEmpty(jsonText) || jsonText.StartsWith("Error"))
                {
                    System.Windows.MessageBox.Show("No hay datos JSON válidos para exportar.", "Exportar JSON", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                // Cuadro de diálogo para guardar el archivo
                Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog();
                saveFileDialog.Filter = "Archivos JSON (*.json)|*.json|Todos los archivos (*.*)|*.*";
                saveFileDialog.DefaultExt = "json";
                saveFileDialog.FileName = "RevitMetadataExport.json";

                if (saveFileDialog.ShowDialog() == true)
                {
                    File.WriteAllText(saveFileDialog.FileName, jsonText, System.Text.Encoding.UTF8);
                    txtStatus.Text = $"Archivo JSON exportado correctamente en: {Path.GetFileName(saveFileDialog.FileName)}";
                    System.Windows.MessageBox.Show("¡El archivo JSON se ha exportado exitosamente!", "Exportación Completada", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Error al exportar JSON: " + ex.Message, "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void BtnExport3D_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ProjectMetadataPackage package = MetadataExporterService.CollectMetadata(
                    _doc,
                    false, // elementDetails
                    false, // projectInfo
                    false, // siteLocation
                    false, // levels
                    false, // categorySummary
                    false, // rooms
                    false, // schedules
                    false, // sheetsViews
                    false, // materials
                    true,  // includeWeb3dModel
                    null,
                    null
                );

                if (package.Interactive3DScene == null || package.Interactive3DScene.Nodes.Count == 0)
                {
                    System.Windows.MessageBox.Show("No se encontraron elementos 3D válidos para la escena web.", "Exportación 3D Web", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                string json3D = JsonConvert.SerializeObject(package.Interactive3DScene, Formatting.Indented);

                Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog();
                saveFileDialog.Filter = "Modelo 3D Web JSON (*.json)|*.json|Todos los archivos (*.*)|*.*";
                saveFileDialog.DefaultExt = "json";
                saveFileDialog.FileName = "Web3D_Model_Stylized.json";

                if (saveFileDialog.ShowDialog() == true)
                {
                    File.WriteAllText(saveFileDialog.FileName, json3D, System.Text.Encoding.UTF8);
                    txtStatus.Text = $"Modelo 3D Web exportado ({package.Interactive3DScene.Nodes.Count} nodos 3D) en: {Path.GetFileName(saveFileDialog.FileName)}";
                    System.Windows.MessageBox.Show($"¡El Modelo 3D Web Estilizado fue exportado con éxito!\nNodos procesados: {package.Interactive3DScene.Nodes.Count}\nListo para renderizar en React Three Fiber.", "Exportación 3D Completada", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Error al exportar Modelo 3D Web: " + ex.Message, "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        public void UpdatePreviewText(string json)
        {
            txtPreview.Text = json;
        }

        public void UpdateStatus(string message)
        {
            txtStatus.Text = message;
        }
    }
}
