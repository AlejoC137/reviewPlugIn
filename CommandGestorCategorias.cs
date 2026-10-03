using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitToSupabasePlugin
{
    [Transaction(TransactionMode.Manual)]
    public class CommandGestorCategorias : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                UIDocument? uidoc = commandData?.Application?.ActiveUIDocument;
                if (uidoc == null || uidoc.Document == null)
                {
                    Autodesk.Revit.UI.TaskDialog.Show("Aviso", "Por favor abre o activa un modelo de Revit antes de usar el Gestor de Categorías.");
                    return Result.Cancelled;
                }

                var window = new GestorCategoriasWindow(uidoc);
                window.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Error en Gestor de Categorías", 
                    $"No se pudo inicializar la ventana del gestor:\n\n{ex.Message}\n\nDetalles:\n{ex.StackTrace}");
                return Result.Cancelled;
            }
        }
    }
}
