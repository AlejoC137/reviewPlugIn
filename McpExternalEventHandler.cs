using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace RevitToSupabasePlugin
{
    /// <summary>
    /// Despachador de hilo seguro para el servidor MCP de Revit.
    /// Ejecuta las herramientas solicitadas por el Asistente de IA en el hilo principal
    /// de la interfaz de usuario de Revit mediante ExternalEvent.
    /// </summary>
    public class McpExternalEventHandler : IExternalEventHandler
    {
        private TaskCompletionSource<JObject> _tcs = new TaskCompletionSource<JObject>();
        private string _currentTool = string.Empty;
        private JObject _currentArgs = new JObject();

        public Task<JObject> EnqueueTaskAsync(ExternalEvent extEvent, string tool, JObject args)
        {
            _tcs = new TaskCompletionSource<JObject>();
            _currentTool = tool;
            _currentArgs = args;

            extEvent.Raise();
            return _tcs.Task;
        }

        public void Execute(UIApplication app)
        {
            UIDocument? uidoc = app.ActiveUIDocument;
            Document? doc = uidoc?.Document;

            try
            {
                JObject result = new JObject();

                if (doc == null)
                {
                    result["error"] = "No hay ningún documento o proyecto abierto en Revit.";
                    _tcs.TrySetResult(result);
                    return;
                }

                switch (_currentTool)
                {
                    case "revit_get_active_session":
                    {
                        var projInfo = doc.ProjectInformation;
                        result["project_name"] = projInfo?.Name ?? "Sin Nombre";
                        result["project_number"] = projInfo?.Number ?? "S/N";
                        result["project_address"] = projInfo?.Address ?? "";
                        result["active_view"] = doc.ActiveView?.Name ?? "Sin Vista";
                        result["view_type"] = doc.ActiveView?.ViewType.ToString() ?? "";
                        result["document_path"] = doc.PathName ?? "Documento no guardado en disco";
                        result["is_workshared"] = doc.IsWorkshared;
                        result["usuario_actual"] = AuthenticationService.UserEmail.Length > 0 
                            ? AuthenticationService.UserEmail 
                            : app.Application.Username;
                        break;
                    }

                    case "revit_get_selection":
                    {
                        var selIds = uidoc!.Selection.GetElementIds();
                        JArray elements = new JArray();

                        foreach (ElementId id in selIds)
                        {
                            Element elem = doc.GetElement(id);
                            if (elem == null) continue;

                            JObject eObj = new JObject
                            {
                                ["id"] = elem.Id.Value,
                                ["name"] = elem.Name,
                                ["category"] = elem.Category?.Name ?? "Desconocida"
                            };

                            // Tipo y Familia
                            ElementType? elemType = doc.GetElement(elem.GetTypeId()) as ElementType;
                            if (elemType != null)
                            {
                                eObj["family"] = elemType.FamilyName ?? "";
                                eObj["type"] = elemType.Name ?? "";
                            }

                            // Nivel
                            if (elem.LevelId != ElementId.InvalidElementId)
                            {
                                Element? lvl = doc.GetElement(elem.LevelId);
                                if (lvl != null) eObj["level"] = lvl.Name;
                            }

                            // Área calculada o parámetro Medida_Area
                            Parameter? pArea = elem.LookupParameter(MeasurementManager.ParamArea) ?? 
                                             elem.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                            if (pArea != null && pArea.HasValue)
                            {
                                double val = pArea.StorageType == StorageType.Double 
                                    ? pArea.AsDouble() * 0.092903 
                                    : double.TryParse(pArea.AsString() ?? "", out double d) ? d : 0;
                                eObj["area_m2"] = Math.Round(val, 2);
                            }

                            // Volumen calculado
                            Parameter? pVol = elem.LookupParameter(MeasurementManager.ParamVolumen) ?? 
                                            elem.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
                            if (pVol != null && pVol.HasValue)
                            {
                                double vVal = pVol.StorageType == StorageType.Double 
                                    ? pVol.AsDouble() * 0.0283168 
                                    : 0;
                                eObj["volumen_m3"] = Math.Round(vVal, 2);
                            }

                            elements.Add(eObj);
                        }

                        result["selected_elements"] = elements;
                        result["count"] = elements.Count;
                        break;
                    }

                    case "revit_convertir_pisos":
                    {
                        int floorIdInt = _currentArgs["floor_id"]?.Value<int>() ?? 0;
                        string materialName = _currentArgs["material_baldosa"]?.ToString() ?? "Porcelanato 60x60";
                        double lenCm = _currentArgs["longitud_cm"]?.Value<double>() ?? 60.0;
                        double widthCm = _currentArgs["ancho_cm"]?.Value<double>() ?? 60.0;

                        if (floorIdInt == 0)
                        {
                            result["status"] = "error";
                            result["message"] = "Debe proporcionar un floor_id válido.";
                            break;
                        }

                        Floor? targetFloor = doc.GetElement(new ElementId(floorIdInt)) as Floor;
                        if (targetFloor == null)
                        {
                            result["status"] = "error";
                            result["message"] = $"No se encontró ningún objeto Floor con ElementId {floorIdInt}.";
                            break;
                        }

                        double areaM2 = 0;
                        using (Transaction t = new Transaction(doc, "Despiece Paramétrico desde Asistente"))
                        {
                            t.Start();

                            MeasurementManager.EnsureMeasurementParameters(doc);

                            Parameter? pArea = targetFloor.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                            if (pArea != null && pArea.HasValue)
                            {
                                areaM2 = Math.Round(pArea.AsDouble() * 0.092903, 2);
                            }

                            // Aplicar mediciones normalizadas al piso
                            MeasurementManager.MeasureAndApply(
                                targetFloor,
                                customAreaSqFt: areaM2 / 0.092903
                            );

                            // Marcar en comentarios para rastreo del sistema de cómputos
                            Parameter? pComm = targetFloor.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                            if (pComm != null && !pComm.IsReadOnly)
                            {
                                pComm.Set($"ENCHAPE-BALDOSA | {materialName} ({lenCm}x{widthCm}cm)");
                            }

                            t.Commit();
                        }

                        double tileAreaM2 = (lenCm / 100.0) * (widthCm / 100.0);
                        int estimatedTiles = tileAreaM2 > 0 ? (int)Math.Ceiling(areaM2 / tileAreaM2 * 1.05) : 0;

                        result["status"] = "success";
                        result["floor_id"] = floorIdInt;
                        result["material"] = materialName;
                        result["area_m2"] = areaM2;
                        result["piezas_estimadas_con_desperdicio"] = estimatedTiles;
                        result["message"] = $"Piso {floorIdInt} parametrizado y preparado para cómputo de {materialName} ({areaM2} m²).";
                        break;
                    }

                    case "revit_sync_to_supabase":
                    {
                        var package = MetadataExporterService.CollectMetadata(
                            doc,
                            includeElementDetails: true,
                            includeProjectInfo: true,
                            includeSiteLocation: true,
                            includeLevels: true,
                            includeCategorySummary: true,
                            includeRooms: true,
                            includeSchedules: true,
                            includeSheetsAndViews: false,
                            includeMaterials: true,
                            includeWeb3dModel: false,
                            progressCallback: null,
                            isInterruptedCheck: null
                        );

                        string responseMsg = "";
                        bool ok = MetadataExporterService.SendToSupabaseDirect(package, out responseMsg);

                        result["status"] = ok ? "success" : "error";
                        result["message"] = responseMsg;
                        result["elementos_sincronizados"] = package.ElementsById?.Count ?? 0;
                        result["schedules_sincronizados"] = package.Schedules?.Count ?? 0;
                        break;
                    }

                    default:
                        result["error"] = $"Herramienta '{_currentTool}' no soportada por el plugin.";
                        break;
                }

                _tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                JObject errObj = new JObject
                {
                    ["status"] = "error",
                    ["message"] = ex.Message,
                    ["stackTrace"] = ex.StackTrace
                };
                _tcs.TrySetResult(errObj);
            }
        }

        public string GetName() => "Review_McpExternalEventHandler";
    }
}
