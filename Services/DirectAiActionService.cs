using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace reviewPlugIn.Services
{
    public class DirectAiFilter
    {
        [JsonProperty("type_name")]
        public string TypeName { get; set; } = "";

        [JsonProperty("family_name")]
        public string FamilyName { get; set; } = "";

        [JsonProperty("level_name")]
        public string LevelName { get; set; } = "";

        [JsonProperty("search_text")]
        public string SearchText { get; set; } = "";
    }

    public class CloudSchemaSummary
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Category { get; set; } = "";
        public string UpdatedAt { get; set; } = "";
        public JToken MapData { get; set; }
        public string DisplayLabel => string.IsNullOrWhiteSpace(Category)
            ? $"🗺️ {Title}"
            : $"🗺️ [{Category.ToUpper()}] {Title}";
    }

    public class DirectAiActionItem
    {
        [JsonProperty("id")]
        public string Id { get; set; } = "";

        [JsonProperty("order")]
        public int Order { get; set; } = 1;

        [JsonProperty("step_name")]
        public string StepName { get; set; } = "";

        [JsonProperty("process")]
        public string ProcessBranch { get; set; } = "Proceso General";

        [JsonProperty("phase")]
        public string Phase { get; set; } = "Fase 1: Ejecución BIM";

        [JsonProperty("path")]
        public string SchemaPath { get; set; } = "";

        [JsonProperty("description")]
        public string Description { get; set; } = "";

        [JsonProperty("action")]
        public string Action { get; set; } = "modify_parameters";

        [JsonProperty("tool")]
        public string Tool { get; set; } = "";

        [JsonProperty("category")]
        public string Category { get; set; } = "Walls";

        [JsonProperty("filter")]
        public DirectAiFilter Filter { get; set; } = new DirectAiFilter();

        [JsonProperty("element_ids")]
        public List<long> ElementIds { get; set; } = new List<long>();

        [JsonProperty("parameters")]
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();

        [JsonProperty("operation")]
        public string Operation { get; set; } = "UPDATE";

        [JsonProperty("instruction")]
        public string Instruction { get; set; } = "";

        [JsonProperty("iteration_mode")]
        public string IterationMode { get; set; } = "ALL_ELEMENTS";

        [JsonProperty("naming_column")]
        public string NamingColumn { get; set; } = "";

        [JsonProperty("grouping_column")]
        public string GroupingColumn { get; set; } = "";

        [JsonProperty("target_schedule_id")]
        public string TargetScheduleId { get; set; } = "";

        [JsonProperty("target_schedule_name")]
        public string TargetScheduleName { get; set; } = "";

        // UI / Simulation properties
        [JsonProperty("enabled")]
        public bool Enabled
        {
            get => IsSelected;
            set => IsSelected = value;
        }

        [JsonIgnore]
        public bool IsSelected { get; set; } = true;

        [JsonIgnore]
        public string EffectiveTool => !string.IsNullOrWhiteSpace(Tool) ? Tool : Action;

        [JsonIgnore]
        public int MatchedCount { get; set; }

        [JsonIgnore]
        public List<ElementId> MatchedRevitIds { get; set; } = new List<ElementId>();

        [JsonIgnore]
        public string FilterSummary
        {
            get
            {
                if (ElementIds != null && ElementIds.Count > 0)
                    return $"IDs explícitos ({ElementIds.Count})";

                if (Filter == null) return "Todos los elementos";
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(Filter.TypeName)) parts.Add($"Tipo~'{Filter.TypeName}'");
                if (!string.IsNullOrWhiteSpace(Filter.FamilyName)) parts.Add($"Fam~'{Filter.FamilyName}'");
                if (!string.IsNullOrWhiteSpace(Filter.LevelName)) parts.Add($"Nivel~'{Filter.LevelName}'");
                if (!string.IsNullOrWhiteSpace(Filter.SearchText)) parts.Add($"Texto~'{Filter.SearchText}'");
                return parts.Count > 0 ? string.Join(", ", parts) : "Todos en categoría";
            }
        }

        [JsonIgnore]
        public string ParametersSummary
        {
            get
            {
                if (Parameters == null || Parameters.Count == 0) return "(Sin parámetros)";
                return string.Join(" | ", Parameters.Select(kv => $"{kv.Key} = \"{kv.Value}\""));
            }
        }

        [JsonIgnore]
        public string SimulationStatus { get; set; } = "Pendiente";
    }

    public static class DirectAiActionService
    {
        private static readonly string SupabaseUrl = "https://irkrljhfbtnjspyvrapa.supabase.co";
        private static readonly string AnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Imlya3JsamhmYnRuanNweXZyYXBhIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzQzMTI3OTEsImV4cCI6MjA4OTg4ODc5MX0.c_Rd0hc11NRXX3aWYOODhU-e5GjGcoxobJQbBOc6LOE";

        /// <summary>
        /// Descarga la lista de Esquemas desde Supabase (`esquemas` y `esquema_nodes`)
        /// para que el usuario pueda cargarlos directamente en el receptor del Plugin.
        /// </summary>
        public static async Task<List<CloudSchemaSummary>> FetchSchemasFromSupabaseAsync()
        {
            var result = new List<CloudSchemaSummary>();
            using (var http = new HttpClient())
            {
                http.DefaultRequestHeaders.Add("apikey", AnonKey);
                http.DefaultRequestHeaders.Add("Authorization", $"Bearer {AnonKey}");

                string url = $"{SupabaseUrl}/rest/v1/esquemas?select=id,title,category,updated_at,map_data&order=updated_at.desc&limit=50";
                var resp = await http.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                    return result;

                string json = await resp.Content.ReadAsStringAsync();
                var arr = JArray.Parse(json);
                foreach (var row in arr)
                {
                    result.Add(new CloudSchemaSummary
                    {
                        Id = row["id"]?.ToString() ?? "",
                        Title = row["title"]?.ToString() ?? "Esquema sin título",
                        Category = row["category"]?.ToString() ?? "BIM",
                        UpdatedAt = row["updated_at"]?.ToString() ?? "",
                        MapData = row["map_data"]
                    });
                }
            }
            return result;
        }

        /// <summary>
        /// Si un esquema en Supabase no tenía `map_data` poblado, reconstruye las acciones desde `esquema_nodes`.
        /// </summary>
        public static async Task<string> FetchSchemaJsonByIdAsync(CloudSchemaSummary schema)
        {
            if (schema == null) return "";
            if (schema.MapData != null && schema.MapData.Type == JTokenType.Object)
            {
                return schema.MapData.ToString(Formatting.Indented);
            }

            using (var http = new HttpClient())
            {
                http.DefaultRequestHeaders.Add("apikey", AnonKey);
                http.DefaultRequestHeaders.Add("Authorization", $"Bearer {AnonKey}");

                string url = $"{SupabaseUrl}/rest/v1/esquema_nodes?esquema_id=eq.{schema.Id}&order=order_index.asc";
                var resp = await http.GetAsync(url);
                if (!resp.IsSuccessStatusCode) return "";

                string nodesJson = await resp.Content.ReadAsStringAsync();
                var rows = JArray.Parse(nodesJson);
                var actionsList = new JArray();
                int order = 1;

                foreach (var r in rows)
                {
                    string hc = r["highlight_color"]?.ToString() ?? "";
                    if (hc.StartsWith("{"))
                    {
                        try
                        {
                            var meta = JObject.Parse(hc);
                            var ra = meta["revitAction"] as JObject;
                            if (ra != null)
                            {
                                var item = new JObject
                                {
                                    ["id"] = r["node_key"]?.ToString() ?? "",
                                    ["order"] = ra["order"] ?? order++,
                                    ["step_name"] = r["name"]?.ToString() ?? "Acción Revit",
                                    ["description"] = r["description"]?.ToString() ?? "",
                                    ["phase"] = ra["phase"] ?? "Fase 1: Ejecución BIM",
                                    ["action"] = ra["tool"] ?? ra["action"] ?? "modify_parameters",
                                    ["tool"] = ra["tool"] ?? ra["action"] ?? "modify_parameters",
                                    ["category"] = ra["category"] ?? "Walls",
                                    ["filter"] = ra["filter"] ?? new JObject(),
                                    ["parameters"] = ra["parameters"] ?? new JObject()
                                };
                                actionsList.Add(item);
                            }
                        }
                        catch { }
                    }
                }

                var wrapper = new JObject
                {
                    ["schema_version"] = "2.0-revit-schema",
                    ["schema_title"] = schema.Title,
                    ["actions"] = actionsList
                };
                return wrapper.ToString(Formatting.Indented);
            }
        }

        /// <summary>
        /// Extrae un resumen exhaustivo y dinámico del modelo de Revit activo
        /// (niveles, fases, tablas de planificación existentes, categorías con recuentos y familias muestra)
        /// para alimentar al Mega-Prompt de la IA y evitar alucinaciones.
        /// </summary>
        public static string ExtractModelContextSummary(Document doc)
        {
            if (doc == null) return "Sin modelo Revit activo conectado.";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== CONTEXTO DEL MODELO REVIT ACTIVO: '{doc.Title}' ===");

            // 1. Niveles
            try
            {
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .Select(l => l.Name)
                    .ToList();
                if (levels.Count > 0)
                {
                    sb.AppendLine($"• NIVELES DETECTADOS ({levels.Count}):");
                    sb.AppendLine("  " + string.Join(", ", levels));
                }
            }
            catch { }

            // 2. Fases del proyecto
            try
            {
                var phases = new FilteredElementCollector(doc)
                    .OfClass(typeof(Phase))
                    .Cast<Phase>()
                    .Select(p => p.Name)
                    .ToList();
                if (phases.Count > 0)
                {
                    sb.AppendLine($"• FASES DE CONSTRUCCIÓN: {string.Join(", ", phases)}");
                }
            }
            catch { }

            // 3. Tablas de Planificación (ViewSchedules) reales en el proyecto
            try
            {
                var schedules = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .Where(vs => !vs.IsTemplate && !vs.IsInternalKeynoteSchedule)
                    .Select(vs =>
                    {
                        string cat = "Multi";
                        try
                        {
                            if (vs.Definition?.CategoryId != null && vs.Definition.CategoryId != ElementId.InvalidElementId)
                            {
                                var c = Category.GetCategory(doc, vs.Definition.CategoryId);
                                if (c != null) cat = c.Name;
                            }
                        }
                        catch { }
                        return $"\"{vs.Name}\" [{cat}]";
                    })
                    .OrderBy(s => s)
                    .Take(75)
                    .ToList();

                if (schedules.Count > 0)
                {
                    sb.AppendLine($"• TABLAS DE PLANIFICACIÓN DEL PROYECTO ({schedules.Count} detectadas):");
                    sb.AppendLine("  " + string.Join("; ", schedules));
                }
            }
            catch { }

            // 4. Categorías principales con recuentos y familias/tipos muestra
            try
            {
                var keyCats = new[]
                {
                    BuiltInCategory.OST_Doors,
                    BuiltInCategory.OST_Walls,
                    BuiltInCategory.OST_Floors,
                    BuiltInCategory.OST_Windows,
                    BuiltInCategory.OST_Rooms,
                    BuiltInCategory.OST_Furniture,
                    BuiltInCategory.OST_Casework,
                    BuiltInCategory.OST_PlumbingFixtures,
                    BuiltInCategory.OST_Ceilings,
                    BuiltInCategory.OST_GenericModel,
                    BuiltInCategory.OST_Sheets
                };

                sb.AppendLine("• CATEGORÍAS PRINCIPALES Y TIPOS DETECTADOS EN EL MODELO:");
                foreach (var bic in keyCats)
                {
                    try
                    {
                        var elements = new FilteredElementCollector(doc)
                            .OfCategory(bic)
                            .WhereElementIsNotElementType()
                            .ToElements();

                        if (elements.Count == 0) continue;

                        string catName = bic.ToString().Replace("OST_", "");
                        var sampleTypes = elements
                            .Select(e =>
                            {
                                var tid = e.GetTypeId();
                                return tid != ElementId.InvalidElementId ? doc.GetElement(tid)?.Name : null;
                            })
                            .Where(n => !string.IsNullOrEmpty(n))
                            .Distinct()
                            .Take(6)
                            .ToList();

                        string sampleStr = sampleTypes.Count > 0 ? $" | Tipos muestra: {string.Join(", ", sampleTypes)}" : "";
                        sb.AppendLine($"  - {catName}: {elements.Count} elementos{sampleStr}");
                    }
                    catch { }
                }
            }
            catch { }

            return sb.ToString();
        }

        /// <summary>
        /// Genera el Mega-Prompt de Alta Rigurosidad (Zero-Laxity) para cualquier IA (Gemini, ChatGPT, Claude).
        /// Obliga a la IA a responder con un JSON 2.0-revit-schema sin texto conversacional,
        /// con especificaciones precisas para flujos de trabajo basados en Markdown y esquemas de Revit.
        /// </summary>
        public static string BuildSystemPrompt(Document doc)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("ROL DEL SISTEMA: Motor de Automatización BIM y Arquitecto Senior en Autodesk Revit API (ARCA Review / ISO 19650).");
            sb.AppendLine("OBJETIVO: Convertir solicitudes de usuario, instrucciones de procesos o ESQUEMAS EN FORMATO MARKDOWN / JSON en un documento ejecutable que cumple estrictamente la especificación `schema_version: \"2.0-revit-schema\"` para el Plugin de Revit.");
            sb.AppendLine();
            sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════════════");
            sb.AppendLine("REGLAS INQUEBRANTABLES (CERO LAXITUD - ZERO HALLUCINATIONS):");
            sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════════════");
            sb.AppendLine("1. SALIDA ESTRICTA: Tu respuesta DEBE SER EXCLUSIVAMENTE UN BLOQUE JSON VÁLIDO (o cercado con ```json).");
            sb.AppendLine("   ESTÁ TERMINANTEMENTE PROHIBIDO incluir introducciones ('¡Hola!', 'Por supuesto, aquí tienes el esquema...'), conclusiones, o explicaciones conversacionales fuera del JSON.");
            sb.AppendLine("2. FIDELIDAD AL ESQUEMA MARKDOWN: Si el usuario te proporciona un esquema de trabajo en Markdown (por ejemplo un mapa de procesos de ARCA Review, un contexto con tablas de planificación, o un árbol `schema_tree`), DEBES INGERIR Y TRADUCIR CADA PASO a una acción concreta en la lista `actions`.");
            sb.AppendLine("3. COHERENCIA CON EL MODELO REVIT: Utiliza nombres de tablas, categorías y parámetros que COINCIDAN con el CONTEXTO DEL MODELO incluido abajo. No inventes nombres de tablas si ya existen en el proyecto.");
            sb.AppendLine("4. ENCADENAMIENTO DE ENSAMBLAJES: Si el flujo pide crear ensamblajes (`create_assemblies`) a partir de una tabla (ej. puertas) y luego generar vistas (`create_assembly_views`), genera ambas acciones secuenciales respetando `order: 1`, `order: 2` y la misma categoría.");
            sb.AppendLine();
            sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════════════");
            sb.AppendLine("CATÁLOGO DE HERRAMIENTAS REVIT SOPORTADAS (campo `action` o `tool`):");
            sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════════════");
            sb.AppendLine("1. \"create_assemblies\":");
            sb.AppendLine("   - Empaqueta elementos de Revit en Ensamblajes (`AssemblyInstance`).");
            sb.AppendLine("   - `category`: Categoría a ensamblar (ej. Doors, Windows, Furniture, Casework, Walls).");
            sb.AppendLine("   - `parameters`: {");
            sb.AppendLine("       \"ScheduleName\": \"Nombre de tabla de referencia\",");
            sb.AppendLine("       \"Fields\": \"Family, Type, Recuento, Nivel\",");
            sb.AppendLine("       \"TargetCategory\": \"Doors\",");
            sb.AppendLine("       \"GroupBy\": \"Type\" (o \"Instance\")");
            sb.AppendLine("     }");
            sb.AppendLine("   - `iteration_mode`: \"BY_TYPE\" (un ensamblaje por tipo de familia) o \"BY_INSTANCE\" (uno por elemento).");
            sb.AppendLine();
            sb.AppendLine("2. \"create_assembly_views\":");
            sb.AppendLine("   - Genera vistas de detalle (Planta, Sección, Isométrica 3D y Tabla de Despiece) para los ensamblajes generados.");
            sb.AppendLine("   - `category`: Categoría del ensamblaje (ej. Doors, Windows).");
            sb.AppendLine("   - `parameters`: {");
            sb.AppendLine("       \"ScheduleName\": \"Nombre de la tabla de despiece/ensamblaje\",");
            sb.AppendLine("       \"Fields\": \"Nivel, Familia y tipo, Área, Comentarios\",");
            sb.AppendLine("       \"ViewsToCreate\": \"Schedule,3D,Plan,Section\"");
            sb.AppendLine("     }");
            sb.AppendLine();
            sb.AppendLine("3. \"create_schedule\":");
            sb.AppendLine("   - Crea una nueva tabla de planificación (`ViewSchedule`) en el modelo.");
            sb.AppendLine("   - `category`: \"Schedules\"");
            sb.AppendLine("   - `parameters`: {");
            sb.AppendLine("       \"ScheduleName\": \"Nombre único de la nueva tabla\",");
            sb.AppendLine("       \"TargetCategory\": \"Doors | Walls | Rooms | Floors | ...\",");
            sb.AppendLine("       \"Fields\": \"Family and Type, Base Constraint, Area, Volume, Comments\"");
            sb.AppendLine("     }");
            sb.AppendLine();
            sb.AppendLine("4. \"duplicate_schedule\":");
            sb.AppendLine("   - Duplica una tabla de planificación existente en el modelo y opcionalmente adiciona campos.");
            sb.AppendLine("   - `category`: \"Schedules\"");
            sb.AppendLine("   - `parameters`: {");
            sb.AppendLine("       \"SourceScheduleName\": \"Nombre de tabla existente en el modelo\",");
            sb.AppendLine("       \"ScheduleName\": \"Nombre de la nueva tabla clonada\",");
            sb.AppendLine("       \"Fields\": \"Campos adicionales o filtrados\"");
            sb.AppendLine("     }");
            sb.AppendLine();
            sb.AppendLine("5. \"modify_parameters\":");
            sb.AppendLine("   - Modifica parámetros de tipo o de instancia en elementos filtrados por categoría, tipo, familia o nivel.");
            sb.AppendLine("   - `category`: Walls, Floors, Doors, Windows, Rooms, etc.");
            sb.AppendLine("   - `filter`: { \"type_name\": \"...\", \"family_name\": \"...\", \"level_name\": \"...\", \"search_text\": \"...\" }");
            sb.AppendLine("   - `parameters`: Diccionario con nombres de parámetros y sus valores a asignar (ej. { \"Comments\": \"AUDITADO\", \"Fire Rating\": \"60 min\" }).");
            sb.AppendLine();
            sb.AppendLine("6. \"assign_type\":");
            sb.AppendLine("   - Cambia el tipo de familia de los elementos filtrados.");
            sb.AppendLine("   - `parameters`: { \"TargetTypeName\": \"Nombre exacto del tipo destino\" }");
            sb.AppendLine();
            sb.AppendLine("7. \"manage_sheets\":");
            sb.AppendLine("   - Crea o actualiza planos/láminas en Revit (`ViewSheet`).");
            sb.AppendLine("   - `parameters`: { \"Sheet Name\": \"...\", \"Sheet Number\": \"...\", \"Drawn By\": \"...\", \"Approved By\": \"...\" }");
            sb.AppendLine();
            sb.AppendLine("8. \"sync_materials\":");
            sb.AppendLine("   - Crea o actualiza un material BIM y lo asigna/sella en elementos.");
            sb.AppendLine("   - `parameters`: { \"MaterialName\": \"...\", \"ColorHex\": \"#10B981\", \"ApplyToElements\": \"true\" }");
            sb.AppendLine();
            sb.AppendLine("9. \"audit_qaqc\":");
            sb.AppendLine("   - Audita parámetros obligatorios en elementos del modelo.");
            sb.AppendLine("   - `parameters`: { \"RequiredParameters\": \"Comments,Mark\", \"StampOnPass\": \"QAQC_OK\", \"StampOnFail\": \"QAQC_INCOMPLETO\" }");
            sb.AppendLine();
            sb.AppendLine("10. \"convert_walls_to_bricks\" / \"convert_floors_to_tiles\" / \"convert_steel_deck\":");
            sb.AppendLine("   - Despiece constructivo y cálculo de piezas modular.");
            sb.AppendLine();
            sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════════════");
            sb.AppendLine("ESTRUCTURA EXACTA DEL JSON (FORMATO 2.0-REVIT-SCHEMA):");
            sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════════════");
            sb.AppendLine(@"{
  ""schema_version"": ""2.0-revit-schema"",
  ""schema_title"": ""FLUJO DE TRABAJO BIM: ENSAMBLAJES Y CÓMPUTOS"",
  ""generated_at"": ""2026-10-27T14:32:00.000Z"",
  ""total_actions"": 2,
  ""enabled_actions"": 2,
  ""actions"": [
    {
      ""id"": ""act_001"",
      ""order"": 1,
      ""enabled"": true,
      ""step_name"": ""➕ CREAR: Ensamblajes de Puertas"",
      ""process"": ""Carpinterías y Despieces"",
      ""phase"": ""Fase 1: Ensamblajes"",
      ""path"": ""CARPINTERÍAS › PUERTAS › ENSAMBLAJES"",
      ""description"": ""Crear AssemblyInstances para las puertas agrupándolas por tipo de familia"",
      ""action"": ""create_assemblies"",
      ""tool"": ""create_assemblies"",
      ""category"": ""Doors"",
      ""filter"": {
        ""type_name"": """",
        ""family_name"": """",
        ""level_name"": """",
        ""search_text"": """"
      },
      ""element_ids"": [],
      ""parameters"": {
        ""ScheduleName"": ""Tabla de planificación de puertas CIC - Trabajo"",
        ""Fields"": ""Family, Type, Recuento, Nivel"",
        ""TargetCategory"": ""Doors"",
        ""GroupBy"": ""Type""
      },
      ""operation"": ""CREATE"",
      ""iteration_mode"": ""BY_TYPE""
    },
    {
      ""id"": ""act_002"",
      ""order"": 2,
      ""enabled"": true,
      ""step_name"": ""➕ GENERAR: Vistas y Tablas de Ensamblaje"",
      ""process"": ""Carpinterías y Despieces"",
      ""phase"": ""Fase 2: Documentación de Ensamblajes"",
      ""path"": ""CARPINTERÍAS › PUERTAS › VISTAS"",
      ""description"": ""Generar vista isométrica 3D, planta, elevación y tabla de despiece para cada ensamblaje"",
      ""action"": ""create_assembly_views"",
      ""tool"": ""create_assembly_views"",
      ""category"": ""Doors"",
      ""filter"": {
        ""type_name"": """",
        ""family_name"": """",
        ""level_name"": """",
        ""search_text"": """"
      },
      ""element_ids"": [],
      ""parameters"": {
        ""ScheduleName"": ""NUEVA_Tabla de despiece de puertas"",
        ""Fields"": ""Nivel, Familia y tipo, Área, Comentarios"",
        ""ViewsToCreate"": ""Schedule,3D,Plan,Section""
      },
      ""operation"": ""CREATE"",
      ""iteration_mode"": ""ALL_ELEMENTS""
    }
  ]
}");
            sb.AppendLine();

            if (doc != null)
            {
                sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════════════");
                sb.AppendLine("CONTEXTO REAL EXTRAÍDO DEL MODELO REVIT ACTIVO:");
                sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════════════");
                sb.AppendLine(ExtractModelContextSummary(doc));
                sb.AppendLine();
            }

            sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════════════");
            sb.AppendLine("INSTRUCCIÓN / SOLICITUD A PROCESAR:");
            sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════════════");
            sb.AppendLine("[Pega aquí el esquema en Markdown o escribe el flujo de trabajo BIM que deseas convertir en acciones de Revit...]");

            return sb.ToString();
        }

        /// <summary>
        /// Extrae limpiamente el bloque JSON principal desde un texto que puede contener
        /// formato Markdown (```json ... ```), explicaciones introductorias o comentarios de IA.
        /// </summary>
        public static string ExtractJsonFromMarkdown(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText)) return "";
            string text = rawText.Trim();

            // 1. Si contiene bloque cercado ```json ... ``` o ```markdown ... ``` o ``` ... ```
            var match = System.Text.RegularExpressions.Regex.Match(
                text,
                @"```(?:json|markdown)?\s*([\s\S]*?)\s*```",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (match.Success)
            {
                string candidate = match.Groups[1].Value.Trim();
                if ((candidate.StartsWith("{") && candidate.EndsWith("}")) ||
                    (candidate.StartsWith("[") && candidate.EndsWith("]")))
                {
                    return candidate;
                }
            }

            // 2. Si no hay bloque cercado o fue insuficiente, buscar el primer '{' y último '}'
            int firstBrace = text.IndexOf('{');
            int lastBrace = text.LastIndexOf('}');
            int firstBracket = text.IndexOf('[');
            int lastBracket = text.LastIndexOf(']');

            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                if (firstBracket >= 0 && lastBracket > firstBracket && firstBracket < firstBrace && lastBracket > lastBrace)
                {
                    return text.Substring(firstBracket, lastBracket - firstBracket + 1).Trim();
                }
                return text.Substring(firstBrace, lastBrace - firstBrace + 1).Trim();
            }

            if (firstBracket >= 0 && lastBracket > firstBracket)
            {
                return text.Substring(firstBracket, lastBracket - firstBracket + 1).Trim();
            }

            return text;
        }

        /// <summary>
        /// Parsea cualquier estructura JSON de entrada:
        /// 1. Paquete compilado de ARCA Review (`{ "schema_version": "2.0-revit-schema", "actions": [...], "schema_tree": {...} }`)
        /// 2. Árbol de Esquema Jerárquico (`{ "name": "...", "children": [...] }` con `revitAction` en los nodos)
        /// 3. Array plano de acciones (`[ { "action": "...", "category": "...", ... } ]`)
        /// 4. Objeto de acción individual (`{ "action": "...", ... }`)
        /// Soporta directamente entradas pegadas desde bloques de Markdown o chats de IA.
        /// </summary>
        public static List<DirectAiActionItem> ParseActionsJson(string rawJson, out string detectedSchemaTitle)
        {
            detectedSchemaTitle = "Esquema de Acciones Revit";
            if (string.IsNullOrWhiteSpace(rawJson))
                throw new ArgumentException("El texto JSON está vacío.");

            string clean = ExtractJsonFromMarkdown(rawJson);
            if (string.IsNullOrWhiteSpace(clean))
                clean = rawJson.Trim();

            var token = JToken.Parse(clean);
            var list = new List<DirectAiActionItem>();

            if (token is JArray arr)
            {
                int order = 1;
                foreach (var itemToken in arr)
                {
                    var parsedItem = ParseSingleOrNodeToken(itemToken, "Proceso Directo", "Raíz", ref order);
                    if (parsedItem != null)
                        list.AddRange(parsedItem);
                }
            }
            else if (token is JObject obj)
            {
                if (obj["schema_title"] != null)
                    detectedSchemaTitle = obj["schema_title"].ToString();
                else if (obj["process_name"] != null)
                    detectedSchemaTitle = obj["process_name"].ToString();
                else if (obj["name"] != null)
                    detectedSchemaTitle = obj["name"].ToString();

                // Case A: Object with `actions` or `steps` array
                var actionsToken = obj["actions"] ?? obj["steps"];
                if (actionsToken is JArray actionsArray && actionsArray.Count > 0)
                {
                    int order = 1;
                    foreach (var actToken in actionsArray)
                    {
                        var parsed = ParseSingleOrNodeToken(actToken, detectedSchemaTitle, detectedSchemaTitle, ref order);
                        if (parsed != null)
                            list.AddRange(parsed);
                    }
                }
                // Case B: Hierarchical Schema Tree with `children` or `schema_tree`
                else if (obj["children"] is JArray || obj["schema_tree"] is JObject)
                {
                    var rootTree = (obj["schema_tree"] as JObject) ?? obj;
                    int order = 1;
                    TraverseSchemaTreeForActions(rootTree, new List<string>(), "", list, ref order);
                }
                // Case C: Single flat action object
                else
                {
                    int order = 1;
                    var single = ParseSingleOrNodeToken(obj, "Acción Directa", detectedSchemaTitle, ref order);
                    if (single != null)
                        list.AddRange(single);
                }
            }
            else
            {
                throw new InvalidOperationException("El JSON debe ser un objeto { ... } o una lista [ ... ].");
            }

            return list.OrderBy(a => a.Order).ToList();
        }

        public static List<DirectAiActionItem> ParseActionsJson(string rawJson)
        {
            return ParseActionsJson(rawJson, out _);
        }

        private static void TraverseSchemaTreeForActions(
            JObject node,
            List<string> currentPath,
            string currentBranchName,
            List<DirectAiActionItem> accumulator,
            ref int orderCounter)
        {
            if (node == null) return;

            string nodeName = node["name"]?.ToString() ?? node["title"]?.ToString() ?? "Nodo";
            var nextPath = new List<string>(currentPath) { nodeName };
            string branchName = currentPath.Count == 1 ? nodeName : currentBranchName;

            JObject blockRevitAction = null;
            if (node["blocks"] is JArray blocksArr)
            {
                foreach (var b in blocksArr.OfType<JObject>())
                {
                    if (b["revitAction"] is JObject bra)
                    {
                        blockRevitAction = bra;
                        break;
                    }
                }
            }

            bool isRevitActionNode =
                string.Equals(node["blockType"]?.ToString(), "revit_action", StringComparison.OrdinalIgnoreCase) ||
                node["revitAction"] != null ||
                blockRevitAction != null ||
                node["action"] != null ||
                node["tool"] != null;

            if (isRevitActionNode)
            {
                var raToken = (node["revitAction"] as JObject) ?? blockRevitAction ?? node;
                var item = CreateActionItemFromToken(
                    raToken,
                    node,
                    string.IsNullOrWhiteSpace(branchName) ? "Proceso Principal" : branchName,
                    string.Join(" › ", nextPath),
                    ref orderCounter
                );
                if (item != null)
                {
                    accumulator.Add(item);
                }
            }

            if (node["children"] is JArray childrenArr)
            {
                foreach (var child in childrenArr.OfType<JObject>())
                {
                    TraverseSchemaTreeForActions(child, nextPath, branchName, accumulator, ref orderCounter);
                }
            }
        }

        private static List<DirectAiActionItem> ParseSingleOrNodeToken(
            JToken token,
            string defaultBranch,
            string defaultPath,
            ref int orderCounter)
        {
            var results = new List<DirectAiActionItem>();
            if (!(token is JObject obj)) return results;

            // If this item is itself a tree branch with children
            if (obj["children"] is JArray children && children.Count > 0 && obj["revitAction"] == null && obj["action"] == null && obj["tool"] == null)
            {
                TraverseSchemaTreeForActions(obj, new List<string>(), obj["name"]?.ToString() ?? defaultBranch, results, ref orderCounter);
                return results;
            }

            var raToken = (obj["revitAction"] as JObject) ?? obj;
            var item = CreateActionItemFromToken(raToken, obj, defaultBranch, defaultPath, ref orderCounter);
            if (item != null)
                results.Add(item);

            return results;
        }

        private static DirectAiActionItem CreateActionItemFromToken(
            JObject raToken,
            JObject outerNode,
            string defaultBranch,
            string defaultPath,
            ref int orderCounter)
        {
            if (raToken == null) return null;

            string tool = raToken["tool"]?.ToString()
                ?? raToken["action"]?.ToString()
                ?? outerNode?["tool"]?.ToString()
                ?? outerNode?["action"]?.ToString()
                ?? "modify_parameters";

            string category = raToken["category"]?.ToString()
                ?? outerNode?["category"]?.ToString()
                ?? "Walls";

            string stepName = outerNode?["step_name"]?.ToString()
                ?? outerNode?["name"]?.ToString()
                ?? outerNode?["title"]?.ToString()
                ?? $"{tool} → {category}";

            string description = outerNode?["description"]?.ToString()
                ?? raToken["description"]?.ToString()
                ?? "";

            string processBranch = outerNode?["process"]?.ToString()
                ?? raToken["process"]?.ToString()
                ?? defaultBranch;

            string phase = raToken["phase"]?.ToString()
                ?? outerNode?["phase"]?.ToString()
                ?? "Fase 1: Ejecución BIM";

            string path = outerNode?["path"]?.ToString()
                ?? defaultPath;

            int order = raToken["order"] != null
                ? raToken["order"].Value<int>()
                : (outerNode?["order"] != null ? outerNode["order"].Value<int>() : orderCounter++);

            bool enabled = raToken["enabled"] != null
                ? raToken["enabled"].Value<bool>()
                : (outerNode?["enabled"] != null ? outerNode["enabled"].Value<bool>() : true);

            var filterObj = new DirectAiFilter();
            var fTok = (raToken["filter"] as JObject) ?? (raToken["filters"] as JObject);
            if (fTok != null)
            {
                string CleanWildcard(string val) => string.IsNullOrWhiteSpace(val) || val.Trim() == "*" ? "" : val.Trim();
                filterObj.TypeName = CleanWildcard(fTok["type_name"]?.ToString());
                filterObj.FamilyName = CleanWildcard(fTok["family_name"]?.ToString());
                filterObj.LevelName = CleanWildcard(fTok["level_name"]?.ToString());
                filterObj.SearchText = CleanWildcard(fTok["search_text"]?.ToString());
            }

            var elementIds = new List<long>();
            if (raToken["element_ids"] is JArray idsArr)
            {
                foreach (var idVal in idsArr)
                {
                    if (long.TryParse(idVal.ToString(), out long parsedId))
                        elementIds.Add(parsedId);
                }
            }

            var parameters = new Dictionary<string, string>();
            if (raToken["parameters"] is JObject pTok)
            {
                foreach (var prop in pTok.Properties())
                {
                    parameters[prop.Name] = prop.Value?.ToString() ?? "";
                }
            }

            string operation = raToken["operation"]?.ToString()
                ?? outerNode?["operation"]?.ToString()
                ?? "UPDATE";

            string instruction = raToken["instruction"]?.ToString()
                ?? outerNode?["instruction"]?.ToString()
                ?? "";

            string targetScheduleId = raToken["target_schedule_id"]?.ToString()
                ?? outerNode?["target_schedule_id"]?.ToString()
                ?? "";

            string targetScheduleName = raToken["target_schedule_name"]?.ToString()
                ?? outerNode?["target_schedule_name"]?.ToString()
                ?? "";

            string iterationMode = raToken["iteration_mode"]?.ToString()
                ?? outerNode?["iteration_mode"]?.ToString()
                ?? "ALL_ELEMENTS";

            string namingColumn = raToken["naming_column"]?.ToString()
                ?? outerNode?["naming_column"]?.ToString()
                ?? "";

            string groupingColumn = raToken["grouping_column"]?.ToString()
                ?? outerNode?["grouping_column"]?.ToString()
                ?? "";

            if (string.IsNullOrWhiteSpace(description) && !string.IsNullOrWhiteSpace(instruction))
            {
                description = instruction;
            }

            return new DirectAiActionItem
            {
                Id = outerNode?["id"]?.ToString() ?? Guid.NewGuid().ToString("N").Substring(0, 8),
                Order = order,
                StepName = stepName,
                ProcessBranch = processBranch,
                Phase = phase,
                SchemaPath = path,
                Description = description,
                Action = tool,
                Tool = tool,
                Category = category,
                Operation = operation,
                Instruction = instruction,
                IterationMode = iterationMode,
                NamingColumn = namingColumn,
                GroupingColumn = groupingColumn,
                TargetScheduleId = targetScheduleId,
                TargetScheduleName = targetScheduleName,
                Filter = filterObj,
                ElementIds = elementIds,
                Parameters = parameters,
                IsSelected = enabled
            };
        }

        /// <summary>
        /// Simula las acciones sobre el documento de Revit activo sin abrir transacción (solo lectura)
        /// para mostrar cuántos elementos coinciden y qué cambios se harán.
        /// </summary>
        public static void SimulateActions(Document doc, List<DirectAiActionItem> actions)
        {
            if (doc == null || actions == null) return;

            foreach (var item in actions)
            {
                try
                {
                    string tool = (item.EffectiveTool ?? "modify_parameters").ToLowerInvariant();

                    if (tool == "create_schedule")
                    {
                        item.MatchedRevitIds = new List<ElementId>();
                        item.MatchedCount = 1;
                        string schedName = item.Parameters != null && item.Parameters.TryGetValue("ScheduleName", out var sn)
                            ? sn
                            : "ARCA_SCHEDULE";
                        item.SimulationStatus = $"✅ Creará Tabla '{schedName}'";
                        continue;
                    }

                    if (tool == "duplicate_schedule")
                    {
                        string sName = item.Parameters != null && item.Parameters.TryGetValue("SourceScheduleName", out var src)
                            ? src
                            : (item.TargetScheduleName ?? "Tabla");
                        item.MatchedCount = 1;
                        item.SimulationStatus = $"✅ Duplicará Tabla '{sName}'";
                        continue;
                    }

                    if (tool == "create_assemblies" || tool == "create_assembly")
                    {
                        var matchedElem = ResolveTargetElements(doc, item);
                        item.MatchedRevitIds = matchedElem.Select(e => e.Id).ToList();
                        item.MatchedCount = item.MatchedRevitIds.Count;
                        int alreadyInAssembly = matchedElem.Count(e => e.AssemblyInstanceId != ElementId.InvalidElementId);
                        int available = item.MatchedCount - alreadyInAssembly;
                        item.SimulationStatus = available > 0
                            ? $"✅ Creará Ensamblajes ({available} elem. disponibles)"
                            : (item.MatchedCount > 0 ? "ℹ️ Elementos ya en ensamblajes" : "⚠️ 0 elementos para ensamblar");
                        continue;
                    }

                    if (tool == "create_assembly_views")
                    {
                        BuiltInCategory? bic = ResolveBuiltInCategory(item.Category);
                        int assemblyCount = 0;
                        try
                        {
                            var assColl = new FilteredElementCollector(doc)
                                .OfClass(typeof(AssemblyInstance))
                                .Cast<AssemblyInstance>();
                            if (bic.HasValue)
                            {
                                var cId = new ElementId(bic.Value);
                                assemblyCount = assColl.Count(a => a.NamingCategoryId == cId || a.GetMemberIds().Any(m => doc.GetElement(m)?.Category?.Id == cId));
                            }
                            else
                            {
                                assemblyCount = assColl.Count();
                            }
                        }
                        catch { }

                        item.MatchedCount = Math.Max(1, assemblyCount);
                        item.SimulationStatus = assemblyCount > 0
                            ? $"✅ Generará Vistas/Tablas ({assemblyCount} ensamblajes)"
                            : "⚡ Generará Vistas en ensamblajes de la cadena";
                        continue;
                    }

                    if (tool == "sync_materials")
                    {
                        var matchedMat = ResolveTargetElements(doc, item);
                        item.MatchedRevitIds = matchedMat.Select(e => e.Id).ToList();
                        item.MatchedCount = Math.Max(1, item.MatchedRevitIds.Count);
                        string matName = item.Parameters != null && item.Parameters.TryGetValue("MaterialName", out var mn)
                            ? mn
                            : "ARCA_Material";
                        item.SimulationStatus = $"✅ Material '{matName}' ({item.MatchedRevitIds.Count} elem.)";
                        continue;
                    }

                    var matchedElements = ResolveTargetElements(doc, item);
                    item.MatchedRevitIds = matchedElements.Select(e => e.Id).ToList();
                    item.MatchedCount = item.MatchedRevitIds.Count;

                    if (item.MatchedCount == 0 && tool == "manage_sheets")
                    {
                        item.MatchedCount = 1;
                        item.SimulationStatus = "✅ Creará / Actualizará Lámina";
                    }
                    else if (item.MatchedCount == 0)
                    {
                        item.SimulationStatus = "⚠️ 0 elementos coinciden";
                    }
                    else
                    {
                        item.SimulationStatus = $"✅ Listo ({item.MatchedCount} elem.)";
                    }
                }
                catch (Exception ex)
                {
                    item.MatchedCount = 0;
                    item.SimulationStatus = $"❌ Error: {ex.Message}";
                }
            }
        }

        /// <summary>
        /// Ejecuta las acciones seleccionadas dentro de una única Transacción de Revit.
        /// </summary>
        public static (int updatedElements, int updatedParams, List<string> logs) ExecuteActions(
            Document doc,
            List<DirectAiActionItem> actions)
        {
            int totalElementsUpdated = 0;
            int totalParamsUpdated = 0;
            var logs = new List<string>();

            if (doc == null || actions == null || actions.Count == 0)
                return (0, 0, logs);

            using (var tx = new Transaction(doc, "ARCA Review - Ejecutar Esquema de Acciones Revit"))
            {
                tx.Start();

                foreach (var item in actions.Where(a => a.IsSelected).OrderBy(a => a.Order))
                {
                    string tool = (item.EffectiveTool ?? "modify_parameters").ToLowerInvariant();
                    logs.Add($"▶ Paso #{item.Order} [{item.ProcessBranch}] {item.StepName} ({tool} / {item.Category})");

                    try
                    {
                        if (tool == "create_schedule")
                        {
                            if (ExecuteCreateScheduleAction(doc, item, logs))
                            {
                                totalElementsUpdated++;
                                totalParamsUpdated++;
                                item.SimulationStatus = "🎉 Tabla Creada";
                            }
                            continue;
                        }

                        if (tool == "duplicate_schedule")
                        {
                            if (ExecuteDuplicateScheduleAction(doc, item, logs))
                            {
                                totalElementsUpdated++;
                                totalParamsUpdated++;
                                item.SimulationStatus = "🎉 Tabla Duplicada";
                            }
                            continue;
                        }

                        if (tool == "create_assemblies" || tool == "create_assembly")
                        {
                            if (ExecuteCreateAssembliesAction(doc, item, logs, out int createdAss))
                            {
                                totalElementsUpdated += createdAss;
                                totalParamsUpdated += createdAss;
                                item.SimulationStatus = $"🎉 {createdAss} Ensamblajes Creados";
                            }
                            continue;
                        }

                        if (tool == "create_assembly_views")
                        {
                            if (ExecuteCreateAssemblyViewsAction(doc, item, logs, out int createdViews))
                            {
                                totalElementsUpdated += createdViews;
                                totalParamsUpdated += createdViews;
                                item.SimulationStatus = $"🎉 {createdViews} Vistas Creadas";
                            }
                            continue;
                        }

                        if (tool == "sync_materials")
                        {
                            int matApplied = ExecuteSyncMaterialAction(doc, item, logs);
                            totalElementsUpdated += Math.Max(1, matApplied);
                            totalParamsUpdated += Math.Max(1, matApplied);
                            item.SimulationStatus = $"🎉 Material Sincronizado ({matApplied})";
                            continue;
                        }

                        var elements = ResolveTargetElements(doc, item);

                        // Si es manage_sheets y no encontró lámina existente, puede crear una nueva
                        if (elements.Count == 0 && tool == "manage_sheets")
                        {
                            if (ExecuteCreateSheetIfMissing(doc, item, logs))
                            {
                                totalElementsUpdated++;
                                totalParamsUpdated++;
                                item.SimulationStatus = "🎉 Lámina Creada";
                            }
                            continue;
                        }

                        int itemElemUpdated = 0;

                        foreach (var el in elements)
                        {
                            bool elModified = false;

                            if (tool == "assign_type" || tool == "change_type")
                            {
                                if (ExecuteAssignTypeOnElement(doc, el, item, logs))
                                {
                                    elModified = true;
                                    totalParamsUpdated++;
                                }
                            }
                            else if (tool == "audit_qaqc")
                            {
                                if (ExecuteAuditQaQcOnElement(doc, el, item, logs))
                                {
                                    elModified = true;
                                    totalParamsUpdated++;
                                }
                            }
                            else if (tool == "convert_walls_to_bricks" || tool == "convert_floors_to_tiles" || tool == "convert_steel_deck")
                            {
                                // Marca los elementos con el metadato constructivo de ARCA Review y aplica los parámetros adicionales
                                string stamp = $"ARCA_{tool.ToUpperInvariant()}: {JsonConvert.SerializeObject(item.Parameters)}";
                                if (SetElementOrTypeParameter(doc, el, "Comments", stamp, out _))
                                {
                                    elModified = true;
                                    totalParamsUpdated++;
                                }
                            }
                            else
                            {
                                // Default: modify_parameters / manage_sheets
                                if (item.Parameters != null)
                                {
                                    foreach (var kv in item.Parameters)
                                    {
                                        if (SetElementOrTypeParameter(doc, el, kv.Key, kv.Value, out string err))
                                        {
                                            elModified = true;
                                            totalParamsUpdated++;
                                        }
                                        else if (!string.IsNullOrEmpty(err))
                                        {
                                            logs.Add($"  [Elem {el.Id.Value}] '{kv.Key}': {err}");
                                        }
                                    }
                                }
                            }

                            if (elModified)
                                itemElemUpdated++;
                        }

                        totalElementsUpdated += itemElemUpdated;
                        item.SimulationStatus = $"🎉 Aplicado ({itemElemUpdated}/{elements.Count})";
                    }
                    catch (Exception ex)
                    {
                        item.SimulationStatus = $"❌ Error: {ex.Message}";
                        logs.Add($"  ❌ Error en Paso #{item.Order}: {ex.Message}");
                    }
                }

                tx.Commit();
            }

            return (totalElementsUpdated, totalParamsUpdated, logs);
        }

        private static bool ExecuteAssignTypeOnElement(
            Document doc,
            Element el,
            DirectAiActionItem item,
            List<string> logs)
        {
            if (item.Parameters == null) return false;
            string targetTypeName = "";
            if (item.Parameters.TryGetValue("TargetTypeName", out var t1)) targetTypeName = t1;
            else if (item.Parameters.TryGetValue("Type", out var t2)) targetTypeName = t2;
            else if (item.Parameters.Values.Count > 0) targetTypeName = item.Parameters.Values.First();

            if (string.IsNullOrWhiteSpace(targetTypeName)) return false;

            var validTypeIds = el.GetValidTypes();
            foreach (var typeId in validTypeIds)
            {
                var typeElem = doc.GetElement(typeId);
                if (typeElem != null && typeElem.Name.IndexOf(targetTypeName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    el.ChangeTypeId(typeId);
                    return true;
                }
            }

            logs.Add($"  [Elem {el.Id.Value}] No se encontró un tipo compatible que contenga '{targetTypeName}'.");
            return false;
        }

        private static bool ExecuteAuditQaQcOnElement(
            Document doc,
            Element el,
            DirectAiActionItem item,
            List<string> logs)
        {
            string reqStr = "Comments,Mark";
            if (item.Parameters != null && item.Parameters.TryGetValue("RequiredParameters", out var r))
                reqStr = r;

            string stampPass = item.Parameters != null && item.Parameters.TryGetValue("StampOnPass", out var sp)
                ? sp
                : "QAQC_APROBADO";
            string stampFail = item.Parameters != null && item.Parameters.TryGetValue("StampOnFail", out var sf)
                ? sf
                : "QAQC_INCOMPLETO";

            var reqParams = reqStr.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Select(s => s.Trim())
                                  .Where(s => !string.IsNullOrEmpty(s))
                                  .ToList();

            var missing = new List<string>();
            foreach (var pName in reqParams)
            {
                if (string.Equals(pName, "Comments", StringComparison.OrdinalIgnoreCase))
                    continue; // Avoid failing on the stamp target itself if auditing others
                var p = el.LookupParameter(pName);
                string val = p != null ? (p.AsString() ?? p.AsValueString() ?? "") : "";
                if (string.IsNullOrWhiteSpace(val))
                    missing.Add(pName);
            }

            string finalStamp = missing.Count == 0
                ? stampPass
                : $"{stampFail} (Falta: {string.Join(", ", missing)})";

            return SetElementOrTypeParameter(doc, el, "Comments", finalStamp, out _);
        }

        private static bool ExecuteCreateAssembliesAction(
            Document doc,
            DirectAiActionItem item,
            List<string> logs,
            out int createdAssembliesCount)
        {
            createdAssembliesCount = 0;
            var elements = ResolveTargetElements(doc, item);
            if (elements.Count == 0)
            {
                logs.Add($"  [Paso #{item.Order}] No se encontraron elementos de categoría '{item.Category}' para ensamblar.");
                return false;
            }

            // Opcional: Si se especificó ScheduleName en parámetros y no existe aún la tabla, la creamos
            if (item.Parameters != null && item.Parameters.TryGetValue("ScheduleName", out var sn) && !string.IsNullOrWhiteSpace(sn))
            {
                var existingSched = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .FirstOrDefault(vs => vs.Name.Equals(sn, StringComparison.OrdinalIgnoreCase));

                if (existingSched == null)
                {
                    ExecuteCreateScheduleAction(doc, item, logs);
                }
            }

            // Determinar categoría para el ensamblaje
            BuiltInCategory? bic = ResolveBuiltInCategory(item.Category);
            ElementId namingCatId = bic.HasValue ? new ElementId(bic.Value) : (elements.First().Category?.Id ?? ElementId.InvalidElementId);
            if (namingCatId == ElementId.InvalidElementId)
            {
                logs.Add($"  [Paso #{item.Order}] Categoría '{item.Category}' no válida como categoría de ensamblaje.");
                return false;
            }

            // Filtrar elementos que NO pertenezcan ya a un ensamblaje
            var availableElements = elements.Where(e => e.AssemblyInstanceId == ElementId.InvalidElementId).ToList();
            if (availableElements.Count == 0)
            {
                logs.Add($"  [Paso #{item.Order}] Todos los {elements.Count} elementos de {item.Category} ya pertenecen a un ensamblaje.");
                return true;
            }

            // Determinar agrupación: ByInstance o ByType
            bool groupByInstance = string.Equals(item.IterationMode, "BY_INSTANCE", StringComparison.OrdinalIgnoreCase);
            if (item.Parameters != null && item.Parameters.TryGetValue("GroupBy", out var gb))
            {
                if (string.Equals(gb, "Instance", StringComparison.OrdinalIgnoreCase))
                    groupByInstance = true;
            }

            var groups = groupByInstance
                ? availableElements.Select(e => new List<Element> { e }).ToList()
                : availableElements.GroupBy(e => e.GetTypeId()).Select(g => g.ToList()).ToList();

            foreach (var group in groups)
            {
                try
                {
                    var memberIds = group.Select(e => e.Id).ToList();
                    var assembly = AssemblyInstance.Create(doc, memberIds, namingCatId);
                    if (assembly != null)
                    {
                        createdAssembliesCount++;
                        string typeName = GetElementTypeName(doc, group.First());
                        string famName = GetElementFamilyName(doc, group.First());
                        string desiredName = !string.IsNullOrWhiteSpace(famName) && !string.IsNullOrWhiteSpace(typeName)
                            ? $"{famName} - {typeName}"
                            : (typeName ?? "Ensamblaje");

                        try
                        {
                            assembly.AssemblyTypeName = desiredName;
                        }
                        catch { }

                        // Aplicar parámetros si aplica
                        if (item.Parameters != null)
                        {
                            foreach (var kv in item.Parameters)
                            {
                                if (kv.Key.Equals("Fields", StringComparison.OrdinalIgnoreCase) ||
                                    kv.Key.Equals("ScheduleName", StringComparison.OrdinalIgnoreCase) ||
                                    kv.Key.Equals("TargetCategory", StringComparison.OrdinalIgnoreCase) ||
                                    kv.Key.Equals("GroupBy", StringComparison.OrdinalIgnoreCase))
                                    continue;

                                SetElementOrTypeParameter(doc, assembly, kv.Key, kv.Value, out _);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logs.Add($"  ⚠️ Error al ensamblar grupo: {ex.Message}");
                }
            }

            try
            {
                doc.Regenerate();
            }
            catch { }

            logs.Add($"  ✅ Creados {createdAssembliesCount} ensamblaje(s) para {item.Category} ({availableElements.Count} elementos empaquetados).");
            return createdAssembliesCount > 0;
        }

        private static bool ExecuteCreateAssemblyViewsAction(
            Document doc,
            DirectAiActionItem item,
            List<string> logs,
            out int createdViewsCount)
        {
            createdViewsCount = 0;
            BuiltInCategory? bic = ResolveBuiltInCategory(item.Category);
            ElementId catId = bic.HasValue ? new ElementId(bic.Value) : ElementId.InvalidElementId;

            // Buscar ensamblajes relevantes
            var allAssemblies = new FilteredElementCollector(doc)
                .OfClass(typeof(AssemblyInstance))
                .Cast<AssemblyInstance>()
                .ToList();

            List<AssemblyInstance> targetAssemblies;
            if (bic.HasValue)
            {
                targetAssemblies = allAssemblies.Where(a =>
                {
                    if (a.NamingCategoryId == catId) return true;
                    return a.GetMemberIds().Any(mId => doc.GetElement(mId)?.Category?.Id == catId);
                }).ToList();
            }
            else
            {
                targetAssemblies = allAssemblies;
            }

            if (targetAssemblies.Count == 0)
            {
                logs.Add($"  [Paso #{item.Order}] No se encontraron ensamblajes de categoría '{item.Category}' para generar vistas.");
                return false;
            }

            string schedName = "Despiece y Cómputo";
            string fieldsCsv = "Family, Type, Recuento, Nivel";
            if (item.Parameters != null)
            {
                if (item.Parameters.TryGetValue("ScheduleName", out var sn) && !string.IsNullOrWhiteSpace(sn))
                    schedName = sn;
                if (item.Parameters.TryGetValue("Fields", out var fl) && !string.IsNullOrWhiteSpace(fl))
                    fieldsCsv = fl;
            }

            var requestedFields = fieldsCsv
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(f => f.Trim())
                .Where(f => !string.IsNullOrEmpty(f))
                .ToList();

            foreach (var assembly in targetAssemblies)
            {
                if (!assembly.AllowsAssemblyViewCreation())
                    continue;

                string aName = assembly.AssemblyTypeName ?? assembly.Name;

                // 1. Crear Tabla de Ensamblaje
                try
                {
                    ViewSchedule? newSched = null;
                    if (catId != ElementId.InvalidElementId)
                    {
                        try
                        {
                            newSched = AssemblyViewUtils.CreateSingleCategorySchedule(doc, assembly.Id, catId);
                        }
                        catch
                        {
                            newSched = AssemblyViewUtils.CreatePartList(doc, assembly.Id);
                        }
                    }
                    else
                    {
                        newSched = AssemblyViewUtils.CreatePartList(doc, assembly.Id);
                    }

                    if (newSched != null)
                    {
                        try
                        {
                            newSched.Name = $"{aName} - {schedName}";
                        }
                        catch
                        {
                            newSched.Name = $"{aName} - {schedName}_{DateTime.Now:HHmmss}";
                        }

                        var def = newSched.Definition;
                        var schedFields = def.GetSchedulableFields();
                        foreach (var rf in requestedFields)
                        {
                            var found = schedFields.FirstOrDefault(sf =>
                                sf.GetName(doc).Equals(rf, StringComparison.OrdinalIgnoreCase) ||
                                sf.GetName(doc).IndexOf(rf, StringComparison.OrdinalIgnoreCase) >= 0);
                            if (found != null)
                            {
                                try { def.AddField(found); } catch { }
                            }
                        }
                        createdViewsCount++;
                    }
                }
                catch (Exception ex)
                {
                    logs.Add($"  ⚠️ Tabla de ensamblaje para '{aName}': {ex.Message}");
                }

                // 2. Crear Vista 3D Isométrica
                try
                {
                    var v3d = AssemblyViewUtils.Create3DOrthographic(doc, assembly.Id);
                    if (v3d != null)
                    {
                        try { v3d.Name = $"{aName} - Vista 3D"; } catch { }
                        createdViewsCount++;
                    }
                }
                catch { }

                // 3. Crear Vista de Planta de Detalle
                try
                {
                    var vPlan = AssemblyViewUtils.CreateDetailSection(doc, assembly.Id, AssemblyDetailViewOrientation.HorizontalDetail);
                    if (vPlan != null)
                    {
                        try { vPlan.Name = $"{aName} - Planta"; } catch { }
                        createdViewsCount++;
                    }
                }
                catch { }

                // 4. Crear Vista de Elevación Frontal
                try
                {
                    var vElev = AssemblyViewUtils.CreateDetailSection(doc, assembly.Id, AssemblyDetailViewOrientation.ElevationFront);
                    if (vElev != null)
                    {
                        try { vElev.Name = $"{aName} - Elevación Frontal"; } catch { }
                        createdViewsCount++;
                    }
                }
                catch { }
            }

            logs.Add($"  ✅ Generadas {createdViewsCount} vistas/tablas para {targetAssemblies.Count} ensamblaje(s) de {item.Category}.");
            return createdViewsCount > 0;
        }

        private static bool ExecuteDuplicateScheduleAction(
            Document doc,
            DirectAiActionItem item,
            List<string> logs)
        {
            string sourceName = "";
            string newName = "Tabla Duplicada";
            if (item.Parameters != null)
            {
                if (item.Parameters.TryGetValue("SourceScheduleName", out var s1)) sourceName = s1;
                else if (item.Parameters.TryGetValue("TargetScheduleName", out var s2)) sourceName = s2;
                else if (!string.IsNullOrWhiteSpace(item.TargetScheduleName)) sourceName = item.TargetScheduleName;

                if (item.Parameters.TryGetValue("ScheduleName", out var n1)) newName = n1;
                else if (item.Parameters.TryGetValue("NewScheduleName", out var n2)) newName = n2;
            }

            ViewSchedule? srcSchedule = null;
            var allSchedules = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(vs => !vs.IsTemplate)
                .ToList();

            if (!string.IsNullOrWhiteSpace(sourceName))
            {
                srcSchedule = allSchedules.FirstOrDefault(vs => vs.Name.IndexOf(sourceName, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            if (srcSchedule == null)
            {
                BuiltInCategory? bic = ResolveBuiltInCategory(item.Category);
                if (bic.HasValue)
                {
                    var catId = new ElementId(bic.Value);
                    srcSchedule = allSchedules.FirstOrDefault(vs => vs.Definition?.CategoryId == catId);
                }
            }

            if (srcSchedule == null)
            {
                logs.Add($"  [Paso #{item.Order}] No se encontró la tabla de planificación '{sourceName}' para duplicar.");
                return false;
            }

            try
            {
                ElementId dupId = srcSchedule.Duplicate(ViewDuplicateOption.Duplicate);
                var dupSchedule = doc.GetElement(dupId) as ViewSchedule;
                if (dupSchedule != null)
                {
                    try
                    {
                        dupSchedule.Name = newName;
                    }
                    catch
                    {
                        dupSchedule.Name = $"{newName}_{DateTime.Now:HHmmss}";
                    }

                    if (item.Parameters != null && item.Parameters.TryGetValue("Fields", out var fieldsCsv) && !string.IsNullOrWhiteSpace(fieldsCsv))
                    {
                        var def = dupSchedule.Definition;
                        var schedFields = def.GetSchedulableFields();
                        var reqFields = fieldsCsv.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(f => f.Trim());
                        foreach (var rf in reqFields)
                        {
                            var f = schedFields.FirstOrDefault(sf => sf.GetName(doc).IndexOf(rf, StringComparison.OrdinalIgnoreCase) >= 0);
                            if (f != null)
                            {
                                try { def.AddField(f); } catch { }
                            }
                        }
                    }

                    logs.Add($"  ✅ Tabla '{srcSchedule.Name}' duplicada como '{dupSchedule.Name}'.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                logs.Add($"  ❌ Error al duplicar tabla '{srcSchedule.Name}': {ex.Message}");
            }

            return false;
        }

        private static bool ExecuteCreateScheduleAction(
            Document doc,
            DirectAiActionItem item,
            List<string> logs)
        {
            string schedName = "ARCA_COMPUTO_BIM";
            string targetCatName = item.Category;
            string fieldsCsv = "Family and Type,Comments";

            if (item.Parameters != null)
            {
                if (item.Parameters.TryGetValue("ScheduleName", out var sn) && !string.IsNullOrWhiteSpace(sn))
                    schedName = sn;
                if (item.Parameters.TryGetValue("TargetCategory", out var tc) && !string.IsNullOrWhiteSpace(tc))
                    targetCatName = tc;
                if (item.Parameters.TryGetValue("Fields", out var fl) && !string.IsNullOrWhiteSpace(fl))
                    fieldsCsv = fl;
            }

            BuiltInCategory? bic = ResolveBuiltInCategory(targetCatName);
            if (!bic.HasValue || bic.Value == BuiltInCategory.OST_Schedules)
                bic = BuiltInCategory.OST_Walls;

            var catId = new ElementId(bic.Value);
            var schedule = ViewSchedule.CreateSchedule(doc, catId);
            try
            {
                schedule.Name = schedName;
            }
            catch
            {
                schedule.Name = $"{schedName}_{DateTime.Now:HHmmss}";
            }

            var requestedFields = fieldsCsv
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(f => f.Trim())
                .Where(f => !string.IsNullOrEmpty(f))
                .ToList();

            var definition = schedule.Definition;
            var schedulableFields = definition.GetSchedulableFields();

            foreach (var reqField in requestedFields)
            {
                var foundField = schedulableFields.FirstOrDefault(sf =>
                    sf.GetName(doc).Equals(reqField, StringComparison.OrdinalIgnoreCase) ||
                    sf.GetName(doc).IndexOf(reqField, StringComparison.OrdinalIgnoreCase) >= 0);

                if (foundField != null)
                {
                    try
                    {
                        definition.AddField(foundField);
                    }
                    catch { }
                }
            }

            logs.Add($"  ✅ Tabla de planificación creada: '{schedule.Name}'");
            return true;
        }

        private static int ExecuteSyncMaterialAction(
            Document doc,
            DirectAiActionItem item,
            List<string> logs)
        {
            string matName = "ARCA_Material_BIM";
            string colorHex = "#94A3B8";

            if (item.Parameters != null)
            {
                if (item.Parameters.TryGetValue("MaterialName", out var mn) && !string.IsNullOrWhiteSpace(mn))
                    matName = mn;
                if (item.Parameters.TryGetValue("ColorHex", out var ch) && !string.IsNullOrWhiteSpace(ch))
                    colorHex = ch;
            }

            Material targetMaterial = new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .FirstOrDefault(m => m.Name.Equals(matName, StringComparison.OrdinalIgnoreCase));

            if (targetMaterial == null)
            {
                ElementId newMatId = Material.Create(doc, matName);
                targetMaterial = doc.GetElement(newMatId) as Material;
            }

            if (targetMaterial != null)
            {
                try
                {
                    string cleanHex = colorHex.TrimStart('#');
                    if (cleanHex.Length == 6)
                    {
                        byte r = Convert.ToByte(cleanHex.Substring(0, 2), 16);
                        byte g = Convert.ToByte(cleanHex.Substring(2, 2), 16);
                        byte b = Convert.ToByte(cleanHex.Substring(4, 2), 16);
                        targetMaterial.Color = new Autodesk.Revit.DB.Color(r, g, b);
                    }
                }
                catch { }
            }

            var elements = ResolveTargetElements(doc, item);
            int appliedCount = 0;
            foreach (var el in elements)
            {
                if (SetElementOrTypeParameter(doc, el, "Comments", $"MATERIAL BIM: {matName}", out _))
                    appliedCount++;
            }

            logs.Add($"  ✅ Material '{matName}' sincronizado en Revit ({appliedCount} elementos etiquetados).");
            return appliedCount;
        }

        private static bool ExecuteCreateSheetIfMissing(
            Document doc,
            DirectAiActionItem item,
            List<string> logs)
        {
            var titleBlock = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .FirstElement();

            ElementId tbId = titleBlock != null ? titleBlock.Id : ElementId.InvalidElementId;
            var newSheet = ViewSheet.Create(doc, tbId);

            if (item.Parameters != null)
            {
                foreach (var kv in item.Parameters)
                {
                    SetElementOrTypeParameter(doc, newSheet, kv.Key, kv.Value, out _);
                }
            }

            logs.Add($"  ✅ Nueva lámina creada: {newSheet.SheetNumber} - {newSheet.Name}");
            return true;
        }

        private static List<Element> ResolveTargetElements(Document doc, DirectAiActionItem item)
        {
            var results = new List<Element>();

            // 1. Si hay IDs explícitos, usarlos directamente
            if (item.ElementIds != null && item.ElementIds.Count > 0)
            {
                foreach (long rawId in item.ElementIds)
                {
                    var el = doc.GetElement(new ElementId(rawId));
                    if (el != null)
                        results.Add(el);
                }
                return results;
            }

            // 2. Resolver por Categoría
            BuiltInCategory? bic = ResolveBuiltInCategory(item.Category);
            FilteredElementCollector collector = bic.HasValue
                ? new FilteredElementCollector(doc).OfCategory(bic.Value).WhereElementIsNotElementType()
                : new FilteredElementCollector(doc).WhereElementIsNotElementType();

            var candidates = collector.ToElements();
            var filter = item.Filter ?? new DirectAiFilter();

            foreach (var el in candidates)
            {
                if (el == null) continue;

                if (!string.IsNullOrWhiteSpace(filter.TypeName))
                {
                    string typeName = GetElementTypeName(doc, el);
                    if (typeName.IndexOf(filter.TypeName, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                }

                if (!string.IsNullOrWhiteSpace(filter.FamilyName))
                {
                    string famName = GetElementFamilyName(doc, el);
                    if (famName.IndexOf(filter.FamilyName, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                }

                if (!string.IsNullOrWhiteSpace(filter.LevelName))
                {
                    string lvlName = GetElementLevelName(doc, el);
                    if (lvlName.IndexOf(filter.LevelName, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                }

                if (!string.IsNullOrWhiteSpace(filter.SearchText))
                {
                    string combined = $"{el.Name} {GetElementTypeName(doc, el)} {GetElementFamilyName(doc, el)}";
                    if (el is ViewSheet vs)
                        combined += $" {vs.SheetNumber} {vs.Name}";
                    if (combined.IndexOf(filter.SearchText, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                }

                results.Add(el);
            }

            return results;
        }

        private static BuiltInCategory? ResolveBuiltInCategory(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName)) return null;
            string clean = categoryName.Trim().ToLowerInvariant();

            switch (clean)
            {
                case "walls":
                case "muros":
                case "ost_walls":
                    return BuiltInCategory.OST_Walls;
                case "floors":
                case "pisos":
                case "suelos":
                case "ost_floors":
                    return BuiltInCategory.OST_Floors;
                case "doors":
                case "puertas":
                case "ost_doors":
                    return BuiltInCategory.OST_Doors;
                case "windows":
                case "ventanas":
                case "ost_windows":
                    return BuiltInCategory.OST_Windows;
                case "rooms":
                case "habitaciones":
                case "ost_rooms":
                    return BuiltInCategory.OST_Rooms;
                case "ceilings":
                case "cielorrasos":
                case "ost_ceilings":
                    return BuiltInCategory.OST_Ceilings;
                case "roofs":
                case "cubiertas":
                case "techos":
                case "ost_roofs":
                    return BuiltInCategory.OST_Roofs;
                case "structuralcolumns":
                case "columns":
                case "columnas":
                case "ost_structuralcolumns":
                    return BuiltInCategory.OST_StructuralColumns;
                case "structuralframing":
                case "framing":
                case "vigas":
                case "ost_structuralframing":
                    return BuiltInCategory.OST_StructuralFraming;
                case "structuralfoundation":
                case "foundations":
                case "cimentaciones":
                case "ost_structuralfoundation":
                    return BuiltInCategory.OST_StructuralFoundation;
                case "plumbingfixtures":
                case "sanitarios":
                case "ost_plumbingfixtures":
                    return BuiltInCategory.OST_PlumbingFixtures;
                case "pipecurves":
                case "pipes":
                case "tuberias":
                case "ost_pipecurves":
                    return BuiltInCategory.OST_PipeCurves;
                case "ductcurves":
                case "ducts":
                case "ductos":
                case "ost_ductcurves":
                    return BuiltInCategory.OST_DuctCurves;
                case "electricalequipment":
                case "equipos electricos":
                case "ost_electricalequipment":
                    return BuiltInCategory.OST_ElectricalEquipment;
                case "lightingfixtures":
                case "luminarias":
                case "ost_lightingfixtures":
                    return BuiltInCategory.OST_LightingFixtures;
                case "furniture":
                case "mobiliario":
                case "ost_furniture":
                    return BuiltInCategory.OST_Furniture;
                case "genericmodel":
                case "modelos genericos":
                case "ost_genericmodel":
                    return BuiltInCategory.OST_GenericModel;
                case "sheets":
                case "laminas":
                case "planos":
                case "ost_sheets":
                    return BuiltInCategory.OST_Sheets;
                case "views":
                case "vistas":
                case "ost_views":
                    return BuiltInCategory.OST_Views;
                case "schedules":
                case "tablas":
                case "ost_schedules":
                    return BuiltInCategory.OST_Schedules;
                case "materials":
                case "materiales":
                case "ost_materials":
                    return BuiltInCategory.OST_Materials;
                default:
                    if (Enum.TryParse<BuiltInCategory>(categoryName, true, out var parsed))
                        return parsed;
                    if (Enum.TryParse<BuiltInCategory>("OST_" + categoryName, true, out var parsedOst))
                        return parsedOst;
                    return null;
            }
        }

        private static string GetElementTypeName(Document doc, Element el)
        {
            try
            {
                ElementId typeId = el.GetTypeId();
                if (typeId != ElementId.InvalidElementId)
                {
                    Element typeEl = doc.GetElement(typeId);
                    if (typeEl != null) return typeEl.Name ?? "";
                }
            }
            catch { }
            return el.Name ?? "";
        }

        private static string GetElementFamilyName(Document doc, Element el)
        {
            try
            {
                if (el is FamilyInstance fi && fi.Symbol?.Family != null)
                    return fi.Symbol.Family.Name ?? "";

                ElementId typeId = el.GetTypeId();
                if (typeId != ElementId.InvalidElementId && doc.GetElement(typeId) is ElementType et)
                    return et.FamilyName ?? "";
            }
            catch { }
            return "";
        }

        private static string GetElementLevelName(Document doc, Element el)
        {
            try
            {
                if (el.LevelId != null && el.LevelId != ElementId.InvalidElementId)
                {
                    var lvl = doc.GetElement(el.LevelId) as Level;
                    if (lvl != null) return lvl.Name ?? "";
                }

                var refLevelParam = el.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM)
                                 ?? el.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT)
                                 ?? el.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);
                if (refLevelParam != null && refLevelParam.StorageType == StorageType.ElementId)
                {
                    var lvl = doc.GetElement(refLevelParam.AsElementId()) as Level;
                    if (lvl != null) return lvl.Name ?? "";
                }
            }
            catch { }
            return "";
        }

        private static bool SetElementOrTypeParameter(
            Document doc,
            Element el,
            string paramName,
            string rawValue,
            out string errorMsg)
        {
            errorMsg = "";
            if (string.IsNullOrWhiteSpace(paramName)) return false;

            Parameter param = FindParameterFlexible(el, paramName);

            if (param == null)
            {
                ElementId typeId = el.GetTypeId();
                if (typeId != ElementId.InvalidElementId)
                {
                    Element typeEl = doc.GetElement(typeId);
                    if (typeEl != null)
                        param = FindParameterFlexible(typeEl, paramName);
                }
            }

            if (param == null)
            {
                errorMsg = "Parámetro no encontrado";
                return false;
            }

            if (param.IsReadOnly)
            {
                errorMsg = "Parámetro de solo lectura";
                return false;
            }

            try
            {
                switch (param.StorageType)
                {
                    case StorageType.String:
                        param.Set(rawValue ?? "");
                        return true;

                    case StorageType.Integer:
                        if (int.TryParse(rawValue, out int intVal))
                        {
                            param.Set(intVal);
                            return true;
                        }
                        if (bool.TryParse(rawValue, out bool boolVal))
                        {
                            param.Set(boolVal ? 1 : 0);
                            return true;
                        }
                        errorMsg = $"Valor entero inválido '{rawValue}'";
                        return false;

                    case StorageType.Double:
                        if (param.SetValueString(rawValue))
                            return true;
                        if (double.TryParse(rawValue, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double dblVal))
                        {
                            param.Set(dblVal);
                            return true;
                        }
                        errorMsg = $"Valor numérico inválido '{rawValue}'";
                        return false;

                    default:
                        errorMsg = "Tipo de parámetro no soportado directamente";
                        return false;
                }
            }
            catch (Exception ex)
            {
                errorMsg = ex.Message;
                return false;
            }
        }

        private static Parameter FindParameterFlexible(Element el, string paramName)
        {
            var direct = el.LookupParameter(paramName);
            if (direct != null) return direct;

            string norm = paramName.Trim().ToLowerInvariant();
            if (norm == "comments" || norm == "comentarios")
                return el.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            if (norm == "mark" || norm == "marca")
                return el.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
            if (norm == "type mark" || norm == "marca de tipo")
                return el.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK);
            if (norm == "fire rating" || norm == "clasificación de incendio" || norm == "resistencia al fuego")
                return el.get_Parameter(BuiltInParameter.FIRE_RATING);
            if (norm == "sheet name" || norm == "nombre de plano")
                return el.get_Parameter(BuiltInParameter.SHEET_NAME);
            if (norm == "sheet number" || norm == "número de plano" || norm == "numero de plano")
                return el.get_Parameter(BuiltInParameter.SHEET_NUMBER);

            foreach (Parameter p in el.Parameters)
            {
                if (p.Definition != null &&
                    string.Equals(p.Definition.Name, paramName, StringComparison.OrdinalIgnoreCase))
                {
                    return p;
                }
            }

            return null;
        }
    }
}
