using System;
using System.IO;
using System.Text;
using System.Net.Http;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace RevitToSupabasePlugin
{
    public class ProjectMetadataPackage
    {
        [JsonProperty("user_email")]
        public string UserEmail { get; set; } = "";

        [JsonProperty("project_name")]
        public string ProjectName => ProjectInfo?.Name ?? "";

        [JsonProperty("project_number")]
        public string ProjectNumber => ProjectInfo?.Number ?? "";

        [JsonProperty("project_address")]
        public string ProjectAddress => ProjectInfo?.Address ?? "";

        [JsonProperty("exported_at")]
        public string ExportedAt { get; set; } = DateTime.Now.ToString("o");

        [JsonProperty("project_info", NullValueHandling = NullValueHandling.Ignore)]
        public ProjectInfoData? ProjectInfo { get; set; }

        [JsonProperty("site_location", NullValueHandling = NullValueHandling.Ignore)]
        public SiteLocationData? SiteLocation { get; set; }

        [JsonProperty("levels", NullValueHandling = NullValueHandling.Ignore)]
        public List<LevelData>? Levels { get; set; }

        [JsonProperty("category_summary", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, int>? CategorySummary { get; set; }

        [JsonProperty("rooms", NullValueHandling = NullValueHandling.Ignore)]
        public List<RoomData>? Rooms { get; set; }

        [JsonProperty("schedules", NullValueHandling = NullValueHandling.Ignore)]
        public List<ScheduleTableData>? Schedules { get; set; }

        [JsonProperty("sheets_and_views", NullValueHandling = NullValueHandling.Ignore)]
        public SheetsAndViewsData? SheetsAndViews { get; set; }

        [JsonProperty("materials", NullValueHandling = NullValueHandling.Ignore)]
        public List<MaterialData>? Materials { get; set; }

        [JsonProperty("elements_by_id", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, ElementMetadataData>? ElementsById { get; set; }

        [JsonProperty("interactive_3d_scene", NullValueHandling = NullValueHandling.Ignore)]
        public Web3DSceneData? Interactive3DScene { get; set; }
    }

    public class Web3DSceneData
    {
        [JsonProperty("metadata")]
        public Web3DSceneMetadata Metadata { get; set; } = new Web3DSceneMetadata();

        [JsonProperty("camera", NullValueHandling = NullValueHandling.Ignore)]
        public Web3DCameraData? Camera { get; set; }

        [JsonProperty("section_box", NullValueHandling = NullValueHandling.Ignore)]
        public Web3DSectionBoxData? SectionBox { get; set; }

        [JsonProperty("layers", NullValueHandling = NullValueHandling.Ignore)]
        public List<Web3DLayerGroup> Layers { get; set; } = new List<Web3DLayerGroup>();

        [JsonProperty("annotations", NullValueHandling = NullValueHandling.Ignore)]
        public List<Web3DAnnotationHotspot> Annotations { get; set; } = new List<Web3DAnnotationHotspot>();

        [JsonProperty("nodes")]
        public List<Web3DNodeData> Nodes { get; set; } = new List<Web3DNodeData>();
    }

    public class Web3DSceneMetadata
    {
        [JsonProperty("format_version")]
        public string FormatVersion { get; set; } = "2.0-web3d-navisworks";

        [JsonProperty("renderer_target")]
        public string RendererTarget { get; set; } = "React Three Fiber / Three.js / Navisworks Viewer";

        [JsonProperty("style")]
        public string Style { get; set; } = "FlatShading / Stylized LowPoly & SectionCut Capable";

        [JsonProperty("total_nodes")]
        public int TotalNodes { get; set; }

        [JsonProperty("total_layers")]
        public int TotalLayers { get; set; }

        [JsonProperty("total_annotations")]
        public int TotalAnnotations { get; set; }

        [JsonProperty("has_section_box")]
        public bool HasSectionBox { get; set; }
    }

    public class Web3DCameraData
    {
        [JsonProperty("eye_position")]
        public double[] EyePosition { get; set; } = new double[3];

        [JsonProperty("target_position")]
        public double[] TargetPosition { get; set; } = new double[3];

        [JsonProperty("up_direction")]
        public double[] UpDirection { get; set; } = new double[3];

        [JsonProperty("is_perspective")]
        public bool IsPerspective { get; set; } = true;

        [JsonProperty("field_of_view_deg")]
        public double FieldOfViewDeg { get; set; } = 50.0;
    }

    public class Web3DSectionBoxData
    {
        [JsonProperty("active")]
        public bool Active { get; set; }

        [JsonProperty("min")]
        public double[] Min { get; set; } = new double[3];

        [JsonProperty("max")]
        public double[] Max { get; set; } = new double[3];

        [JsonProperty("center")]
        public double[] Center { get; set; } = new double[3];

        [JsonProperty("size")]
        public double[] Size { get; set; } = new double[3];
    }

    public class Web3DLayerGroup
    {
        [JsonProperty("id")]
        public string Id { get; set; } = "";

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("type")]
        public string Type { get; set; } = "category"; // "category" or "level"

        [JsonProperty("color_hex")]
        public string ColorHex { get; set; } = "#888888";

        [JsonProperty("element_ids")]
        public List<long> ElementIds { get; set; } = new List<long>();
    }

    public class Web3DAnnotationHotspot
    {
        [JsonProperty("id")]
        public string Id { get; set; } = "";

        [JsonProperty("title")]
        public string Title { get; set; } = "";

        [JsonProperty("description")]
        public string Description { get; set; } = "";

        [JsonProperty("category")]
        public string Category { get; set; } = "";

        [JsonProperty("position")]
        public double[] Position { get; set; } = new double[3];

        [JsonProperty("element_id")]
        public long? ElementId { get; set; }
    }

    public class Web3DMeshData
    {
        [JsonProperty("vertices")]
        public double[] Vertices { get; set; } = Array.Empty<double>();

        [JsonProperty("indices")]
        public int[] Indices { get; set; } = Array.Empty<int>();

        [JsonProperty("normals")]
        public double[] Normals { get; set; } = Array.Empty<double>();
    }

    public class Web3DNodeData
    {
        [JsonProperty("element_id")]
        public long ElementId { get; set; }

        [JsonProperty("unique_id")]
        public string UniqueId { get; set; } = "";

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("category")]
        public string Category { get; set; } = "";

        [JsonProperty("level")]
        public string Level { get; set; } = "";

        [JsonProperty("geometry_type")]
        public string GeometryType { get; set; } = "box";

        [JsonProperty("position")]
        public double[] Position { get; set; } = new double[3];

        [JsonProperty("dimensions")]
        public double[] Dimensions { get; set; } = new double[3];

        [JsonProperty("rotation")]
        public double[] Rotation { get; set; } = new double[3];

        [JsonProperty("color_hex")]
        public string ColorHex { get; set; } = "#888888";

        [JsonProperty("flat_shading")]
        public bool FlatShading { get; set; } = true;

        [JsonProperty("mesh_data", NullValueHandling = NullValueHandling.Ignore)]
        public Web3DMeshData? MeshData { get; set; }

        [JsonProperty("quick_params", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string>? QuickParams { get; set; }
    }

    public class RoomData
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("number")]
        public string Number { get; set; } = "";

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("area_m2")]
        public double AreaM2 { get; set; }

        [JsonProperty("perimeter_m")]
        public double PerimeterM { get; set; }

        [JsonProperty("volume_m3")]
        public double VolumeM3 { get; set; }

        [JsonProperty("level")]
        public string Level { get; set; } = "";

        [JsonProperty("department")]
        public string Department { get; set; } = "";

        [JsonProperty("comments")]
        public string Comments { get; set; } = "";
    }

    public class ScheduleTableData
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("headers")]
        public List<string> Headers { get; set; } = new List<string>();

        [JsonProperty("rows")]
        public List<List<string>> Rows { get; set; } = new List<List<string>>();
    }

    public class SheetsAndViewsData
    {
        [JsonProperty("sheets")]
        public List<SheetData> Sheets { get; set; } = new List<SheetData>();

        [JsonProperty("views")]
        public List<ViewData> Views { get; set; } = new List<ViewData>();
    }

    public class SheetData
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("sheet_number")]
        public string SheetNumber { get; set; } = "";

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("revision")]
        public string Revision { get; set; } = "";

        [JsonProperty("drawn_by")]
        public string DrawnBy { get; set; } = "";
    }

    public class ViewData
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("view_type")]
        public string ViewType { get; set; } = "";

        [JsonProperty("scale")]
        public int Scale { get; set; }

        [JsonProperty("is_template")]
        public bool IsTemplate { get; set; }
    }

    public class MaterialData
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("category")]
        public string Category { get; set; } = "";

        [JsonProperty("color_rgb")]
        public string ColorRgb { get; set; } = "";
    }

    public class ProjectInfoData
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("number")]
        public string Number { get; set; } = "";

        [JsonProperty("client")]
        public string Client { get; set; } = "";

        [JsonProperty("status")]
        public string Status { get; set; } = "";

        [JsonProperty("issue_date")]
        public string IssueDate { get; set; } = "";

        [JsonProperty("address")]
        public string Address { get; set; } = "";

        [JsonProperty("building_name")]
        public string BuildingName { get; set; } = "";

        [JsonProperty("author")]
        public string Author { get; set; } = "";
    }

    public class SiteLocationData
    {
        [JsonProperty("latitude")]
        public double Latitude { get; set; }

        [JsonProperty("longitude")]
        public double Longitude { get; set; }

        [JsonProperty("elevation_m")]
        public double ElevationMeters { get; set; }

        [JsonProperty("time_zone")]
        public double TimeZone { get; set; }

        [JsonProperty("place_name")]
        public string PlaceName { get; set; } = "";
    }

    public class LevelData
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("elevation_m")]
        public double ElevationMeters { get; set; }
    }

    public class ElementMetadataData
    {
        [JsonProperty("element_id")]
        public long ElementId { get; set; }

        [JsonProperty("unique_id")]
        public string UniqueId { get; set; } = "";

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("category")]
        public string Category { get; set; } = "";

        [JsonProperty("family_name")]
        public string FamilyName { get; set; } = "";

        [JsonProperty("type_name")]
        public string TypeName { get; set; } = "";

        [JsonProperty("level")]
        public string Level { get; set; } = "";

        [JsonProperty("volume_m3")]
        public double? VolumeM3 { get; set; }

        [JsonProperty("area_m2")]
        public double? AreaM2 { get; set; }

        [JsonProperty("length_m")]
        public double? LengthM { get; set; }

        [JsonProperty("parameters")]
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
    }

    public class MetadataExporterService
    {
        private const string SUPABASE_URL = "https://irkrljhfbtnjspyvrapa.supabase.co";
        private const string SUPABASE_ANON_KEY = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Imlya3JsamhmYnRuanNweXZyYXBhIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzQzMTI3OTEsImV4cCI6MjA4OTg4ODc5MX0.c_Rd0hc11NRXX3aWYOODhU-e5GjGcoxobJQbBOc6LOE";

        public static ProjectMetadataPackage CollectMetadata(
            Document doc,
            bool includeElementDetails,
            bool includeProjectInfo,
            bool includeSiteLocation,
            bool includeLevels,
            bool includeCategorySummary,
            bool includeRooms,
            bool includeSchedules,
            bool includeSheetsAndViews,
            bool includeMaterials,
            bool includeWeb3dModel,
            Action<int, int, string>? progressCallback,
            Func<bool>? isInterruptedCheck)
        {
            ProjectMetadataPackage package = new ProjectMetadataPackage();
            Dictionary<string, Web3DLayerGroup> layerGroupsDict = new Dictionary<string, Web3DLayerGroup>();

            if (includeWeb3dModel)
            {
                package.Interactive3DScene = new Web3DSceneData();

                // 0. Recopilar datos de Vista 3D activa (Cámara y Caja de Corte 3D / Section Box)
                try
                {
                    View3D? activeView3D = doc.ActiveView as View3D;
                    if (activeView3D == null)
                    {
                        FilteredElementCollector v3dCol = new FilteredElementCollector(doc)
                            .OfClass(typeof(View3D))
                            .WhereElementIsNotElementType();
                        foreach (View3D v in v3dCol.ToElements())
                        {
                            if (!v.IsTemplate)
                            {
                                activeView3D = v;
                                if (v.Name == "{3D}" || v.Name.Contains("3D")) break;
                            }
                        }
                    }

                    if (activeView3D != null)
                    {
                        // Extracción de Cámara vía ViewOrientation3D
                        ViewOrientation3D orientation = activeView3D.GetOrientation();
                        XYZ eye = orientation.EyePosition;
                        XYZ viewDir = orientation.ForwardDirection;
                        XYZ upDir = orientation.UpDirection;
                        XYZ target = eye + viewDir * 10.0;

                        package.Interactive3DScene.Camera = new Web3DCameraData
                        {
                            EyePosition = new double[] { Math.Round(eye.X * 0.3048, 3), Math.Round(eye.Z * 0.3048, 3), Math.Round(-eye.Y * 0.3048, 3) },
                            TargetPosition = new double[] { Math.Round(target.X * 0.3048, 3), Math.Round(target.Z * 0.3048, 3), Math.Round(-target.Y * 0.3048, 3) },
                            UpDirection = new double[] { Math.Round(upDir.X, 3), Math.Round(upDir.Z, 3), Math.Round(-upDir.Y, 3) },
                            IsPerspective = activeView3D.IsPerspective,
                            FieldOfViewDeg = 50.0
                        };

                        // Extracción de Caja de Corte 3D (Section Box)
                        if (activeView3D.IsSectionBoxActive)
                        {
                            BoundingBoxXYZ sBox = activeView3D.GetSectionBox();
                            if (sBox != null)
                            {
                                Transform tr = sBox.Transform;
                                XYZ minWorld = tr.OfPoint(sBox.Min);
                                XYZ maxWorld = tr.OfPoint(sBox.Max);

                                double sMinX = Math.Min(minWorld.X, maxWorld.X) * 0.3048;
                                double sMaxX = Math.Max(minWorld.X, maxWorld.X) * 0.3048;
                                double sMinY = Math.Min(minWorld.Z, maxWorld.Z) * 0.3048;
                                double sMaxY = Math.Max(minWorld.Z, maxWorld.Z) * 0.3048;
                                double sMinZ = -Math.Max(minWorld.Y, maxWorld.Y) * 0.3048;
                                double sMaxZ = -Math.Min(minWorld.Y, maxWorld.Y) * 0.3048;

                                double cX = (sMinX + sMaxX) / 2.0;
                                double cY = (sMinY + sMaxY) / 2.0;
                                double cZ = (sMinZ + sMaxZ) / 2.0;

                                package.Interactive3DScene.SectionBox = new Web3DSectionBoxData
                                {
                                    Active = true,
                                    Min = new double[] { Math.Round(sMinX, 3), Math.Round(sMinY, 3), Math.Round(sMinZ, 3) },
                                    Max = new double[] { Math.Round(sMaxX, 3), Math.Round(sMaxY, 3), Math.Round(sMaxZ, 3) },
                                    Center = new double[] { Math.Round(cX, 3), Math.Round(cY, 3), Math.Round(cZ, 3) },
                                    Size = new double[] { Math.Round(Math.Abs(sMaxX - sMinX), 3), Math.Round(Math.Abs(sMaxY - sMinY), 3), Math.Round(Math.Abs(sMaxZ - sMinZ), 3) }
                                };
                                package.Interactive3DScene.Metadata.HasSectionBox = true;
                            }
                        }
                    }
                }
                catch { }

                // Recopilar anotaciones y notas de texto en 3D
                try
                {
                    FilteredElementCollector textCol = new FilteredElementCollector(doc)
                        .OfClass(typeof(TextNote))
                        .WhereElementIsNotElementType();

                    foreach (TextNote note in textCol.ToElements())
                    {
                        XYZ pos = note.Coord;
                        string text = note.Text ?? "";
                        if (string.IsNullOrWhiteSpace(text)) continue;

                        package.Interactive3DScene.Annotations.Add(new Web3DAnnotationHotspot
                        {
                            Id = note.Id.Value.ToString(),
                            Title = text.Length > 35 ? text.Substring(0, 35) + "..." : text,
                            Description = text,
                            Category = "TextNote",
                            Position = new double[] { Math.Round(pos.X * 0.3048, 3), Math.Round(pos.Z * 0.3048, 3), Math.Round(-pos.Y * 0.3048, 3) }
                        });
                    }
                }
                catch { }
            }

            // 1. Extraer Project Information
            if (includeProjectInfo && doc.ProjectInformation != null)
            {
                ProjectInfo pi = doc.ProjectInformation;
                package.ProjectInfo = new ProjectInfoData
                {
                    Name = pi.Name ?? "",
                    Number = pi.Number ?? "",
                    Client = pi.ClientName ?? "",
                    Status = pi.Status ?? "",
                    IssueDate = pi.IssueDate ?? "",
                    Address = pi.Address ?? "",
                    BuildingName = pi.BuildingName ?? "",
                    Author = pi.Author ?? ""
                };
            }

            // 2. Extraer Site Location
            if (includeSiteLocation && doc.SiteLocation != null)
            {
                SiteLocation sl = doc.SiteLocation;
                package.SiteLocation = new SiteLocationData
                {
                    Latitude = sl.Latitude * (180.0 / Math.PI),
                    Longitude = sl.Longitude * (180.0 / Math.PI),
                    ElevationMeters = sl.Elevation * 0.3048,
                    TimeZone = sl.TimeZone,
                    PlaceName = sl.PlaceName ?? ""
                };
            }

            // 3. Extraer Niveles
            if (includeLevels)
            {
                package.Levels = new List<LevelData>();
                FilteredElementCollector levelCollector = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .WhereElementIsNotElementType();

                foreach (Level lvl in levelCollector.ToElements())
                {
                    package.Levels.Add(new LevelData
                    {
                        Id = lvl.Id.Value,
                        Name = lvl.Name,
                        ElevationMeters = lvl.Elevation * 0.3048
                    });
                }
            }

            // 3.5 Extraer Habitaciones (Rooms)
            if (includeRooms)
            {
                package.Rooms = new List<RoomData>();
                FilteredElementCollector roomCollector = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType();

                foreach (Element elem in roomCollector.ToElements())
                {
                    if (elem is Autodesk.Revit.DB.Architecture.Room room)
                    {
                        RoomData rd = new RoomData
                        {
                            Id = room.Id.Value,
                            Number = room.Number ?? "",
                            Name = room.Name ?? "",
                            AreaM2 = Math.Round(room.Area * 0.092903, 2),
                            PerimeterM = Math.Round(room.Perimeter * 0.3048, 2),
                            VolumeM3 = Math.Round(room.Volume * 0.0283168, 2),
                            Level = room.Level?.Name ?? "",
                            Department = room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT)?.AsString() ?? "",
                            Comments = room.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? ""
                        };
                        package.Rooms.Add(rd);
                    }
                }
            }

            // 3.6 Extraer Tablas de Planificación (Schedules / ViewSchedule)
            if (includeSchedules)
            {
                package.Schedules = new List<ScheduleTableData>();
                FilteredElementCollector schedCollector = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .WhereElementIsNotElementType();

                foreach (ViewSchedule vs in schedCollector.ToElements())
                {
                    if (vs.IsTemplate || vs.Title.StartsWith("<")) continue;

                    ScheduleTableData std = new ScheduleTableData
                    {
                        Id = vs.Id.Value,
                        Name = vs.Name ?? ""
                    };

                    try
                    {
                        TableData td = vs.GetTableData();
                        TableSectionData? tsd = td?.GetSectionData(SectionType.Body);
                        if (tsd != null)
                        {
                            int numRows = tsd.NumberOfRows;
                            int numCols = tsd.NumberOfColumns;

                            if (numRows > 0 && numCols > 0)
                            {
                                for (int col = 0; col < numCols; col++)
                                {
                                    string cellText = vs.GetCellText(SectionType.Body, 0, col);
                                    std.Headers.Add(cellText ?? $"Col {col + 1}");
                                }

                                for (int row = 1; row < numRows; row++)
                                {
                                    List<string> rowData = new List<string>();
                                    for (int col = 0; col < numCols; col++)
                                    {
                                        string cellText = vs.GetCellText(SectionType.Body, row, col);
                                        rowData.Add(cellText ?? "");
                                    }
                                    std.Rows.Add(rowData);
                                }
                            }
                        }
                    }
                    catch { }

                    package.Schedules.Add(std);
                }
            }

            // 3.7 Extraer Planos y Vistas (Sheets & Views)
            if (includeSheetsAndViews)
            {
                package.SheetsAndViews = new SheetsAndViewsData();

                FilteredElementCollector sheetCollector = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .WhereElementIsNotElementType();

                foreach (ViewSheet sheet in sheetCollector.ToElements())
                {
                    string drawnByVal = sheet.get_Parameter(BuiltInParameter.SHEET_DRAWN_BY)?.AsString() ?? "";
                    string revVal = sheet.get_Parameter(BuiltInParameter.SHEET_CURRENT_REVISION)?.AsString() ?? "";
                    package.SheetsAndViews.Sheets.Add(new SheetData
                    {
                        Id = sheet.Id.Value,
                        SheetNumber = sheet.SheetNumber ?? "",
                        Name = sheet.Name ?? "",
                        Revision = revVal,
                        DrawnBy = drawnByVal
                    });
                }

                FilteredElementCollector viewCollector = new FilteredElementCollector(doc)
                    .OfClass(typeof(Autodesk.Revit.DB.View))
                    .WhereElementIsNotElementType();

                foreach (Autodesk.Revit.DB.View v in viewCollector.ToElements())
                {
                    if (v.IsTemplate || v is ViewSchedule || v is ViewSheet) continue;

                    package.SheetsAndViews.Views.Add(new ViewData
                    {
                        Id = v.Id.Value,
                        Name = v.Name ?? "",
                        ViewType = v.ViewType.ToString(),
                        Scale = v.Scale,
                        IsTemplate = v.IsTemplate
                    });
                }
            }

            // 3.8 Extraer Materiales del Modelo
            if (includeMaterials)
            {
                package.Materials = new List<MaterialData>();
                FilteredElementCollector matCollector = new FilteredElementCollector(doc)
                    .OfClass(typeof(Material));

                foreach (Material mat in matCollector.ToElements())
                {
                    string rgb = mat.Color != null ? $"{mat.Color.Red},{mat.Color.Green},{mat.Color.Blue}" : "";
                    package.Materials.Add(new MaterialData
                    {
                        Id = mat.Id.Value,
                        Name = mat.Name ?? "",
                        Category = mat.MaterialCategory ?? "",
                        ColorRgb = rgb
                    });
                }
            }

            // 4. Recopilar Elementos del Modelo (Filtrar elementos 3D / físicos)
            FilteredElementCollector elementCollector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .WhereElementIsViewIndependent();

            IList<Element> allElements = elementCollector.ToElements();
            int total = allElements.Count;

            if (includeCategorySummary) package.CategorySummary = new Dictionary<string, int>();
            if (includeElementDetails) package.ElementsById = new Dictionary<string, ElementMetadataData>();

            for (int i = 0; i < total; i++)
            {
                if (isInterruptedCheck != null && isInterruptedCheck())
                {
                    break;
                }

                Element elem = allElements[i];
                if (elem.Category == null || elem.Category.CategoryType != CategoryType.Model)
                    continue;

                string catName = elem.Category.Name;

                // Actualizar conteo por categoría
                if (includeCategorySummary && package.CategorySummary != null)
                {
                    if (!package.CategorySummary.ContainsKey(catName))
                    {
                        package.CategorySummary[catName] = 0;
                    }
                    package.CategorySummary[catName]++;
                }

                // Detalle por ID de elemento
                if (includeElementDetails && package.ElementsById != null)
                {
                    long elemId = elem.Id.Value;
                    string strId = elemId.ToString();

                    ElementMetadataData metadata = new ElementMetadataData
                    {
                        ElementId = elemId,
                        UniqueId = elem.UniqueId,
                        Category = catName,
                        Name = elem.Name ?? ""
                    };

                    ElementType? elemType = doc.GetElement(elem.GetTypeId()) as ElementType;
                    if (elemType != null)
                    {
                        metadata.FamilyName = elemType.FamilyName ?? "";
                        metadata.TypeName = elemType.Name ?? "";
                    }

                    if (elem.LevelId != ElementId.InvalidElementId)
                    {
                        Element? lvlElem = doc.GetElement(elem.LevelId);
                        if (lvlElem != null) metadata.Level = lvlElem.Name;
                    }

                    Parameter pVol = elem.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
                    if (pVol != null && pVol.HasValue) metadata.VolumeM3 = pVol.AsDouble() * 0.0283168;

                    Parameter pArea = elem.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                    if (pArea != null && pArea.HasValue) metadata.AreaM2 = pArea.AsDouble() * 0.092903;

                    Parameter pLen = elem.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
                    if (pLen != null && pLen.HasValue) metadata.LengthM = pLen.AsDouble() * 0.3048;

                    foreach (Parameter p in elem.Parameters)
                    {
                        if (p == null || !p.HasValue || string.IsNullOrEmpty(p.Definition?.Name)) continue;
                        string pName = p.Definition.Name;
                        if (!metadata.Parameters.ContainsKey(pName))
                        {
                            string valStr = p.AsValueString() ?? p.AsString() ?? "";
                            if (!string.IsNullOrEmpty(valStr))
                            {
                                metadata.Parameters[pName] = valStr;
                            }
                        }
                    }

                    package.ElementsById[strId] = metadata;
                }

                // 3D Model Web Extraction (Estilizado, FlatShading, Bajo Peso)
                if (includeWeb3dModel && package.Interactive3DScene != null)
                {
                    BoundingBoxXYZ bbox = elem.get_BoundingBox(null);
                    if (bbox != null)
                    {
                        XYZ min = bbox.Min;
                        XYZ max = bbox.Max;

                        double width = Math.Max(0.05, Math.Abs(max.X - min.X) * 0.3048);
                        double depth = Math.Max(0.05, Math.Abs(max.Y - min.Y) * 0.3048);
                        double height = Math.Max(0.05, Math.Abs(max.Z - min.Z) * 0.3048);

                        // Coordenadas TresJS: X = X, Y = Z (arriba), Z = -Y
                        double posX = (min.X + max.X) / 2.0 * 0.3048;
                        double posY = (min.Z + max.Z) / 2.0 * 0.3048;
                        double posZ = -((min.Y + max.Y) / 2.0 * 0.3048);

                        string lvlName = "";
                        if (elem.LevelId != ElementId.InvalidElementId)
                        {
                            Element? lvlElem = doc.GetElement(elem.LevelId);
                            if (lvlElem != null) lvlName = lvlElem.Name;
                        }

                        Web3DNodeData node = new Web3DNodeData
                        {
                            ElementId = elem.Id.Value,
                            UniqueId = elem.UniqueId,
                            Name = elem.Name ?? "",
                            Category = catName,
                            Level = lvlName,
                            GeometryType = "box",
                            Position = new double[] { Math.Round(posX, 3), Math.Round(posY, 3), Math.Round(posZ, 3) },
                            Dimensions = new double[] { Math.Round(width, 3), Math.Round(height, 3), Math.Round(depth, 3) },
                            Rotation = new double[] { 0, 0, 0 },
                            ColorHex = GetCategoryStylizedColor(catName),
                            FlatShading = true,
                            QuickParams = new Dictionary<string, string>()
                        };

                        Parameter pMark = elem.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                        if (pMark != null && pMark.HasValue && !string.IsNullOrEmpty(pMark.AsString())) node.QuickParams["Mark"] = pMark.AsString()!;

                        Parameter pComments = elem.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                        if (pComments != null && pComments.HasValue && !string.IsNullOrEmpty(pComments.AsString())) node.QuickParams["Comments"] = pComments.AsString()!;

                        ElementType? eType = doc.GetElement(elem.GetTypeId()) as ElementType;
                        if (eType != null)
                        {
                            if (!string.IsNullOrEmpty(eType.FamilyName)) node.QuickParams["Family"] = eType.FamilyName;
                            if (!string.IsNullOrEmpty(eType.Name)) node.QuickParams["Type"] = eType.Name;
                        }

                        package.Interactive3DScene.Nodes.Add(node);

                        if (!layerGroupsDict.ContainsKey(catName))
                        {
                            layerGroupsDict[catName] = new Web3DLayerGroup
                            {
                                Id = "cat_" + catName.ToLower().Replace(" ", "_"),
                                Name = catName,
                                Type = "category",
                                ColorHex = GetCategoryStylizedColor(catName)
                            };
                        }
                        layerGroupsDict[catName].ElementIds.Add(elem.Id.Value);
                    }
                }

                if (i % 50 == 0 || i == total - 1)
                {
                    progressCallback?.Invoke(i + 1, total, $"{i + 1} / {total} Elementos Procesados");
                }
            }

            if (includeWeb3dModel && package.Interactive3DScene != null)
            {
                package.Interactive3DScene.Layers.AddRange(layerGroupsDict.Values);
                package.Interactive3DScene.Metadata.TotalNodes = package.Interactive3DScene.Nodes.Count;
                package.Interactive3DScene.Metadata.TotalLayers = package.Interactive3DScene.Layers.Count;
                package.Interactive3DScene.Metadata.TotalAnnotations = package.Interactive3DScene.Annotations.Count;
            }

            return package;
        }

        private static string GetCategoryStylizedColor(string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName)) return "#888888";
            string lower = categoryName.ToLower();

            if (lower.Contains("wall") || lower.Contains("muro") || lower.Contains("pared")) return "#4A90E2";
            if (lower.Contains("floor") || lower.Contains("suelo") || lower.Contains("piso")) return "#7ED321";
            if (lower.Contains("roof") || lower.Contains("cubierta") || lower.Contains("techo")) return "#D0021B";
            if (lower.Contains("window") || lower.Contains("ventana")) return "#50E3C2";
            if (lower.Contains("door") || lower.Contains("puerta")) return "#F5A623";
            if (lower.Contains("column") || lower.Contains("columna") || lower.Contains("structural") || lower.Contains("estruc")) return "#9013FE";
            if (lower.Contains("furniture") || lower.Contains("mueble") || lower.Contains("mobiliario")) return "#F8E71C";
            if (lower.Contains("pipe") || lower.Contains("tuber") || lower.Contains("duct") || lower.Contains("conducto")) return "#FF5722";
            if (lower.Contains("stair") || lower.Contains("escalera") || lower.Contains("rail") || lower.Contains("baranda")) return "#E91E63";

            return "#B8E986";
        }

        public static bool SendToSupabaseDirect(ProjectMetadataPackage package, out string responseMessage)
        {
            using (HttpClient client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(20);
                
                string endpoint = $"{SUPABASE_URL}/rest/v1/plugin_project_data";
                client.DefaultRequestHeaders.Add("apikey", SUPABASE_ANON_KEY);
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {SUPABASE_ANON_KEY}");

                string json = JsonConvert.SerializeObject(package, Formatting.None, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
                StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

                try
                {
                    HttpResponseMessage response = client.PostAsync(endpoint, content).GetAwaiter().GetResult();
                    if (response.IsSuccessStatusCode)
                    {
                        responseMessage = "¡Metadata guardada exitosamente en Supabase!\nPuedes verla en la pestaña /fromPlugIn de review2.";
                        return true;
                    }
                    else
                    {
                        string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        responseMessage = $"Error de Supabase ({response.StatusCode}): {body}";
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    responseMessage = $"Error de red al conectar con Supabase: {ex.Message}";
                    return false;
                }
            }
        }
    }
}
