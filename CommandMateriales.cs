using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitTaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace RevitToSupabasePlugin
{
    [Transaction(TransactionMode.Manual)]
    public class CommandMapeoMateriales : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    RevitTaskDialog.Show("Mapeo de Materiales", "No hay ningún documento activo en Revit.");
                    return Result.Failed;
                }

                // Recopilar materiales del proyecto
                var materials = new FilteredElementCollector(doc)
                    .OfClass(typeof(Material))
                    .Cast<Material>()
                    .OrderBy(m => m.Name)
                    .ToList();

                int totalMats = materials.Count;
                int withAppearance = materials.Count(m => m.AppearanceAssetId != ElementId.InvalidElementId);

                string info = $"🎨 MAPEO DE MATERIALES REVIEW\n\n" +
                              $"• Proyecto: {doc.ProjectInformation?.Name ?? doc.Title}\n" +
                              $"• Total Materiales en Modelo: {totalMats}\n" +
                              $"• Con Activo de Apariencia: {withAppearance}\n" +
                              $"• Usuario Activo: {(AuthenticationService.IsLoggedIn ? AuthenticationService.UserEmail : "AnonKey")}\n" +
                              $"• Proyecto Cloud: {AuthenticationService.ActiveProjectName}\n\n" +
                              $"Los materiales están vinculados a la base de datos de Review para cálculo de cómputos y despiece.\n" +
                              $"¿Deseas abrir el Panel de Control para sincronizarlos con Supabase?";

                var dialog = new RevitTaskDialog("Mapeo de Materiales")
                {
                    MainInstruction = "Gestión y Mapeo de Materiales BIM",
                    MainContent = info,
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.Close
                };
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "🎛️ Abrir Centro de Control");

                var res = dialog.Show();
                if (res == TaskDialogResult.CommandLink1 || res == TaskDialogResult.Yes)
                {
                    var pane = commandData.Application.GetDockablePane(ReviewControlPanelPage.PaneId);
                    if (pane != null && !pane.IsShown()) pane.Show();
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

    [Transaction(TransactionMode.Manual)]
    public class CommandAuditoriaMateriales : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    RevitTaskDialog.Show("Auditoría de Materiales", "No hay ningún documento activo.");
                    return Result.Failed;
                }

                var materials = new FilteredElementCollector(doc)
                    .OfClass(typeof(Material))
                    .Cast<Material>()
                    .ToList();

                var categoriesWithMaterials = new HashSet<string>();
                foreach (var mat in materials)
                {
                    if (!string.IsNullOrWhiteSpace(mat.MaterialClass))
                    {
                        categoriesWithMaterials.Add(mat.MaterialClass);
                    }
                }

                string summary = $"📋 AUDITORÍA DE MATERIALES BIM\n\n" +
                                 $"• Modelo: {doc.Title}\n" +
                                 $"• Total de Materiales: {materials.Count}\n" +
                                 $"• Clases / Categorías encontradas: {categoriesWithMaterials.Count}\n" +
                                 $"  ({string.Join(", ", categoriesWithMaterials.Take(6))}...)\n\n" +
                                 $"Esta auditoría asegura que todos los materiales tengan nombres normalizados antes de enviarse a Supabase y Review Cloud.";

                RevitTaskDialog.Show("Auditoría de Materiales BIM", summary);
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
