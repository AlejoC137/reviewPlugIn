using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace RevitToSupabasePlugin
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CommandGestorDetalles : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;

            if (uidoc == null || uidoc.Document == null)
            {
                TaskDialog.Show("Error", "No hay ningún documento activo en Revit.");
                return Result.Failed;
            }

            try
            {
                GestorDetallesWindow window = new GestorDetallesWindow(uidoc);
                WindowInteropHelper helper = new WindowInteropHelper(window);
                helper.Owner = uiapp.MainWindowHandle;
                window.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Error", "No se pudo abrir el Gestor de Detalles: " + ex.Message);
                return Result.Failed;
            }
        }
    }
}
