using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Newtonsoft.Json;

namespace RevitToSupabasePlugin
{
    public class IfcJsonHeader
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "ifcJSON";

        [JsonProperty("version")]
        public string Version { get; set; } = "4.0.0";

        [JsonProperty("originatingSystem")]
        public string OriginatingSystem { get; set; } = "RevitToSupabasePlugin / reviewPlugIn";

        [JsonProperty("timeStamp")]
        public string TimeStamp { get; set; } = DateTime.UtcNow.ToString("o");

        [JsonProperty("data")]
        public List<IfcJsonObject> Data { get; set; } = new List<IfcJsonObject>();
    }

    public class IfcJsonObject
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "IfcProduct";

        [JsonProperty("globalId")]
        public string GlobalId { get; set; } = Guid.NewGuid().ToString();

        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string? Name { get; set; }

        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string? Description { get; set; }

        [JsonProperty("objectType", NullValueHandling = NullValueHandling.Ignore)]
        public string? ObjectType { get; set; }

        [JsonProperty("elevation", NullValueHandling = NullValueHandling.Ignore)]
        public double? Elevation { get; set; }

        [JsonProperty("refLatitude", NullValueHandling = NullValueHandling.Ignore)]
        public double[]? RefLatitude { get; set; }

        [JsonProperty("refLongitude", NullValueHandling = NullValueHandling.Ignore)]
        public double[]? RefLongitude { get; set; }

        [JsonProperty("area", NullValueHandling = NullValueHandling.Ignore)]
        public double? Area { get; set; }

        [JsonProperty("volume", NullValueHandling = NullValueHandling.Ignore)]
        public double? Volume { get; set; }

        [JsonProperty("level", NullValueHandling = NullValueHandling.Ignore)]
        public string? Level { get; set; }

        [JsonProperty("category", NullValueHandling = NullValueHandling.Ignore)]
        public string? Category { get; set; }

        [JsonProperty("hasProperties", NullValueHandling = NullValueHandling.Ignore)]
        public List<IfcJsonPropertySet>? HasProperties { get; set; }

        [JsonProperty("geometry", NullValueHandling = NullValueHandling.Ignore)]
        public IfcJsonGeometry? Geometry { get; set; }
    }

    public class IfcJsonPropertySet
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "IfcPropertySet";

        [JsonProperty("globalId")]
        public string GlobalId { get; set; } = Guid.NewGuid().ToString();

        [JsonProperty("name")]
        public string Name { get; set; } = "Pset_RevitParameters";

        [JsonProperty("hasProperties")]
        public List<IfcJsonPropertySingleValue> HasProperties { get; set; } = new List<IfcJsonPropertySingleValue>();
    }

    public class IfcJsonPropertySingleValue
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "IfcPropertySingleValue";

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("nominalValue")]
        public object? NominalValue { get; set; }
    }

    public class IfcJsonGeometry
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "FacetedBrep";

        [JsonProperty("position", NullValueHandling = NullValueHandling.Ignore)]
        public double[]? Position { get; set; }

        [JsonProperty("vertices", NullValueHandling = NullValueHandling.Ignore)]
        public double[]? Vertices { get; set; }

        [JsonProperty("indices", NullValueHandling = NullValueHandling.Ignore)]
        public int[]? Indices { get; set; }

        [JsonProperty("normals", NullValueHandling = NullValueHandling.Ignore)]
        public double[]? Normals { get; set; }

        [JsonProperty("colorHex", NullValueHandling = NullValueHandling.Ignore)]
        public string? ColorHex { get; set; }
    }

    public static class IfcJsonExporterService
    {
        public static IfcJsonHeader ExportDocumentToIfcJson(Document doc, string userEmail = "")
        {
            var header = new IfcJsonHeader();
            var dataList = new List<IfcJsonObject>();

            // 1. IfcProject & IfcBuilding
            var projectInfo = doc.ProjectInformation;
            string projectName = projectInfo != null ? projectInfo.Name : doc.Title;
            string projectNumber = projectInfo != null ? projectInfo.Number : "";
            string buildingName = projectInfo != null ? projectInfo.BuildingName : "Main Building";

            var ifcProject = new IfcJsonObject
            {
                Type = "IfcProject",
                GlobalId = Guid.NewGuid().ToString(),
                Name = string.IsNullOrWhiteSpace(projectName) ? doc.Title : projectName,
                Description = $"Project Number: {projectNumber} | Created by {userEmail}",
                ObjectType = "Revit Project"
            };
            dataList.Add(ifcProject);

            var ifcBuilding = new IfcJsonObject
            {
                Type = "IfcBuilding",
                GlobalId = Guid.NewGuid().ToString(),
                Name = string.IsNullOrWhiteSpace(buildingName) ? "Edificio Principal" : buildingName,
                ObjectType = "Building"
            };
            dataList.Add(ifcBuilding);

            // 2. IfcSite
            try
            {
                var site = doc.SiteLocation;
                if (site != null)
                {
                    var ifcSite = new IfcJsonObject
                    {
                        Type = "IfcSite",
                        GlobalId = Guid.NewGuid().ToString(),
                        Name = site.PlaceName ?? "Sitio Georreferenciado",
                        RefLatitude = new double[] { site.Latitude * 180.0 / Math.PI },
                        RefLongitude = new double[] { site.Longitude * 180.0 / Math.PI },
                        Elevation = site.Elevation * 0.3048
                    };
                    dataList.Add(ifcSite);
                }
            }
            catch { }

            // 3. IfcBuildingStorey (Levels)
            var levelsCollector = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .WhereElementIsNotElementType()
                .Cast<Level>();

            foreach (var lvl in levelsCollector)
            {
                var storey = new IfcJsonObject
                {
                    Type = "IfcBuildingStorey",
                    GlobalId = lvl.UniqueId,
                    Name = lvl.Name,
                    Elevation = Math.Round(lvl.Elevation * 0.3048, 3)
                };
                dataList.Add(storey);
            }

            // 4. IfcSpace (Rooms)
            var roomsCollector = new FilteredElementCollector(doc)
                .OfClass(typeof(SpatialElement))
                .WhereElementIsNotElementType();

            foreach (var elem in roomsCollector)
            {
                if (elem is Room room && room.Area > 0)
                {
                    string dept = room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT)?.AsString() ?? "Room";
                    var ifcSpace = new IfcJsonObject
                    {
                        Type = "IfcSpace",
                        GlobalId = room.UniqueId,
                        Name = room.Name,
                        Description = $"Room #{room.Number}",
                        Level = room.Level?.Name ?? "",
                        Area = Math.Round(room.Area * 0.092903, 2),
                        Volume = Math.Round(room.Volume * 0.0283168, 2),
                        ObjectType = string.IsNullOrWhiteSpace(dept) ? "Room" : dept
                    };

                    // Parameter properties
                    var pset = ExtractPropertySet(room);
                    if (pset.HasProperties.Count > 0)
                    {
                        ifcSpace.HasProperties = new List<IfcJsonPropertySet> { pset };
                    }

                    dataList.Add(ifcSpace);
                }
            }

            // 5. Physical Building Elements
            var categoriesToExport = new BuiltInCategory[]
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Columns,
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_Doors,
                BuiltInCategory.OST_Windows,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Roofs,
                BuiltInCategory.OST_Ceilings,
                BuiltInCategory.OST_CurtainWallPanels,
                BuiltInCategory.OST_CurtainWallMullions,
                BuiltInCategory.OST_Furniture,
                BuiltInCategory.OST_Planting,
                BuiltInCategory.OST_Stairs,
                BuiltInCategory.OST_StairsRailing,
                BuiltInCategory.OST_SpecialityEquipment,
                BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_DuctCurves,
                BuiltInCategory.OST_MechanicalEquipment
            };

            var options = new Options
            {
                DetailLevel = ViewDetailLevel.Fine,
                ComputeReferences = true
            };

            foreach (var cat in categoriesToExport)
            {
                var collector = new FilteredElementCollector(doc)
                    .OfCategory(cat)
                    .WhereElementIsNotElementType();

                foreach (var elem in collector)
                {
                    if (elem.Category == null) continue;

                    string ifcType = MapRevitCategoryToIfcType(elem.Category.Id);
                    string elemLevelName = elem.LevelId != ElementId.InvalidElementId && doc.GetElement(elem.LevelId) is Level l ? l.Name : "";

                    var ifcElem = new IfcJsonObject
                    {
                        Type = ifcType,
                        GlobalId = elem.UniqueId,
                        Name = elem.Name,
                        Category = elem.Category.Name,
                        Level = elemLevelName,
                        ObjectType = elem.Name
                    };

                    // Extract PropertySet
                    var pset = ExtractPropertySet(elem);
                    if (pset.HasProperties.Count > 0)
                    {
                        ifcElem.HasProperties = new List<IfcJsonPropertySet> { pset };
                    }

                    // Extract 3D Geometry mesh real en coordenadas de mundo
                    try
                    {
                        var geomElem = elem.get_Geometry(options);
                        if (geomElem != null)
                        {
                            var meshData = ExtractMeshFromGeometry(geomElem);
                            if (meshData != null && meshData.Vertices != null && meshData.Vertices.Length > 0)
                            {
                                meshData.ColorHex = GetCategoryColor(elem.Category.Name);
                                ifcElem.Geometry = meshData;
                            }
                        }
                    }
                    catch { }

                    dataList.Add(ifcElem);
                }
            }

            header.Data = dataList;
            return header;
        }

        private static string GetCategoryColor(string categoryName)
        {
            string cat = categoryName.ToLower();
            if (cat.Contains("wall") || cat.Contains("muro")) return "#E0E0E0";
            if (cat.Contains("window") || cat.Contains("ventana") || cat.Contains("glazing") || cat.Contains("vidrio")) return "#88CCFF";
            if (cat.Contains("door") || cat.Contains("puerta")) return "#C0A080";
            if (cat.Contains("floor") || cat.Contains("suelo") || cat.Contains("piso") || cat.Contains("slab")) return "#D8D8D8";
            if (cat.Contains("roof") || cat.Contains("cubierta") || cat.Contains("techo")) return "#B0B0B0";
            if (cat.Contains("plant") || cat.Contains("vegeta")) return "#66BB66";
            if (cat.Contains("furniture") || cat.Contains("mueble")) return "#D2B48C";
            return "#D0D0D0";
        }

        private static string MapRevitCategoryToIfcType(ElementId catId)
        {
            long id = catId.Value;
            if (id == (long)BuiltInCategory.OST_Walls) return "IfcWall";
            if (id == (long)BuiltInCategory.OST_Columns || id == (long)BuiltInCategory.OST_StructuralColumns) return "IfcColumn";
            if (id == (long)BuiltInCategory.OST_Doors) return "IfcDoor";
            if (id == (long)BuiltInCategory.OST_Windows || id == (long)BuiltInCategory.OST_CurtainWallPanels) return "IfcWindow";
            if (id == (long)BuiltInCategory.OST_CurtainWallMullions) return "IfcMember";
            if (id == (long)BuiltInCategory.OST_Floors) return "IfcSlab";
            if (id == (long)BuiltInCategory.OST_Roofs) return "IfcRoof";
            if (id == (long)BuiltInCategory.OST_Stairs) return "IfcStair";
            if (id == (long)BuiltInCategory.OST_StairsRailing) return "IfcRailing";
            if (id == (long)BuiltInCategory.OST_PipeCurves) return "IfcPipeSegment";
            if (id == (long)BuiltInCategory.OST_DuctCurves) return "IfcDuctSegment";
            if (id == (long)BuiltInCategory.OST_Furniture) return "IfcFurnishingElement";
            if (id == (long)BuiltInCategory.OST_Planting) return "IfcElementProxy";
            return "IfcBuildingElementProxy";
        }

        private static IfcJsonPropertySet ExtractPropertySet(Element elem)
        {
            var pset = new IfcJsonPropertySet
            {
                Name = "Pset_RevitParameters"
            };

            foreach (Parameter p in elem.Parameters)
            {
                if (p == null || !p.HasValue || string.IsNullOrWhiteSpace(p.Definition.Name)) continue;
                string valStr = p.AsValueString() ?? p.AsString() ?? "";
                if (string.IsNullOrWhiteSpace(valStr) && p.StorageType == StorageType.Double)
                {
                    valStr = Math.Round(p.AsDouble(), 4).ToString();
                }
                else if (string.IsNullOrWhiteSpace(valStr) && p.StorageType == StorageType.Integer)
                {
                    valStr = p.AsInteger().ToString();
                }

                if (!string.IsNullOrWhiteSpace(valStr))
                {
                    pset.HasProperties.Add(new IfcJsonPropertySingleValue
                    {
                        Name = p.Definition.Name,
                        NominalValue = valStr
                    });
                }
            }

            return pset;
        }

        private static IfcJsonGeometry? ExtractMeshFromGeometry(GeometryElement geomElem, Transform? parentTransform = null)
        {
            var vertices = new List<double>();
            var indices = new List<int>();
            var normals = new List<double>();
            int vertIndexOffset = 0;

            Action<Mesh, Transform?> processRevitMesh = (Mesh mesh, Transform? xform) =>
            {
                if (mesh == null || mesh.Vertices == null || mesh.Vertices.Count == 0) return;

                int baseIdx = vertIndexOffset;
                for (int i = 0; i < mesh.Vertices.Count; i++)
                {
                    XYZ pt = mesh.Vertices[i];
                    if (xform != null) pt = xform.OfPoint(pt);

                    // Conversión de pies a metros y cambio a ejes WebGL/Three.js (X=East, Y=Up, Z=-North)
                    vertices.Add(Math.Round(pt.X * 0.3048, 5));
                    vertices.Add(Math.Round(pt.Z * 0.3048, 5));
                    vertices.Add(Math.Round(-pt.Y * 0.3048, 5));
                }

                for (int i = 0; i < mesh.NumTriangles; i++)
                {
                    MeshTriangle tri = mesh.get_Triangle(i);
                    indices.Add(baseIdx + (int)tri.get_Index(0));
                    indices.Add(baseIdx + (int)tri.get_Index(1));
                    indices.Add(baseIdx + (int)tri.get_Index(2));

                    XYZ v0 = tri.get_Vertex(0);
                    XYZ v1 = tri.get_Vertex(1);
                    XYZ v2 = tri.get_Vertex(2);
                    XYZ norm = (v1 - v0).CrossProduct(v2 - v0);
                    if (norm.GetLength() > 0.00001) norm = norm.Normalize();
                    if (xform != null) norm = xform.OfVector(norm).Normalize();

                    normals.Add(Math.Round(norm.X, 4));
                    normals.Add(Math.Round(norm.Z, 4));
                    normals.Add(Math.Round(-norm.Y, 4));
                }

                vertIndexOffset += mesh.Vertices.Count;
            };

            Action<GeometryElement, Transform?>? processGeomElem = null;
            processGeomElem = (GeometryElement gElem, Transform? xform) =>
            {
                if (gElem == null) return;
                foreach (GeometryObject gObj in gElem)
                {
                    if (gObj is Solid solid && solid.Faces != null && solid.Faces.Size > 0)
                    {
                        foreach (Face face in solid.Faces)
                        {
                            Mesh faceMesh = face.Triangulate();
                            if (faceMesh != null) processRevitMesh(faceMesh, xform);
                        }
                    }
                    else if (gObj is Mesh directMesh)
                    {
                        processRevitMesh(directMesh, xform);
                    }
                    else if (gObj is GeometryInstance gInst)
                    {
                        Transform instXform = gInst.Transform;
                        Transform combinedXform = xform != null ? xform.Multiply(instXform) : instXform;

                        GeometryElement instGeom = gInst.GetInstanceGeometry();
                        int currentVertCount = vertices.Count;
                        if (instGeom != null)
                        {
                            processGeomElem!(instGeom, xform);
                        }
                        if (vertices.Count == currentVertCount)
                        {
                            GeometryElement symGeom = gInst.GetSymbolGeometry();
                            if (symGeom != null)
                            {
                                processGeomElem!(symGeom, combinedXform);
                            }
                        }
                    }
                }
            };

            processGeomElem(geomElem, parentTransform);

            if (vertices.Count == 0) return null;

            return new IfcJsonGeometry
            {
                Type = "FacetedBrep",
                Vertices = vertices.ToArray(),
                Indices = indices.ToArray(),
                Normals = normals.ToArray(),
                ColorHex = "#CCCCCC"
            };
        }

        public static string ToJsonString(IfcJsonHeader ifcJsonDoc)
        {
            return JsonConvert.SerializeObject(ifcJsonDoc, Formatting.Indented);
        }

        public static void SaveToFile(IfcJsonHeader ifcJsonDoc, string filePath)
        {
            string json = ToJsonString(ifcJsonDoc);
            File.WriteAllText(filePath, json, Encoding.UTF8);
        }
    }
}
