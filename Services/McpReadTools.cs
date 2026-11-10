using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using View = Autodesk.Revit.DB.View;
using Color = Autodesk.Revit.DB.Color;

namespace RevitToSupabasePlugin
{
    /// <summary>
    /// Fase 1 del MCP compatible: herramientas de SOLO LECTURA.
    /// Los nombres y parámetros siguen el catálogo público de herramientas
    /// (get_*) para que los prompts existentes funcionen. La implementación
    /// es propia, escrita sobre la API de Revit; no se copia código de terceros.
    /// Todas se ejecutan en el hilo principal de Revit (vía ExternalEvent).
    /// </summary>
    public static class McpReadTools
    {
        public const int MaxBatch = 1000;
        private const int DefaultLimit = 2000;

        private static readonly Dictionary<string, Func<UIDocument, Document, JObject, JObject>> Handlers = new()
        {
            ["get_active_view_in_revit"] = GetActiveView,
            ["get_user_selection_in_revit"] = GetUserSelection,
            ["get_category_by_keyword"] = GetCategoryByKeyword,
            ["get_model_categories"] = GetModelCategories,
            ["get_elements_by_category"] = GetElementsByCategory,
            ["get_object_classes_from_elementids"] = GetObjectClasses,
            ["get_categories_from_elementids"] = GetCategoriesFromElementIds,
            ["get_all_used_families_in_model"] = GetAllUsedFamiliesInModel,
            ["get_all_used_families_of_category"] = GetAllUsedFamiliesOfCategory,
            ["get_all_used_types_of_families"] = GetAllUsedTypesOfFamilies,
            ["get_element_types_for_elementids"] = GetElementTypesForElementIds,
            ["get_all_elementids_for_specific_type_ids"] = GetElementIdsForTypeIds,
            ["get_parameters_from_elementid"] = GetParametersFromElementId,
            ["get_parameter_value_for_element_ids"] = GetParameterValueForElementIds,
            ["get_material_layers_from_types"] = GetMaterialLayersFromTypes,
            ["get_location_for_element_ids"] = GetLocationForElementIds,
            ["get_boundingboxes_for_element_ids"] = GetBoundingBoxes,
            ["get_host_id_for_element_ids"] = GetHostIdForElementIds,
            ["get_all_elements_shown_in_view"] = GetElementsShownInView,
            ["get_viewports_and_schedules_on_sheets"] = GetViewportsAndSchedulesOnSheets,
            ["get_schedules_info_and_columns"] = GetSchedulesInfo,
            ["get_graphic_overrides_for_element_ids_in_view"] = GetGraphicOverridesForElements,
            ["get_graphic_filters_applied_to_views"] = GetGraphicFiltersApplied,
            ["get_graphic_overrides_view_filters"] = GetGraphicOverridesViewFilters,
            ["get_all_warnings_in_the_model"] = GetAllWarnings,
            ["get_all_project_units"] = GetProjectUnits,
            ["get_all_workset_information"] = GetWorksetInformation,
            ["get_worksets_from_elementids"] = GetWorksetsFromElementIds,
            ["get_worksharing_information_for_element_ids"] = GetWorksharingInfo,
        };

        public static bool Handles(string toolName) => Handlers.ContainsKey(toolName);

        public static JObject Execute(UIDocument uidoc, Document doc, string toolName, JObject args)
        {
            return Handlers[toolName](uidoc, doc, args);
        }

        // ------------------------------------------------------------------
        // Vista y selección
        // ------------------------------------------------------------------

        private static JObject GetActiveView(UIDocument u, Document d, JObject a)
        {
            View v = d.ActiveView;
            return Ok(new JObject
            {
                ["view_id"] = v.Id.Value,
                ["name"] = v.Name,
                ["view_type"] = v.ViewType.ToString(),
                ["scale"] = v.Scale,
                ["detail_level"] = v.DetailLevel.ToString(),
                ["level"] = (v as ViewPlan)?.GenLevel?.Name ?? "",
                ["is_template"] = v.IsTemplate
            });
        }

        private static JObject GetUserSelection(UIDocument u, Document d, JObject a)
        {
            var items = new JArray();
            foreach (ElementId id in u.Selection.GetElementIds())
            {
                Element? e = d.GetElement(id);
                if (e != null) items.Add(ElementSummary(e));
            }
            return Ok(new JObject { ["count"] = items.Count, ["elements"] = items });
        }

        // ------------------------------------------------------------------
        // Categorías y clases
        // ------------------------------------------------------------------

        private static JObject GetCategoryByKeyword(UIDocument u, Document d, JObject a)
        {
            string keyword = ReadString(a, "keyword");
            var items = new JArray();
            foreach (Category c in d.Settings.Categories)
            {
                if (c.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    items.Add(CategoryJson(c));
            }
            return Ok(new JObject { ["count"] = items.Count, ["categories"] = items });
        }

        private static JObject GetModelCategories(UIDocument u, Document d, JObject a)
        {
            var items = new JArray();
            foreach (Category c in d.Settings.Categories) items.Add(CategoryJson(c));
            return Ok(new JObject { ["count"] = items.Count, ["categories"] = items });
        }

        private static JObject GetElementsByCategory(UIDocument u, Document d, JObject a)
        {
            long catId = ReadLong(a, "category_id");
            bool includeTypes = a["include_types"]?.Value<bool>() ?? false;
            int limit = a["limit"]?.Value<int>() ?? DefaultLimit;

            var collector = new FilteredElementCollector(d).OfCategoryId(new ElementId(catId));
            if (!includeTypes) collector = collector.WhereElementIsNotElementType();

            var ids = collector.ToElementIds().Select(i => i.Value).ToList();
            return Ok(new JObject
            {
                ["category_id"] = catId,
                ["total"] = ids.Count,
                ["truncated"] = ids.Count > limit,
                ["element_ids"] = JArray.FromObject(ids.Take(limit))
            });
        }

        private static JObject GetObjectClasses(UIDocument u, Document d, JObject a)
        {
            var items = new JArray();
            foreach (ElementId id in ReadIds(a))
            {
                Element? e = d.GetElement(id);
                items.Add(new JObject
                {
                    ["element_id"] = id.Value,
                    ["class"] = e == null ? null : e.GetType().FullName
                });
            }
            return Ok(new JObject { ["items"] = items });
        }

        private static JObject GetCategoriesFromElementIds(UIDocument u, Document d, JObject a)
        {
            var items = new JArray();
            foreach (ElementId id in ReadIds(a))
            {
                Element? e = d.GetElement(id);
                items.Add(new JObject
                {
                    ["element_id"] = id.Value,
                    ["category_id"] = e?.Category?.Id.Value,
                    ["category_name"] = e?.Category?.Name
                });
            }
            return Ok(new JObject { ["items"] = items });
        }

        // ------------------------------------------------------------------
        // Familias y tipos
        // ------------------------------------------------------------------

        private static JObject GetAllUsedFamiliesInModel(UIDocument u, Document d, JObject a)
        {
            return UsedFamilies(d, null);
        }

        private static JObject GetAllUsedFamiliesOfCategory(UIDocument u, Document d, JObject a)
        {
            return UsedFamilies(d, ReadLong(a, "category_id"));
        }

        /// <summary>Familias con al menos una instancia en el modelo, con conteo.</summary>
        private static JObject UsedFamilies(Document d, long? categoryId)
        {
            var counts = new Dictionary<long, (string name, string category, int instances)>();
            foreach (FamilyInstance fi in new FilteredElementCollector(d).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>())
            {
                if (categoryId.HasValue && fi.Category?.Id.Value != categoryId.Value) continue;
                Family? fam = fi.Symbol?.Family;
                if (fam == null) continue;

                long key = fam.Id.Value;
                counts.TryGetValue(key, out var prev);
                counts[key] = (fam.Name, fam.FamilyCategory?.Name ?? "", prev.instances + 1);
            }

            var items = new JArray();
            foreach (var kv in counts.OrderBy(k => k.Value.name))
            {
                items.Add(new JObject
                {
                    ["family_id"] = kv.Key,
                    ["family_name"] = kv.Value.name,
                    ["category"] = kv.Value.category,
                    ["instance_count"] = kv.Value.instances
                });
            }
            return Ok(new JObject { ["count"] = items.Count, ["families"] = items });
        }

        private static JObject GetAllUsedTypesOfFamilies(UIDocument u, Document d, JObject a)
        {
            long famId = ReadLong(a, "family_id");
            var fam = d.GetElement(new ElementId(famId)) as Family
                      ?? throw new ArgumentException($"family_id {famId} no es una familia válida.");

            var instancesByType = new Dictionary<long, int>();
            foreach (FamilyInstance fi in new FilteredElementCollector(d).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>())
            {
                long t = fi.GetTypeId().Value;
                instancesByType[t] = instancesByType.TryGetValue(t, out int c) ? c + 1 : 1;
            }

            var items = new JArray();
            foreach (ElementId symId in fam.GetFamilySymbolIds())
            {
                var sym = d.GetElement(symId) as FamilySymbol;
                items.Add(new JObject
                {
                    ["type_id"] = symId.Value,
                    ["type_name"] = sym?.Name ?? "",
                    ["instance_count"] = instancesByType.TryGetValue(symId.Value, out int n) ? n : 0
                });
            }
            return Ok(new JObject { ["family_id"] = famId, ["family_name"] = fam.Name, ["types"] = items });
        }

        private static JObject GetElementTypesForElementIds(UIDocument u, Document d, JObject a)
        {
            var items = new JArray();
            foreach (ElementId id in ReadIds(a))
            {
                Element? e = d.GetElement(id);
                ElementId tid = e?.GetTypeId() ?? ElementId.InvalidElementId;
                Element? type = tid == ElementId.InvalidElementId ? null : d.GetElement(tid);
                items.Add(new JObject
                {
                    ["element_id"] = id.Value,
                    ["type_id"] = tid == ElementId.InvalidElementId ? null : (JToken)tid.Value,
                    ["type_name"] = type?.Name,
                    ["family_name"] = (type as ElementType)?.FamilyName
                });
            }
            return Ok(new JObject { ["items"] = items });
        }

        private static JObject GetElementIdsForTypeIds(UIDocument u, Document d, JObject a)
        {
            var wanted = new HashSet<long>(ReadIds(a, "type_ids").Select(i => i.Value));
            var ids = new FilteredElementCollector(d)
                .WhereElementIsNotElementType()
                .Where(e => wanted.Contains(e.GetTypeId().Value))
                .Select(e => e.Id.Value)
                .ToList();
            return Ok(new JObject { ["count"] = ids.Count, ["element_ids"] = JArray.FromObject(ids) });
        }

        private static JObject GetMaterialLayersFromTypes(UIDocument u, Document d, JObject a)
        {
            var items = new JArray();
            foreach (ElementId id in ReadIds(a, "type_ids"))
            {
                var layers = new JArray();
                var host = d.GetElement(id) as HostObjAttributes;
                CompoundStructure? cs = host?.GetCompoundStructure();
                if (cs != null)
                {
                    foreach (CompoundStructureLayer layer in cs.GetLayers())
                    {
                        layers.Add(new JObject
                        {
                            ["function"] = layer.Function.ToString(),
                            ["material_id"] = layer.MaterialId == ElementId.InvalidElementId ? null : (JToken)layer.MaterialId.Value,
                            ["material_name"] = layer.MaterialId == ElementId.InvalidElementId ? null : d.GetElement(layer.MaterialId)?.Name,
                            ["width_mm"] = ToMm(layer.Width)
                        });
                    }
                }
                items.Add(new JObject { ["type_id"] = id.Value, ["layers"] = layers });
            }
            return Ok(new JObject { ["items"] = items });
        }

        // ------------------------------------------------------------------
        // Parámetros
        // ------------------------------------------------------------------

        private static JObject GetParametersFromElementId(UIDocument u, Document d, JObject a)
        {
            long id = ReadLong(a, "element_id");
            Element e = d.GetElement(new ElementId(id))
                        ?? throw new ArgumentException($"element_id {id} no existe.");

            var items = new JArray();
            foreach (Parameter p in e.Parameters)
            {
                items.Add(new JObject
                {
                    ["name"] = p.Definition.Name,
                    ["storage_type"] = p.StorageType.ToString(),
                    ["value"] = JToken.FromObject(ParamValue(p) ?? (object)""),
                    ["display_value"] = p.AsValueString(),
                    ["is_read_only"] = p.IsReadOnly,
                    ["is_shared"] = p.IsShared
                });
            }
            return Ok(new JObject { ["element_id"] = id, ["count"] = items.Count, ["parameters"] = items });
        }

        private static JObject GetParameterValueForElementIds(UIDocument u, Document d, JObject a)
        {
            string name = ReadString(a, "parameter_name");
            var items = new JArray();
            foreach (ElementId id in ReadIds(a))
            {
                Element? e = d.GetElement(id);
                Parameter? p = e?.LookupParameter(name);
                items.Add(new JObject
                {
                    ["element_id"] = id.Value,
                    ["found"] = p != null,
                    ["value"] = p == null ? null : JToken.FromObject(ParamValue(p) ?? (object)""),
                    ["display_value"] = p?.AsValueString()
                });
            }
            return Ok(new JObject { ["parameter_name"] = name, ["items"] = items });
        }

        // ------------------------------------------------------------------
        // Geometría y jerarquía
        // ------------------------------------------------------------------

        private static JObject GetLocationForElementIds(UIDocument u, Document d, JObject a)
        {
            var items = new JArray();
            foreach (ElementId id in ReadIds(a))
            {
                Element? e = d.GetElement(id);
                var obj = new JObject { ["element_id"] = id.Value, ["units"] = "mm" };
                switch (e?.Location)
                {
                    case LocationPoint lp:
                        obj["type"] = "point";
                        obj["point"] = PointMm(lp.Point);
                        obj["rotation_deg"] = Math.Round(lp.Rotation * 180.0 / Math.PI, 2);
                        break;
                    case LocationCurve lc:
                        obj["type"] = "curve";
                        obj["start"] = PointMm(lc.Curve.GetEndPoint(0));
                        obj["end"] = PointMm(lc.Curve.GetEndPoint(1));
                        obj["length_mm"] = ToMm(lc.Curve.Length);
                        break;
                    default:
                        obj["type"] = "none";
                        break;
                }
                items.Add(obj);
            }
            return Ok(new JObject { ["items"] = items });
        }

        private static JObject GetBoundingBoxes(UIDocument u, Document d, JObject a)
        {
            View? view = OptionalView(d, a);
            var items = new JArray();
            foreach (ElementId id in ReadIds(a))
            {
                BoundingBoxXYZ? bb = d.GetElement(id)?.get_BoundingBox(view);
                items.Add(new JObject
                {
                    ["element_id"] = id.Value,
                    ["min_mm"] = bb == null ? null : PointMm(bb.Min),
                    ["max_mm"] = bb == null ? null : PointMm(bb.Max)
                });
            }
            return Ok(new JObject { ["items"] = items });
        }

        private static JObject GetHostIdForElementIds(UIDocument u, Document d, JObject a)
        {
            var items = new JArray();
            foreach (ElementId id in ReadIds(a))
            {
                Element? host = (d.GetElement(id) as FamilyInstance)?.Host;
                items.Add(new JObject
                {
                    ["element_id"] = id.Value,
                    ["host_id"] = host == null ? null : (JToken)host.Id.Value,
                    ["host_name"] = host?.Name
                });
            }
            return Ok(new JObject { ["items"] = items });
        }

        // ------------------------------------------------------------------
        // Vistas, láminas y planificaciones
        // ------------------------------------------------------------------

        private static JObject GetElementsShownInView(UIDocument u, Document d, JObject a)
        {
            View view = ResolveView(d, a);
            int limit = a["limit"]?.Value<int>() ?? DefaultLimit;
            var ids = new FilteredElementCollector(d, view.Id).WhereElementIsNotElementType().ToElementIds()
                .Select(i => i.Value).ToList();
            return Ok(new JObject
            {
                ["view_id"] = view.Id.Value,
                ["total"] = ids.Count,
                ["truncated"] = ids.Count > limit,
                ["element_ids"] = JArray.FromObject(ids.Take(limit))
            });
        }

        private static JObject GetViewportsAndSchedulesOnSheets(UIDocument u, Document d, JObject a)
        {
            var sheets = new JArray();
            foreach (ViewSheet sh in new FilteredElementCollector(d).OfClass(typeof(ViewSheet)).Cast<ViewSheet>())
            {
                if (sh.IsPlaceholder) continue;

                var viewports = new JArray();
                foreach (Viewport vp in new FilteredElementCollector(d, sh.Id).OfClass(typeof(Viewport)).Cast<Viewport>())
                {
                    viewports.Add(new JObject
                    {
                        ["viewport_id"] = vp.Id.Value,
                        ["view_id"] = vp.ViewId.Value,
                        ["view_name"] = d.GetElement(vp.ViewId)?.Name
                    });
                }

                var schedules = new JArray();
                foreach (ScheduleSheetInstance ss in new FilteredElementCollector(d, sh.Id).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>())
                {
                    if (ss.IsTitleblockRevisionSchedule) continue;
                    schedules.Add(new JObject
                    {
                        ["schedule_id"] = ss.ScheduleId.Value,
                        ["schedule_name"] = d.GetElement(ss.ScheduleId)?.Name
                    });
                }

                sheets.Add(new JObject
                {
                    ["sheet_id"] = sh.Id.Value,
                    ["sheet_number"] = sh.SheetNumber,
                    ["sheet_name"] = sh.Name,
                    ["viewports"] = viewports,
                    ["schedules"] = schedules
                });
            }
            return Ok(new JObject { ["count"] = sheets.Count, ["sheets"] = sheets });
        }

        private static JObject GetSchedulesInfo(UIDocument u, Document d, JObject a)
        {
            var items = new JArray();
            foreach (ViewSchedule vs in new FilteredElementCollector(d).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>())
            {
                if (vs.IsTemplate) continue;
                ScheduleDefinition def = vs.Definition;
                var columns = new JArray();
                for (int i = 0; i < def.GetFieldCount(); i++)
                    columns.Add(def.GetField(i).GetName());

                items.Add(new JObject
                {
                    ["schedule_id"] = vs.Id.Value,
                    ["name"] = vs.Name,
                    ["category_id"] = def.CategoryId.Value,
                    ["column_count"] = columns.Count,
                    ["columns"] = columns
                });
            }
            return Ok(new JObject { ["count"] = items.Count, ["schedules"] = items });
        }

        // ------------------------------------------------------------------
        // Gráficos y filtros de vista
        // ------------------------------------------------------------------

        private static JObject GetGraphicOverridesForElements(UIDocument u, Document d, JObject a)
        {
            View view = ResolveView(d, a);
            var items = new JArray();
            foreach (ElementId id in ReadIds(a))
            {
                OverrideGraphicSettings ogs = view.GetElementOverrides(id);
                items.Add(new JObject
                {
                    ["element_id"] = id.Value,
                    ["halftone"] = ogs.Halftone,
                    ["transparency"] = ogs.Transparency,
                    ["projection_line_color"] = ColorJson(ogs.ProjectionLineColor)
                });
            }
            return Ok(new JObject { ["view_id"] = view.Id.Value, ["items"] = items });
        }

        private static JObject GetGraphicFiltersApplied(UIDocument u, Document d, JObject a)
        {
            View view = ResolveView(d, a);
            var items = new JArray();
            foreach (ElementId fid in view.GetFilters())
            {
                items.Add(new JObject
                {
                    ["filter_id"] = fid.Value,
                    ["name"] = d.GetElement(fid)?.Name,
                    ["enabled"] = view.GetIsFilterEnabled(fid),
                    ["visible"] = view.GetFilterVisibility(fid)
                });
            }
            return Ok(new JObject { ["view_id"] = view.Id.Value, ["count"] = items.Count, ["filters"] = items });
        }

        private static JObject GetGraphicOverridesViewFilters(UIDocument u, Document d, JObject a)
        {
            View view = ResolveView(d, a);
            var items = new JArray();
            foreach (ElementId fid in view.GetFilters())
            {
                OverrideGraphicSettings ogs = view.GetFilterOverrides(fid);
                items.Add(new JObject
                {
                    ["filter_id"] = fid.Value,
                    ["name"] = d.GetElement(fid)?.Name,
                    ["halftone"] = ogs.Halftone,
                    ["transparency"] = ogs.Transparency,
                    ["projection_line_color"] = ColorJson(ogs.ProjectionLineColor)
                });
            }
            return Ok(new JObject { ["view_id"] = view.Id.Value, ["items"] = items });
        }

        // ------------------------------------------------------------------
        // Modelo: avisos, unidades, worksets
        // ------------------------------------------------------------------

        private static JObject GetAllWarnings(UIDocument u, Document d, JObject a)
        {
            int limit = a["limit"]?.Value<int>() ?? 500;
            var items = new JArray();
            foreach (FailureMessage fm in d.GetWarnings())
            {
                if (items.Count >= limit) break;
                items.Add(new JObject
                {
                    ["description"] = fm.GetDescriptionText(),
                    ["severity"] = fm.GetSeverity().ToString(),
                    ["failing_element_ids"] = JArray.FromObject(fm.GetFailingElements().Take(50).Select(i => i.Value))
                });
            }
            return Ok(new JObject { ["count"] = items.Count, ["warnings"] = items });
        }

        private static JObject GetProjectUnits(UIDocument u, Document d, JObject a)
        {
            Units units = d.GetUnits();
            var result = new JObject();
            foreach (var (label, spec) in new (string, ForgeTypeId)[]
                     {
                         ("length", SpecTypeId.Length),
                         ("area", SpecTypeId.Area),
                         ("volume", SpecTypeId.Volume)
                     })
            {
                result[label] = units.GetFormatOptions(spec).GetUnitTypeId().TypeId;
            }
            return Ok(new JObject { ["units"] = result });
        }

        private static JObject GetWorksetInformation(UIDocument u, Document d, JObject a)
        {
            if (!d.IsWorkshared)
                return Error("El documento no es de worksharing (no tiene worksets).");

            var items = new JArray();
            foreach (Workset ws in new FilteredWorksetCollector(d))
            {
                items.Add(new JObject
                {
                    ["workset_id"] = ws.Id.IntegerValue,
                    ["name"] = ws.Name,
                    ["kind"] = ws.Kind.ToString(),
                    ["is_open"] = ws.IsOpen,
                    ["is_editable"] = ws.IsEditable,
                    ["owner"] = ws.Owner
                });
            }
            return Ok(new JObject { ["count"] = items.Count, ["worksets"] = items });
        }

        private static JObject GetWorksetsFromElementIds(UIDocument u, Document d, JObject a)
        {
            if (!d.IsWorkshared)
                return Error("El documento no es de worksharing (no tiene worksets).");

            WorksetTable table = d.GetWorksetTable();
            var items = new JArray();
            foreach (ElementId id in ReadIds(a))
            {
                Element? e = d.GetElement(id);
                string? wsName = null;
                int? wsId = null;
                if (e != null)
                {
                    wsId = e.WorksetId.IntegerValue;
                    wsName = table.GetWorkset(e.WorksetId)?.Name;
                }
                items.Add(new JObject
                {
                    ["element_id"] = id.Value,
                    ["workset_id"] = wsId,
                    ["workset_name"] = wsName
                });
            }
            return Ok(new JObject { ["items"] = items });
        }

        private static JObject GetWorksharingInfo(UIDocument u, Document d, JObject a)
        {
            if (!d.IsWorkshared)
                return Error("El documento no es de worksharing (no tiene worksets).");

            var items = new JArray();
            foreach (ElementId id in ReadIds(a))
            {
                var tip = WorksharingUtils.GetWorksharingTooltipInfo(d, id);
                items.Add(new JObject
                {
                    ["element_id"] = id.Value,
                    ["checkout_status"] = WorksharingUtils.GetCheckoutStatus(d, id).ToString(),
                    ["creator"] = tip.Creator,
                    ["owner"] = tip.Owner,
                    ["last_changed_by"] = tip.LastChangedBy
                });
            }
            return Ok(new JObject { ["items"] = items });
        }

        // ------------------------------------------------------------------
        // Manifiesto de herramientas (tools/list)
        // ------------------------------------------------------------------

        public static JArray Manifest()
        {
            var tools = new JArray();
            void Add(string name, string desc, JObject props, params string[] required)
            {
                tools.Add(new JObject
                {
                    ["name"] = name,
                    ["description"] = desc,
                    ["inputSchema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = props,
                        ["required"] = new JArray(required)
                    }
                });
            }

            JObject IdsProp(string desc = "Lista de ElementIds (máximo 1000).") => new JObject
            {
                ["element_ids"] = new JObject
                {
                    ["type"] = "array",
                    ["items"] = new JObject { ["type"] = "integer" },
                    ["maxItems"] = MaxBatch,
                    ["description"] = desc
                }
            };
            JObject Merge(JObject a, JObject b) { foreach (var kv in b) a[kv.Key] = kv.Value; return a; }
            JObject P(string key, string type, string desc) => new JObject
            {
                [key] = new JObject { ["type"] = type, ["description"] = desc }
            };
            JObject ViewProp() => P("view_id", "integer", "ElementId de la vista. Si se omite, se usa la vista activa.");

            Add("get_active_view_in_revit", "Devuelve la vista activa del documento.", new JObject());
            Add("get_user_selection_in_revit", "Devuelve los elementos seleccionados por el usuario.", new JObject());
            Add("get_category_by_keyword", "Busca categorías cuyo nombre contenga la palabra clave (ej. Door, Wall).",
                P("keyword", "string", "Palabra clave en el idioma del modelo."), "keyword");
            Add("get_model_categories", "Lista todas las categorías del modelo con su id y nombre.", new JObject());
            Add("get_elements_by_category", "Devuelve los ElementIds de una categoría.",
                Merge(P("category_id", "integer", "Id de la categoría."),
                    Merge(P("include_types", "boolean", "Incluir tipos (por defecto false)."),
                          P("limit", "integer", "Máximo de ids devueltos (por defecto 2000)."))), "category_id");
            Add("get_object_classes_from_elementids", "Devuelve la clase completa (ej. Autodesk.Revit.DB.Wall) de cada elemento.",
                IdsProp(), "element_ids");
            Add("get_categories_from_elementids", "Devuelve la categoría (id y nombre) de cada elemento.",
                IdsProp(), "element_ids");
            Add("get_all_used_families_in_model", "Familias con al menos una instancia en el modelo, con conteo.", new JObject());
            Add("get_all_used_families_of_category", "Familias usadas dentro de una categoría.",
                P("category_id", "integer", "Id de la categoría."), "category_id");
            Add("get_all_used_types_of_families", "Tipos de una familia con su conteo de instancias.",
                P("family_id", "integer", "ElementId de la familia."), "family_id");
            Add("get_element_types_for_elementids", "Tipo de cada elemento (id, nombre, familia).",
                IdsProp(), "element_ids");
            Add("get_all_elementids_for_specific_type_ids", "Elementos que usan alguno de los tipos indicados.",
                P("type_ids", "array", "Lista de ids de tipo."), "type_ids");
            Add("get_parameters_from_elementid", "Todos los parámetros de un elemento con su valor.",
                P("element_id", "integer", "ElementId del elemento."), "element_id");
            Add("get_parameter_value_for_element_ids", "Valor de un parámetro (por nombre) para varios elementos.",
                Merge(IdsProp(), P("parameter_name", "string", "Nombre exacto del parámetro.")),
                "element_ids", "parameter_name");
            Add("get_material_layers_from_types", "Capas de material (función, material, espesor en mm) de tipos de muro/piso.",
                P("type_ids", "array", "Lista de ids de tipo."), "type_ids");
            Add("get_location_for_element_ids", "Ubicación (punto o curva) de cada elemento, en mm.",
                IdsProp(), "element_ids");
            Add("get_boundingboxes_for_element_ids", "Caja envolvente (mm) de cada elemento.",
                Merge(IdsProp(), ViewProp()), "element_ids");
            Add("get_host_id_for_element_ids", "Anfitrión (host) de cada familia hospedada.",
                IdsProp(), "element_ids");
            Add("get_all_elements_shown_in_view", "Elementos visibles en una vista.",
                Merge(ViewProp(), P("limit", "integer", "Máximo de ids (por defecto 2000).")));
            Add("get_viewports_and_schedules_on_sheets", "Láminas con sus viewports y planificaciones colocadas.", new JObject());
            Add("get_schedules_info_and_columns", "Planificaciones del modelo con sus columnas.", new JObject());
            Add("get_graphic_overrides_for_element_ids_in_view", "Overrides gráficos (halftone, transparencia, color) de elementos en una vista.",
                Merge(IdsProp(), ViewProp()), "element_ids");
            Add("get_graphic_filters_applied_to_views", "Filtros aplicados a una vista con su visibilidad.",
                ViewProp());
            Add("get_graphic_overrides_view_filters", "Overrides gráficos de los filtros de una vista.",
                ViewProp());
            Add("get_all_warnings_in_the_model", "Avisos (warnings) del modelo con elementos afectados.",
                P("limit", "integer", "Máximo de avisos (por defecto 500)."));
            Add("get_all_project_units", "Unidades de longitud, área y volumen del proyecto.", new JObject());
            Add("get_all_workset_information", "Worksets del modelo (requiere worksharing).", new JObject());
            Add("get_worksets_from_elementids", "Workset de cada elemento (requiere worksharing).",
                IdsProp(), "element_ids");
            Add("get_worksharing_information_for_element_ids", "Estado de propiedad (checkout), creador y último editor de cada elemento.",
                IdsProp(), "element_ids");

            return tools;
        }

        // ------------------------------------------------------------------
        // Utilidades
        // ------------------------------------------------------------------

        private static JObject Ok(JObject payload)
        {
            payload["status"] = "success";
            return payload;
        }

        private static JObject Error(string message) => new JObject
        {
            ["status"] = "error",
            ["error"] = message
        };

        private static List<ElementId> ReadIds(JObject a, string key = "element_ids")
        {
            var arr = a[key] as JArray;
            if (arr == null || arr.Count == 0)
                throw new ArgumentException($"'{key}' es requerido: lista de enteros no vacía.");
            if (arr.Count > MaxBatch)
                throw new ArgumentException($"'{key}' admite máximo {MaxBatch} ids por llamada (recibidos {arr.Count}).");
            return arr.Select(t => new ElementId(t.Value<long>())).ToList();
        }

        private static long ReadLong(JObject a, string key)
        {
            JToken? t = a[key];
            if (t == null || t.Type == JTokenType.Null)
                throw new ArgumentException($"'{key}' es requerido.");
            return t.Value<long>();
        }

        private static string ReadString(JObject a, string key)
        {
            string? s = a[key]?.ToString();
            if (string.IsNullOrWhiteSpace(s))
                throw new ArgumentException($"'{key}' es requerido.");
            return s;
        }

        private static View ResolveView(Document d, JObject a)
        {
            return OptionalView(d, a) ?? d.ActiveView;
        }

        private static View? OptionalView(Document d, JObject a)
        {
            JToken? t = a["view_id"];
            if (t == null || t.Type == JTokenType.Null) return null;
            long id = t.Value<long>();
            return d.GetElement(new ElementId(id)) as View
                   ?? throw new ArgumentException($"view_id {id} no es una vista válida.");
        }

        private static JObject ElementSummary(Element e) => new JObject
        {
            ["id"] = e.Id.Value,
            ["name"] = e.Name,
            ["class"] = e.GetType().Name,
            ["category"] = e.Category?.Name ?? "",
            ["type_id"] = e.GetTypeId().Value
        };

        private static JObject CategoryJson(Category c) => new JObject
        {
            ["category_id"] = c.Id.Value,
            ["name"] = c.Name,
            ["category_type"] = c.CategoryType.ToString(),
            ["can_add_subcategory"] = c.CanAddSubcategory
        };

        private static object? ParamValue(Parameter p)
        {
            switch (p.StorageType)
            {
                case StorageType.String: return p.AsString();
                case StorageType.Integer: return p.AsInteger();
                case StorageType.Double: return p.AsDouble();
                case StorageType.ElementId: return p.AsElementId().Value;
                default: return null;
            }
        }

        private static JToken? ColorJson(Color c)
        {
            if (!c.IsValid) return null;
            return new JObject { ["r"] = c.Red, ["g"] = c.Green, ["b"] = c.Blue };
        }

        private static double ToMm(double feet) =>
            Math.Round(UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters), 2);

        private static JObject PointMm(XYZ p) => new JObject
        {
            ["x"] = ToMm(p.X),
            ["y"] = ToMm(p.Y),
            ["z"] = ToMm(p.Z)
        };
    }
}
