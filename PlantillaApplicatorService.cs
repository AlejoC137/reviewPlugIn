using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace RevitToSupabasePlugin
{
    /// <summary>
    /// Resultado devuelto después de aplicar el JSON de la IA al modelo Revit.
    /// </summary>
    public class ApplicatorResult
    {
        public int Changes { get; set; } = 0;
        public List<string> Log { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();
    }

    /// <summary>
    /// Aplica un JSON generado por IA sobre la plantilla Revit activa dentro de una Transaction.
    /// Secciones soportadas: project_info, levels, materials, text_note_types,
    /// views, sheets, schedules_to_create, project_parameters, type_names.
    /// </summary>
    public static class PlantillaApplicatorService
    {
        private static double MmToFt(double mm) => mm / 304.8;

        // ═════════════════════════════════════════════════════════════════
        // ENTRY POINT
        // ═════════════════════════════════════════════════════════════════

        public static ApplicatorResult ApplyJson(Document doc, JObject data)
        {
            var result = new ApplicatorResult();

            ApplyProjectInfo(doc, data["project_info"] as JObject, result);
            ApplyLevels(doc, data["levels"] as JArray, result);
            ApplyMaterials(doc, data["materials"] as JArray, result);
            ApplyTextStyles(doc, data["text_note_types"] as JArray, result);
            ApplyViews(doc, data["views"] as JArray, result);
            ApplySheets(doc, data["sheets"] as JArray, result);
            CreateSchedules(doc, data["schedules_to_create"] as JArray, result);
            ApplyProjectParameters(doc, data["project_parameters"] as JArray, result);
            ApplyTypeNames(doc, data["type_names"] as JArray, result);

            return result;
        }

        // ═════════════════════════════════════════════════════════════════
        // 1. PROJECT INFO
        // ═════════════════════════════════════════════════════════════════

        private static void ApplyProjectInfo(Document doc, JObject? info, ApplicatorResult result)
        {
            if (info == null || doc.ProjectInformation == null) return;
            try
            {
                ProjectInfo pi = doc.ProjectInformation;
                TrySet(() => { if (info["name"] != null) { pi.Name = info["name"]!.ToString(); result.Log.Add($"✅ Nombre → \"{pi.Name}\""); result.Changes++; } });
                TrySet(() => { if (info["number"] != null) { pi.Number = info["number"]!.ToString(); result.Log.Add($"✅ Número → \"{pi.Number}\""); result.Changes++; } });
                TrySet(() => { if (info["client"] != null) { pi.ClientName = info["client"]!.ToString(); result.Log.Add($"✅ Cliente → \"{pi.ClientName}\""); result.Changes++; } });
                TrySet(() => { if (info["status"] != null) { pi.Status = info["status"]!.ToString(); result.Log.Add($"✅ Estado → \"{pi.Status}\""); result.Changes++; } });
                TrySet(() => { if (info["address"] != null) { pi.Address = info["address"]!.ToString(); result.Log.Add($"✅ Dirección → \"{pi.Address}\""); result.Changes++; } });
                TrySet(() => { if (info["building_name"] != null) { pi.BuildingName = info["building_name"]!.ToString(); result.Log.Add($"✅ Edificio → \"{pi.BuildingName}\""); result.Changes++; } });
                TrySet(() => { if (info["author"] != null) { pi.Author = info["author"]!.ToString(); result.Log.Add($"✅ Autor → \"{pi.Author}\""); result.Changes++; } });
                TrySet(() => { if (info["issue_date"] != null) { pi.IssueDate = info["issue_date"]!.ToString(); result.Log.Add($"✅ Fecha emisión → \"{pi.IssueDate}\""); result.Changes++; } });
            }
            catch (Exception ex) { result.Errors.Add($"⚠️ project_info: {ex.Message}"); }
        }

        // ═════════════════════════════════════════════════════════════════
        // 2. NIVELES
        // ═════════════════════════════════════════════════════════════════

        private static void ApplyLevels(Document doc, JArray? levels, ApplicatorResult result)
        {
            if (levels == null) return;
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(Level)).Cast<Level>().ToList();

            foreach (var token in levels)
            {
                string currentName = token["current_name"]?.ToString() ?? "";
                string newName = token["new_name"]?.ToString() ?? "";

                Level? found = existing.FirstOrDefault(l =>
                    l.Name.Equals(currentName, StringComparison.OrdinalIgnoreCase));

                if (found == null) { result.Errors.Add($"⚠️ Nivel no encontrado: \"{currentName}\""); continue; }

                TrySet(() =>
                {
                    if (!string.IsNullOrEmpty(newName) && newName != currentName)
                    {
                        found.Name = newName;
                        result.Log.Add($"✅ Nivel \"{currentName}\" → \"{newName}\"");
                        result.Changes++;
                    }
                });

                TrySet(() =>
                {
                    if (token["elevation_mm"] != null)
                    {
                        double mm = Convert.ToDouble(token["elevation_mm"]);
                        found.Elevation = MmToFt(mm);
                        result.Log.Add($"✅ Elevación \"{(string.IsNullOrEmpty(newName) ? currentName : newName)}\" → {mm} mm");
                        result.Changes++;
                    }
                });
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // 3. MATERIALES
        // ═════════════════════════════════════════════════════════════════

        private static void ApplyMaterials(Document doc, JArray? materials, ApplicatorResult result)
        {
            if (materials == null) return;
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(Material)).Cast<Material>().ToList();

            foreach (var token in materials)
            {
                string currentName = token["current_name"]?.ToString() ?? "";
                Material? found = existing.FirstOrDefault(m =>
                    m.Name.Equals(currentName, StringComparison.OrdinalIgnoreCase));

                if (found == null) { result.Errors.Add($"⚠️ Material no encontrado: \"{currentName}\""); continue; }

                string displayName = currentName;

                TrySet(() =>
                {
                    string newName = token["new_name"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(newName) && newName != currentName)
                    {
                        found.Name = newName;
                        displayName = newName;
                        result.Log.Add($"✅ Material \"{currentName}\" → \"{newName}\"");
                        result.Changes++;
                    }
                });

                TrySet(() =>
                {
                    if (token["color_r"] != null || token["color_g"] != null || token["color_b"] != null)
                    {
                        byte r = token["color_r"] != null ? Convert.ToByte(Math.Clamp(Convert.ToInt32(token["color_r"]), 0, 255)) : found.Color.Red;
                        byte g = token["color_g"] != null ? Convert.ToByte(Math.Clamp(Convert.ToInt32(token["color_g"]), 0, 255)) : found.Color.Green;
                        byte b = token["color_b"] != null ? Convert.ToByte(Math.Clamp(Convert.ToInt32(token["color_b"]), 0, 255)) : found.Color.Blue;
                        found.Color = new Color(r, g, b);
                        result.Log.Add($"✅ Color \"{displayName}\" → RGB({r},{g},{b})");
                        result.Changes++;
                    }
                });

                TrySet(() =>
                {
                    if (token["category"] != null)
                    {
                        found.MaterialCategory = token["category"]!.ToString();
                        result.Changes++;
                    }
                });
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // 4. ESTILOS DE TEXTO (TextNoteType)
        // ═════════════════════════════════════════════════════════════════

        private static void ApplyTextStyles(Document doc, JArray? styles, ApplicatorResult result)
        {
            if (styles == null) return;
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType)).Cast<TextNoteType>().ToList();

            foreach (var token in styles)
            {
                string typeName = token["type_name"]?.ToString() ?? "";
                TextNoteType? found = existing.FirstOrDefault(t =>
                    t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

                if (found == null) { result.Errors.Add($"⚠️ Estilo de texto no encontrado: \"{typeName}\""); continue; }

                TrySet(() =>
                {
                    if (token["new_name"] != null)
                    {
                        string newName = token["new_name"]!.ToString();
                        if (!string.IsNullOrEmpty(newName)) { found.Name = newName; result.Log.Add($"✅ Texto \"{typeName}\" → \"{newName}\""); result.Changes++; }
                    }
                });
                TrySet(() =>
                {
                    if (token["font_size_mm"] != null)
                    {
                        double mm = Convert.ToDouble(token["font_size_mm"]);
                        Parameter? p = found.get_Parameter(BuiltInParameter.TEXT_SIZE);
                        if (p != null && !p.IsReadOnly) { p.Set(MmToFt(mm)); result.Log.Add($"✅ Tamaño texto \"{typeName}\" → {mm}mm"); result.Changes++; }
                    }
                });
                TrySet(() =>
                {
                    if (token["bold"] != null)
                    {
                        bool bold = Convert.ToBoolean(token["bold"]);
                        Parameter? p = found.get_Parameter(BuiltInParameter.TEXT_STYLE_BOLD);
                        if (p != null && !p.IsReadOnly) { p.Set(bold ? 1 : 0); result.Changes++; }
                    }
                });
                TrySet(() =>
                {
                    if (token["italic"] != null)
                    {
                        bool italic = Convert.ToBoolean(token["italic"]);
                        Parameter? p = found.get_Parameter(BuiltInParameter.TEXT_STYLE_ITALIC);
                        if (p != null && !p.IsReadOnly) { p.Set(italic ? 1 : 0); result.Changes++; }
                    }
                });
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // 5. VISTAS
        // ═════════════════════════════════════════════════════════════════

        private static void ApplyViews(Document doc, JArray? views, ApplicatorResult result)
        {
            if (views == null) return;
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate && !(v is ViewSchedule) && !(v is ViewSheet))
                .ToList();

            foreach (var token in views)
            {
                string currentName = token["current_name"]?.ToString() ?? "";
                View? found = existing.FirstOrDefault(v =>
                    v.Name.Equals(currentName, StringComparison.OrdinalIgnoreCase));

                if (found == null) { result.Errors.Add($"⚠️ Vista no encontrada: \"{currentName}\""); continue; }

                TrySet(() =>
                {
                    if (token["new_name"] != null)
                    {
                        string newName = token["new_name"]!.ToString();
                        if (!string.IsNullOrEmpty(newName) && newName != currentName)
                        { found.Name = newName; result.Log.Add($"✅ Vista \"{currentName}\" → \"{newName}\""); result.Changes++; }
                    }
                });
                TrySet(() =>
                {
                    if (token["scale"] != null)
                    { found.Scale = Convert.ToInt32(token["scale"]); result.Log.Add($"✅ Escala \"{currentName}\" → 1:{found.Scale}"); result.Changes++; }
                });
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // 6. PLANOS (ViewSheet)
        // ═════════════════════════════════════════════════════════════════

        private static void ApplySheets(Document doc, JArray? sheets, ApplicatorResult result)
        {
            if (sheets == null) return;
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet)).Cast<ViewSheet>().ToList();

            foreach (var token in sheets)
            {
                string currentNumber = token["current_number"]?.ToString() ?? "";
                ViewSheet? found = existing.FirstOrDefault(s =>
                    s.SheetNumber.Equals(currentNumber, StringComparison.OrdinalIgnoreCase));

                if (found == null) { result.Errors.Add($"⚠️ Plano no encontrado: \"{currentNumber}\""); continue; }

                TrySet(() =>
                {
                    if (token["new_number"] != null)
                    {
                        string newNum = token["new_number"]!.ToString();
                        if (!string.IsNullOrEmpty(newNum) && newNum != currentNumber)
                        { found.SheetNumber = newNum; result.Log.Add($"✅ Plano \"{currentNumber}\" → \"{newNum}\""); result.Changes++; }
                    }
                });
                TrySet(() =>
                {
                    if (token["new_name"] != null)
                    {
                        string newName = token["new_name"]!.ToString();
                        if (!string.IsNullOrEmpty(newName)) { found.Name = newName; result.Log.Add($"✅ Nombre plano → \"{newName}\""); result.Changes++; }
                    }
                });
                TrySet(() => SetSheetParam(found, BuiltInParameter.SHEET_DRAWN_BY, token["drawn_by"]?.ToString(), ref result.Changes));
                TrySet(() => SetSheetParam(found, BuiltInParameter.SHEET_CHECKED_BY, token["checked_by"]?.ToString(), ref result.Changes));
                TrySet(() => SetSheetParam(found, BuiltInParameter.SHEET_DESIGNED_BY, token["designed_by"]?.ToString(), ref result.Changes));
                TrySet(() => SetSheetParam(found, BuiltInParameter.SHEET_APPROVED_BY, token["approved_by"]?.ToString(), ref result.Changes));
            }
        }

        private static void SetSheetParam(ViewSheet sheet, BuiltInParameter bip, string? value, ref int changes)
        {
            if (string.IsNullOrEmpty(value)) return;
            Parameter? p = sheet.get_Parameter(bip);
            if (p != null && !p.IsReadOnly) { p.Set(value); changes++; }
        }

        // ═════════════════════════════════════════════════════════════════
        // 7. CREAR SCHEDULES
        // ═════════════════════════════════════════════════════════════════

        private static readonly Dictionary<string, BuiltInCategory> CategoryMap =
            new Dictionary<string, BuiltInCategory>(StringComparer.OrdinalIgnoreCase)
            {
                { "Rooms",                  BuiltInCategory.OST_Rooms },
                { "Habitaciones",           BuiltInCategory.OST_Rooms },
                { "Walls",                  BuiltInCategory.OST_Walls },
                { "Muros",                  BuiltInCategory.OST_Walls },
                { "Doors",                  BuiltInCategory.OST_Doors },
                { "Puertas",                BuiltInCategory.OST_Doors },
                { "Windows",                BuiltInCategory.OST_Windows },
                { "Ventanas",               BuiltInCategory.OST_Windows },
                { "Floors",                 BuiltInCategory.OST_Floors },
                { "Pisos",                  BuiltInCategory.OST_Floors },
                { "Ceilings",               BuiltInCategory.OST_Ceilings },
                { "Furniture",              BuiltInCategory.OST_Furniture },
                { "Mobiliario",             BuiltInCategory.OST_Furniture },
                { "Structural Columns",     BuiltInCategory.OST_StructuralColumns },
                { "Structural Framing",     BuiltInCategory.OST_StructuralFraming },
                { "Generic Models",         BuiltInCategory.OST_GenericModel },
                { "Mechanical Equipment",   BuiltInCategory.OST_MechanicalEquipment },
                { "Plumbing Fixtures",      BuiltInCategory.OST_PlumbingFixtures },
                { "Electrical Fixtures",    BuiltInCategory.OST_ElectricalFixtures },
                { "Areas",                  BuiltInCategory.OST_Areas },
                { "Stairs",                 BuiltInCategory.OST_Stairs },
                { "Escaleras",              BuiltInCategory.OST_Stairs },
                { "Railings",               BuiltInCategory.OST_Railings },
                { "Ramps",                  BuiltInCategory.OST_Ramps },
            };

        private static void CreateSchedules(Document doc, JArray? schedules, ApplicatorResult result)
        {
            if (schedules == null) return;

            foreach (var token in schedules)
            {
                string name = token["name"]?.ToString() ?? "";
                string categoryName = token["category"]?.ToString() ?? "";
                JArray? fields = token["fields"] as JArray;

                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(categoryName)) continue;

                if (!CategoryMap.TryGetValue(categoryName, out BuiltInCategory bic))
                {
                    result.Errors.Add($"⚠️ Categoría no soportada para schedule: \"{categoryName}\"");
                    continue;
                }

                TrySet(() =>
                {
                    ViewSchedule vs = ViewSchedule.CreateSchedule(doc, new ElementId(bic));
                    vs.Name = name;
                    result.Log.Add($"✅ Schedule creado: \"{name}\" ({categoryName})");
                    result.Changes++;

                    if (fields != null && fields.Count > 0)
                    {
                        ScheduleDefinition def = vs.Definition;
                        IList<SchedulableField> schedulable = def.GetSchedulableFields();

                        foreach (var fieldToken in fields)
                        {
                            string fieldName = fieldToken.ToString();
                            SchedulableField? sf = FindSchedulableField(doc, schedulable, fieldName);
                            if (sf != null)
                            {
                                try { def.AddField(sf); result.Changes++; }
                                catch { }
                            }
                        }
                    }
                }, ex => result.Errors.Add($"⚠️ Error creando schedule \"{name}\": {ex.Message}"));
            }
        }

        private static SchedulableField? FindSchedulableField(
            Document doc, IList<SchedulableField> fields, string fieldName)
        {
            foreach (var sf in fields)
            {
                try
                {
                    string label = "";
                    long paramIdValue = sf.ParameterId.Value;

                    if (Enum.IsDefined(typeof(BuiltInParameter), (int)paramIdValue))
                    {
                        var bip = (BuiltInParameter)(int)paramIdValue;
                        label = LabelUtils.GetLabelFor(bip);
                    }
                    else
                    {
                        var elem = doc.GetElement(sf.ParameterId);
                        if (elem != null) label = elem.Name;
                    }

                    if (label.Equals(fieldName, StringComparison.OrdinalIgnoreCase))
                        return sf;
                }
                catch { }
            }
            return null;
        }

        // ═════════════════════════════════════════════════════════════════
        // 8. PARÁMETROS DE PROYECTO
        // ═════════════════════════════════════════════════════════════════

        private static void ApplyProjectParameters(Document doc, JArray? parameters, ApplicatorResult result)
        {
            if (parameters == null || doc.ProjectInformation == null) return;

            foreach (var token in parameters)
            {
                string pName = token["name"]?.ToString() ?? "";
                string pValue = token["value"]?.ToString() ?? "";
                if (string.IsNullOrEmpty(pName)) continue;

                TrySet(() =>
                {
                    Parameter? p = doc.ProjectInformation.LookupParameter(pName);
                    if (p != null && !p.IsReadOnly)
                    {
                        p.Set(pValue);
                        result.Log.Add($"✅ Parámetro \"{pName}\" → \"{pValue}\"");
                        result.Changes++;
                    }
                    else result.Errors.Add($"⚠️ Parámetro no encontrado/solo lectura: \"{pName}\"");
                });
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // 9. TIPOS DE ELEMENTO (ElementType)
        // ═════════════════════════════════════════════════════════════════

        private static void ApplyTypeNames(Document doc, JArray? types, ApplicatorResult result)
        {
            if (types == null) return;

            var allTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(ElementType)).Cast<ElementType>().ToList();

            foreach (var token in types)
            {
                string familyName = token["family_name"]?.ToString() ?? "";
                string currentName = token["current_name"]?.ToString() ?? "";
                string newName = token["new_name"]?.ToString() ?? "";

                if (string.IsNullOrEmpty(currentName) || string.IsNullOrEmpty(newName)) continue;

                ElementType? found = allTypes.FirstOrDefault(et =>
                    et.Name.Equals(currentName, StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrEmpty(familyName) ||
                     et.FamilyName.Equals(familyName, StringComparison.OrdinalIgnoreCase)));

                if (found == null) { result.Errors.Add($"⚠️ Tipo no encontrado: \"{familyName}:{currentName}\""); continue; }

                TrySet(() =>
                {
                    found.Name = newName;
                    result.Log.Add($"✅ Tipo \"{currentName}\" → \"{newName}\"");
                    result.Changes++;
                });
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // HELPERS
        // ═════════════════════════════════════════════════════════════════

        private static void TrySet(Action action, Action<Exception>? onError = null)
        {
            try { action(); }
            catch (Exception ex) { onError?.Invoke(ex); }
        }
    }
}
