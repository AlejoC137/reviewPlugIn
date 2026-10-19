using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Media3D;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace RevitToSupabasePlugin
{
    public enum ModularElementCategory
    {
        TejaCubierta,
        ZocaloPerimetral,
        BaldosaEnchape,
        JuntaPega,
        CieloRasoListon,
        CaballeteCumbrera
    }

    public class ModularFamilySpec
    {
        public string FamilyName { get; set; } = "Teja_Barro_Modular";
        public string TypeName { get; set; } = "Estándar 45x22 cm";
        public ModularElementCategory Category { get; set; } = ModularElementCategory.TejaCubierta;
        public string Subtype { get; set; } = "Barro / Colonial Curva";

        // Dimensiones principales en cm (y mm para juntas/biseles)
        public double LengthCm { get; set; } = 45.0;
        public double WidthCm { get; set; } = 22.0;
        public double ThicknessCm { get; set; } = 1.8;
        public double WaveHeightCm { get; set; } = 6.5;
        public double OverlapUCm { get; set; } = 3.0;
        public double OverlapVCm { get; set; } = 7.0;
        public double BevelMm { get; set; } = 2.0;

        public ElementId MaterialId { get; set; } = ElementId.InvalidElementId;
        public string MaterialName { get; set; } = "";
        public string OutputDirectory { get; set; } = "";
    }

    public class InPlaceFamilyItem
    {
        public ElementId InstanceId { get; set; } = ElementId.InvalidElementId;
        public ElementId FamilyId { get; set; } = ElementId.InvalidElementId;
        public string FamilyName { get; set; } = "";
        public string TypeName { get; set; } = "";
        public string CategoryName { get; set; } = "";
        public BuiltInCategory BuiltInCategory { get; set; } = BuiltInCategory.OST_GenericModel;

        // Origen Cartesiano (0,0,0) detectado en coordenadas de proyecto (pies)
        public XYZ OriginMinFt { get; set; } = XYZ.Zero;
        public XYZ MaxCornerFt { get; set; } = XYZ.Zero;

        // Dimensiones medidas en cm
        public double LengthCm { get; set; }
        public double WidthCm { get; set; }
        public double HeightCm { get; set; }
        public double VolumeM3 { get; set; }
        public int SolidCount { get; set; }

        public ElementId MaterialId { get; set; } = ElementId.InvalidElementId;
        public string MaterialName { get; set; } = "Por Categoría";

        public List<Solid> ExtractedSolids { get; set; } = new List<Solid>();

        public string DisplayTitle => $"{FamilyName} ({CategoryName})";
        public string DimensionsSummary => $"{LengthCm:F1} × {WidthCm:F1} × {HeightCm:F1} cm | {SolidCount} sólido(s)";
    }

    public static class ModularFamilyStudioService
    {
        public static string GetDefaultFamiliesFolder()
        {
            string baseDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
            string candidate = Path.Combine(baseDir, "Familias", "Floors-Tiles");
            if (Directory.Exists(candidate)) return candidate;

            string repoCandidate = @"G:\Mi unidad\git\reviewPlugIn\Familias\Floors-Tiles";
            if (Directory.Exists(repoCandidate)) return repoCandidate;

            Directory.CreateDirectory(candidate);
            return candidate;
        }

        public static string? FindSeedFamilyPath(bool preferLinearPega)
        {
            string folder = GetDefaultFamiliesFolder();
            string primary = Path.Combine(folder, preferLinearPega ? "Base Tiles Pega.rfa" : "Tiles Base.rfa");
            if (File.Exists(primary)) return primary;

            string fallback = Path.Combine(folder, preferLinearPega ? "Tiles Base.rfa" : "Base Tiles Pega.rfa");
            if (File.Exists(fallback)) return fallback;

            return null;
        }

        public static string? FindGenericModelTemplatePath(Autodesk.Revit.ApplicationServices.Application app)
        {
            var searchDirs = new List<string>();
            if (!string.IsNullOrEmpty(app.FamilyTemplatePath) && Directory.Exists(app.FamilyTemplatePath))
                searchDirs.Add(app.FamilyTemplatePath);

            searchDirs.Add(@"C:\ProgramData\Autodesk\RVT 2027\Family Templates");
            searchDirs.Add(@"C:\ProgramData\Autodesk\RVT 2026\Family Templates");
            searchDirs.Add(@"C:\ProgramData\Autodesk\RVT 2025\Family Templates");

            string[] preferredNames = new[]
            {
                "Metric Generic Model.rft",
                "Modelo genérico métrico.rft",
                "Metric Generic Model face based.rft",
                "Modelo genérico métrico basado en cara.rft",
                "Generic Model.rft"
            };

            foreach (string dir in searchDirs.Distinct())
            {
                if (!Directory.Exists(dir)) continue;
                foreach (string name in preferredNames)
                {
                    try
                    {
                        var matches = Directory.GetFiles(dir, name, SearchOption.AllDirectories);
                        if (matches.Length > 0) return matches[0];
                    }
                    catch { }
                }
            }
            return null;
        }

        /// <summary>
        /// Crea una familia cargable (.rfa) paramétrica desde el origen (0,0,0) pensada para el motor de modulación,
        /// la guarda en disco y la carga automáticamente en el documento activo.
        /// </summary>
        public static FamilySymbol? CreateAndLoadModularFamily(Document projectDoc, ModularFamilySpec spec, out string savedRfaPath)
        {
            savedRfaPath = "";
            var app = projectDoc.Application;

            string outDir = !string.IsNullOrWhiteSpace(spec.OutputDirectory) && Directory.Exists(spec.OutputDirectory)
                ? spec.OutputDirectory
                : GetDefaultFamiliesFolder();

            string safeFamilyName = SanitizeFileName(spec.FamilyName);
            if (string.IsNullOrWhiteSpace(safeFamilyName)) safeFamilyName = "Familia_Modular_Review";
            savedRfaPath = Path.Combine(outDir, safeFamilyName + ".rfa");

            bool isLinearContract = spec.Category == ModularElementCategory.ZocaloPerimetral
                                 || spec.Category == ModularElementCategory.JuntaPega
                                 || spec.Category == ModularElementCategory.CieloRasoListon
                                 || spec.Category == ModularElementCategory.CaballeteCumbrera;

            bool useNativeSeedRig = (spec.Category == ModularElementCategory.BaldosaEnchape && spec.Subtype.Contains("Rectificada"))
                                 || (spec.Category == ModularElementCategory.JuntaPega && spec.Subtype.Contains("Base Tiles Pega"));

            string? seedRfa = FindSeedFamilyPath(isLinearContract);
            string? rftPath = FindGenericModelTemplatePath(app);

            Document? famDoc = null;
            string tempSeedCopy = "";

            try
            {
                if (seedRfa != null && (useNativeSeedRig || rftPath == null))
                {
                    tempSeedCopy = Path.Combine(Path.GetTempPath(), $"ReviewSeed_{Guid.NewGuid():N}.rfa");
                    File.Copy(seedRfa, tempSeedCopy, true);
                    famDoc = app.OpenDocumentFile(tempSeedCopy);
                }
                else if (rftPath != null)
                {
                    famDoc = app.NewFamilyDocument(rftPath);
                }
                else if (seedRfa != null)
                {
                    tempSeedCopy = Path.Combine(Path.GetTempPath(), $"ReviewSeed_{Guid.NewGuid():N}.rfa");
                    File.Copy(seedRfa, tempSeedCopy, true);
                    famDoc = app.OpenDocumentFile(tempSeedCopy);
                }
                else
                {
                    throw new InvalidOperationException("No se encontró plantilla .rft ni familia semilla en Familias\\Floors-Tiles.");
                }

                using (Transaction tFam = new Transaction(famDoc, "Construir Familia Modular (0,0)"))
                {
                    tFam.Start();

                    FamilyManager famMgr = famDoc.FamilyManager;
                    if (famMgr.Types.Size == 0)
                    {
                        famMgr.NewType(string.IsNullOrWhiteSpace(spec.TypeName) ? "Estándar" : spec.TypeName);
                    }
                    else if (!string.IsNullOrWhiteSpace(spec.TypeName))
                    {
                        try { famMgr.RenameCurrentType(spec.TypeName); } catch { }
                    }

                    // Asegurar contrato de parámetros para instanciamiento desde (0,0)
                    FamilyParameter? fpLength = EnsureFamilyParameter(famMgr, "Length", SpecTypeId.Length, false);
                    FamilyParameter? fpWidth = EnsureFamilyParameter(famMgr, "Width", SpecTypeId.Length, false);
                    FamilyParameter? fpThickness = EnsureFamilyParameter(famMgr, "Thickness", SpecTypeId.Length, false);
                    FamilyParameter? fpHeight = EnsureFamilyParameter(famMgr, "Height", SpecTypeId.Length, false);
                    FamilyParameter? fpOffset = EnsureFamilyParameter(famMgr, "Offset", SpecTypeId.Length, false);
                    FamilyParameter? fpOverlapU = EnsureFamilyParameter(famMgr, "Overlap_U", SpecTypeId.Length, false);
                    FamilyParameter? fpOverlapV = EnsureFamilyParameter(famMgr, "Overlap_V", SpecTypeId.Length, false);
                    FamilyParameter? fpWave = EnsureFamilyParameter(famMgr, "WaveHeight", SpecTypeId.Length, false);
                    FamilyParameter? fpBevel = EnsureFamilyParameter(famMgr, "Bevel", SpecTypeId.Length, false);
                    FamilyParameter? fpMat = EnsureFamilyParameter(famMgr, "Mat", SpecTypeId.Reference.Material, false);

                    // Parámetros de instancia clave del motor ("Lenght", "Cut Lat", "Cut Top")
                    FamilyParameter? fpInstLenght = EnsureFamilyParameter(famMgr, "Lenght", SpecTypeId.Length, true);
                    FamilyParameter? fpCutLat = EnsureFamilyParameter(famMgr, "Cut Lat", SpecTypeId.Length, true);
                    FamilyParameter? fpCutTop = EnsureFamilyParameter(famMgr, "Cut Top", SpecTypeId.Length, true);

                    double lenFt = Math.Max(0.02, spec.LengthCm / 30.48);
                    double widFt = Math.Max(0.01, spec.WidthCm / 30.48);
                    double thkFt = Math.Max(0.005, spec.ThicknessCm / 30.48);
                    double waveFt = Math.Max(0.0, spec.WaveHeightCm / 30.48);
                    double ovUFt = Math.Max(0.0, spec.OverlapUCm / 30.48);
                    double ovVFt = Math.Max(0.0, spec.OverlapVCm / 30.48);
                    double bevFt = Math.Max(0.0, spec.BevelMm / 304.8);

                    TrySetFamilyParam(famMgr, fpLength, lenFt);
                    TrySetFamilyParam(famMgr, fpWidth, widFt);
                    TrySetFamilyParam(famMgr, fpThickness, thkFt);
                    TrySetFamilyParam(famMgr, fpHeight, waveFt > 0 ? waveFt : thkFt);
                    TrySetFamilyParam(famMgr, fpOverlapU, ovUFt);
                    TrySetFamilyParam(famMgr, fpOverlapV, ovVFt);
                    TrySetFamilyParam(famMgr, fpWave, waveFt);
                    TrySetFamilyParam(famMgr, fpBevel, bevFt);
                    if (isLinearContract)
                        TrySetFamilyParam(famMgr, fpInstLenght, lenFt);

                    // Si no usamos el rig nativo de Tiles Base / Base Tiles Pega, reemplazamos la geometría por el sólido procedural en (0,0,0)
                    if (!useNativeSeedRig)
                    {
                        ClearExistingFamilySolids(famDoc);
                        Solid? proceduralSolid = BuildProceduralSolidAtOrigin(spec, lenFt, widFt, thkFt, waveFt, bevFt);
                        if (proceduralSolid != null && proceduralSolid.Volume > 1e-9)
                        {
                            FreeFormElement freeForm = FreeFormElement.Create(famDoc, proceduralSolid);
                            if (fpMat != null)
                            {
                                Parameter? elemMatParam = freeForm.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                                if (elemMatParam != null && famMgr.CanElementParameterBeAssociated(elemMatParam))
                                {
                                    try { famMgr.AssociateElementParameterToFamilyParameter(elemMatParam, fpMat); } catch { }
                                }
                            }
                        }
                    }

                    tFam.Commit();
                }

                SaveAsOptions saveOpts = new SaveAsOptions { OverwriteExistingFile = true };
                famDoc.SaveAs(savedRfaPath, saveOpts);
                famDoc.Close(false);
                famDoc = null;
            }
            finally
            {
                if (famDoc != null && famDoc.IsValidObject)
                {
                    try { famDoc.Close(false); } catch { }
                }
                if (!string.IsNullOrEmpty(tempSeedCopy) && File.Exists(tempSeedCopy))
                {
                    try { File.Delete(tempSeedCopy); } catch { }
                }
            }

            // Cargar en el proyecto activo y configurar su FamilySymbol
            FamilySymbol? resultSymbol = null;
            using (Transaction tProj = new Transaction(projectDoc, $"Cargar Familia Modular {safeFamilyName}"))
            {
                tProj.Start();
                projectDoc.LoadFamily(savedRfaPath, new ModularFamilyLoadOptions(), out Family loadedFamily);

                if (loadedFamily == null)
                {
                    loadedFamily = new FilteredElementCollector(projectDoc)
                        .OfClass(typeof(Family))
                        .Cast<Family>()
                        .FirstOrDefault(f => f.Name.Equals(safeFamilyName, StringComparison.OrdinalIgnoreCase));
                }

                if (loadedFamily != null)
                {
                    foreach (ElementId symId in loadedFamily.GetFamilySymbolIds())
                    {
                        if (projectDoc.GetElement(symId) is FamilySymbol sym)
                        {
                            if (!sym.IsActive) sym.Activate();
                            resultSymbol = sym;
                        }
                    }

                    if (resultSymbol != null)
                    {
                        if (!string.IsNullOrWhiteSpace(spec.TypeName) && !resultSymbol.Name.Equals(spec.TypeName, StringComparison.OrdinalIgnoreCase))
                        {
                            try { resultSymbol.Name = spec.TypeName; } catch { }
                        }

                        SetProjectSymbolParameters(resultSymbol, spec);
                    }
                }
                tProj.Commit();
            }

            return resultSymbol;
        }

        private static void SetProjectSymbolParameters(FamilySymbol symbol, ModularFamilySpec spec)
        {
            double lenFt = Math.Max(0.02, spec.LengthCm / 30.48);
            double widFt = Math.Max(0.01, spec.WidthCm / 30.48);
            double thkFt = Math.Max(0.005, spec.ThicknessCm / 30.48);
            double waveFt = Math.Max(0.0, spec.WaveHeightCm / 30.48);

            TrySetParam(symbol, "Length", lenFt);
            TrySetParam(symbol, "Width", widFt);
            TrySetParam(symbol, "Thickness", thkFt);
            TrySetParam(symbol, "Height", waveFt > 0 ? waveFt : thkFt);
            TrySetParam(symbol, "Overlap_U", spec.OverlapUCm / 30.48);
            TrySetParam(symbol, "Overlap_V", spec.OverlapVCm / 30.48);
            TrySetParam(symbol, "WaveHeight", waveFt);
            TrySetParam(symbol, "Bevel", spec.BevelMm / 304.8);

            if (spec.MaterialId != ElementId.InvalidElementId)
            {
                Parameter? pMat = symbol.LookupParameter("Mat") ?? symbol.LookupParameter("Material");
                if (pMat != null && !pMat.IsReadOnly)
                {
                    try { pMat.Set(spec.MaterialId); } catch { }
                }
            }
        }

        private static void TrySetParam(Element elem, string name, double val)
        {
            Parameter? p = elem.LookupParameter(name);
            if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Double)
            {
                try { p.Set(val); } catch { }
            }
        }

        private static FamilyParameter? EnsureFamilyParameter(FamilyManager famMgr, string name, ForgeTypeId specType, bool isInstance)
        {
            FamilyParameter? existing = famMgr.get_Parameter(name);
            if (existing != null) return existing;

            try
            {
                ForgeTypeId group = specType == SpecTypeId.Reference.Material
                    ? GroupTypeId.Materials
                    : GroupTypeId.Geometry;
                return famMgr.AddParameter(name, group, specType, isInstance);
            }
            catch
            {
                return null;
            }
        }

        private static void TrySetFamilyParam(FamilyManager famMgr, FamilyParameter? fp, double val)
        {
            if (fp == null || famMgr.CurrentType == null) return;
            try
            {
                if (!fp.IsReadOnly && fp.StorageType == StorageType.Double)
                    famMgr.Set(fp, val);
            }
            catch { }
        }

        private static void ClearExistingFamilySolids(Document famDoc)
        {
            var formIds = new FilteredElementCollector(famDoc)
                .WhereElementIsNotElementType()
                .Where(e => e is GenericForm || e is FreeFormElement || e is GeomCombination)
                .Select(e => e.Id)
                .ToList();

            foreach (ElementId id in formIds)
            {
                try { famDoc.Delete(id); } catch { }
            }
        }

        /// <summary>
        /// Construye el sólido 3D con esquina inferior izquierda en el origen Cartesiano (0,0,0) (X >= 0, Y >= 0, Z >= 0).
        /// X corresponde a la dirección U (Largo o Ancho modular), Y a la dirección V, Z a la altura/espesor.
        /// </summary>
        public static Solid? BuildProceduralSolidAtOrigin(ModularFamilySpec spec, double lenFt, double widFt, double thkFt, double waveFt, double bevFt)
        {
            try
            {
                switch (spec.Category)
                {
                    case ModularElementCategory.TejaCubierta:
                        return BuildRoofTileSolidAtOrigin(spec.Subtype, lenFt, widFt, thkFt, Math.Max(thkFt * 1.5, waveFt));

                    case ModularElementCategory.ZocaloPerimetral:
                        return BuildSkirtingSolidAtOrigin(spec.Subtype, lenFt, widFt, Math.Max(thkFt, waveFt > 0 ? waveFt : 0.25), bevFt);

                    case ModularElementCategory.CaballeteCumbrera:
                        return BuildRidgeCapSolidAtOrigin(spec.Subtype, lenFt, widFt, thkFt, Math.Max(thkFt * 2.0, waveFt));

                    case ModularElementCategory.CieloRasoListon:
                    case ModularElementCategory.BaldosaEnchape:
                    case ModularElementCategory.JuntaPega:
                    default:
                        return BuildBoxSolidAtOrigin(lenFt, widFt, thkFt);
                }
            }
            catch
            {
                return BuildBoxSolidAtOrigin(lenFt, widFt, thkFt);
            }
        }

        private static Solid BuildBoxSolidAtOrigin(double lenX, double widY, double thkZ)
        {
            XYZ p0 = new XYZ(0, 0, 0);
            XYZ p1 = new XYZ(lenX, 0, 0);
            XYZ p2 = new XYZ(lenX, widY, 0);
            XYZ p3 = new XYZ(0, widY, 0);

            CurveLoop loop = new CurveLoop();
            loop.Append(Line.CreateBound(p0, p1));
            loop.Append(Line.CreateBound(p1, p2));
            loop.Append(Line.CreateBound(p2, p3));
            loop.Append(Line.CreateBound(p3, p0));

            return GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { loop }, XYZ.BasisZ, thkZ);
        }

        /// <summary>
        /// Construye el perfil de Teja en el plano YZ (Ancho en Y desde 0 hasta widFt, Alto en Z) y lo extruye a lo largo de X (0 a lenFt).
        /// </summary>
        private static Solid BuildRoofTileSolidAtOrigin(string subtype, double lenX, double widY, double thkZ, double waveZ)
        {
            var pts = new List<XYZ>();
            int segments = 12;

            if (subtype.Contains("Plana"))
            {
                return BuildBoxSolidAtOrigin(lenX, widY, thkZ);
            }
            else if (subtype.Contains("Termoacústica") || subtype.Contains("Trapezoidal"))
            {
                // Perfil trapezoidal de 2 crestas desde Y=0..widY
                double step = widY / 8.0;
                double[] zHeights = { 0, waveZ, waveZ, 0, 0, waveZ, waveZ, 0, 0 };
                for (int i = 0; i <= 8; i++)
                    pts.Add(new XYZ(0, i * step, zHeights[i]));
                for (int i = 8; i >= 0; i--)
                    pts.Add(new XYZ(0, i * step, zHeights[i] + thkZ));
            }
            else
            {
                // Teja de Barro / Colonial Curva (onda sinusoidal S desde (0,0,0))
                for (int i = 0; i <= segments; i++)
                {
                    double t = (double)i / segments;
                    double y = t * widY;
                    double z = (waveZ * 0.5) * (1.0 - Math.Cos(2.0 * Math.PI * t));
                    pts.Add(new XYZ(0, y, z));
                }
                for (int i = segments; i >= 0; i--)
                {
                    double t = (double)i / segments;
                    double y = t * widY;
                    double z = (waveZ * 0.5) * (1.0 - Math.Cos(2.0 * Math.PI * t)) + thkZ;
                    pts.Add(new XYZ(0, y, z));
                }
            }

            CurveLoop loop = CreateClosedLoopFromPoints(pts);
            return GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { loop }, XYZ.BasisX, lenX);
        }

        /// <summary>
        /// Construye un Zócalo Perimetral con origen en (0,0,0): extruido en X (0..lenX), espesor en Y (0..thkY), altura en Z (0..heightZ).
        /// </summary>
        private static Solid BuildSkirtingSolidAtOrigin(string subtype, double lenX, double thkY, double heightZ, double bevFt)
        {
            double b = Math.Min( Math.Max(0.005, bevFt), Math.Min(thkY * 0.6, heightZ * 0.4));
            var pts = new List<XYZ>();

            if (subtype.Contains("Escalonado"))
            {
                pts.Add(new XYZ(0, 0, 0));
                pts.Add(new XYZ(0, thkY, 0));
                pts.Add(new XYZ(0, thkY, heightZ * 0.65));
                pts.Add(new XYZ(0, thkY * 0.65, heightZ * 0.65));
                pts.Add(new XYZ(0, thkY * 0.65, heightZ * 0.85));
                pts.Add(new XYZ(0, thkY * 0.35, heightZ * 0.85));
                pts.Add(new XYZ(0, thkY * 0.35, heightZ));
                pts.Add(new XYZ(0, 0, heightZ));
            }
            else if (subtype.Contains("Media Caña") || subtype.Contains("Pecho"))
            {
                pts.Add(new XYZ(0, 0, 0));
                pts.Add(new XYZ(0, thkY, 0));
                pts.Add(new XYZ(0, thkY, heightZ - b));
                pts.Add(new XYZ(0, thkY * 0.6, heightZ - b * 0.3));
                pts.Add(new XYZ(0, thkY * 0.25, heightZ));
                pts.Add(new XYZ(0, 0, heightZ));
            }
            else
            {
                // Recto Biselado
                pts.Add(new XYZ(0, 0, 0));
                pts.Add(new XYZ(0, thkY, 0));
                pts.Add(new XYZ(0, thkY, heightZ - b));
                pts.Add(new XYZ(0, Math.Max(0.002, thkY - b), heightZ));
                pts.Add(new XYZ(0, 0, heightZ));
            }

            CurveLoop loop = CreateClosedLoopFromPoints(pts);
            return GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { loop }, XYZ.BasisX, lenX);
        }

        private static Solid BuildRidgeCapSolidAtOrigin(string subtype, double lenX, double widY, double thkZ, double heightZ)
        {
            double halfY = widY * 0.5;
            var pts = new List<XYZ>
            {
                new XYZ(0, 0, 0),
                new XYZ(0, halfY, heightZ),
                new XYZ(0, widY, 0),
                new XYZ(0, widY, thkZ),
                new XYZ(0, halfY, heightZ + thkZ),
                new XYZ(0, 0, thkZ)
            };
            CurveLoop loop = CreateClosedLoopFromPoints(pts);
            return GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { loop }, XYZ.BasisX, lenX);
        }

        private static CurveLoop CreateClosedLoopFromPoints(List<XYZ> pts)
        {
            CurveLoop loop = new CurveLoop();
            for (int i = 0; i < pts.Count; i++)
            {
                XYZ a = pts[i];
                XYZ b = pts[(i + 1) % pts.Count];
                if (a.DistanceTo(b) > 1e-4)
                {
                    loop.Append(Line.CreateBound(a, b));
                }
            }
            return loop;
        }

        // =========================================================================
        // 2. GESTOR Y CONVERTIDOR DE FAMILIAS IN-SITU A FAMILIAS CARGABLES (.RFA)
        // =========================================================================

        public static List<InPlaceFamilyItem> GetInPlaceFamilies(Document doc)
        {
            var result = new List<InPlaceFamilyItem>();
            if (doc == null) return result;

            var instances = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .Where(fi => fi.Symbol?.Family != null && fi.Symbol.Family.IsInPlace)
                .ToList();

            Options geomOpts = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = false
            };

            foreach (FamilyInstance fi in instances)
            {
                var solids = ExtractSolidsFromElement(fi, geomOpts);
                if (solids.Count == 0) continue;

                double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
                double totalVolFt3 = 0;
                ElementId detectedMatId = ElementId.InvalidElementId;

                foreach (Solid s in solids)
                {
                    totalVolFt3 += s.Volume;
                    BoundingBoxXYZ bb = s.GetBoundingBox();
                    if (bb != null)
                    {
                        Transform tr = bb.Transform;
                        XYZ[] corners = new[]
                        {
                            tr.OfPoint(bb.Min),
                            tr.OfPoint(bb.Max),
                            tr.OfPoint(new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z)),
                            tr.OfPoint(new XYZ(bb.Max.X, bb.Min.Y, bb.Max.Z))
                        };
                        foreach (XYZ c in corners)
                        {
                            if (c.X < minX) minX = c.X;
                            if (c.Y < minY) minY = c.Y;
                            if (c.Z < minZ) minZ = c.Z;
                            if (c.X > maxX) maxX = c.X;
                            if (c.Y > maxY) maxY = c.Y;
                            if (c.Z > maxZ) maxZ = c.Z;
                        }
                    }

                    if (detectedMatId == ElementId.InvalidElementId)
                    {
                        foreach (Face f in s.Faces)
                        {
                            if (f.MaterialElementId != ElementId.InvalidElementId)
                            {
                                detectedMatId = f.MaterialElementId;
                                break;
                            }
                        }
                    }
                }

                if (minX == double.MaxValue) continue;

                string matName = "Por Categoría";
                if (detectedMatId != ElementId.InvalidElementId && doc.GetElement(detectedMatId) is Autodesk.Revit.DB.Material matElem)
                    matName = matElem.Name;

                BuiltInCategory bic = BuiltInCategory.OST_GenericModel;
                if (fi.Category != null)
                {
                    try { bic = (BuiltInCategory)fi.Category.Id.Value; } catch { }
                }

                result.Add(new InPlaceFamilyItem
                {
                    InstanceId = fi.Id,
                    FamilyId = fi.Symbol.Family.Id,
                    FamilyName = fi.Symbol.Family.Name,
                    TypeName = fi.Symbol.Name,
                    CategoryName = fi.Category?.Name ?? "Modelos genéricos",
                    BuiltInCategory = bic,
                    OriginMinFt = new XYZ(minX, minY, minZ),
                    MaxCornerFt = new XYZ(maxX, maxY, maxZ),
                    LengthCm = Math.Max(0.1, (maxX - minX) * 30.48),
                    WidthCm = Math.Max(0.1, (maxY - minY) * 30.48),
                    HeightCm = Math.Max(0.1, (maxZ - minZ) * 30.48),
                    VolumeM3 = totalVolFt3 * 0.0283168466,
                    SolidCount = solids.Count,
                    MaterialId = detectedMatId,
                    MaterialName = matName,
                    ExtractedSolids = solids
                });
            }

            return result.OrderBy(i => i.CategoryName).ThenBy(i => i.FamilyName).ToList();
        }

        private static List<Solid> ExtractSolidsFromElement(Element elem, Options opts)
        {
            var list = new List<Solid>();
            GeometryElement? geomElem = elem.get_Geometry(opts);
            if (geomElem == null) return list;

            CollectSolidsRecursive(geomElem, Transform.Identity, list);
            return list;
        }

        private static void CollectSolidsRecursive(GeometryElement geomElem, Transform currentTransform, List<Solid> solids)
        {
            foreach (GeometryObject obj in geomElem)
            {
                if (obj is Solid solid && solid.Volume > 1e-6 && solid.Faces.Size > 0)
                {
                    Solid transformed = currentTransform.IsIdentity
                        ? solid
                        : SolidUtils.CreateTransformed(solid, currentTransform);
                    solids.Add(transformed);
                }
                else if (obj is GeometryInstance geomInst)
                {
                    Transform nextTr = currentTransform.Multiply(geomInst.Transform);
                    GeometryElement instGeom = geomInst.GetSymbolGeometry();
                    if (instGeom != null)
                    {
                        CollectSolidsRecursive(instGeom, nextTr, solids);
                    }
                }
            }
        }

        /// <summary>
        /// Convierte una Familia In-Situ a una Familia Cargable (.rfa) normalizando su geometría al origen Cartesiano (0,0,0),
        /// inyectándole el contrato de parámetros modulares, cargándola en el proyecto y opcionalmente reemplazando el In-Situ en su (0,0).
        /// </summary>
        public static FamilySymbol? ConvertInPlaceToLoadableFamily(
            Document projectDoc,
            InPlaceFamilyItem item,
            string newFamilyName,
            string newTypeName,
            ElementId overrideMaterialId,
            bool replaceInPlaceInModel,
            out string savedRfaPath,
            out FamilyInstance? placedReplacement)
        {
            savedRfaPath = "";
            placedReplacement = null;
            if (projectDoc == null || item.ExtractedSolids.Count == 0) return null;

            var app = projectDoc.Application;
            string safeFamilyName = SanitizeFileName(string.IsNullOrWhiteSpace(newFamilyName) ? item.FamilyName + "_Cargable" : newFamilyName);
            savedRfaPath = Path.Combine(GetDefaultFamiliesFolder(), safeFamilyName + ".rfa");

            string? rftPath = FindGenericModelTemplatePath(app);
            string? seedRfa = FindSeedFamilyPath(false);

            Document? famDoc = null;
            string tempSeedCopy = "";

            try
            {
                if (rftPath != null)
                {
                    famDoc = app.NewFamilyDocument(rftPath);
                }
                else if (seedRfa != null)
                {
                    tempSeedCopy = Path.Combine(Path.GetTempPath(), $"InPlaceSeed_{Guid.NewGuid():N}.rfa");
                    File.Copy(seedRfa, tempSeedCopy, true);
                    famDoc = app.OpenDocumentFile(tempSeedCopy);
                }
                else
                {
                    throw new InvalidOperationException("No se encontró plantilla de familia (.rft) ni semilla (.rfa).");
                }

                // Trasladar todos los sólidos para que su esquina mínima descanse exactamente en (0,0,0)
                XYZ shiftToOrigin = new XYZ(-item.OriginMinFt.X, -item.OriginMinFt.Y, -item.OriginMinFt.Z);
                Transform toOriginTransform = Transform.CreateTranslation(shiftToOrigin);

                using (Transaction tFam = new Transaction(famDoc, "Convertir In-Situ a Cargable (0,0)"))
                {
                    tFam.Start();

                    if (!string.IsNullOrEmpty(tempSeedCopy))
                    {
                        ClearExistingFamilySolids(famDoc);
                    }

                    FamilyManager famMgr = famDoc.FamilyManager;
                    if (famMgr.Types.Size == 0)
                        famMgr.NewType(string.IsNullOrWhiteSpace(newTypeName) ? "Estándar" : newTypeName);
                    else if (!string.IsNullOrWhiteSpace(newTypeName))
                    {
                        try { famMgr.RenameCurrentType(newTypeName); } catch { }
                    }

                    FamilyParameter? fpLength = EnsureFamilyParameter(famMgr, "Length", SpecTypeId.Length, false);
                    FamilyParameter? fpWidth = EnsureFamilyParameter(famMgr, "Width", SpecTypeId.Length, false);
                    FamilyParameter? fpHeight = EnsureFamilyParameter(famMgr, "Height", SpecTypeId.Length, false);
                    FamilyParameter? fpThickness = EnsureFamilyParameter(famMgr, "Thickness", SpecTypeId.Length, false);
                    FamilyParameter? fpMat = EnsureFamilyParameter(famMgr, "Mat", SpecTypeId.Reference.Material, false);
                    FamilyParameter? fpInstLenght = EnsureFamilyParameter(famMgr, "Lenght", SpecTypeId.Length, true);
                    EnsureFamilyParameter(famMgr, "Cut Lat", SpecTypeId.Length, true);
                    EnsureFamilyParameter(famMgr, "Cut Top", SpecTypeId.Length, true);

                    double lenFt = item.LengthCm / 30.48;
                    double widFt = item.WidthCm / 30.48;
                    double hgtFt = item.HeightCm / 30.48;

                    TrySetFamilyParam(famMgr, fpLength, lenFt);
                    TrySetFamilyParam(famMgr, fpWidth, widFt);
                    TrySetFamilyParam(famMgr, fpHeight, hgtFt);
                    TrySetFamilyParam(famMgr, fpThickness, hgtFt);
                    TrySetFamilyParam(famMgr, fpInstLenght, lenFt);

                    foreach (Solid s in item.ExtractedSolids)
                    {
                        Solid normalizedSolid = SolidUtils.CreateTransformed(s, toOriginTransform);
                        FreeFormElement ff = FreeFormElement.Create(famDoc, normalizedSolid);
                        if (fpMat != null)
                        {
                            Parameter? elemMatParam = ff.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                            if (elemMatParam != null && famMgr.CanElementParameterBeAssociated(elemMatParam))
                            {
                                try { famMgr.AssociateElementParameterToFamilyParameter(elemMatParam, fpMat); } catch { }
                            }
                        }
                    }

                    tFam.Commit();
                }

                famDoc.SaveAs(savedRfaPath, new SaveAsOptions { OverwriteExistingFile = true });
                famDoc.Close(false);
                famDoc = null;
            }
            finally
            {
                if (famDoc != null && famDoc.IsValidObject)
                {
                    try { famDoc.Close(false); } catch { }
                }
                if (!string.IsNullOrEmpty(tempSeedCopy) && File.Exists(tempSeedCopy))
                {
                    try { File.Delete(tempSeedCopy); } catch { }
                }
            }

            FamilySymbol? loadedSymbol = null;
            using (Transaction tProj = new Transaction(projectDoc, $"Cargar y Reemplazar In-Situ {safeFamilyName}"))
            {
                tProj.Start();
                projectDoc.LoadFamily(savedRfaPath, new ModularFamilyLoadOptions(), out Family loadedFam);

                if (loadedFam == null)
                {
                    loadedFam = new FilteredElementCollector(projectDoc)
                        .OfClass(typeof(Family))
                        .Cast<Family>()
                        .FirstOrDefault(f => f.Name.Equals(safeFamilyName, StringComparison.OrdinalIgnoreCase));
                }

                if (loadedFam != null)
                {
                    foreach (ElementId id in loadedFam.GetFamilySymbolIds())
                    {
                        if (projectDoc.GetElement(id) is FamilySymbol sym)
                        {
                            if (!sym.IsActive) sym.Activate();
                            loadedSymbol = sym;
                        }
                    }
                }

                if (loadedSymbol != null)
                {
                    ElementId matToApply = overrideMaterialId != ElementId.InvalidElementId ? overrideMaterialId : item.MaterialId;
                    if (matToApply != ElementId.InvalidElementId)
                    {
                        Parameter? pMat = loadedSymbol.LookupParameter("Mat") ?? loadedSymbol.LookupParameter("Material");
                        if (pMat != null && !pMat.IsReadOnly)
                        {
                            try { pMat.Set(matToApply); } catch { }
                        }
                    }

                    if (replaceInPlaceInModel)
                    {
                        try
                        {
                            placedReplacement = projectDoc.Create.NewFamilyInstance(
                                item.OriginMinFt,
                                loadedSymbol,
                                StructuralType.NonStructural);

                            Element? oldInPlace = projectDoc.GetElement(item.InstanceId);
                            if (oldInPlace != null)
                            {
                                projectDoc.Delete(item.InstanceId);
                            }
                        }
                        catch { }
                    }
                }

                tProj.Commit();
            }

            return loadedSymbol;
        }

        // =========================================================================
        // 3. MOTOR DE INSTANCIAMIENTO EN PROYECTO DESDE (0,0) (TEJAS Y ZÓCALOS)
        // =========================================================================

        /// <summary>
        /// Instancia una familia cargable de Teja instancia por instancia sobre las caras superiores inclinadas de una Cubierta (RoofBase) o Piso,
        /// estableciendo el origen Cartesiano (0,0) en el alero inferior izquierdo de cada faldón y aplicando traslapos y recortes perimetrales.
        /// </summary>
        public static int InstantiateRoofTilesOnHost(
            Document doc,
            Element hostRoofOrFloor,
            FamilySymbol tileSymbol,
            double overlapUCm,
            double overlapVCm,
            ElementId overrideMaterialId)
        {
            if (doc == null || hostRoofOrFloor == null || tileSymbol == null) return 0;

            Options geomOpts = new Options
            {
                ComputeReferences = true,
                DetailLevel = ViewDetailLevel.Fine
            };

            GeometryElement? geomElem = hostRoofOrFloor.get_Geometry(geomOpts);
            if (geomElem == null) return 0;

            var slopedFaces = new List<PlanarFace>();
            foreach (GeometryObject obj in geomElem)
            {
                if (obj is Solid solid && solid.Faces.Size > 0)
                {
                    foreach (Face f in solid.Faces)
                    {
                        if (f is PlanarFace pf && pf.FaceNormal.Z > 0.15)
                        {
                            slopedFaces.Add(pf);
                        }
                    }
                }
            }

            if (slopedFaces.Count == 0) return 0;

            double tileLenFt = GetSymbolLengthFt(tileSymbol, "Length", 45.0 / 30.48);
            double tileWidFt = GetSymbolLengthFt(tileSymbol, "Width", 22.0 / 30.48);
            double ovUFt = Math.Max(0.0, overlapUCm / 30.48);
            double ovVFt = Math.Max(0.0, overlapVCm / 30.48);

            double stepU = Math.Max(0.1, tileWidFt - ovUFt);
            double stepV = Math.Max(0.1, tileLenFt - ovVFt);

            int placedCount = 0;

            using (Transaction t = new Transaction(doc, "Instanciar Tejas en Cubierta desde (0,0)"))
            {
                t.Start();
                if (!tileSymbol.IsActive) tileSymbol.Activate();

                if (overrideMaterialId != ElementId.InvalidElementId)
                {
                    Parameter? pMat = tileSymbol.LookupParameter("Mat") ?? tileSymbol.LookupParameter("Material");
                    if (pMat != null && !pMat.IsReadOnly)
                    {
                        try { pMat.Set(overrideMaterialId); } catch { }
                    }
                }

                foreach (PlanarFace pf in slopedFaces)
                {
                    IList<CurveLoop> loops = pf.GetEdgesAsCurveLoops();
                    if (loops.Count == 0) continue;

                    CurveLoop outerLoop = loops.OrderByDescending(l => l.GetExactLength()).First();

                    // Sistema Cartesiano (0,0) del faldón:
                    // dirU = dirección horizontal del alero; dirV = dirección ascendente de la pendiente
                    XYZ normal = pf.FaceNormal.Normalize();
                    XYZ dirU = XYZ.BasisZ.CrossProduct(normal);
                    if (dirU.GetLength() < 1e-4)
                        dirU = XYZ.BasisX;
                    else
                        dirU = dirU.Normalize();

                    XYZ dirV = normal.CrossProduct(dirU).Normalize();

                    var rawPts3D = new List<XYZ>();
                    foreach (Curve c in outerLoop)
                        rawPts3D.Add(c.GetEndPoint(0));

                    double minU = double.MaxValue, minV = double.MaxValue;
                    double maxU = double.MinValue, maxV = double.MinValue;
                    XYZ refPt = pf.Origin;

                    foreach (XYZ pt in rawPts3D)
                    {
                        XYZ d = pt - refPt;
                        double u = d.DotProduct(dirU);
                        double v = d.DotProduct(dirV);
                        if (u < minU) minU = u;
                        if (v < minV) minV = v;
                        if (u > maxU) maxU = u;
                        if (v > maxV) maxV = v;
                    }

                    // Origen Cartesiano (0,0) en la esquina inferior izquierda del alero
                    XYZ origin00 = refPt + minU * dirU + minV * dirV;
                    double spanU = maxU - minU;
                    double spanV = maxV - minV;

                    var poly2D = rawPts3D.Select(pt =>
                    {
                        XYZ d = pt - origin00;
                        return new UV(d.DotProduct(dirU), d.DotProduct(dirV));
                    }).ToList();

                    int cols = (int)Math.Ceiling(spanU / stepU) + 1;
                    int rows = (int)Math.Ceiling(spanV / stepV) + 1;

                    for (int r = 0; r < rows; r++)
                    {
                        double v0 = r * stepV;
                        double v1 = v0 + tileLenFt;

                        for (int c = 0; c < cols; c++)
                        {
                            double u0 = c * stepU;
                            double u1 = u0 + tileWidFt;

                            var clipped = FloorTilingService.ClipPolygonWithRect(poly2D, u0, v0, u1, v1);
                            if (clipped == null || clipped.Count < 3) continue;

                            double area2D = ComputePolygonArea2D(clipped);
                            if (area2D < 1e-4) continue;

                            double clipMinU = clipped.Min(p => p.U);
                            double clipMaxU = clipped.Max(p => p.U);
                            double clipMinV = clipped.Min(p => p.V);
                            double clipMaxV = clipped.Max(p => p.V);

                            double actualWid = clipMaxU - clipMinU;
                            double actualLen = clipMaxV - clipMinV;

                            XYZ tileOrigin3D = origin00 + clipMinU * dirU + clipMinV * dirV;

                            FamilyInstance? inst = null;
                            try
                            {
                                if (pf.Reference != null)
                                {
                                    inst = doc.Create.NewFamilyInstance(pf.Reference, tileOrigin3D, dirV, tileSymbol);
                                }
                            }
                            catch { inst = null; }

                            if (inst == null)
                            {
                                try
                                {
                                    inst = doc.Create.NewFamilyInstance(tileOrigin3D, tileSymbol, StructuralType.NonStructural);
                                }
                                catch { }
                            }

                            if (inst != null)
                            {
                                double cutLat = Math.Max(0.0, tileWidFt - actualWid);
                                double cutTop = Math.Max(0.0, tileLenFt - actualLen);

                                if (cutLat > 0.003)
                                    TrySetParam(inst, "Cut Lat", cutLat);
                                if (cutTop > 0.003)
                                    TrySetParam(inst, "Cut Top", cutTop);

                                placedCount++;
                            }
                        }
                    }
                }

                t.Commit();
            }

            return placedCount;
        }

        /// <summary>
        /// Instancia segmentos de Zócalo Perimetral (.rfa) a lo largo del borde de un piso seleccionado desde su esquina (0,0).
        /// </summary>
        public static int InstantiateSkirtingOnFloor(
            Document doc,
            Floor floor,
            FamilySymbol skirtingSymbol,
            ElementId overrideMaterialId)
        {
            if (doc == null || floor == null || skirtingSymbol == null) return 0;

            Reference? topRef = HostObjectUtils.GetTopFaces(floor).FirstOrDefault();
            if (topRef == null) return 0;

            PlanarFace? topFace = floor.GetGeometryObjectFromReference(topRef) as PlanarFace;
            if (topFace == null) return 0;

            IList<CurveLoop> loops = topFace.GetEdgesAsCurveLoops();
            if (loops.Count == 0) return 0;

            int count = 0;
            using (Transaction t = new Transaction(doc, "Instanciar Zócalos Perimetrales desde (0,0)"))
            {
                t.Start();
                if (!skirtingSymbol.IsActive) skirtingSymbol.Activate();

                if (overrideMaterialId != ElementId.InvalidElementId)
                {
                    Parameter? pMat = skirtingSymbol.LookupParameter("Mat") ?? skirtingSymbol.LookupParameter("Material");
                    if (pMat != null && !pMat.IsReadOnly)
                    {
                        try { pMat.Set(overrideMaterialId); } catch { }
                    }
                }

                foreach (CurveLoop loop in loops)
                {
                    foreach (Curve c in loop)
                    {
                        XYZ p0 = c.GetEndPoint(0);
                        XYZ p1 = c.GetEndPoint(1);
                        double segLen = p0.DistanceTo(p1);
                        if (segLen < 0.02) continue;

                        XYZ dir = (p1 - p0).Normalize();
                        FamilyInstance? inst = null;

                        try
                        {
                            inst = doc.Create.NewFamilyInstance(topRef, p0, dir, skirtingSymbol);
                        }
                        catch
                        {
                            try
                            {
                                inst = doc.Create.NewFamilyInstance(p0, skirtingSymbol, StructuralType.NonStructural);
                            }
                            catch { }
                        }

                        if (inst != null)
                        {
                            TrySetParam(inst, "Lenght", segLen);
                            TrySetParam(inst, "Length", segLen);
                            TrySetParam(inst, "Largo", segLen);
                            count++;
                        }
                    }
                }

                t.Commit();
            }

            return count;
        }

        private static double GetSymbolLengthFt(FamilySymbol sym, string paramName, double fallbackFt)
        {
            Parameter? p = sym.LookupParameter(paramName);
            if (p != null && p.StorageType == StorageType.Double && p.AsDouble() > 0.01)
                return p.AsDouble();
            return fallbackFt;
        }

        private static double ComputePolygonArea2D(List<UV> pts)
        {
            double area = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                UV a = pts[i];
                UV b = pts[(i + 1) % pts.Count];
                area += (a.U * b.V) - (b.U * a.V);
            }
            return Math.Abs(area) * 0.5;
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
        }

        /// <summary>
        /// Convierte una lista de sólidos de Revit trasladados al origen en un MeshGeometry3D de WPF para vista previa 3D interactiva.
        /// </summary>
        public static MeshGeometry3D BuildWpfMeshFromInPlaceItem(InPlaceFamilyItem item)
        {
            MeshGeometry3D mesh = new MeshGeometry3D();
            if (item == null || item.ExtractedSolids.Count == 0) return mesh;

            XYZ centerFt = (item.OriginMinFt + item.MaxCornerFt) * 0.5;

            foreach (Solid s in item.ExtractedSolids)
            {
                foreach (Face f in s.Faces)
                {
                    try
                    {
                        Mesh revitMesh = f.Triangulate();
                        if (revitMesh == null) continue;

                        int baseIdx = mesh.Positions.Count;
                        for (int i = 0; i < revitMesh.Vertices.Count; i++)
                        {
                            XYZ v = revitMesh.Vertices[i] - centerFt;
                            mesh.Positions.Add(new Point3D(v.X * 30.48, v.Y * 30.48, v.Z * 30.48));
                        }

                        for (int t = 0; t < revitMesh.NumTriangles; t++)
                        {
                            MeshTriangle tri = revitMesh.get_Triangle(t);
                            mesh.TriangleIndices.Add(baseIdx + (int)tri.get_Index(0));
                            mesh.TriangleIndices.Add(baseIdx + (int)tri.get_Index(1));
                            mesh.TriangleIndices.Add(baseIdx + (int)tri.get_Index(2));
                        }
                    }
                    catch { }
                }
            }

            return mesh;
        }
    }

    public class ModularFamilyLoadOptions : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = true;
            return true;
        }

        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = true;
            return true;
        }
    }
}
