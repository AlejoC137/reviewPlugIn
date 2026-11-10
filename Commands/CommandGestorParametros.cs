using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitToSupabasePlugin.Views;

namespace RevitToSupabasePlugin
{
    [Transaction(TransactionMode.Manual)]
    public class CommandGestorParametros : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiApp = commandData.Application;
                if (uiApp.ActiveUIDocument == null || uiApp.ActiveUIDocument.Document == null)
                {
                    Autodesk.Revit.UI.TaskDialog.Show("ARCA Review", "Abre un proyecto (.rvt) o familia (.rfa) para iniciar el Gestor Integral de Parámetros.");
                    return Result.Cancelled;
                }

                var window = new GestorParametrosWindow(uiApp);
                window.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                Autodesk.Revit.UI.TaskDialog.Show("Error - Gestor de Parámetros", $"Ocurrió un error al abrir el Gestor Integral de Parámetros:\n\n{ex.Message}");
                return Result.Failed;
            }
        }
    }
}
