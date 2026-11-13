using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitToSupabasePlugin.Views;

namespace RevitToSupabasePlugin
{
    [Transaction(TransactionMode.Manual)]
    public class CommandEstudioFamiliasParametros : IExternalCommand
    {
        private static EstudioFamiliasParametrosWindow? _activeWindow;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiApp = commandData.Application;
                if (uiApp.ActiveUIDocument == null || uiApp.ActiveUIDocument.Document == null)
                {
                    Autodesk.Revit.UI.TaskDialog.Show("ARCA Review", "Abre un proyecto (.rvt) o familia (.rfa) para usar Familias & Parámetros.");
                    return Result.Cancelled;
                }

                if (_activeWindow != null && _activeWindow.IsLoaded)
                {
                    if (_activeWindow.WindowState == System.Windows.WindowState.Minimized)
                        _activeWindow.WindowState = System.Windows.WindowState.Normal;
                    _activeWindow.Activate();
                    return Result.Succeeded;
                }

                _activeWindow = new EstudioFamiliasParametrosWindow(uiApp);
                new WindowInteropHelper(_activeWindow).Owner = uiApp.MainWindowHandle;
                _activeWindow.Closed += (s, e) => _activeWindow = null;
                _activeWindow.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                Autodesk.Revit.UI.TaskDialog.Show("Error - Familias & Parámetros", $"No se pudo abrir Familias & Parámetros:\n\n{ex.Message}");
                return Result.Failed;
            }
        }
    }
}
