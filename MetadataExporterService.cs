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

        [JsonProperty("elements_by_id", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, ElementMetadataData>? ElementsById { get; set; }
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
            Action<int, int, string>? progressCallback,
            Func<bool>? isInterruptedCheck)
        {
            ProjectMetadataPackage package = new ProjectMetadataPackage();

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

                if (i % 50 == 0 || i == total - 1)
                {
                    progressCallback?.Invoke(i + 1, total, $"{i + 1} / {total} Elementos Procesados");
                }
            }

            return package;
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
