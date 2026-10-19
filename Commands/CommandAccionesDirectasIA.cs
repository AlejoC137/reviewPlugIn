using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using reviewPlugIn.Views;
using RevitTaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace RevitToSupabasePlugin
{
    [Transaction(TransactionMode.Manual)]
    public class CommandAccionesDirectasIA : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiapp = commandData.Application;
                UIDocument uidoc = uiapp.ActiveUIDocument;

                if (uidoc == null || uidoc.Document == null)
                {
                    RevitTaskDialog.Show("Review IA", "Por favor abre un proyecto o modelo en Revit antes de ejecutar Acciones Directas de IA.");
                    return Result.Cancelled;
                }

                var window = new AccionesDirectasIAWindow(uidoc);
                var helper = new System.Windows.Interop.WindowInteropHelper(window)
                {
                    Owner = uiapp.MainWindowHandle
                };
                window.ShowDialog();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                RevitTaskDialog.Show("Error Review IA", "Ocurrió un error al abrir la ventana de Acciones Directas de IA:\n" + ex.Message);
                return Result.Failed;
            }
        }
    }
}
