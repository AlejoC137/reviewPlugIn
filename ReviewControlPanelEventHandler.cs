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
        OpenSheetsQueue,
        OpenLadrillos,
        OpenPisos,
        OpenSteelDeck,
        OpenSchedulesIA
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
        
        // Callbacks hacia la interfaz de usuario
        public Action<BimModelMetrics>? OnMetricsRefreshed { get; set; }
        public Action<bool, string>? OnSyncCompleted { get; set; }
        public Action<string>? OnLogMessage { get; set; }

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

                    case ControlPanelAction.OpenSchedulesIA:
                        if (doc != null)
                        {
                            var wSchedules = new SchedulesIAWindow(doc, uidoc);
                            wSchedules.ShowDialog();
                        }
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
