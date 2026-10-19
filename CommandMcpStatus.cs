using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitToSupabasePlugin
{
    [Transaction(TransactionMode.Manual)]
    public class CommandMcpStatus : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiapp = commandData.Application;
                DockablePane pane = uiapp.GetDockablePane(ReviewControlPanelPage.PaneId);

                if (pane != null)
                {
                    if (!pane.IsShown())
                    {
                        pane.Show();
                    }

                    ReviewControlPanelPage.Instance?.AddLog("📡 Visualizando estado de Servidor Bi-MCP en el Panel de Control.");
                }
                else
                {
                    Autodesk.Revit.UI.TaskDialog.Show("Review MCP", "El Panel de Control no está disponible.");
                    return Result.Failed;
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
