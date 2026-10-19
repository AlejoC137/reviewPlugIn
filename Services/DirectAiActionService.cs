using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace reviewPlugIn.Services
{
    public class DirectAiActionItem : INotifyPropertyChanged
    {
        private bool _isSelected = true;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public long ElementId { get; set; }
        public string Category { get; set; } = "";
        public string ElementName { get; set; } = "";
        public string ParameterName { get; set; } = "";
        public string CurrentValue { get; set; } = "";
        public string TargetValue { get; set; } = "";
        public string Status { get; set; } = "Listo";
        public string? Warning { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public class DirectAiSimulationResult
    {
        public bool Success { get; set; }
        public string ActionType { get; set; } = "";
        public string Summary { get; set; } = "";
        public List<DirectAiActionItem> Items { get; set; } = new List<DirectAiActionItem>();
        public List<string> Logs { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();
    }

    public class DirectAiExecutionResult
    {
        public bool Success { get; set; }
        public int ModifiedCount { get; set; }
        public string Summary { get; set; } = "";
        public List<string> Logs { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();
    }

    public static class DirectAiActionService
    {
        public static BuiltInCategory? ResolveCategory(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName)) return null;
            return categoryName.Trim().ToLowerInvariant() switch
            {
                "walls" or "muros" or "paredes" => BuiltInCategory.OST_Walls,
                "floors" or "pisos" or "suelos" => BuiltInCategory.OST_Floors,
                "doors" or "puertas" => BuiltInCategory.OST_Doors,
                "windows" or "ventanas" => BuiltInCategory.OST_Windows,
                "rooms" or "habitaciones" or "espacios" => BuiltInCategory.OST_Rooms,
                "ceilings" or "cielorrasos" or "techos" => BuiltInCategory.OST_Ceilings,
                "roofs" or "cubiertas" => BuiltInCategory.OST_Roofs,
                "structuralcolumns" or "structural columns" or "columnas" or "pilares" => BuiltInCategory.OST_StructuralColumns,
                "structuralframing" or "structural framing" or "vigas" or "steeldeck" => BuiltInCategory.OST_StructuralFraming,
                "genericmodel" or "generic models" or "modelos genericos" => BuiltInCategory.OST_GenericModel,
                "plumbingfixtures" or "aparatos sanitarios" or "fontaneria" => BuiltInCategory.OST_PlumbingFixtures,
                "pipecurves" or "tuberias" => BuiltInCategory.OST_PipeCurves,
                "sheets" or "planos" or "hojas" => BuiltInCategory.OST_Sheets,
                "views" or "vistas" => BuiltInCategory.OST_Views,
                "titleblocks" or "title blocks" or "rotulos" => BuiltInCategory.OST_TitleBlocks,
                _ => null
            };
        }

        /// <summary>
        /// Simula la ejecución de una instrucción o lote JSON sin modificar el modelo de Revit.
        /// </summary>
        public static DirectAiSimulationResult Simulate(Document doc, string jsonText)
        {
            var result = new DirectAiSimulationResult();

            if (doc == null)
            {
                result.Errors.Add("No hay ningún documento activo en Revit.");
                return result;
            }

            if (string.IsNullOrWhiteSpace(jsonText))
            {
                result.Errors.Add("El JSON de instrucción está vacío.");
                return result;
            }

            string cleanJson = ExtractJson(jsonText);

            if (string.IsNullOrWhiteSpace(cleanJson) || (!cleanJson.StartsWith("{") && !cleanJson.StartsWith("[")))
            {
                if (jsonText.Trim().StartsWith("#") || jsonText.Contains("Walls (Muros)") || jsonText.Contains("- "))
                {
                    result.Errors.Add("El texto ingresado parece ser el texto del Prompt o una lista en Markdown, no un bloque JSON ejecutable.\n\n" +
                                      "👉 Instrucciones:\n" +
                                      "1. Pega el prompt copiado en el chat con Gemini.\n" +
                                      "2. Pídele lo que deseas (ej: 'Modifica los muros de ladrillo a color rojo').\n" +
                                      "3. Copia ÚNICAMENTE el bloque JSON devuelto por Gemini (o selecciona una plantilla de ejemplo en el desplegable superior).");
                    return result;
                }

                result.Errors.Add("No se pudo detectar un objeto JSON válido ({ ... } o [ ... ]) en el texto ingresado.");
                return result;
            }

            try
            {
                JToken rootToken = JToken.Parse(cleanJson);
                List<JObject> actionBlocks = new List<JObject>();

                if (rootToken is JArray arr)
                {
                    foreach (var item in arr)
                    {
                        if (item is JObject obj) actionBlocks.Add(obj);
                    }
                }
                else if (rootToken is JObject singleObj)
                {
                    actionBlocks.Add(singleObj);
                }
                else
                {
                    result.Errors.Add("El JSON debe ser un objeto {} o un arreglo de objetos [{}].");
                    return result;
                }

                if (actionBlocks.Count == 0)
                {
                    result.Errors.Add("No se encontraron bloques de acción válidos en el JSON.");
                    return result;
                }

                foreach (var actionObj in actionBlocks)
                {
                    SimulateSingleAction(doc, actionObj, result);
                }

                result.Success = result.Errors.Count == 0;
                result.Summary = $"Simulación completada: {result.Items.Count} parámetro(s) evaluados en elementos del modelo.";
            }
            catch (JsonReaderException jex)
            {
                result.Errors.Add($"Error de sintaxis JSON (Línea {jex.LineNumber}, Posición {jex.LinePosition}): {jex.Message}");
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Error analizando JSON: {ex.Message}");
            }

            return result;
        }

        private static void SimulateSingleAction(Document doc, JObject actionObj, DirectAiSimulationResult result)
        {
            // Extraer tipo de acción
            string action = actionObj["action"]?.ToString() ?? actionObj["tool"]?.ToString() ?? "modify_parameters";
            result.ActionType = action;

            // Extraer argumentos si vienen anidados como en MCP {"tool": "...", "arguments": {...}}
            JObject args = actionObj["arguments"] as JObject ?? actionObj;

            if (action.Contains("modify", StringComparison.OrdinalIgnoreCase) ||
                action.Contains("param", StringComparison.OrdinalIgnoreCase) ||
                action.Equals("revit_modify_parameters", StringComparison.OrdinalIgnoreCase))
            {
                SimulateModifyParameters(doc, args, result);
            }
            else if (action.Contains("sheet", StringComparison.OrdinalIgnoreCase) ||
                     action.Equals("revit_manage_sheets", StringComparison.OrdinalIgnoreCase))
            {
                SimulateManageSheets(doc, args, result);
            }
            else
            {
                // Acción genérica o desconocida: intentar procesar parámetros si existen
                if (args["parameters"] != null)
                {
                    SimulateModifyParameters(doc, args, result);
                }
                else
                {
                    result.Logs.Add($"ℹ️ Acción reconocida: '{action}'. Se procesará en la ejecución principal.");
                }
            }
        }

        private static void SimulateModifyParameters(Document doc, JObject args, DirectAiSimulationResult result)
        {
            var parametersObj = args["parameters"] as JObject;
            if (parametersObj == null || !parametersObj.Properties().Any())
            {
                result.Errors.Add("El bloque de modificación no especifica el objeto 'parameters' con los pares clave-valor a modificar.");
                return;
            }

            List<Element> targetElements = ResolveTargetElements(doc, args, result.Logs);

            if (targetElements.Count == 0)
            {
                result.Logs.Add("⚠️ No se encontraron elementos en el modelo que cumplan con los filtros especificados.");
                return;
            }

            result.Logs.Add($"✅ Elementos coincidentes encontrados: {targetElements.Count}");

            foreach (var elem in targetElements)
            {
                string elemName = elem.Name;
                string catName = elem.Category?.Name ?? "Sin Categoría";

                foreach (var prop in parametersObj.Properties())
                {
                    string paramName = prop.Name;
                    string targetVal = prop.Value?.ToString() ?? "";

                    Parameter? p = FindParameter(elem, paramName);
                    var item = new DirectAiActionItem
                    {
                        ElementId = elem.Id.Value,
                        Category = catName,
                        ElementName = elemName,
                        ParameterName = paramName,
                        TargetValue = targetVal,
                        IsSelected = true
                    };

                    if (p == null)
                    {
                        item.CurrentValue = "[No existe]";
                        item.Status = "Advertencia";
                        item.Warning = $"El parámetro '{paramName}' no existe en este elemento.";
                    }
                    else if (p.IsReadOnly)
                    {
                        item.CurrentValue = ParameterToString(p);
                        item.Status = "Bloqueado";
                        item.Warning = $"El parámetro '{paramName}' es de solo lectura en Revit.";
                        item.IsSelected = false;
                    }
                    else
                    {
                        item.CurrentValue = ParameterToString(p);
                        item.Status = "Listo";
                    }

                    result.Items.Add(item);
                }
            }
        }

        private static void SimulateManageSheets(Document doc, JObject args, DirectAiSimulationResult result)
        {
            string subAction = args["sub_action"]?.ToString() ?? args["action"]?.ToString() ?? "rename";
            string sheetNum = args["sheet_number"]?.ToString() ?? "";
            string newName = args["new_name"]?.ToString() ?? args["target_name"]?.ToString() ?? "";
            string newNum = args["new_number"]?.ToString() ?? args["target_number"]?.ToString() ?? "";

            var allSheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsTemplate)
                .ToList();

            if (!string.IsNullOrWhiteSpace(sheetNum))
            {
                var target = allSheets.FirstOrDefault(s => string.Equals(s.SheetNumber, sheetNum.Trim(), StringComparison.OrdinalIgnoreCase));
                if (target != null)
                {
                    if (!string.IsNullOrWhiteSpace(newName))
                    {
                        result.Items.Add(new DirectAiActionItem
                        {
                            ElementId = target.Id.Value,
                            Category = "Sheets",
                            ElementName = target.SheetNumber,
                            ParameterName = "Sheet Name",
                            CurrentValue = target.Name,
                            TargetValue = newName,
                            Status = "Listo",
                            IsSelected = true
                        });
                    }
                    if (!string.IsNullOrWhiteSpace(newNum))
                    {
                        result.Items.Add(new DirectAiActionItem
                        {
                            ElementId = target.Id.Value,
                            Category = "Sheets",
                            ElementName = target.SheetNumber,
                            ParameterName = "Sheet Number",
                            CurrentValue = target.SheetNumber,
                            TargetValue = newNum,
                            Status = "Listo",
                            IsSelected = true
                        });
                    }
                }
                else
                {
                    result.Errors.Add($"No se encontró la lámina con número '{sheetNum}' en el modelo.");
                }
            }
        }

        private static List<Element> ResolveTargetElements(Document doc, JObject args, List<string> logs)
        {
            var matched = new List<Element>();

            // 1. Por IDs explícitos
            var idsArray = args["element_ids"] as JArray;
            if (idsArray != null && idsArray.Count > 0)
            {
                foreach (var t in idsArray)
                {
                    if (long.TryParse(t.ToString(), out long parsedId))
                    {
                        Element? el = doc.GetElement(new ElementId(parsedId));
                        if (el != null) matched.Add(el);
                    }
                }
                return matched;
            }

            // 2. Por Categoría y Filtros
            string categoryName = args["category"]?.ToString() ?? args["filter_category"]?.ToString() ?? "";
            BuiltInCategory? bic = ResolveCategory(categoryName);

            FilteredElementCollector collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            if (bic.HasValue)
            {
                collector.OfCategory(bic.Value);
            }
            else if (!string.IsNullOrWhiteSpace(categoryName))
            {
                // Buscar categoría por nombre
                var foundCat = doc.Settings.Categories
                    .Cast<Category>()
                    .FirstOrDefault(c => c.Name.Equals(categoryName, StringComparison.OrdinalIgnoreCase));
                if (foundCat != null)
                {
                    collector.OfCategoryId(foundCat.Id);
                }
            }

            // Filtros opcionales
            JObject? filterObj = args["filter"] as JObject;
            string filterType = filterObj?["type_name"]?.ToString() ?? args["type_name"]?.ToString() ?? "";
            string filterFamily = filterObj?["family_name"]?.ToString() ?? args["family_name"]?.ToString() ?? "";
            string filterLevel = filterObj?["level_name"]?.ToString() ?? args["level_name"]?.ToString() ?? "";
            string filterContains = filterObj?["search_text"]?.ToString() ?? args["search_text"]?.ToString() ?? "";

            foreach (Element el in collector)
            {
                if (el.Category == null) continue;

                // Filtro por Tipo de Familia
                if (!string.IsNullOrWhiteSpace(filterType))
                {
                    ElementId typeId = el.GetTypeId();
                    if (typeId != ElementId.InvalidElementId)
                    {
                        Element? elType = doc.GetElement(typeId);
                        if (elType != null && !elType.Name.Contains(filterType, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }
                    else if (!el.Name.Contains(filterType, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                // Filtro por Nivel
                if (!string.IsNullOrWhiteSpace(filterLevel))
                {
                    string elLevel = GetElementLevelName(doc, el);
                    if (!elLevel.Contains(filterLevel, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                // Filtro por Texto libre en nombre o comentarios
                if (!string.IsNullOrWhiteSpace(filterContains))
                {
                    string comments = el.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? "";
                    if (!el.Name.Contains(filterContains, StringComparison.OrdinalIgnoreCase) &&
                        !comments.Contains(filterContains, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                matched.Add(el);
            }

            return matched;
        }

        /// <summary>
        /// Aplica las modificaciones en el documento activo dentro de una Transacción de Revit.
        /// </summary>
        public static DirectAiExecutionResult Execute(Document doc, string jsonText, List<DirectAiActionItem>? approvedItems = null)
        {
            var result = new DirectAiExecutionResult();

            if (doc == null)
            {
                result.Errors.Add("No hay ningún documento activo en Revit.");
                return result;
            }

            // Si no se pasaron items aprobados previamente, simular primero
            if (approvedItems == null || approvedItems.Count == 0)
            {
                var sim = Simulate(doc, jsonText);
                if (!sim.Success)
                {
                    result.Errors.AddRange(sim.Errors);
                    return result;
                }
                approvedItems = sim.Items.Where(i => i.IsSelected).ToList();
            }
            else
            {
                approvedItems = approvedItems.Where(i => i.IsSelected).ToList();
            }

            if (approvedItems.Count == 0)
            {
                result.Errors.Add("No hay modificaciones seleccionadas para aplicar.");
                return result;
            }

            int count = 0;
            using (Transaction t = new Transaction(doc, "⚡ Review: Acción Directa IA"))
            {
                t.Start();
                try
                {
                    foreach (var item in approvedItems)
                    {
                        Element? el = doc.GetElement(new ElementId(item.ElementId));
                        if (el == null) continue;

                        // Manejo especial de Sheet Name y Number
                        if (el is ViewSheet sheet)
                        {
                            if (item.ParameterName.Equals("Sheet Name", StringComparison.OrdinalIgnoreCase))
                            {
                                sheet.Name = item.TargetValue;
                                count++;
                                continue;
                            }
                            if (item.ParameterName.Equals("Sheet Number", StringComparison.OrdinalIgnoreCase))
                            {
                                sheet.SheetNumber = item.TargetValue;
                                count++;
                                continue;
                            }
                        }

                        Parameter? p = FindParameter(el, item.ParameterName);
                        if (p != null && !p.IsReadOnly)
                        {
                            bool success = SetParameterValueSafe(doc, p, item.TargetValue);
                            if (success) count++;
                        }
                    }

                    t.Commit();
                    result.Success = true;
                    result.ModifiedCount = count;
                    result.Summary = $"Se han aplicado exitosamente {count} modificación(es) en el modelo de Revit.";
                    result.Logs.Add($"✅ Transacción confirmada: {count} valores actualizados.");
                }
                catch (Exception ex)
                {
                    if (t.HasStarted()) t.RollBack();
                    result.Success = false;
                    result.Errors.Add($"Error ejecutando transacción en Revit: {ex.Message}");
                }
            }

            return result;
        }

        private static bool SetParameterValueSafe(Document doc, Parameter p, string value)
        {
            try
            {
                switch (p.StorageType)
                {
                    case StorageType.String:
                        p.Set(value);
                        return true;

                    case StorageType.Double:
                        if (double.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double dVal))
                        {
                            p.Set(dVal);
                            return true;
                        }
                        break;

                    case StorageType.Integer:
                        if (int.TryParse(value, out int iVal))
                        {
                            p.Set(iVal);
                            return true;
                        }
                        if (bool.TryParse(value, out bool bVal))
                        {
                            p.Set(bVal ? 1 : 0);
                            return true;
                        }
                        break;

                    case StorageType.ElementId:
                        // Si es un parámetro de Material, intentar buscar el material por nombre
                        Material? mat = new FilteredElementCollector(doc)
                            .OfClass(typeof(Material))
                            .Cast<Material>()
                            .FirstOrDefault(m => string.Equals(m.Name, value, StringComparison.OrdinalIgnoreCase) || m.Name.Contains(value, StringComparison.OrdinalIgnoreCase));

                        if (mat != null)
                        {
                            p.Set(mat.Id);
                            return true;
                        }
                        if (long.TryParse(value, out long elemIdVal))
                        {
                            p.Set(new ElementId(elemIdVal));
                            return true;
                        }
                        break;
                }
            }
            catch
            {
            }
            return false;
        }

        public static Parameter? FindParameter(Element elem, string paramName)
        {
            if (string.IsNullOrWhiteSpace(paramName)) return null;

            Parameter? p = elem.LookupParameter(paramName);
            if (p != null) return p;

            if (paramName.Equals("Comments", StringComparison.OrdinalIgnoreCase) ||
                paramName.Equals("Comentarios", StringComparison.OrdinalIgnoreCase))
            {
                return elem.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            }

            if (paramName.Equals("Cost", StringComparison.OrdinalIgnoreCase) ||
                paramName.Equals("Costo", StringComparison.OrdinalIgnoreCase))
            {
                return elem.get_Parameter(BuiltInParameter.ALL_MODEL_COST);
            }

            if (paramName.Equals("Mark", StringComparison.OrdinalIgnoreCase) ||
                paramName.Equals("Marca", StringComparison.OrdinalIgnoreCase))
            {
                return elem.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
            }

            return null;
        }

        public static string ParameterToString(Parameter p)
        {
            if (p == null || !p.HasValue) return "[Vacío]";
            return p.StorageType switch
            {
                StorageType.String => p.AsString() ?? "[Vacío]",
                StorageType.Double => p.AsValueString() ?? p.AsDouble().ToString("0.###"),
                StorageType.Integer => p.AsInteger().ToString(),
                StorageType.ElementId => p.AsElementId()?.Value.ToString() ?? "-1",
                _ => "[Desconocido]"
            };
        }

        private static string GetElementLevelName(Document doc, Element elem)
        {
            ElementId lvlId = elem.LevelId;
            if (lvlId != null && lvlId != ElementId.InvalidElementId)
            {
                Element? lvl = doc.GetElement(lvlId);
                if (lvl != null) return lvl.Name;
            }

            Parameter? p = elem.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM)
                        ?? elem.get_Parameter(BuiltInParameter.LEVEL_PARAM)
                        ?? elem.LookupParameter("Level")
                        ?? elem.LookupParameter("Nivel");

            return p?.AsValueString() ?? p?.AsString() ?? "";
        }

        /// <summary>
        /// Genera el prompt de sistema y guía de esquemas para copiar y pegar en Gemini Desktop o Web.
        /// </summary>
        public static string GetGeminiSystemPrompt()
        {
            return @"# INSTRUCCIONES PARA GEMINI — ACCIONES DIRECTAS EN REVIT (ARCA REVIEW)

Eres el Asistente Experto BIM del plugin ARCA Review para Autodesk Revit®.
Cuando el usuario te solicite modificar parámetros, filtrar elementos o gestionar planos en su modelo, responde ÚNICAMENTE con un bloque de código JSON con la siguiente estructura, sin texto explicativo previo ni posterior.

## ESQUEMA 1: MODIFICAR PARÁMETROS EN ELEMENTOS (Filtro por Categoría y Tipo)
```json
{
  ""action"": ""modify_parameters"",
  ""category"": ""Walls"",
  ""filter"": {
    ""type_name"": ""Muro Ladrillo 15cm"",
    ""level_name"": ""Nivel 1""
  },
  ""parameters"": {
    ""Comentarios"": ""Revisado por IA"",
    ""Material de Acabado"": ""Pintura Blanca""
  }
}
```

## ESQUEMA 2: MODIFICAR ELEMENTOS POR ELEMENT_IDS
```json
{
  ""action"": ""modify_parameters"",
  ""element_ids"": [1024501, 1024502],
  ""parameters"": {
    ""Comentarios"": ""Aprobado"",
    ""Marca"": ""M-01""
  }
}
```

## ESQUEMA 3: RENOMBRAR O MODIFICAR LÁMINAS (SHEETS)
```json
{
  ""action"": ""manage_sheets"",
  ""sheet_number"": ""A101"",
  ""new_name"": ""PLANTA ARQUITECTÓNICA - NIVEL 1"",
  ""new_number"": ""ARQ-101""
}
```

## ESQUEMA 4: LOTE MÚLTIPLE (VARIAS ACCIONES EN SECUENCIA)
```json
[
  {
    ""action"": ""modify_parameters"",
    ""category"": ""Walls"",
    ""filter"": { ""type_name"": ""Muro Exterior"" },
    ""parameters"": { ""Comentarios"": ""Fachada Principal"" }
  },
  {
    ""action"": ""modify_parameters"",
    ""category"": ""Doors"",
    ""parameters"": { ""Comentarios"": ""Revisar Cerradura"" }
  }
]
```

## CATEGORÍAS VÁLIDAS:
- Walls (Muros)
- Floors (Pisos / Suelos)
- Doors (Puertas)
- Windows (Ventanas)
- Rooms (Habitaciones)
- Ceilings (Cielorrasos)
- Roofs (Cubiertas)
- StructuralColumns (Columnas)
- StructuralFraming (Vigas / Steel Deck)
- PlumbingFixtures (Aparatos Sanitarios)
- Sheets (Planos)
";
        }

        /// <summary>
        /// Extrae limpiamente el bloque JSON si el usuario pegó una respuesta con Markdown (```json ... ```) o texto circundante.
        /// </summary>
        public static string ExtractJson(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText)) return "";

            string trimmed = rawText.Trim();

            // 1. Si viene en un bloque Markdown con ```json
            int codeBlockStart = trimmed.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
            if (codeBlockStart >= 0)
            {
                int start = codeBlockStart + 7;
                int codeBlockEnd = trimmed.IndexOf("```", start, StringComparison.OrdinalIgnoreCase);
                if (codeBlockEnd > start)
                {
                    return trimmed.Substring(start, codeBlockEnd - start).Trim();
                }
                else
                {
                    return trimmed.Substring(start).Trim();
                }
            }

            // 2. Si viene en un bloque genérico ```
            int genericBlockStart = trimmed.IndexOf("```", StringComparison.OrdinalIgnoreCase);
            if (genericBlockStart >= 0)
            {
                int start = genericBlockStart + 3;
                int nextLine = trimmed.IndexOf('\n', start);
                if (nextLine > start && nextLine - start < 15) start = nextLine + 1;
                int genericBlockEnd = trimmed.IndexOf("```", start, StringComparison.OrdinalIgnoreCase);
                if (genericBlockEnd > start)
                {
                    return trimmed.Substring(start, genericBlockEnd - start).Trim();
                }
            }

            // 3. Buscar el primer '{' o '[' y el último '}' o ']'
            int firstBrace = trimmed.IndexOf('{');
            int firstBracket = trimmed.IndexOf('[');

            int startIdx = -1;
            char endChar = '}';

            if (firstBrace >= 0 && firstBracket >= 0)
            {
                if (firstBrace < firstBracket) { startIdx = firstBrace; endChar = '}'; }
                else { startIdx = firstBracket; endChar = ']'; }
            }
            else if (firstBrace >= 0)
            {
                startIdx = firstBrace;
                endChar = '}';
            }
            else if (firstBracket >= 0)
            {
                startIdx = firstBracket;
                endChar = ']';
            }

            if (startIdx >= 0)
            {
                int endIdx = trimmed.LastIndexOf(endChar);
                if (endIdx > startIdx)
                {
                    return trimmed.Substring(startIdx, endIdx - startIdx + 1).Trim();
                }
            }

            return trimmed;
        }
    }
}
