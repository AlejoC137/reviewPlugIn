using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitToSupabasePlugin
{
    public enum ControlPanelAction
    {
        None,
        RefreshMetrics,
        SyncSupabase,
        SyncMaterials,
        OpenSheetsQueue,
        OpenLadrillos,
        OpenPisos,
        OpenSteelDeck,
        OpenSchedulesIA,
        OpenPlantillas,
        OpenAsistenteAnotacion,
        OpenFamilyRevisor,
        OpenSheetRevisor,
        PreviewMetadataJson,
        ExportIfcJson,
        Export3dModel,
        ExportFullMetadataJson
    }

    public class BimModelMetrics
    {
        public string DocumentTitle { get; set; } = "Sin Documento";
        public string ProjectName { get; set; } = "Sin Proyecto";
        public string ProjectNumber { get; set; } = "0000";
        public int WallsCount { get; set; }
        public int FloorsCount { get; set; }
        public int StructuralCount { get; set; }
        public int PlumbingCount { get; set; }
        public int RoomsCount { get; set; }
        public int SchedulesCount { get; set; }
        public int SheetsCount { get; set; }
        public int LevelsCount { get; set; }
        public double TotalWallAreaM2 { get; set; }
        public double TotalFloorAreaM2 { get; set; }
        public DateTime LastRefreshed { get; set; } = DateTime.Now;
    }

    public class ReviewControlPanelEventHandler : IExternalEventHandler
    {
        public ControlPanelAction CurrentAction { get; set; } = ControlPanelAction.None;
        
        // Parámetros para exportación local de metadata y archivos
        public string ExportTargetFilePath { get; set; } = string.Empty;
        public bool ExportIncludeProjectInfo { get; set; } = true;
        public bool ExportIncludeSiteLocation { get; set; } = true;
        public bool ExportIncludeLevels { get; set; } = true;
        public bool ExportIncludeCategorySummary { get; set; } = true;
        public bool ExportIncludeElementDetails { get; set; } = true;
        public bool ExportIncludeRooms { get; set; } = true;
        public bool ExportIncludeSchedules { get; set; } = true;
        public bool ExportIncludeSheetsAndViews { get; set; } = true;
        public bool ExportIncludeMaterials { get; set; } = true;
        public bool ExportIncludeWeb3dModel { get; set; } = true;

        // Callbacks hacia la interfaz de usuario
        public Action<BimModelMetrics>? OnMetricsRefreshed { get; set; }
        public Action<bool, string>? OnSyncCompleted { get; set; }
        public Action<string>? OnLogMessage { get; set; }
        public Action<string>? OnPreviewGenerated { get; set; }
        public Action<bool, string>? OnExportCompleted { get; set; }

        public void Execute(UIApplication uiapp)
        {
            var uidoc = uiapp.ActiveUIDocument;
            var doc = uidoc?.Document;

            if (doc == null && CurrentAction != ControlPanelAction.None)
            {
                OnLogMessage?.Invoke("⚠️ No hay ningún documento activo abierto en Revit.");
                CurrentAction = ControlPanelAction.None;
                return;
            }

            try
            {
                switch (CurrentAction)
                {
                    case ControlPanelAction.RefreshMetrics:
                        if (doc != null) ExecuteRefreshMetrics(doc);
                        break;

                    case ControlPanelAction.SyncSupabase:
                        if (doc != null) ExecuteSyncSupabase(doc);
                        break;

                    case ControlPanelAction.OpenSheetsQueue:
                        if (doc != null)
                        {
                            var wSheetsQueue = new reviewPlugIn.Views.SyncSheetsQueueWindow(doc);
                            wSheetsQueue.ShowDialog();
                            ExecuteRefreshMetrics(doc);
                        }
                        break;

                    case ControlPanelAction.OpenLadrillos:
                        if (uidoc != null)
                        {
                            var wLadrillos = new ManagerFamiliasLadrilloWindow(uidoc);
                            wLadrillos.ShowDialog();
                        }
                        break;

                    case ControlPanelAction.OpenPisos:
                        if (uidoc != null)
                        {
                            var wPisos = new ManagerFamiliasPisoWindow(uidoc);
                            wPisos.ShowDialog();
                        }
                        break;

                    case ControlPanelAction.OpenSteelDeck:
                        if (uidoc != null)
                        {
                            var wSteel = new ManagerFamiliasSteelDeckWindow(uidoc);
                            wSteel.ShowDialog();
                        }
                        break;

                    case ControlPanelAction.SyncMaterials:
                        if (doc != null)
                        {
                            OnLogMessage?.Invoke("🎨 Sincronizando materiales desde Supabase...");
                            try
                            {
                                System.Threading.Tasks.Task.Run(async () =>
                                {
                                    string projectName = doc.ProjectInformation?.Name ?? doc.Title;
                                    var supabase = new SupabaseService();
                                    string jsonResponse = await supabase.GetProjectDataAsync(projectName);
                                    var records = Newtonsoft.Json.Linq.JArray.Parse(jsonResponse);
                                    if (records.Count > 0)
                                    {
                                        var latest = (Newtonsoft.Json.Linq.JObject)records[0];
                                        var matsToken = latest["materials"];
                                        if (matsToken != null && matsToken.Type == Newtonsoft.Json.Linq.JTokenType.Array)
                                        {
                                            var materialsList = matsToken.ToObject<System.Collections.Generic.List<MaterialData>>();
                                            if (materialsList != null && materialsList.Count > 0)
                                            {
                                                var handler = new SyncMaterialsEventHandler(materialsList);
                                                var extEvent = ExternalEvent.Create(handler);
                                                extEvent.Raise();
                                                OnLogMessage?.Invoke($"✅ {materialsList.Count} materiales enviados para actualización.");
                                                return;
                                            }
                                        }
                                    }
                                    OnLogMessage?.Invoke("ℹ️ No se encontraron nuevos materiales registrados en Supabase.");
                                });
                            }
                            catch (Exception ex)
                            {
                                OnLogMessage?.Invoke($"❌ Error en sincronización de materiales: {ex.Message}");
                            }
                        }
                        break;

                    case ControlPanelAction.OpenPlantillas:
                        if (doc != null)
                        {
                            var wPlantilla = new ConfiguradorIAPlantillaWindow(doc);
                            wPlantilla.ShowDialog();
                        }
                        break;

                    case ControlPanelAction.OpenAsistenteAnotacion:
                        if (uidoc != null)
                        {
                            var wAnotacion = new AsistenteAnotacionWindow(uidoc);
                            wAnotacion.ShowDialog();
                        }
                        break;

                    case ControlPanelAction.OpenFamilyRevisor:
                        if (uidoc != null)
                        {
                            var wFam = new FamilyRevisorWindow(uidoc);
                            wFam.ShowDialog();
                        }
                        break;

                    case ControlPanelAction.OpenSheetRevisor:
                        if (uidoc != null)
                        {
                            var wSheets = new SheetRevisorWindow(uidoc);
                            wSheets.ShowDialog();
                        }
                        break;

                    case ControlPanelAction.PreviewMetadataJson:
                        if (doc != null) ExecutePreviewMetadata(doc);
                        break;

                    case ControlPanelAction.ExportIfcJson:
                        if (doc != null) ExecuteExportIfcJson(doc);
                        break;

                    case ControlPanelAction.Export3dModel:
                        if (doc != null) ExecuteExport3dModel(doc);
                        break;

                    case ControlPanelAction.ExportFullMetadataJson:
                        if (doc != null) ExecuteExportFullMetadataJson(doc);
                        break;

                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                OnLogMessage?.Invoke($"❌ Error en acción {CurrentAction}: {ex.Message}");
            }
            finally
            {
                CurrentAction = ControlPanelAction.None;
            }
        }

        private void ExecutePreviewMetadata(Document doc)
        {
            OnLogMessage?.Invoke("🔍 Generando vista previa de metadata del proyecto...");
            var package = MetadataExporterService.CollectMetadata(
                doc,
                ExportIncludeElementDetails,
                ExportIncludeProjectInfo,
                ExportIncludeSiteLocation,
                ExportIncludeLevels,
                ExportIncludeCategorySummary,
                ExportIncludeRooms,
                ExportIncludeSchedules,
                ExportIncludeSheetsAndViews,
                ExportIncludeMaterials,
                ExportIncludeWeb3dModel,
                null,
                null
            );

            package.UserEmail = AuthenticationService.UserEmail;
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(package, Newtonsoft.Json.Formatting.Indented);
            OnLogMessage?.Invoke($"✅ Vista previa generada ({json.Length:N0} caracteres).");
            OnPreviewGenerated?.Invoke(json);
        }

        private void ExecuteExportIfcJson(Document doc)
        {
            if (string.IsNullOrWhiteSpace(ExportTargetFilePath))
            {
                OnLogMessage?.Invoke("⚠️ Ruta destino no especificada para ifcJSON.");
                OnExportCompleted?.Invoke(false, "Ruta no especificada");
                return;
            }

            OnLogMessage?.Invoke("🏗️ Exportando modelo en formato ifcJSON Standard...");
            var ifcDoc = IfcJsonExporterService.ExportDocumentToIfcJson(doc, AuthenticationService.UserEmail);
            string ifcJsonText = IfcJsonExporterService.ToJsonString(ifcDoc);

            System.IO.File.WriteAllText(ExportTargetFilePath, ifcJsonText, System.Text.Encoding.UTF8);
            string fileName = System.IO.Path.GetFileName(ExportTargetFilePath);
            OnLogMessage?.Invoke($"✅ Archivo ifcJSON guardado: {fileName} ({ifcDoc.Data.Count} objetos BIM)");
            OnExportCompleted?.Invoke(true, $"¡Modelo ifcJSON exportado exitosamente!\nArchivo: {fileName}\nObjetos BIM: {ifcDoc.Data.Count}");
        }

        private void ExecuteExport3dModel(Document doc)
        {
            if (string.IsNullOrWhiteSpace(ExportTargetFilePath))
            {
                OnLogMessage?.Invoke("⚠️ Ruta destino no especificada para Modelo 3D.");
                OnExportCompleted?.Invoke(false, "Ruta no especificada");
                return;
            }

            OnLogMessage?.Invoke("🎨 Generando escena 3D Web (Three.js / React Three Fiber)...");
            var package = MetadataExporterService.CollectMetadata(
                doc,
                false, false, false, false, false, false, false, false, false,
                true, // includeWeb3dModel
                null, null
            );

            if (package.Interactive3DScene == null || package.Interactive3DScene.Nodes.Count == 0)
            {
                OnLogMessage?.Invoke("⚠️ No se encontraron elementos 3D exportables en el modelo.");
                OnExportCompleted?.Invoke(false, "No se encontraron elementos 3D válidos.");
                return;
            }

            string json3D = Newtonsoft.Json.JsonConvert.SerializeObject(package.Interactive3DScene, Newtonsoft.Json.Formatting.Indented);
            System.IO.File.WriteAllText(ExportTargetFilePath, json3D, System.Text.Encoding.UTF8);
            string fileName = System.IO.Path.GetFileName(ExportTargetFilePath);
            OnLogMessage?.Invoke($"✅ Modelo 3D Web guardado: {fileName} ({package.Interactive3DScene.Nodes.Count} nodos)");
            OnExportCompleted?.Invoke(true, $"¡Modelo 3D Web exportado exitosamente!\nArchivo: {fileName}\nNodos 3D: {package.Interactive3DScene.Nodes.Count}");
        }

        private void ExecuteExportFullMetadataJson(Document doc)
        {
            if (string.IsNullOrWhiteSpace(ExportTargetFilePath))
            {
                OnLogMessage?.Invoke("⚠️ Ruta destino no especificada para JSON de metadata.");
                OnExportCompleted?.Invoke(false, "Ruta no especificada");
                return;
            }

            OnLogMessage?.Invoke("💾 Exportando metadata completa del proyecto a archivo JSON...");
            var package = MetadataExporterService.CollectMetadata(
                doc,
                ExportIncludeElementDetails,
                ExportIncludeProjectInfo,
                ExportIncludeSiteLocation,
                ExportIncludeLevels,
                ExportIncludeCategorySummary,
                ExportIncludeRooms,
                ExportIncludeSchedules,
                ExportIncludeSheetsAndViews,
                ExportIncludeMaterials,
                ExportIncludeWeb3dModel,
                null,
                null
            );

            package.UserEmail = AuthenticationService.UserEmail;
            string jsonFull = Newtonsoft.Json.JsonConvert.SerializeObject(package, Newtonsoft.Json.Formatting.Indented);
            System.IO.File.WriteAllText(ExportTargetFilePath, jsonFull, System.Text.Encoding.UTF8);
            string fileName = System.IO.Path.GetFileName(ExportTargetFilePath);
            OnLogMessage?.Invoke($"✅ Metadata completa guardada: {fileName} ({jsonFull.Length:N0} caracteres)");
            OnExportCompleted?.Invoke(true, $"¡Archivo JSON de metadata exportado exitosamente!\nArchivo: {fileName}\nTamaño: {jsonFull.Length:N0} caracteres");
        }

        private void ExecuteRefreshMetrics(Document doc)
        {
            OnLogMessage?.Invoke("🔄 Analizando elementos y cómputos del modelo activo...");

            var metrics = new BimModelMetrics
            {
                DocumentTitle = doc.Title,
                ProjectName = doc.ProjectInformation?.Name ?? doc.Title,
                ProjectNumber = doc.ProjectInformation?.Number ?? "S/N",
                LastRefreshed = DateTime.Now
            };

            // 1. Muros
            var wallsCollector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType()
                .Cast<Wall>()
                .ToList();
            metrics.WallsCount = wallsCollector.Count;

            double wallAreaSqFt = 0;
            foreach (var w in wallsCollector)
            {
                var p = w.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                if (p != null && p.HasValue) wallAreaSqFt += p.AsDouble();
            }
            metrics.TotalWallAreaM2 = Math.Round(wallAreaSqFt * 0.092903, 2);

            // 2. Pisos / Suelos
            var floorsCollector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Floors)
                .WhereElementIsNotElementType()
                .Cast<Floor>()
                .ToList();
            metrics.FloorsCount = floorsCollector.Count;

            double floorAreaSqFt = 0;
            foreach (var f in floorsCollector)
            {
                var p = f.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                if (p != null && p.HasValue) floorAreaSqFt += p.AsDouble();
            }
            metrics.TotalFloorAreaM2 = Math.Round(floorAreaSqFt * 0.092903, 2);

            // 3. Estructuras (Columnas y vigas / framing)
            var framing = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType()
                .GetElementCount();
            var columns = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .WhereElementIsNotElementType()
                .GetElementCount();
            metrics.StructuralCount = framing + columns;

            // 4. Riego / MEP (Tuberías y aparatos)
            var pipes = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_PipeCurves)
                .WhereElementIsNotElementType()
                .GetElementCount();
            var plumbing = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
                .WhereElementIsNotElementType()
                .GetElementCount();
            metrics.PlumbingCount = pipes + plumbing;

            // 5. Habitaciones
            metrics.RoomsCount = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .GetElementCount();

            // 6. Tablas / Schedules
            metrics.SchedulesCount = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .WhereElementIsNotElementType()
                .GetElementCount();

            // 7. Láminas / Sheets
            metrics.SheetsCount = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .WhereElementIsNotElementType()
                .GetElementCount();

            // 8. Niveles
            metrics.LevelsCount = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .WhereElementIsNotElementType()
                .GetElementCount();

            OnLogMessage?.Invoke($"✅ Modelo analizado: {metrics.WallsCount} muros ({metrics.TotalWallAreaM2} m²), {metrics.FloorsCount} pisos ({metrics.TotalFloorAreaM2} m²), {metrics.RoomsCount} habitaciones, {metrics.SheetsCount} láminas.");
            OnMetricsRefreshed?.Invoke(metrics);
        }

        private void ExecuteSyncSupabase(Document doc)
        {
            OnLogMessage?.Invoke("☁️ Recopilando metadata para exportar a Supabase...");

            var package = MetadataExporterService.CollectMetadata(
                doc,
                true,  // includeElementDetails
                true,  // includeProjectInfo
                true,  // includeSiteLocation
                true,  // includeLevels
                true,  // includeCategorySummary
                true,  // includeRooms
                true,  // includeSchedules
                true,  // includeSheetsAndViews
                true,  // includeMaterials
                false, // includeWeb3dModel
                null,
                null
            );

            package.UserEmail = AuthenticationService.UserEmail;

            bool success = MetadataExporterService.SendToSupabaseDirect(package, out string msg);
            if (success)
            {
                OnLogMessage?.Invoke("🚀 ¡Sincronización a Supabase completada con éxito!");
            }
            else
            {
                OnLogMessage?.Invoke($"❌ Fallo en sincronización: {msg}");
            }

            OnSyncCompleted?.Invoke(success, msg);
        }

        public string GetName() => "ReviewControlPanelEventHandler";
    }
}
