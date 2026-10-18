using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitTaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace RevitToSupabasePlugin
{
    [Transaction(TransactionMode.Manual)]
    public class CommandToggleControlPanel : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiapp = commandData.Application;
                DockablePane pane = uiapp.GetDockablePane(ReviewControlPanelPage.PaneId);

                if (pane != null)
                {
                    if (pane.IsShown())
                    {
                        pane.Hide();
                    }
                    else
                    {
                        pane.Show();
                        ReviewControlPanelPage.Instance?.TriggerMetricsRefresh();
                    }
                }
                else
                {
                    RevitTaskDialog.Show("Review Control Panel", "No se encontró el Panel Acoplable registrado. Asegúrate de iniciar Revit con el Add-in cargado.");
                    return Result.Failed;
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                RevitTaskDialog.Show("Error Control Panel", $"Error al alternar el panel: {ex.Message}");
                return Result.Failed;
            }
        }
    }
}
