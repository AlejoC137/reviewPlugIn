using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitToSupabasePlugin
{
    public class BrickType
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        
        [JsonPropertyName("width_cm")]
        public double WidthCm { get; set; }
        
        [JsonPropertyName("height_cm")]
        public double HeightCm { get; set; }
        
        [JsonPropertyName("length_cm")]
        public double LengthCm { get; set; }

        [JsonPropertyName("material")]
        public string? Material { get; set; }
        
        [JsonPropertyName("has_mortar")]
        public bool HasMortar { get; set; }
        
        [JsonPropertyName("has_trims")]
        public bool HasTrims { get; set; }
        
        [JsonPropertyName("has_dovela")]
        public bool HasDovela { get; set; }
        
        [JsonPropertyName("has_canchado")]
        public bool HasCanchado { get; set; }

        [JsonPropertyName("profile_path")]
        public string? ProfilePath { get; set; }

        [JsonPropertyName("has_dovelas")]
        public bool HasDovelas { get; set; }

        [JsonPropertyName("has_esquinas")]
        public bool HasEsquinas { get; set; }

        [JsonPropertyName("profile_orientation")]
        public string? ProfileOrientation { get; set; }

        [JsonPropertyName("profile_offset_x")]
        public double ProfileOffsetX { get; set; }

        [JsonPropertyName("profile_offset_y")]
        public double ProfileOffsetY { get; set; }

        [JsonPropertyName("profile_rotation")]
        public double ProfileRotation { get; set; }
    }

    public class BricksDatabase
    {
        [JsonPropertyName("bricks")]
        public List<BrickType>? Bricks { get; set; }
    }

    [Transaction(TransactionMode.Manual)]
    public class CommandGenerarFamiliasLadrillos : IExternalCommand
    {
        public Result Execute(
          ExternalCommandData commandData,
          ref string message,
          ElementSet elements)
        {
            try
            {
                var window = new ManagerFamiliasLadrilloWindow();
                if (window.ShowDialog() == true && window.GeneratedBrick != null)
                {
                    BrickType brick = window.GeneratedBrick;
                    Document doc = commandData.Application.ActiveUIDocument.Document;
                    Autodesk.Revit.ApplicationServices.Application app = commandData.Application.Application;

                    string version = app.VersionNumber;
                    string[] possibleTemplates = new string[]
                    {
                        $@"C:\ProgramData\Autodesk\RVT {version}\Family Templates\Spanish\Modelo genérico métrico.rft",
                        $@"C:\ProgramData\Autodesk\RVT {version}\Family Templates\English\Metric Generic Model.rft",
                        $@"C:\ProgramData\Autodesk\RVT {version}\Family Templates\English_I\Metric Generic Model.rft"
                    };

                    string? templatePath = possibleTemplates.FirstOrDefault(File.Exists);

                    if (templatePath == null)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Error", "No se encontró la plantilla de Modelo Genérico Métrico en las rutas estándar.");
                        return Result.Failed;
                    }

                    // Convert cm to feet (Revit internal units)
                    double widthFt = brick.WidthCm / 30.48;
                    double lengthFt = brick.LengthCm / 30.48;
                    double heightFt = brick.HeightCm / 30.48;

                    string tempRfaPath = Path.Combine(Path.GetTempPath(), $"{brick.Name}_{Guid.NewGuid().ToString().Substring(0, 4)}.rfa");

                    Document familyDoc = app.NewFamilyDocument(templatePath);
                    if (familyDoc == null)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Error", "No se pudo crear el documento de familia.");
                        return Result.Failed;
                    }

                    using (Transaction t = new Transaction(familyDoc, "Crear Ladrillo"))
                    {
                        t.Start();

                        // Get Ref. Level plane
                        FilteredElementCollector collector = new FilteredElementCollector(familyDoc);
                        collector.OfClass(typeof(Level));
                        Level? refLevel = collector.FirstElement() as Level;
                        SketchPlane sketchPlane = SketchPlane.Create(familyDoc, refLevel.Id);

                        if (!string.IsNullOrEmpty(brick.ProfilePath) && File.Exists(brick.ProfilePath))
                        {
                            // === LÓGICA DE SWEEP ===
                            // 1. Cargar la familia de perfil
                            if (familyDoc.LoadFamily(brick.ProfilePath, out Family profileFamily))
                            {
                                // 2. Obtener el símbolo (tipo)
                                ISet<ElementId> symbolIds = profileFamily.GetFamilySymbolIds();
                                if (symbolIds.Count > 0)
                                {
                                    FamilySymbol? profileSymbol = familyDoc.GetElement(symbolIds.First()) as FamilySymbol;
                                    if (profileSymbol != null)
                                    {
                                        // 3. Crear el perfil de barrido y aplicar rotación/offsets
                                        FamilySymbolProfile sweepProfile = familyDoc.Application.Create.NewFamilySymbolProfile(profileSymbol);
                                        sweepProfile.Angle = brick.ProfileRotation * (Math.PI / 180.0); // Radianes
                                        sweepProfile.XOffset = brick.ProfileOffsetX / 30.48; // cm a pies
                                        sweepProfile.YOffset = brick.ProfileOffsetY / 30.48; // cm a pies

                                        // 4. Definir la ruta (Path) según la orientación
                                        ReferenceArray path = new ReferenceArray();
                                        Curve? pathCurve = null;

                                        // Asumimos que el barrido se hace desde el centro (0,0,0) a lo largo de la profundidad elegida
                                        if (brick.ProfileOrientation == "Frontal") // Profundidad = Y (Ancho)
                                        {
                                            pathCurve = Line.CreateBound(new XYZ(0, -widthFt / 2, 0), new XYZ(0, widthFt / 2, 0));
                                        }
                                        else if (brick.ProfileOrientation == "Lateral") // Profundidad = X (Largo)
                                        {
                                            pathCurve = Line.CreateBound(new XYZ(0, 0, 0), new XYZ(lengthFt, 0, 0)); // Ajustado a origin=0
                                        }
                                        else // Superior (Profundidad = Z = Alto)
                                        {
                                            pathCurve = Line.CreateBound(new XYZ(0, 0, 0), new XYZ(0, 0, heightFt));
                                        }

                                        if (pathCurve != null)
                                        {
                                            ModelCurve modelCurve = familyDoc.FamilyCreate.NewModelCurve(pathCurve, sketchPlane);
                                            path.Append(modelCurve.GeometryCurve.Reference);
                                            Sweep sweep = familyDoc.FamilyCreate.NewSweep(true, path, sweepProfile, 0, ProfilePlaneLocation.Start);
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            // === LÓGICA DE EXTRUSIÓN BASE Y VOIDS PARAMÉTRICOS ===
                            
                            // 1. Sólido Principal del Ladrillo (Origen en X=0, Y=0, Z=0. Extrusión hacia X)
                            Plane planeYZ = Plane.CreateByNormalAndOrigin(XYZ.BasisX, XYZ.Zero);
                            SketchPlane spYZ = SketchPlane.Create(familyDoc, planeYZ);
                            
                            CurveArray curveArray = new CurveArray();
                            curveArray.Append(Line.CreateBound(new XYZ(0, -widthFt / 2, 0), new XYZ(0, widthFt / 2, 0)));
                            curveArray.Append(Line.CreateBound(new XYZ(0, widthFt / 2, 0), new XYZ(0, widthFt / 2, heightFt)));
                            curveArray.Append(Line.CreateBound(new XYZ(0, widthFt / 2, heightFt), new XYZ(0, -widthFt / 2, heightFt)));
                            curveArray.Append(Line.CreateBound(new XYZ(0, -widthFt / 2, heightFt), new XYZ(0, -widthFt / 2, 0)));
                            
                            CurveArrArray curveArrArray = new CurveArrArray();
                            curveArrArray.Append(curveArray);
                            
                            Extrusion brickSolid = familyDoc.FamilyCreate.NewExtrusion(true, curveArrArray, spYZ, lengthFt);

                            // 2. Parámetros de Instancia
                            FamilyParameter paramRecorteV = null;
                            FamilyParameter paramRecorteP = null;
                            FamilyParameter paramRecorteI = null;
                            FamilyParameter paramGrosorPega = null;
                            FamilyParameter paramVerPega = null;
                            FamilyParameter paramProfundidadDovela = null;

                            if (brick.HasTrims)
                            {
                                paramRecorteV = familyDoc.FamilyManager.AddParameter("RecorteVertical", GroupTypeId.Geometry, SpecTypeId.Length, true);
                                paramRecorteP = familyDoc.FamilyManager.AddParameter("RecorteProfundidad", GroupTypeId.Geometry, SpecTypeId.Length, true);
                                paramRecorteI = familyDoc.FamilyManager.AddParameter("RecorteIzquierdo", GroupTypeId.Geometry, SpecTypeId.Length, true);
                                
                                familyDoc.FamilyManager.Set(paramRecorteV, 0.003);
                                familyDoc.FamilyManager.Set(paramRecorteP, 0.003);
                                familyDoc.FamilyManager.Set(paramRecorteI, 0.003);
                            }

                            if (brick.HasMortar)
                            {
                                paramGrosorPega = familyDoc.FamilyManager.AddParameter("GrosorPega", GroupTypeId.Geometry, SpecTypeId.Length, true);
                                paramVerPega = familyDoc.FamilyManager.AddParameter("VerPega", GroupTypeId.Visibility, SpecTypeId.Boolean.YesNo, true);
                                familyDoc.FamilyManager.Set(paramGrosorPega, 0.005);
                            }

                            if (brick.HasDovela)
                            {
                                paramProfundidadDovela = familyDoc.FamilyManager.AddParameter("ProfundidadDovela", GroupTypeId.Geometry, SpecTypeId.Length, true);
                                familyDoc.FamilyManager.Set(paramProfundidadDovela, 0.003);
                            }

                            // 3. Void para Recortes
                            Extrusion voidV = null, voidP = null, voidI = null;
                            if (brick.HasTrims)
                            {
                                // Recorte Vertical (Corta desde arriba hacia abajo)
                                Plane planeXYTop = Plane.CreateByNormalAndOrigin(new XYZ(0, 0, -1), new XYZ(0, 0, heightFt));
                                SketchPlane spXYTop = SketchPlane.Create(familyDoc, planeXYTop);
                                CurveArray cvVoidV = new CurveArray();
                                cvVoidV.Append(Line.CreateBound(new XYZ(0, -widthFt / 2, heightFt), new XYZ(lengthFt, -widthFt / 2, heightFt)));
                                cvVoidV.Append(Line.CreateBound(new XYZ(lengthFt, -widthFt / 2, heightFt), new XYZ(lengthFt, widthFt / 2, heightFt)));
                                cvVoidV.Append(Line.CreateBound(new XYZ(lengthFt, widthFt / 2, heightFt), new XYZ(0, widthFt / 2, heightFt)));
                                cvVoidV.Append(Line.CreateBound(new XYZ(0, widthFt / 2, heightFt), new XYZ(0, -widthFt / 2, heightFt)));
                                CurveArrArray cvaVoidV = new CurveArrArray(); cvaVoidV.Append(cvVoidV);
                                voidV = familyDoc.FamilyCreate.NewExtrusion(false, cvaVoidV, spXYTop, 0.1); 
                                familyDoc.FamilyManager.AssociateElementParameterToFamilyParameter(voidV.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM), paramRecorteV);

                                // Recorte Profundidad (Derecho, corta hacia la izquierda)
                                Plane planeYZRight = Plane.CreateByNormalAndOrigin(new XYZ(-1, 0, 0), new XYZ(lengthFt, 0, 0));
                                SketchPlane spYZRight = SketchPlane.Create(familyDoc, planeYZRight);
                                CurveArray cvVoidP = new CurveArray();
                                cvVoidP.Append(Line.CreateBound(new XYZ(lengthFt, -widthFt / 2, 0), new XYZ(lengthFt, widthFt / 2, 0)));
                                cvVoidP.Append(Line.CreateBound(new XYZ(lengthFt, widthFt / 2, 0), new XYZ(lengthFt, widthFt / 2, heightFt)));
                                cvVoidP.Append(Line.CreateBound(new XYZ(lengthFt, widthFt / 2, heightFt), new XYZ(lengthFt, -widthFt / 2, heightFt)));
                                cvVoidP.Append(Line.CreateBound(new XYZ(lengthFt, -widthFt / 2, heightFt), new XYZ(lengthFt, -widthFt / 2, 0)));
                                CurveArrArray cvaVoidP = new CurveArrArray(); cvaVoidP.Append(cvVoidP);
                                voidP = familyDoc.FamilyCreate.NewExtrusion(false, cvaVoidP, spYZRight, 0.1);
                                familyDoc.FamilyManager.AssociateElementParameterToFamilyParameter(voidP.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM), paramRecorteP);

                                // Recorte Izquierdo (Izquierdo, corta hacia la derecha)
                                Plane planeYZLeft = Plane.CreateByNormalAndOrigin(new XYZ(1, 0, 0), new XYZ(0, 0, 0));
                                SketchPlane spYZLeft = SketchPlane.Create(familyDoc, planeYZLeft);
                                CurveArray cvVoidI = new CurveArray();
                                cvVoidI.Append(Line.CreateBound(new XYZ(0, -widthFt / 2, 0), new XYZ(0, widthFt / 2, 0)));
                                cvVoidI.Append(Line.CreateBound(new XYZ(0, widthFt / 2, 0), new XYZ(0, widthFt / 2, heightFt)));
                                cvVoidI.Append(Line.CreateBound(new XYZ(0, widthFt / 2, heightFt), new XYZ(0, -widthFt / 2, heightFt)));
                                cvVoidI.Append(Line.CreateBound(new XYZ(0, -widthFt / 2, heightFt), new XYZ(0, -widthFt / 2, 0)));
                                CurveArrArray cvaVoidI = new CurveArrArray(); cvaVoidI.Append(cvVoidI);
                                voidI = familyDoc.FamilyCreate.NewExtrusion(false, cvaVoidI, spYZLeft, 0.1);
                                familyDoc.FamilyManager.AssociateElementParameterToFamilyParameter(voidI.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM), paramRecorteI);
                            }

                            // 5. Pega (Sólido Vertical derecho y Sólido Horizontal superior)
                            if (brick.HasMortar)
                            {
                                Plane planeYZRight = Plane.CreateByNormalAndOrigin(new XYZ(-1, 0, 0), new XYZ(lengthFt, 0, 0));
                                SketchPlane spYZRight = SketchPlane.Create(familyDoc, planeYZRight);
                                CurveArray cvVoidP = new CurveArray();
                                cvVoidP.Append(Line.CreateBound(new XYZ(lengthFt, -widthFt / 2, 0), new XYZ(lengthFt, widthFt / 2, 0)));
                                cvVoidP.Append(Line.CreateBound(new XYZ(lengthFt, widthFt / 2, 0), new XYZ(lengthFt, widthFt / 2, heightFt)));
                                cvVoidP.Append(Line.CreateBound(new XYZ(lengthFt, widthFt / 2, heightFt), new XYZ(lengthFt, -widthFt / 2, heightFt)));
                                cvVoidP.Append(Line.CreateBound(new XYZ(lengthFt, -widthFt / 2, heightFt), new XYZ(lengthFt, -widthFt / 2, 0)));
                                CurveArrArray cvaVoidP = new CurveArrArray(); cvaVoidP.Append(cvVoidP);

                                Extrusion pegaLat = familyDoc.FamilyCreate.NewExtrusion(true, cvaVoidP, spYZRight, 0.1);
                                familyDoc.FamilyManager.AssociateElementParameterToFamilyParameter(pegaLat.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM), paramGrosorPega);
                                familyDoc.FamilyManager.AssociateElementParameterToFamilyParameter(pegaLat.get_Parameter(BuiltInParameter.IS_VISIBLE_PARAM), paramVerPega);
                                
                                Plane planeXYTopPos = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, heightFt));
                                SketchPlane spXYTopPos = SketchPlane.Create(familyDoc, planeXYTopPos);
                                CurveArray cvVoidV = new CurveArray();
                                cvVoidV.Append(Line.CreateBound(new XYZ(0, -widthFt / 2, heightFt), new XYZ(lengthFt, -widthFt / 2, heightFt)));
                                cvVoidV.Append(Line.CreateBound(new XYZ(lengthFt, -widthFt / 2, heightFt), new XYZ(lengthFt, widthFt / 2, heightFt)));
                                cvVoidV.Append(Line.CreateBound(new XYZ(lengthFt, widthFt / 2, heightFt), new XYZ(0, widthFt / 2, heightFt)));
                                cvVoidV.Append(Line.CreateBound(new XYZ(0, widthFt / 2, heightFt), new XYZ(0, -widthFt / 2, heightFt)));
                                CurveArrArray cvaVoidV = new CurveArrArray(); cvaVoidV.Append(cvVoidV);

                                Extrusion pegaSup = familyDoc.FamilyCreate.NewExtrusion(true, cvaVoidV, spXYTopPos, 0.1);
                                familyDoc.FamilyManager.AssociateElementParameterToFamilyParameter(pegaSup.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM), paramGrosorPega);
                                familyDoc.FamilyManager.AssociateElementParameterToFamilyParameter(pegaSup.get_Parameter(BuiltInParameter.IS_VISIBLE_PARAM), paramVerPega);
                            }

                            // 6. Void para Dovela (Cilindro vertical en el centro)
                            Extrusion voidDovela = null;
                            if (brick.HasDovela)
                            {
                                Plane planeXYTop = Plane.CreateByNormalAndOrigin(new XYZ(0, 0, -1), new XYZ(0, 0, heightFt));
                                SketchPlane spXYTop = SketchPlane.Create(familyDoc, planeXYTop);

                                CurveArray cvDovela = new CurveArray();
                                XYZ center = new XYZ(lengthFt / 2, 0, heightFt);
                                double radius = 0.08; // ~1 pulgada de radio (2 pulgadas diametro)
                                Arc arc1 = Arc.Create(center, radius, 0, Math.PI, XYZ.BasisX, XYZ.BasisY);
                                Arc arc2 = Arc.Create(center, radius, Math.PI, 2 * Math.PI, XYZ.BasisX, XYZ.BasisY);
                                cvDovela.Append(arc1);
                                cvDovela.Append(arc2);
                                CurveArrArray cvaDovela = new CurveArrArray(); cvaDovela.Append(cvDovela);
                                voidDovela = familyDoc.FamilyCreate.NewExtrusion(false, cvaDovela, spXYTop, 0.1);
                                familyDoc.FamilyManager.AssociateElementParameterToFamilyParameter(voidDovela.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM), paramProfundidadDovela);
                            }

                            // 7. Asegurar que los Voids corten el Sólido Principal
                            try
                            {
                                if (voidV != null) SolidSolidCutUtils.AddCutBetweenSolids(familyDoc, brickSolid, voidV);
                                if (voidP != null) SolidSolidCutUtils.AddCutBetweenSolids(familyDoc, brickSolid, voidP);
                                if (voidI != null) SolidSolidCutUtils.AddCutBetweenSolids(familyDoc, brickSolid, voidI);
                                if (voidDovela != null) SolidSolidCutUtils.AddCutBetweenSolids(familyDoc, brickSolid, voidDovela);
                            }
                            catch { }
                        }

                        t.Commit();
                    }

                    familyDoc.SaveAs(tempRfaPath);
                    familyDoc.Close();

                    // Load into project
                    using (Transaction tLoad = new Transaction(doc, "Cargar Ladrillo"))
                    {
                        tLoad.Start();
                        doc.LoadFamily(tempRfaPath, out Family loadedFamily);
                        
                        if (loadedFamily != null)
                        {
                            // Try to activate a symbol
                            ISet<ElementId> symbolIds = loadedFamily.GetFamilySymbolIds();
                            if (symbolIds.Count > 0)
                            {
                                FamilySymbol symbol = doc.GetElement(symbolIds.First()) as FamilySymbol;
                                if (symbol != null && !symbol.IsActive)
                                {
                                    symbol.Activate();
                                }
                            }
                        }
                        tLoad.Commit();
                    }

                    // Save to DB
                    string assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    string dirPath = Path.GetDirectoryName(assemblyPath) ?? string.Empty;
                    string jsonPath = Path.Combine(dirPath, "BricksDB.json");

                    if (!File.Exists(jsonPath))
                    {
                        string projRoot = Path.GetFullPath(Path.Combine(dirPath, "..", ".."));
                        jsonPath = Path.Combine(projRoot, "BricksDB.json");
                    }

                    BricksDatabase db = new BricksDatabase { Bricks = new List<BrickType>() };
                    if (File.Exists(jsonPath))
                    {
                        string jsonContent = File.ReadAllText(jsonPath);
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        db = JsonSerializer.Deserialize<BricksDatabase>(jsonContent, options) ?? db;
                        if (db.Bricks == null) db.Bricks = new List<BrickType>();
                    }

                    // Remove existing brick with the same name to avoid duplicates
                    db.Bricks.RemoveAll(b => b.Name == brick.Name);
                    db.Bricks.Add(brick);

                    var saveOptions = new JsonSerializerOptions { WriteIndented = true };
                    File.WriteAllText(jsonPath, JsonSerializer.Serialize(db, saveOptions));

                    Autodesk.Revit.UI.TaskDialog.Show("Éxito", $"La familia '{brick.Name}' ha sido generada 3D y cargada en el proyecto correctamente.");
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
