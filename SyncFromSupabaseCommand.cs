using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitToSupabasePlugin
{
    [Transaction(TransactionMode.Manual)]
    public class CommandSyncMaterials : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null) return Result.Failed;

            Document doc = uidoc.Document;

            try
            {
                // Run sync synchronously for simplicity in the external command
                Task.Run(async () => await SyncMaterialsAsync(doc, uiapp)).Wait();
                return Result.Succeeded;
            }
            catch (AggregateException ae)
            {
                message = ae.Flatten().InnerExceptions.FirstOrDefault()?.Message ?? ae.Message;
                Autodesk.Revit.UI.TaskDialog.Show("Error de Sincronización", message);
                return Result.Failed;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                Autodesk.Revit.UI.TaskDialog.Show("Error de Sincronización", ex.ToString());
                return Result.Failed;
            }
        }

        private async Task SyncMaterialsAsync(Document doc, UIApplication uiapp)
        {
            string projectName = doc.ProjectInformation?.Name ?? "";
            if (string.IsNullOrEmpty(projectName))
            {
                throw new Exception("El proyecto de Revit no tiene un nombre configurado en la Información del Proyecto.");
            }

            SupabaseService supabase = new SupabaseService();
            string jsonResponse = await supabase.GetProjectDataAsync(projectName);

            JArray records = JArray.Parse(jsonResponse);
            if (records.Count == 0)
            {
                throw new Exception($"No se encontraron datos en Supabase para el proyecto: {projectName}");
            }

            JObject latestRecord = (JObject)records[0];
            JToken materialsToken = latestRecord["materials"];

            if (materialsToken == null || materialsToken.Type != JTokenType.Array)
            {
                throw new Exception("El registro en Supabase no contiene un arreglo de materiales válido.");
            }

            List<MaterialData> materialsFromWeb = materialsToken.ToObject<List<MaterialData>>();
            if (materialsFromWeb == null || materialsFromWeb.Count == 0)
            {
                throw new Exception("La lista de materiales está vacía en Supabase.");
            }

            SyncMaterialsEventHandler handler = new SyncMaterialsEventHandler(materialsFromWeb);
            ExternalEvent extEvent = ExternalEvent.Create(handler);
            extEvent.Raise();
        }
    }

    public class SyncMaterialsEventHandler : IExternalEventHandler
    {
        private List<MaterialData> _materialsFromWeb;

        public SyncMaterialsEventHandler(List<MaterialData> materialsFromWeb)
        {
            _materialsFromWeb = materialsFromWeb;
        }

        public void Execute(UIApplication uiapp)
        {
            UIDocument uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null) return;
            Document doc = uidoc.Document;

            int updatedCount = 0;

            using (Transaction t = new Transaction(doc, "Sincronizar Materiales desde Review"))
            {
                t.Start();

                foreach (var webMat in _materialsFromWeb)
                {
                    ElementId matId = new ElementId(webMat.Id);
                    Material revitMat = doc.GetElement(matId) as Material;

                    if (revitMat != null)
                    {
                        if (revitMat.Name != webMat.Name)
                        {
                            try
                            {
                                revitMat.Name = webMat.Name;
                                updatedCount++;
                            }
                            catch (Exception)
                            {
                                // Podría fallar si el nombre ya está en uso
                            }
                        }
                    }
                }

                t.Commit();
            }

            Autodesk.Revit.UI.TaskDialog.Show("Sincronización Completada", $"Se han actualizado {updatedCount} materiales exitosamente desde la nube.");
        }

        public string GetName()
        {
            return "SyncMaterialsEventHandler";
        }
    }
}
