using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitToSupabasePlugin
{
    public class CategoryAssignmentResult
    {
        public int SuccessCount { get; set; } = 0;
        public int ErrorCount { get; set; } = 0;
        public List<string> Messages { get; set; } = new List<string>();
        public List<ElementId> ConvertedElementIds { get; set; } = new List<ElementId>();
    }

    public static class CategoryAssignmentService
    {
        public static List<SystemCategoryItem> GetAvailableSystemCategories()
        {
            return new List<SystemCategoryItem>
            {
                new SystemCategoryItem
                {
                    DisplayName = "Suelos / Pisos (OST_Floors)",
                    Category = BuiltInCategory.OST_Floors,
                    IconText = "🪜",
                    Description = "Categoría nativa para acabados de piso, baldosas, morteros y fraguas. Aparece en Tablas de Suelos y filtros de piso."
                },
                new SystemCategoryItem
                {
                    DisplayName = "Muros (OST_Walls)",
                    Category = BuiltInCategory.OST_Walls,
                    IconText = "🧱",
                    Description = "Categoría nativa para enchapes verticales en muros, mampostería y zócalos altos."
                },
                new SystemCategoryItem
                {
                    DisplayName = "Techos (OST_Ceilings)",
                    Category = BuiltInCategory.OST_Ceilings,
                    IconText = "🏠",
                    Description = "Categoría nativa para cielorrasos y acabados superiores."
                },
                new SystemCategoryItem
                {
                    DisplayName = "Cubiertas (OST_Roofs)",
                    Category = BuiltInCategory.OST_Roofs,
                    IconText = "🏗️",
                    Description = "Categoría nativa para terrazas, losas exteriores y cubiertas."
                },
                new SystemCategoryItem
                {
                    DisplayName = "Armazón Estructural (OST_StructuralFraming)",
                    Category = BuiltInCategory.OST_StructuralFraming,
                    IconText = "🔩",
                    Description = "Categoría nativa para vigas, viguetas, láminas Steel Deck y conectores estructurales."
                },
                new SystemCategoryItem
                {
                    DisplayName = "Modelos Genéricos (OST_GenericModel)",
                    Category = BuiltInCategory.OST_GenericModel,
                    IconText = "📦",
                    Description = "Categoría estándar de Revit para elementos sin comportamiento específico de sistema."
                }
            };
        }

        public static List<ScannedElementItem> ScanElements(Document doc, bool pluginOnly, bool activeViewOnly)
        {
            List<ScannedElementItem> items = new List<ScannedElementItem>();
            if (doc == null) return items;

            try
            {
                // Coleccionar por multicategoría
                List<BuiltInCategory> targetCats = new List<BuiltInCategory>
                {
                    BuiltInCategory.OST_GenericModel,
                    BuiltInCategory.OST_Floors,
                    BuiltInCategory.OST_Walls,
                    BuiltInCategory.OST_StructuralFraming
                };
                ElementMulticategoryFilter catFilter = new ElementMulticategoryFilter(targetCats);

                FilteredElementCollector collector = (activeViewOnly && doc.ActiveView != null)
                    ? new FilteredElementCollector(doc, doc.ActiveView.Id)
                    : new FilteredElementCollector(doc);

                var elements = collector.WherePasses(catFilter).WhereElementIsNotElementType().ToElements();

                // También coleccionar DirectShapes explícitamente
                FilteredElementCollector dsCollector = (activeViewOnly && doc.ActiveView != null)
                    ? new FilteredElementCollector(doc, doc.ActiveView.Id)
                    : new FilteredElementCollector(doc);
                var dsElements = dsCollector.OfClass(typeof(DirectShape)).WhereElementIsNotElementType().ToElements();

                HashSet<ElementId> visited = new HashSet<ElementId>();
                List<Element> allCandidates = new List<Element>();

                foreach (var el in elements)
                {
                    if (el != null && visited.Add(el.Id)) allCandidates.Add(el);
                }
                foreach (var el in dsElements)
                {
                    if (el != null && visited.Add(el.Id)) allCandidates.Add(el);
                }

                foreach (Element elem in allCandidates)
                {
                    Category? cat = elem.Category;
                    if (cat == null) continue;

                    string comments = GetParameterString(elem, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    string name = elem.Name ?? string.Empty;
                    bool isPlugin = IsPluginCreatedElement(elem, comments, name);

                    if (pluginOnly && !isPlugin)
                    {
                        continue;
                    }

                    // Determinar rol
                    string pluginRole = DeterminePluginRole(comments, name);

                    // Material
                    string matName = "Por Categoría";
                    ElementId matId = ElementId.InvalidElementId;
                    Parameter pMat = elem.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                    if (pMat != null && pMat.HasValue)
                    {
                        matId = pMat.AsElementId();
                        if (matId != ElementId.InvalidElementId && doc.GetElement(matId) is Material m)
                        {
                            matName = m.Name;
                        }
                    }

                    // Nivel
                    string levelName = "Sin Nivel";
                    ElementId levelId = ElementId.InvalidElementId;
                    if (elem.LevelId != ElementId.InvalidElementId)
                    {
                        levelId = elem.LevelId;
                        if (doc.GetElement(levelId) is Level lvl) levelName = lvl.Name;
                    }
                    else
                    {
                        Parameter pLvl = elem.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)
                                      ?? elem.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);
                        if (pLvl != null && pLvl.HasValue)
                        {
                            levelId = pLvl.AsElementId();
                            if (levelId != ElementId.InvalidElementId && doc.GetElement(levelId) is Level lvl) levelName = lvl.Name;
                        }
                    }

                    // Volumen y Área aproximada
                    double volM3 = 0.0;
                    double areaM2 = 0.0;
                    ExtractGeometryMetrics(elem, out volM3, out areaM2);

                    BuiltInCategory bic = BuiltInCategory.INVALID;
                    try { bic = cat.BuiltInCategory; } catch { }

                    items.Add(new ScannedElementItem
                    {
                        Id = elem.Id,
                        Name = name,
                        ElementTypeKind = elem is DirectShape ? "DirectShape" : (elem is FamilyInstance ? "FamilyInstance" : elem.GetType().Name),
                        CurrentCategoryName = cat.Name,
                        CurrentBuiltInCategory = bic,
                        Comments = comments,
                        MaterialName = matName,
                        MaterialId = matId,
                        LevelName = levelName,
                        LevelId = levelId,
                        VolumeM3 = Math.Round(volM3, 4),
                        AreaM2 = Math.Round(areaM2, 3),
                        IsPluginElement = isPlugin,
                        PluginRole = pluginRole,
                        ElementRef = elem,
                        IsSelected = true
                    });
                }
            }
            catch { }

            return items.OrderByDescending(i => i.IsPluginElement).ThenBy(i => i.PluginRole).ToList();
        }

        private static bool IsPluginCreatedElement(Element elem, string comments, string name)
        {
            string upperComm = comments?.ToUpperInvariant() ?? "";
            string upperName = name?.ToUpperInvariant() ?? "";

            if (upperComm.StartsWith("ENCHAPE-") ||
                upperComm.StartsWith("STEELDECK-") ||
                upperComm.StartsWith("LADRILLO-") ||
                upperComm.StartsWith("PEGA-") ||
                upperComm.StartsWith("ZOCALO-"))
            {
                return true;
            }

            if (upperName.Contains("BALDOSA") ||
                upperName.Contains("TILES BASE") ||
                upperName.Contains("BASE TILES") ||
                upperName.Contains("MORTERO BASE") ||
                upperName.Contains("CAMA DE PEGA") ||
                upperName.Contains("FRAGUA DE JUNTA") ||
                upperName.Contains("JUNTA PERIMETRAL") ||
                upperName.Contains("ZOCALO PERIMETRAL") ||
                upperName.Contains("LÁMINA STEEL DECK") ||
                upperName.Contains("CONCRETO LOSA MIXTA") ||
                upperName.Contains("LADRILLOBASE"))
            {
                return true;
            }

            return false;
        }

        private static string DeterminePluginRole(string comments, string name)
        {
            string upperComm = comments?.ToUpperInvariant() ?? "";
            string upperName = name?.ToUpperInvariant() ?? "";

            if (upperComm.Contains("BALDOSA-ENTERA") || upperName.Contains("TILES BASE") || upperName.Contains("BALDOSA ENTERA"))
                return "Baldosa Entera";
            if (upperComm.Contains("BALDOSA-CORTADA") || upperName.Contains("BALDOSA CORTADA"))
                return "Baldosa Cortada";
            if (upperComm.Contains("MORTERO-BASE") || upperName.Contains("MORTERO BASE"))
                return "Mortero Base / Nivelación";
            if (upperComm.Contains("PEGA-CAMA") || upperName.Contains("CAMA DE PEGA"))
                return "Cama de Pega / Adhesivo";
            if (upperComm.Contains("PEGA-FRAGUA") || upperComm.Contains("JUNTA-PERIMETRAL") || upperName.Contains("FRAGUA") || upperName.Contains("JUNTA"))
                return "Fragua / Junta 3D";
            if (upperComm.Contains("ZOCALO") || upperName.Contains("ZOCALO"))
                return "Zócalo Perimetral";
            if (upperComm.Contains("STEELDECK-LAMINA"))
                return "Lámina Steel Deck";
            if (upperComm.Contains("STEELDECK-CONCRETO"))
                return "Concreto Capa Compresión";
            if (upperComm.Contains("STEELDECK-STUD"))
                return "Conector Stud";

            return "Elemento Genérico";
        }

        private static void ExtractGeometryMetrics(Element elem, out double volumeM3, out double areaM2)
        {
            volumeM3 = 0.0;
            areaM2 = 0.0;
            if (elem == null) return;

            try
            {
                Options opt = new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false };
                GeometryElement geom = elem.get_Geometry(opt);
                if (geom == null) return;

                double totalVolFt3 = 0.0;
                double totalAreaFt2 = 0.0;

                Action<Solid> processSolid = (s) =>
                {
                    if (s != null && s.Volume > 1e-6)
                    {
                        totalVolFt3 += s.Volume;
                        if (s.Faces != null)
                        {
                            foreach (Face face in s.Faces)
                            {
                                if (face != null) totalAreaFt2 += face.Area;
                            }
                        }
                    }
                };

                foreach (GeometryObject obj in geom)
                {
                    if (obj is Solid solid)
                    {
                        processSolid(solid);
                    }
                    else if (obj is GeometryInstance gi)
                    {
                        GeometryElement instGeom = gi.GetInstanceGeometry();
                        if (instGeom != null)
                        {
                            foreach (GeometryObject instObj in instGeom)
                            {
                                if (instObj is Solid sInst) processSolid(sInst);
                            }
                        }
                    }
                }

                volumeM3 = totalVolFt3 * 0.0283168466;
                areaM2 = (totalAreaFt2 / 2.0) * 0.09290304;
            }
            catch { }
        }

        public static DirectShapeType? GetOrCreateDirectShapeType(Document doc, ElementId categoryId, string typeName)
        {
            if (doc == null) return null;
            try
            {
                var existing = new FilteredElementCollector(doc)
                    .OfClass(typeof(DirectShapeType))
                    .Cast<DirectShapeType>()
                    .FirstOrDefault(t => t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase) && t.Category != null && t.Category.Id == categoryId);

                if (existing != null) return existing;

                DirectShapeType newType = DirectShapeType.Create(doc, typeName, categoryId);
                return newType;
            }
            catch
            {
                return null;
            }
        }

        public static CategoryAssignmentResult ReassignCategoryToElements(
            Document doc,
            List<ScannedElementItem> itemsToConvert,
            BuiltInCategory targetCategory,
            bool preserveMetadata)
        {
            var result = new CategoryAssignmentResult();
            if (doc == null || itemsToConvert == null || itemsToConvert.Count == 0) return result;

            ElementId targetCatId = new ElementId(targetCategory);
            if (!DirectShape.IsValidCategoryId(targetCatId, doc))
            {
                result.ErrorCount = itemsToConvert.Count;
                result.Messages.Add($"La categoría seleccionada no es admitida por Revit para elementos de forma directa.");
                return result;
            }

            using (Transaction t = new Transaction(doc, "Asignar Categoría de Sistema a Elementos"))
            {
                t.Start();

                foreach (var item in itemsToConvert)
                {
                    try
                    {
                        Element? elem = doc.GetElement(item.Id);
                        if (elem == null)
                        {
                            result.ErrorCount++;
                            continue;
                        }

                        List<GeometryObject> solids = ExtractSolidsFromElement(elem);

                        if (solids.Count > 0)
                        {
                            DirectShape newDs = DirectShape.CreateElement(doc, targetCatId);
                            newDs.SetShape(solids);

                            string baseName = !string.IsNullOrEmpty(item.Name) ? item.Name : "Elemento Reasignado";
                            newDs.Name = baseName;

                            // Asociar DirectShapeType para habilitar "Editar Tipo" en Revit
                            string typeName = !string.IsNullOrEmpty(item.PluginRole) && item.PluginRole != "Elemento Genérico"
                                ? $"Piso - {item.PluginRole}"
                                : baseName;

                            DirectShapeType? dsType = GetOrCreateDirectShapeType(doc, targetCatId, typeName);
                            if (dsType != null)
                            {
                                try { newDs.SetTypeId(dsType.Id); } catch { }
                            }

                            if (preserveMetadata)
                            {
                                CopyStandardParameters(elem, newDs);
                                if (item.MaterialId != ElementId.InvalidElementId)
                                {
                                    Parameter pMat = newDs.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                                    if (pMat != null && !pMat.IsReadOnly) pMat.Set(item.MaterialId);
                                }
                            }

                            result.ConvertedElementIds.Add(newDs.Id);
                            doc.Delete(elem.Id);
                            result.SuccessCount++;
                        }
                        else
                        {
                            result.ErrorCount++;
                            result.Messages.Add($"Elemento {item.Id}: No se pudo extraer geometría sólida para reasignar.");
                        }
                    }
                    catch (Exception ex)
                    {
                        result.ErrorCount++;
                        result.Messages.Add($"Elemento {item.Id}: {ex.Message}");
                    }
                }

                t.Commit();
            }

            return result;
        }

        public static FloorType? GetOrCreateCustomFloorType(Document doc, string typeName, double thicknessFt, ElementId materialId)
        {
            if (doc == null) return null;
            try
            {
                var existing = new FilteredElementCollector(doc)
                    .OfClass(typeof(FloorType))
                    .Cast<FloorType>()
                    .FirstOrDefault(ft => ft.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

                if (existing != null) return existing;

                FloorType? baseFloorType = new FilteredElementCollector(doc)
                    .OfClass(typeof(FloorType))
                    .Cast<FloorType>()
                    .FirstOrDefault();

                if (baseFloorType == null) return null;

                FloorType? newFloorType = baseFloorType.Duplicate(typeName) as FloorType;
                if (newFloorType != null)
                {
                    CompoundStructure cs = newFloorType.GetCompoundStructure();
                    if (cs != null)
                    {
                        var layers = cs.GetLayers();
                        if (layers.Count > 0)
                        {
                            if (thicknessFt > 0.001) layers[0].Width = thicknessFt;
                            if (materialId != ElementId.InvalidElementId) layers[0].MaterialId = materialId;
                            cs.SetLayers(layers);
                            newFloorType.SetCompoundStructure(cs);
                        }
                    }
                    return newFloorType;
                }
            }
            catch { }
            return null;
        }

        public static CategoryAssignmentResult ConvertToNativeFloors(
            Document doc,
            List<ScannedElementItem> itemsToConvert,
            ElementId floorTypeId,
            ElementId fallbackLevelId,
            bool deleteOriginals)
        {
            var result = new CategoryAssignmentResult();
            if (doc == null || itemsToConvert == null || itemsToConvert.Count == 0) return result;

            if (floorTypeId == ElementId.InvalidElementId)
            {
                result.ErrorCount = itemsToConvert.Count;
                result.Messages.Add("Debe seleccionar un Tipo de Piso (FloorType) válido para la conversión a suelo nativo.");
                return result;
            }

            using (Transaction t = new Transaction(doc, "Convertir a Suelos Nativos (Floors)"))
            {
                t.Start();

                foreach (var item in itemsToConvert)
                {
                    try
                    {
                        Element? elem = doc.GetElement(item.Id);
                        if (elem == null)
                        {
                            result.ErrorCount++;
                            continue;
                        }

                        CurveLoop? horizontalLoop = ExtractHorizontalBoundary(elem, out double elevationZ);
                        if (horizontalLoop == null || horizontalLoop.IsOpen())
                        {
                            result.ErrorCount++;
                            result.Messages.Add($"Elemento {item.Id}: No se encontró un contorno horizontal cerrado para generar el suelo.");
                            continue;
                        }

                        ElementId targetLevelId = item.LevelId != ElementId.InvalidElementId ? item.LevelId : fallbackLevelId;
                        if (targetLevelId == ElementId.InvalidElementId)
                        {
                            Level? nearestLevel = FindNearestLevel(doc, elevationZ);
                            if (nearestLevel != null) targetLevelId = nearestLevel.Id;
                        }

                        if (targetLevelId == ElementId.InvalidElementId)
                        {
                            result.ErrorCount++;
                            result.Messages.Add($"Elemento {item.Id}: No se encontró un nivel de referencia.");
                            continue;
                        }

                        // Proyectar loop al plano Z del nivel
                        if (doc.GetElement(targetLevelId) is Level lvl)
                        {
                            double levelElevation = lvl.Elevation;
                            double heightOffset = elevationZ - levelElevation;

                            CurveLoop levelLoop = ProjectLoopToZ(horizontalLoop, levelElevation);

                            Floor newFloor = Floor.Create(doc, new List<CurveLoop> { levelLoop }, floorTypeId, targetLevelId);
                            if (newFloor != null)
                            {
                                Parameter pOffset = newFloor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
                                if (pOffset != null && !pOffset.IsReadOnly)
                                {
                                    pOffset.Set(heightOffset);
                                }

                                Parameter pComm = newFloor.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                                if (pComm != null && !pComm.IsReadOnly)
                                {
                                    pComm.Set(!string.IsNullOrEmpty(item.Comments) ? item.Comments : "ENCHAPE-SUELO-NATIVO");
                                }

                                result.ConvertedElementIds.Add(newFloor.Id);
                                result.SuccessCount++;

                                if (deleteOriginals)
                                {
                                    doc.Delete(elem.Id);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        result.ErrorCount++;
                        result.Messages.Add($"Elemento {item.Id}: {ex.Message}");
                    }
                }

                t.Commit();
            }

            return result;
        }

        public static ViewSchedule? CreateFloorSchedule(Document doc, string scheduleName = "📊 Cómputo de Suelos y Enchapes (OST_Floors)")
        {
            if (doc == null) return null;
            try
            {
                ElementId floorCatId = new ElementId(BuiltInCategory.OST_Floors);
                var existing = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .FirstOrDefault(vs => vs.Name.Equals(scheduleName, StringComparison.OrdinalIgnoreCase));

                if (existing != null) return existing;

                ViewSchedule sched = ViewSchedule.CreateSchedule(doc, floorCatId);
                sched.Name = scheduleName;

                ScheduleDefinition def = sched.Definition;
                var schedulableFields = def.GetSchedulableFields();

                // 1. Familia y Tipo
                var fFam = schedulableFields.FirstOrDefault(sf => sf.ParameterId == new ElementId(BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM));
                if (fFam != null)
                {
                    var sf = def.AddField(fFam);
                    sf.ColumnHeading = "Familia y Tipo";
                }

                // 2. Comentarios
                var fComm = schedulableFields.FirstOrDefault(sf => sf.ParameterId == new ElementId(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS));
                if (fComm != null)
                {
                    var sf = def.AddField(fComm);
                    sf.ColumnHeading = "Rol Constructivo / Detalle";
                }

                // 3. Nivel
                var fLvl = schedulableFields.FirstOrDefault(sf => sf.ParameterId == new ElementId(BuiltInParameter.LEVEL_PARAM));
                if (fLvl != null) def.AddField(fLvl);

                // 4. Recuento / Cantidad
                var fCount = schedulableFields.FirstOrDefault(sf => sf.FieldType == ScheduleFieldType.Count);
                if (fCount != null)
                {
                    var sf = def.AddField(fCount);
                    sf.ColumnHeading = "Cantidad (Pzas)";
                }

                // 5. Área
                var fArea = schedulableFields.FirstOrDefault(sf => sf.ParameterId == new ElementId(BuiltInParameter.HOST_AREA_COMPUTED));
                if (fArea != null)
                {
                    var sf = def.AddField(fArea);
                    sf.ColumnHeading = "Área Total";
                }

                return sched;
            }
            catch
            {
                return null;
            }
        }

        private static CurveLoop ProjectLoopToZ(CurveLoop originalLoop, double targetZ)
        {
            CurveLoop newLoop = new CurveLoop();
            foreach (Curve c in originalLoop)
            {
                XYZ p0 = c.GetEndPoint(0);
                XYZ p1 = c.GetEndPoint(1);
                XYZ projP0 = new XYZ(p0.X, p0.Y, targetZ);
                XYZ projP1 = new XYZ(p1.X, p1.Y, targetZ);

                if (projP0.DistanceTo(projP1) > 1e-6)
                {
                    newLoop.Append(Line.CreateBound(projP0, projP1));
                }
            }
            return newLoop;
        }

        private static CurveLoop? ExtractHorizontalBoundary(Element elem, out double elevationZ)
        {
            elevationZ = 0.0;
            if (elem == null) return null;
            try
            {
                Options opt = new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = true };
                GeometryElement geom = elem.get_Geometry(opt);
                if (geom == null) return null;

                List<Solid> solids = new List<Solid>();
                foreach (GeometryObject obj in geom)
                {
                    if (obj is Solid s && s.Volume > 1e-6) solids.Add(s);
                    else if (obj is GeometryInstance gi)
                    {
                        GeometryElement instG = gi.GetInstanceGeometry();
                        if (instG != null)
                        {
                            foreach (GeometryObject instObj in instG)
                            {
                                if (instObj is Solid sInst && sInst.Volume > 1e-6) solids.Add(sInst);
                            }
                        }
                    }
                }

                if (solids.Count == 0) return null;

                PlanarFace? bestHorizontalFace = null;
                double maxZ = double.MinValue;

                foreach (Solid s in solids)
                {
                    if (s.Faces == null) continue;
                    foreach (Face f in s.Faces)
                    {
                        if (f is PlanarFace pf)
                        {
                            if (Math.Abs(pf.FaceNormal.Z - 1.0) < 0.05)
                            {
                                if (pf.Origin.Z > maxZ)
                                {
                                    maxZ = pf.Origin.Z;
                                    bestHorizontalFace = pf;
                                }
                            }
                        }
                    }
                }

                if (bestHorizontalFace != null)
                {
                    elevationZ = bestHorizontalFace.Origin.Z;
                    var loops = bestHorizontalFace.GetEdgesAsCurveLoops();
                    if (loops != null && loops.Count > 0)
                    {
                        return loops.OrderByDescending(l => l.GetExactLength()).FirstOrDefault();
                    }
                }

                BoundingBoxXYZ bbox = elem.get_BoundingBox(null);
                if (bbox != null)
                {
                    elevationZ = bbox.Max.Z;
                    XYZ c1 = new XYZ(bbox.Min.X, bbox.Min.Y, elevationZ);
                    XYZ c2 = new XYZ(bbox.Max.X, bbox.Min.Y, elevationZ);
                    XYZ c3 = new XYZ(bbox.Max.X, bbox.Max.Y, elevationZ);
                    XYZ c4 = new XYZ(bbox.Min.X, bbox.Max.Y, elevationZ);

                    CurveLoop loop = new CurveLoop();
                    loop.Append(Line.CreateBound(c1, c2));
                    loop.Append(Line.CreateBound(c2, c3));
                    loop.Append(Line.CreateBound(c3, c4));
                    loop.Append(Line.CreateBound(c4, c1));
                    return loop;
                }
            }
            catch { }

            return null;
        }

        private static Level? FindNearestLevel(Document doc, double elevationZ)
        {
            if (doc == null) return null;
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => Math.Abs(l.Elevation - elevationZ))
                .FirstOrDefault();
        }

        private static List<GeometryObject> ExtractSolidsFromElement(Element elem)
        {
            List<GeometryObject> solids = new List<GeometryObject>();
            if (elem == null) return solids;
            try
            {
                Options opt = new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = true };
                GeometryElement geom = elem.get_Geometry(opt);
                if (geom == null) return solids;

                foreach (GeometryObject obj in geom)
                {
                    if (obj is Solid s && s.Volume > 1e-6)
                    {
                        solids.Add(s);
                    }
                    else if (obj is GeometryInstance gi)
                    {
                        GeometryElement instG = gi.GetInstanceGeometry();
                        if (instG != null)
                        {
                            foreach (GeometryObject instObj in instG)
                            {
                                if (instObj is Solid sInst && sInst.Volume > 1e-6)
                                {
                                    solids.Add(sInst);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return solids;
        }

        private static void CopyStandardParameters(Element source, Element target)
        {
            if (source == null || target == null) return;
            try
            {
                string comm = GetParameterString(source, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (!string.IsNullOrEmpty(comm))
                {
                    Parameter p = target.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    if (p != null && !p.IsReadOnly) p.Set(comm);
                }

                string desc = GetParameterString(source, BuiltInParameter.ALL_MODEL_DESCRIPTION);
                if (!string.IsNullOrEmpty(desc))
                {
                    Parameter p = target.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION);
                    if (p != null && !p.IsReadOnly) p.Set(desc);
                }

                string mark = GetParameterString(source, BuiltInParameter.ALL_MODEL_MARK);
                if (!string.IsNullOrEmpty(mark))
                {
                    Parameter p = target.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                    if (p != null && !p.IsReadOnly) p.Set(mark);
                }

                Parameter pMatSrc = source.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                if (pMatSrc != null && pMatSrc.HasValue)
                {
                    Parameter pMatTgt = target.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                    if (pMatTgt != null && !pMatTgt.IsReadOnly) pMatTgt.Set(pMatSrc.AsElementId());
                }
            }
            catch { }
        }

        private static string GetParameterString(Element elem, BuiltInParameter bip)
        {
            if (elem == null) return string.Empty;
            try
            {
                Parameter p = elem.get_Parameter(bip);
                if (p != null && p.HasValue)
                {
                    return p.AsString() ?? p.AsValueString() ?? string.Empty;
                }
            }
            catch { }
            return string.Empty;
        }
    }
}
