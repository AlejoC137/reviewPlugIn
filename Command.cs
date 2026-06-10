using System;
using System.Net.Http;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;

namespace RevitToSupabasePlugin
{
    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // URL de tu Local Server o MCP que sirve de puente con Supabase
                string localServerUrl = "http://localhost:3000/api/mcp-data";

                // Llamada HTTP síncrona dentro del comando de Revit
                string jsonResponse = GetDataFromLocalServer(localServerUrl);

                if (string.IsNullOrEmpty(jsonResponse))
                {
                    TaskDialog.Show("Error de Conexión", "No se pudo obtener respuesta del Local Server. Verifica que tu app Node/React esté corriendo.");
                    return Result.Failed;
                }

                // Deserializar la respuesta (Ajusta la clase SupabaseDataModel según las columnas de tu BD)
                List<SupabaseDataModel> dataList = JsonConvert.DeserializeObject<List<SupabaseDataModel>>(jsonResponse);

                if (dataList != null && dataList.Count > 0)
                {
                    string resultMessage = "Datos obtenidos de Supabase vía Local Server:\n\n";
                    foreach (var item in dataList)
                    {
                        resultMessage += $"ID: {item.Id} - Nombre: {item.Name} - Estado: {item.Status}\n";
                    }

                    TaskDialog.Show("Sincronización Exitosa", resultMessage);
                }
                else
                {
                    TaskDialog.Show("Aviso", "La consulta al Local Server no devolvió datos.");
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Excepción Crítica", ex.ToString());
                return Result.Failed;
            }
        }

        private string GetDataFromLocalServer(string url)
        {
            using (HttpClient client = new HttpClient())
            {
                try
                {
                    // Timeout de 10 segundos para evitar que Revit se congele indefinidamente si el server cae
                    client.Timeout = TimeSpan.FromSeconds(10);

                    // Se utiliza GetAwaiter().GetResult() para mantener la sincronía requerida por la API de Revit
                    HttpResponseMessage response = client.GetAsync(url).GetAwaiter().GetResult();

                    if (response.IsSuccessStatusCode)
                    {
                        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    }
                    else
                    {
                        return null;
                    }
                }
                catch
                {
                    return null;
                }
            }
        }
    }

    // Modelo de datos de ejemplo para mapear el JSON que viene de tu Local Server / Supabase
    public class SupabaseDataModel
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }
    }
}
