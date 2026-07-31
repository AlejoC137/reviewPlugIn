using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitColor = Autodesk.Revit.DB.Color;
using RevitView  = Autodesk.Revit.DB.View;

namespace RevitToSupabasePlugin
{
    // ══════════════════════════════════════════════════════════════════════════════
    // DATA MODELS
    // ══════════════════════════════════════════════════════════════════════════════

    public class PlantillaJsonConfig
    {
        [JsonProperty("project_info")]
        public PlantillaProjectInfo? ProjectInfo { get; set; }

        [JsonProperty("levels")]
        public List<PlantillaLevel>? Levels { get; set; }

        [JsonProperty("materials")]
        public List<PlantillaMaterial>? Materials { get; set; }

        [JsonProperty("text_styles")]
        public List<PlantillaTextStyle>? TextStyles { get; set; }

        [JsonProperty("views")]
        public List<PlantillaView>? Views { get; set; }

        [JsonProperty("sheets")]
        public List<PlantillaSheet>? Sheets { get; set; }

        [JsonProperty("schedules_to_create")]
        public List<PlantillaSchedule>? SchedulesToCreate { get; set; }

        [JsonProperty("project_parameters")]
        public List<PlantillaParameter>? ProjectParameters { get; set; }
    }

    public class PlantillaProjectInfo
    {
        [JsonProperty("name")] public string? Name { get; set; }
        [JsonProperty("number")] public string? Number { get; set; }
        [JsonProperty("client")] public string? Client { get; set; }
        [JsonProperty("author")] public string? Author { get; set; }
        [JsonProperty("address")] public string? Address { get; set; }
        [JsonProperty("building_name")] public string? BuildingName { get; set; }
        [JsonProperty("organization_name")] public string? OrganizationName { get; set; }
        [JsonProperty("status")] public string? Status { get; set; }
        [JsonProperty("issue_date")] public string? IssueDate { get; set; }
    }

    public class PlantillaLevel
    {
        [JsonProperty("current_name")] public string CurrentName { get; set; } = "";
        [JsonProperty("new_name")] public string? NewName { get; set; }
        [JsonProperty("elevation_mm")] public double? ElevationMm { get; set; }
    }

    public class PlantillaMaterial
    {
        [JsonProperty("current_name")] public string CurrentName { get; set; } = "";
        [JsonProperty("new_name")] public string? NewName { get; set; }
        [JsonProperty("color_r")] public int? ColorR { get; set; }
        [JsonProperty("color_g")] public int? ColorG { get; set; }
        [JsonProperty("color_b")] public int? ColorB { get; set; }
    }

    public class PlantillaTextStyle
    {
        [JsonProperty("type_name")] public string TypeName { get; set; } = "";
        [JsonProperty("font_size_mm")] public double? FontSizeMm { get; set; }
        [JsonProperty("bold")] public bool? Bold { get; set; }
        [JsonProperty("italic")] public bool? Italic { get; set; }
    }

    public class PlantillaView
    {
        [JsonProperty("current_name")] public string CurrentName { get; set; } = "";
        [JsonProperty("new_name")] public string? NewName { get; set; }
        [JsonProperty("scale")] public int? Scale { get; set; }
    }

    public class PlantillaSheet
    {
        [JsonProperty("current_number")] public string CurrentNumber { get; set; } = "";
        [JsonProperty("new_number")] public string? NewNumber { get; set; }
        [JsonProperty("new_name")] public string? NewName { get; set; }
        [JsonProperty("drawn_by")] public string? DrawnBy { get; set; }
        [JsonProperty("checked_by")] public string? CheckedBy { get; set; }
        [JsonProperty("approved_by")] public string? ApprovedBy { get; set; }
    }

    public class PlantillaSchedule
    {
        [JsonProperty("name")] public string Name { get; set; } = "";
        [JsonProperty("category")] public string Category { get; set; } = "Rooms";
        [JsonProperty("fields")] public List<string>? Fields { get; set; }
    }

    public class PlantillaParameter
    {
        [JsonProperty("name")] public string Name { get; set; } = "";
        [JsonProperty("value")] public string Value { get; set; } = "";
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // SERVICE
    // ══════════════════════════════════════════════════════════════════════════════

    public class ApplicatorResult
    {
        public bool Success { get; set; }
        public List<string> Log { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public string Summary => $"{Log.Count} cambios aplicados, {Errors.Count} errores.";
    }

    public static class PlantillaApplicatorService
    {
        private const double MmToFt = 1.0 / 304.8;

        public static ApplicatorResult ApplyJson(Document doc, string jsonText)
        {
            var result = new ApplicatorResult();

            jsonText = jsonText.Trim();
            if (jsonText.StartsWith("```"))
            {
                var lines = jsonText.Split('\n');
                var sb = new System.Text.StringBuilder();
                bool inBlock = false;
                foreach (var line in lines)
                {
                    if (line.TrimStart().StartsWith("```"))
                    {
                        inBlock = !inBlock;
                        continue;
                    }
                    if (inBlock || !jsonText.Contains("```"))
                        sb.AppendLine(line);
                }
                jsonText = sb.ToString().Trim();
            }

            PlantillaJsonConfig? config;
            try
            {
                config = JsonConvert.DeserializeObject<PlantillaJsonConfig>(jsonText);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Errors.Add($"JSON inválido: {ex.Message}");
                return result;
            }

            if (config == null)
            {
                result.Success = false;
                result.Errors.Add("El JSON está vacío o no pudo ser interpretado.");
                return result;
            }

            using (Transaction tx = new Transaction(doc, "IA: Aplicar Configuración de Plantilla"))
            {
                tx.Start();
                try
                {
                    if (config.ProjectInfo != null)
                        ApplyProjectInfo(doc, config.ProjectInfo, result);

                    if (config.Levels != null)
                        ApplyLevels(doc, config.Levels, result);

                    if (config.Materials != null)
                        ApplyMaterials(doc, config.Materials, result);

                    if (config.TextStyles != null)
                        ApplyTextStyles(doc, config.TextStyles, result);

                    if (config.Views != null)
                        ApplyViews(doc, config.Views, result);

                    if (config.Sheets != null)
                        ApplySheets(doc, config.Sheets, result);

                    if (config.SchedulesToCreate != null)
                        CreateSchedules(doc, config.SchedulesToCreate, result);

                    if (config.ProjectParameters != null)
                        ApplyProjectParameters(doc, config.ProjectParameters, result);

                    tx.Commit();
                    result.Success = true;
                }
                catch (Exception ex)
                {
                    try { tx.RollBack(); } catch { }
                    result.Success = false;
                    result.Errors.Add($"Excepción durante la transacción: {ex.Message}");
                }
            }

            return result;
        }

        private static void ApplyProjectInfo(Document doc, PlantillaProjectInfo info, ApplicatorResult result)
        {
            try
            {
                ProjectInfo pi = doc.ProjectInformation;
                if (!string.IsNullOrWhiteSpace(info.Name)) { pi.Name = info.Name; result.Log.Add($"Nombre del proyecto → \"{info.Name}\""); }
                if (!string.IsNullOrWhiteSpace(info.Number)) { pi.Number = info.Number; result.Log.Add($"Número del proyecto → \"{info.Number}\""); }
                if (!string.IsNullOrWhiteSpace(info.Address)) { pi.Address = info.Address; result.Log.Add($"Dirección → \"{info.Address}\""); }
                if (!string.IsNullOrWhiteSpace(info.Author)) { pi.Author = info.Author; result.Log.Add($"Autor → \"{info.Author}\""); }
                if (!string.IsNullOrWhiteSpace(info.BuildingName)) { pi.BuildingName = info.BuildingName; result.Log.Add($"Edificio → \"{info.BuildingName}\""); }
                if (!string.IsNullOrWhiteSpace(info.OrganizationName)) { pi.OrganizationName = info.OrganizationName; result.Log.Add($"Organización → \"{info.OrganizationName}\""); }

                TrySetParam(pi, "Project Status", info.Status, result);
                TrySetParam(pi, "Project Issue Date", info.IssueDate, result);
                TrySetParam(pi, "Client Name", info.Client, result);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Error en ProjectInfo: {ex.Message}");
            }
        }

        private static void TrySetParam(Element elem, string paramName, string? value, ApplicatorResult result)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var p = elem.LookupParameter(paramName);
            if (p != null && !p.IsReadOnly)
            {
                p.Set(value);
                result.Log.Add($"Parámetro '{paramName}' → \"{value}\"");
            }
        }

        private static void ApplyLevels(Document doc, List<PlantillaLevel> levels, ApplicatorResult result)
        {
            var existing = new Dictionary<string, Level>(StringComparer.OrdinalIgnoreCase);
            using (FilteredElementCollector col = new FilteredElementCollector(doc))
            {
                col.OfClass(typeof(Level));
                foreach (Element e in col)
                {
                    if (e is Level lv) existing[lv.Name] = lv;
                }
            }

            foreach (var cfg in levels)
            {
                if (!existing.TryGetValue(cfg.CurrentName, out Level? level))
                {
                    result.Errors.Add($"Nivel no encontrado: \"{cfg.CurrentName}\"");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(cfg.NewName) && cfg.NewName != cfg.CurrentName)
                {
                    level.Name = cfg.NewName;
                    result.Log.Add($"Nivel renombrado: \"{cfg.CurrentName}\" → \"{cfg.NewName}\"");
                }

                if (cfg.ElevationMm.HasValue)
                {
                    double ft = cfg.ElevationMm.Value * MmToFt;
                    level.Elevation = ft;
                    result.Log.Add($"Nivel \"{cfg.NewName ?? cfg.CurrentName}\" elevación → {cfg.ElevationMm}mm");
                }
            }
        }

        private static void ApplyMaterials(Document doc, List<PlantillaMaterial> materials, ApplicatorResult result)
        {
            var existing = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
            using (FilteredElementCollector col = new FilteredElementCollector(doc))
            {
                col.OfClass(typeof(Material));
                foreach (Element e in col)
                {
                    if (e is Material m) existing[m.Name] = m;
                }
            }

            foreach (var cfg in materials)
            {
                if (!existing.TryGetValue(cfg.CurrentName, out Material? mat))
                {
                    result.Errors.Add($"Material no encontrado: \"{cfg.CurrentName}\"");
                    continue;
                }

                if (cfg.ColorR.HasValue && cfg.ColorG.HasValue && cfg.ColorB.HasValue)
                {
                    mat.Color = new RevitColor(
                        (byte)Math.Clamp(cfg.ColorR.Value, 0, 255),
                        (byte)Math.Clamp(cfg.ColorG.Value, 0, 255),
                        (byte)Math.Clamp(cfg.ColorB.Value, 0, 255));
                    result.Log.Add($"Material \"{cfg.CurrentName}\" color → RGB({cfg.ColorR},{cfg.ColorG},{cfg.ColorB})");
                }

                if (!string.IsNullOrWhiteSpace(cfg.NewName) && cfg.NewName != cfg.CurrentName)
                {
                    mat.Name = cfg.NewName;
                    result.Log.Add($"Material renombrado: \"{cfg.CurrentName}\" → \"{cfg.NewName}\"");
                }
            }
        }

        private static void ApplyTextStyles(Document doc, List<PlantillaTextStyle> styles, ApplicatorResult result)
        {
            using (FilteredElementCollector col = new FilteredElementCollector(doc))
            {
                col.OfClass(typeof(TextNoteType));
                foreach (Element e in col)
                {
                    if (e is not TextNoteType tnt) continue;
                    var cfg = styles.Find(s => string.Equals(s.TypeName, tnt.Name, StringComparison.OrdinalIgnoreCase));
                    if (cfg == null) continue;

                    if (cfg.FontSizeMm.HasValue)
                    {
                        double ft = cfg.FontSizeMm.Value * MmToFt;
                        Parameter? pSize = tnt.get_Parameter(BuiltInParameter.TEXT_SIZE);
                        if (pSize != null && !pSize.IsReadOnly)
                        {
                            pSize.Set(ft);
                            result.Log.Add($"TextStyle \"{tnt.Name}\" tamaño → {cfg.FontSizeMm}mm");
                        }
                    }

                    if (cfg.Bold.HasValue)
                    {
                        Parameter? pBold = tnt.get_Parameter(BuiltInParameter.TEXT_STYLE_BOLD);
                        if (pBold != null && !pBold.IsReadOnly)
                        {
                            pBold.Set(cfg.Bold.Value ? 1 : 0);
                            result.Log.Add($"TextStyle \"{tnt.Name}\" bold → {cfg.Bold}");
                        }
                    }

                    if (cfg.Italic.HasValue)
                    {
                        Parameter? pItalic = tnt.get_Parameter(BuiltInParameter.TEXT_STYLE_ITALIC);
                        if (pItalic != null && !pItalic.IsReadOnly)
                        {
                            pItalic.Set(cfg.Italic.Value ? 1 : 0);
                            result.Log.Add($"TextStyle \"{tnt.Name}\" italic → {cfg.Italic}");
                        }
                    }
                }
            }
        }

        private static void ApplyViews(Document doc, List<PlantillaView> views, ApplicatorResult result)
        {
            using (FilteredElementCollector col = new FilteredElementCollector(doc))
            {
                col.OfClass(typeof(RevitView));
                foreach (Element e in col)
                {
                    if (e is not RevitView v || v is ViewSheet) continue;
                    var cfg = views.Find(vv => string.Equals(vv.CurrentName, v.Name, StringComparison.OrdinalIgnoreCase));
                    if (cfg == null) continue;

                    if (cfg.Scale.HasValue && cfg.Scale.Value > 0)
                    {
                        v.Scale = cfg.Scale.Value;
                        result.Log.Add($"Vista \"{v.Name}\" escala → 1:{cfg.Scale}");
                    }

                    if (!string.IsNullOrWhiteSpace(cfg.NewName) && cfg.NewName != cfg.CurrentName)
                    {
                        string oldName = v.Name;
                        v.Name = cfg.NewName;
                        result.Log.Add($"Vista renombrada: \"{oldName}\" → \"{cfg.NewName}\"");
                    }
                }
            }
        }

        private static void ApplySheets(Document doc, List<PlantillaSheet> sheets, ApplicatorResult result)
        {
            using (FilteredElementCollector col = new FilteredElementCollector(doc))
            {
                col.OfClass(typeof(ViewSheet));
                foreach (Element e in col)
                {
                    if (e is not ViewSheet vs) continue;
                    var cfg = sheets.Find(s => string.Equals(s.CurrentNumber, vs.SheetNumber, StringComparison.OrdinalIgnoreCase));
                    if (cfg == null) continue;

                    if (!string.IsNullOrWhiteSpace(cfg.NewNumber))
                    {
                        vs.SheetNumber = cfg.NewNumber;
                        result.Log.Add($"Plano número: \"{cfg.CurrentNumber}\" → \"{cfg.NewNumber}\"");
                    }

                    if (!string.IsNullOrWhiteSpace(cfg.NewName))
                    {
                        vs.Name = cfg.NewName;
                        result.Log.Add($"Plano nombre: \"{cfg.CurrentNumber}\" → \"{cfg.NewName}\"");
                    }

                    TrySetParam(vs, "Drawn By", cfg.DrawnBy, result);
                    TrySetParam(vs, "Checked By", cfg.CheckedBy, result);
                    TrySetParam(vs, "Approved By", cfg.ApprovedBy, result);
                }
            }
        }

        private static readonly Dictionary<string, BuiltInCategory> CategoryMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Rooms"] = BuiltInCategory.OST_Rooms,
            ["Walls"] = BuiltInCategory.OST_Walls,
            ["Doors"] = BuiltInCategory.OST_Doors,
            ["Windows"] = BuiltInCategory.OST_Windows,
            ["Floors"] = BuiltInCategory.OST_Floors,
            ["Ceilings"] = BuiltInCategory.OST_Ceilings,
            ["Stairs"] = BuiltInCategory.OST_Stairs,
            ["Columns"] = BuiltInCategory.OST_Columns,
            ["Structural Columns"] = BuiltInCategory.OST_StructuralColumns,
            ["Structural Framing"] = BuiltInCategory.OST_StructuralFraming,
            ["Furniture"] = BuiltInCategory.OST_Furniture,
            ["Generic Models"] = BuiltInCategory.OST_GenericModel,
        };

        private static void CreateSchedules(Document doc, List<PlantillaSchedule> schedules, ApplicatorResult result)
        {
            foreach (var cfg in schedules)
            {
                try
                {
                    if (!CategoryMap.TryGetValue(cfg.Category, out BuiltInCategory bic))
                    {
                        result.Errors.Add($"Categoría desconocida para schedule: \"{cfg.Category}\"");
                        continue;
                    }

                    ViewSchedule vs = ViewSchedule.CreateSchedule(doc, new ElementId(bic));
                    vs.Name = cfg.Name;
                    result.Log.Add($"Schedule creado: \"{cfg.Name}\" (categoría: {cfg.Category})");

                    if (cfg.Fields != null)
                    {
                        ScheduleDefinition def = vs.Definition;
                        IList<SchedulableField> schedulableFields = def.GetSchedulableFields();

                        foreach (string fieldName in cfg.Fields)
                        {
                            foreach (SchedulableField sf in schedulableFields)
                            {
                                string label = sf.GetName(doc);
                                if (string.Equals(label, fieldName, StringComparison.OrdinalIgnoreCase))
                                {
                                    def.AddField(sf);
                                    result.Log.Add($"  Campo añadido: \"{fieldName}\"");
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Error creando schedule \"{cfg.Name}\": {ex.Message}");
                }
            }
        }

        private static void ApplyProjectParameters(Document doc, List<PlantillaParameter> parameters, ApplicatorResult result)
        {
            ProjectInfo pi = doc.ProjectInformation;
            foreach (var cfg in parameters)
            {
                Parameter? p = pi.LookupParameter(cfg.Name);
                if (p == null)
                {
                    result.Errors.Add($"Parámetro de proyecto no encontrado: \"{cfg.Name}\"");
                    continue;
                }

                if (p.IsReadOnly)
                {
                    result.Errors.Add($"Parámetro \"{cfg.Name}\" es de solo lectura.");
                    continue;
                }

                switch (p.StorageType)
                {
                    case StorageType.String:
                        p.Set(cfg.Value);
                        result.Log.Add($"Parámetro \"{cfg.Name}\" → \"{cfg.Value}\"");
                        break;
                    case StorageType.Double:
                        if (double.TryParse(cfg.Value, out double d))
                        {
                            p.Set(d);
                            result.Log.Add($"Parámetro \"{cfg.Name}\" → {d}");
                        }
                        break;
                    case StorageType.Integer:
                        if (int.TryParse(cfg.Value, out int i))
                        {
                            p.Set(i);
                            result.Log.Add($"Parámetro \"{cfg.Name}\" → {i}");
                        }
                        break;
                }
            }
        }
    }
}
