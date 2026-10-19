using System;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace RevitToSupabasePlugin
{
    public partial class AsistenteAnotacionWindow : Window
    {
        private readonly UIDocument _uidoc;
        private readonly Document _doc;

        public AsistenteAnotacionWindow(UIDocument uidoc)
        {
            InitializeComponent();
            _uidoc = uidoc ?? throw new ArgumentNullException(nameof(uidoc));
            _doc = uidoc.Document;

            LoadSummaryMetrics();
        }

        private void LoadSummaryMetrics()
        {
            try
            {
                txtProjectName.Text = $"Proyecto: {_doc.ProjectInformation?.Name ?? _doc.Title}";
                txtDocTitle.Text = $"Archivo: {_doc.Title}";

                int sheetCount = new FilteredElementCollector(_doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Count(s => !s.IsTemplate);
                txtKpiSheets.Text = sheetCount.ToString();

                int familyCount = new FilteredElementCollector(_doc)
                    .OfClass(typeof(Family))
                    .GetElementCount();
                txtKpiFamilies.Text = familyCount.ToString();

                int draftingCount = new FilteredElementCollector(_doc)
                    .OfClass(typeof(ViewDrafting))
                    .GetElementCount();
                txtKpiDrafting.Text = draftingCount.ToString();
            }
            catch
            {
                // Ignorar errores de métricas secundarias
            }
        }

        private void BtnOpenAmbientador_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new PlanAmbientadorWindow(_uidoc)
                {
                    Owner = this
                };
                win.ShowDialog();
                LoadSummaryMetrics();
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", "No se pudo abrir Plan Ambientador: " + ex.Message);
            }
        }

        private void BtnOpenDetalles_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new GestorDetallesWindow(_uidoc)
                {
                    Owner = this
                };
                win.ShowDialog();
                LoadSummaryMetrics();
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", "No se pudo abrir Gestor de Detalles: " + ex.Message);
            }
        }

        private void BtnOpenFamilyRevisor_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new FamilyRevisorWindow(_uidoc)
                {
                    Owner = this
                };
                win.ShowDialog();
                LoadSummaryMetrics();
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", "No se pudo abrir Family Revisor: " + ex.Message);
            }
        }

        private void BtnOpenSheetRevisor_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new SheetRevisorWindow(_uidoc)
                {
                    Owner = this
                };
                win.ShowDialog();
                LoadSummaryMetrics();
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", "No se pudo abrir ProSheets (Renombrador de Sheets): " + ex.Message);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
