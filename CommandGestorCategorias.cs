using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitToSupabasePlugin
{
    [Transaction(TransactionMode.Manual)]
    public class CommandGestorCategorias : IExternalCommand
    {
        private static GestorCategoriasWindow? _activeWindow;

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

                if (_activeWindow != null && _activeWindow.IsLoaded)
                {
                    _activeWindow.Activate();
                    if (_activeWindow.WindowState == System.Windows.WindowState.Minimized)
                    {
                        _activeWindow.WindowState = System.Windows.WindowState.Normal;
                    }
                    return Result.Succeeded;
                }

                var handler = new GestorCategoriasExternalEventHandler();
                var extEvent = ExternalEvent.Create(handler);
                handler.SetExternalEvent(extEvent);

                var window = new GestorCategoriasWindow(uidoc, extEvent, handler);
                _activeWindow = window;
                window.Closed += (s, e) => _activeWindow = null;

                try
                {
                    var revitHandle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                    if (revitHandle != IntPtr.Zero)
                    {
                        new System.Windows.Interop.WindowInteropHelper(window).Owner = revitHandle;
                    }
                }
                catch { }

                window.Show();
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
