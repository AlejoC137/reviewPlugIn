using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using View = Autodesk.Revit.DB.View;

namespace RevitToSupabasePlugin
{
    public static class DetailViewExtractor
    {
        public static DetailMetaPrompt ExtractCurrentView(Document doc, View view, UserOptionalInputs? inputs)
        {
            var prompt = new DetailMetaPrompt();
            prompt.Context.ViewName = view.Name;
            prompt.Context.ViewType = view.ViewType.ToString();
            prompt.Context.ViewScale = view.Scale;
            prompt.Context.Discipline = view.Discipline.ToString();
            prompt.OptionalInputs = inputs ?? new UserOptionalInputs();

            // Determinar bounding box de corte de la vista si tiene CropBox activo
            BoundingBoxXYZ cropBox = view.CropBox;
            FilteredElementCollector collector;

            if (view.CropBoxActive && cropBox != null)
            {
                Outline outline = new Outline(cropBox.Min, cropBox.Max);
                BoundingBoxIntersectsFilter boxFilter = new BoundingBoxIntersectsFilter(outline);
                collector = new FilteredElementCollector(doc, view.Id)
                    .WherePasses(boxFilter)
                    .WhereElementIsNotElementType();
            }
            else
            {
                collector = new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType();
            }

            bool hasWall = false;
            bool hasFloor = false;
            bool hasRoof = false;
            bool hasWindow = false;

            ElementId winCatId = new ElementId(BuiltInCategory.OST_Windows);
            ElementId doorCatId = new ElementId(BuiltInCategory.OST_Doors);

            foreach (Element elem in collector)
            {
                if (elem is Wall wall)
                {
                    hasWall = true;
                    prompt.CutElements.Add(ExtractWallData(doc, wall));
                }
                else if (elem is Floor floor)
                {
                    hasFloor = true;
                    prompt.CutElements.Add(ExtractFloorData(doc, floor));
                }
                else if (elem is RoofBase roof)
                {
                    hasRoof = true;
                    prompt.CutElements.Add(ExtractRoofData(doc, roof));
                }
                else if (elem is FamilyInstance fi)
                {
                    if (fi.Category != null && fi.Category.Id == winCatId)
                    {
                        hasWindow = true;
                        prompt.CutElements.Add(ExtractWindowData(fi));
                    }
                    else if (fi.Category != null && fi.Category.Id == doorCatId)
                    {
                        prompt.CutElements.Add(ExtractDoorData(fi));
                    }
                }
            }

            // Diagnóstico y clasificación de arquetipo constructivo
            if (hasWall && hasFloor)
            {
                prompt.CriticalNodesDetected.Add("NODO ARQUETIPO: Encuentro Muro - Losa (Auditar continuidad de aislamiento, barrera hidrófuga y puente térmico de canto)");
            }
            if (hasWall && hasRoof)
            {
                prompt.CriticalNodesDetected.Add("NODO ARQUETIPO: Encuentro Muro - Cubierta / Parapeto (Auditar remate de babeta, pendiente y aislamiento superior)");
            }
            if (hasWall && hasWindow)
            {
                prompt.CriticalNodesDetected.Add("NODO ARQUETIPO: Encuentro Muro - Vano Carpintería (Auditar vierteaguas, dintel con gotero y sellado de jamba)");
            }

            if (prompt.CriticalNodesDetected.Count == 0)
            {
                prompt.CriticalNodesDetected.Add("NODO GENÉRICO: Detalle sin nodos predefinidos directos detectados en la sección.");
            }

            return prompt;
        }

        private static DetectedElementData ExtractWallData(Document doc, Wall wall)
        {
            var data = new DetectedElementData
            {
                ElementId = wall.Id.Value,
                Category = "OST_Walls",
                TypeName = wall.WallType.Name,
                IsStructural = wall.StructuralUsage != Autodesk.Revit.DB.Structure.StructuralWallUsage.NonBearing
            };

            CompoundStructure cs = wall.WallType.GetCompoundStructure();
            if (cs != null)
            {
                foreach (CompoundStructureLayer layer in cs.GetLayers())
                {
                    Material? mat = doc.GetElement(layer.MaterialId) as Material;
                    data.Layers.Add(new LayerData
                    {
                        Function = layer.Function.ToString(),
                        MaterialName = mat != null ? mat.Name : "Sin Material Asignado",
                        ThicknessMm = Math.Round(layer.Width * 304.8, 1)
                    });
                }
            }
            return data;
        }

        private static DetectedElementData ExtractFloorData(Document doc, Floor floor)
        {
            FloorType? fType = doc.GetElement(floor.GetTypeId()) as FloorType;
            var data = new DetectedElementData
            {
                ElementId = floor.Id.Value,
                Category = "OST_Floors",
                TypeName = fType != null ? fType.Name : "Suelo Genérico",
                IsStructural = floor.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL)?.AsInteger() == 1
            };

            if (fType != null)
            {
                CompoundStructure cs = fType.GetCompoundStructure();
                if (cs != null)
                {
                    foreach (CompoundStructureLayer layer in cs.GetLayers())
                    {
                        Material? mat = doc.GetElement(layer.MaterialId) as Material;
                        data.Layers.Add(new LayerData
                        {
                            Function = layer.Function.ToString(),
                            MaterialName = mat != null ? mat.Name : "Sin Material Asignado",
                            ThicknessMm = Math.Round(layer.Width * 304.8, 1)
                        });
                    }
                }
            }
            return data;
        }

        private static DetectedElementData ExtractRoofData(Document doc, RoofBase roof)
        {
            RoofType? rType = doc.GetElement(roof.GetTypeId()) as RoofType;
            var data = new DetectedElementData
            {
                ElementId = roof.Id.Value,
                Category = "OST_Roofs",
                TypeName = rType != null ? rType.Name : "Cubierta Genérica",
                IsStructural = false
            };

            if (rType != null)
            {
                CompoundStructure cs = rType.GetCompoundStructure();
                if (cs != null)
                {
                    foreach (CompoundStructureLayer layer in cs.GetLayers())
                    {
                        Material? mat = doc.GetElement(layer.MaterialId) as Material;
                        data.Layers.Add(new LayerData
                        {
                            Function = layer.Function.ToString(),
                            MaterialName = mat != null ? mat.Name : "Sin Material Asignado",
                            ThicknessMm = Math.Round(layer.Width * 304.8, 1)
                        });
                    }
                }
            }
            return data;
        }

        private static DetectedElementData ExtractWindowData(FamilyInstance fi)
        {
            return new DetectedElementData
            {
                ElementId = fi.Id.Value,
                Category = "OST_Windows",
                TypeName = fi.Symbol != null ? $"{fi.Symbol.FamilyName} - {fi.Symbol.Name}" : "Ventana Genérica",
                IsStructural = false
            };
        }

        private static DetectedElementData ExtractDoorData(FamilyInstance fi)
        {
            return new DetectedElementData
            {
                ElementId = fi.Id.Value,
                Category = "OST_Doors",
                TypeName = fi.Symbol != null ? $"{fi.Symbol.FamilyName} - {fi.Symbol.Name}" : "Puerta Genérica",
                IsStructural = false
            };
        }
    }
}
