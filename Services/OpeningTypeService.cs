using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace RevitToSupabasePlugin
{
    /// <summary>
    /// Crea o actualiza un tipo de puerta/ventana (p. ej. "P03") con hasta 4 capas paramétricas
    /// (nombre, material y espesor por capa) y un indicador de acústica.
    /// Los valores de las capas viven en parámetros de tipo, por lo que se pueden editar después.
    /// </summary>
    public static class OpeningTypeService
    {
        public const int MaxLayers = 8;

        private const string ParamAcoustic = "Acústica";
        private const string ParamDescription = "Descripción Capas";
        private const string ParamTotalThickness = "Espesor Total Capas";

        public static JObject CreateOrUpdate(Autodesk.Revit.ApplicationServices.Application app, Document doc, JObject args)
        {
            var result = new JObject();
            try
            {
                string typeMark = (args["type_mark"]?.ToString() ?? "").Trim();
                if (string.IsNullOrWhiteSpace(typeMark))
                    throw new ArgumentException("El parámetro 'type_mark' es obligatorio (ej. 'P03').");

                string categoryStr = args["category"]?.ToString() ?? "Windows";
                bool isWindow = !categoryStr.Equals("Doors", StringComparison.OrdinalIgnoreCase)
                                && !categoryStr.Equals("Puertas", StringComparison.OrdinalIgnoreCase);

                string familyName = string.IsNullOrWhiteSpace(args["family_name"]?.ToString())
                    ? $"{typeMark} - {(isWindow ? "Ventana" : "Puerta")} Acústica"
                    : args["family_name"]!.ToString().Trim();

                bool acoustic = args["acoustic"]?.Value<bool>() ?? true;
                var layers = ParseLayers(args["layers"] as JArray);
                double? widthMm = args["width_mm"]?.Value<double>();
                double? heightMm = args["height_mm"]?.Value<double>();

                // Familia existente en el proyecto -> editar; si no, crear desde plantilla.
                Family? existing = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .FirstOrDefault(f => f.Name == familyName);

                Document famDoc;
                bool created;
                if (existing != null)
                {
                    famDoc = doc.EditFamily(existing);
                    created = false;
                }
                else
                {
                    string? template = FindTemplate(app, isWindow);
                    if (template == null)
                        throw new FileNotFoundException(
                            $"No se encontró la plantilla de {(isWindow ? "ventana" : "puerta")} (.rft) de Revit 2027.");
                    famDoc = app.NewFamilyDocument(template);
                    created = true;
                }

                try
                {
                    using (Transaction tx = new Transaction(famDoc, $"Configurar tipo {typeMark}"))
                    {
                        tx.Start();
                        FamilyManager fm = famDoc.FamilyManager;

                        EnsureParameter(fm, ParamAcoustic, SpecTypeId.Boolean.YesNo, false);
                        EnsureParameter(fm, ParamDescription, SpecTypeId.String.Text, false);
                        EnsureParameter(fm, ParamTotalThickness, SpecTypeId.Length, false);
                        for (int i = 1; i <= MaxLayers; i++)
                        {
                            EnsureParameter(fm, $"Capa{i}_Nombre", SpecTypeId.String.Text, false);
                            EnsureParameter(fm, $"Capa{i}_Material", SpecTypeId.String.Text, false);
                            EnsureParameter(fm, $"Capa{i}_Espesor", SpecTypeId.Length, false);
                        }

                        FamilyType type = FindOrCreateType(fm, typeMark);
                        fm.CurrentType = type;

                        FamilyParameter? typeMarkParam = null;
                        try { typeMarkParam = fm.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK); } catch { }
                        if (typeMarkParam != null) fm.Set(typeMarkParam, typeMark);

                        SetBool(fm, ParamAcoustic, acoustic);

                        double totalFt = 0;
                        for (int i = 1; i <= MaxLayers; i++)
                        {
                            LayerSpec? l = i <= layers.Count ? layers[i - 1] : null;
                            SetText(fm, $"Capa{i}_Nombre", l?.Name ?? "");
                            SetText(fm, $"Capa{i}_Material", l?.Material ?? "");
                            SetLength(fm, $"Capa{i}_Espesor", (l?.ThicknessMm ?? 0) / 304.8);
                            totalFt += (l?.ThicknessMm ?? 0) / 304.8;
                        }
                        SetLength(fm, ParamTotalThickness, totalFt);
                        SetText(fm, ParamDescription, string.Join(" | ", layers.Select((l, idx) =>
                            $"{idx + 1}. {l.Name} ({l.Material}, {l.ThicknessMm:0.#} mm)")));

                        if (widthMm.HasValue) SetDimension(fm, "Width", widthMm.Value / 304.8);
                        if (heightMm.HasValue) SetDimension(fm, "Height", heightMm.Value / 304.8);

                        if (layers.Count > 0 && !HasLayerGeometry(famDoc))
                            BuildLayerGeometry(famDoc, fm, layers);

                        tx.Commit();
                    }

                    string tempPath = Path.Combine(Path.GetTempPath(), SanitizeFileName(familyName) + ".rfa");
                    famDoc.SaveAs(tempPath, new SaveAsOptions { OverwriteExistingFile = true });

                    using (Transaction tProj = new Transaction(doc, $"Cargar familia {familyName}"))
                    {
                        tProj.Start();
                        doc.LoadFamily(tempPath, new OverwriteLoadOptions(), out Family _);
                        tProj.Commit();
                    }
                    try { File.Delete(tempPath); } catch { }
                }
                finally
                {
                    famDoc.Close(false);
                }

                FamilySymbol? symbol = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(s => s.FamilyName == familyName && s.Name == typeMark);

                if (symbol == null)
                    throw new InvalidOperationException($"La familia '{familyName}' se cargó pero no se encontró el tipo '{typeMark}'.");

                using (Transaction tAct = new Transaction(doc, "Activar tipo"))
                {
                    tAct.Start();
                    if (!symbol.IsActive) symbol.Activate();
                    tAct.Commit();
                }

                result["status"] = "success";
                result["action"] = created ? "created" : "updated";
                result["family_name"] = familyName;
                result["type_name"] = typeMark;
                result["type_id"] = symbol.Id.Value;
                result["acoustic"] = acoustic;
                result["layers_count"] = layers.Count;
                result["layers"] = new JArray(layers.Select(l => new JObject
                {
                    ["name"] = l.Name,
                    ["material"] = l.Material,
                    ["thickness_mm"] = l.ThicknessMm
                }));
            }
            catch (Exception ex)
            {
                result["status"] = "error";
                result["error"] = ex.Message;
            }
            return result;
        }

        /// <summary>
        /// Crea una extrusión rectangular por capa (plano XZ, extruida en Y = espesor de pared).
        /// Cada capa tiene su posición inicial/final ligada a fórmulas de los parámetros de espesor,
        /// de modo que al cambiar Capa{i}_Espesor la geometría se actualiza sola.
        /// </summary>
        private static void BuildLayerGeometry(Document famDoc, FamilyManager fm, List<LayerSpec> layers)
        {
            double widthFt = ReadTypeDimension(fm, "Width", 1000.0 / 304.8);
            double heightFt = ReadTypeDimension(fm, "Height", 1200.0 / 304.8);

            SketchPlane sp = SketchPlane.Create(famDoc, Plane.CreateByNormalAndOrigin(XYZ.BasisY, XYZ.Zero));

            for (int i = 1; i <= layers.Count; i++)
            {
                string iniName = $"Ini_Capa{i}";
                string finName = $"Fin_Capa{i}";

                FamilyParameter ini = FindParam(fm, iniName)
                    ?? fm.AddParameter(iniName, GroupTypeId.Data, SpecTypeId.Length, true);
                FamilyParameter fin = FindParam(fm, finName)
                    ?? fm.AddParameter(finName, GroupTypeId.Data, SpecTypeId.Length, true);

                string prevSum = i == 1
                    ? "0"
                    : string.Join(" + ", Enumerable.Range(1, i - 1).Select(k => $"Capa{k}_Espesor"));
                fm.SetFormula(ini, prevSum);
                fm.SetFormula(fin, $"{iniName} + Capa{i}_Espesor");

                XYZ p0 = new XYZ(-widthFt / 2, 0, 0);
                XYZ p1 = new XYZ(widthFt / 2, 0, 0);
                XYZ p2 = new XYZ(widthFt / 2, 0, heightFt);
                XYZ p3 = new XYZ(-widthFt / 2, 0, heightFt);

                CurveArray loop = new CurveArray();
                loop.Append(Line.CreateBound(p0, p1));
                loop.Append(Line.CreateBound(p1, p2));
                loop.Append(Line.CreateBound(p2, p3));
                loop.Append(Line.CreateBound(p3, p0));

                CurveArrArray profile = new CurveArrArray();
                profile.Append(loop);

                double lengthFt = layers[i - 1].ThicknessMm / 304.8;
                Extrusion ext = famDoc.FamilyCreate.NewExtrusion(true, profile, sp, lengthFt);

                fm.AssociateElementParameterToFamilyParameter(
                    ext.get_Parameter(BuiltInParameter.EXTRUSION_START_PARAM), ini);
                fm.AssociateElementParameterToFamilyParameter(
                    ext.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM), fin);
            }
        }

        private static double ReadTypeDimension(FamilyManager fm, string name, double fallbackFt)
        {
            var p = FindParam(fm, name);
            if (p == null || fm.CurrentType == null) return fallbackFt;
            try
            {
                double v = fm.CurrentType.AsDouble(p) ?? 0;
                return v > 0 ? v : fallbackFt;
            }
            catch { return fallbackFt; }
        }

        private static bool HasLayerGeometry(Document famDoc) =>
            new FilteredElementCollector(famDoc).OfClass(typeof(Extrusion)).Any();

        private sealed class LayerSpec
        {
            public string Name { get; set; } = "";
            public string Material { get; set; } = "";
            public double ThicknessMm { get; set; }
        }

        private static List<LayerSpec> ParseLayers(JArray? arr)
        {
            var list = new List<LayerSpec>();
            if (arr == null) return list;
            if (arr.Count > MaxLayers)
                throw new ArgumentException($"Máximo {MaxLayers} capas por tipo; recibidas {arr.Count}.");
            foreach (JToken t in arr)
            {
                double mm = t["thickness_mm"]?.Value<double>() ?? 0;
                if (mm <= 0)
                    throw new ArgumentException($"La capa '{t["name"]}' necesita 'thickness_mm' mayor que 0.");
                list.Add(new LayerSpec
                {
                    Name = t["name"]?.ToString() ?? "",
                    Material = t["material"]?.ToString() ?? "",
                    ThicknessMm = mm
                });
            }
            return list;
        }

        private static string? FindTemplate(Autodesk.Revit.ApplicationServices.Application app, bool isWindow)
        {
            string file = isWindow ? "Metric Window.rft" : "Metric Door.rft";
            string root = @"C:\ProgramData\Autodesk\RVT 2027\Family Templates";
            if (!Directory.Exists(root)) return null;
            string[] preferred = { "English", "Spanish" };
            foreach (string lang in preferred)
            {
                string p = Path.Combine(root, lang, file);
                if (File.Exists(p)) return p;
            }
            return Directory.EnumerateFiles(root, file, SearchOption.AllDirectories).FirstOrDefault();
        }

        private static void EnsureParameter(FamilyManager fm, string name, ForgeTypeId spec, bool isInstance)
        {
            foreach (FamilyParameter p in fm.Parameters)
                if (p.Definition.Name == name) return;
            fm.AddParameter(name, GroupTypeId.Data, spec, isInstance);
        }

        private static FamilyType FindOrCreateType(FamilyManager fm, string typeName)
        {
            foreach (FamilyType t in fm.Types)
                if (t.Name == typeName) return t;
            return fm.NewType(typeName);
        }

        private static FamilyParameter? FindParam(FamilyManager fm, string name)
            => fm.Parameters.Cast<FamilyParameter>().FirstOrDefault(p => p.Definition.Name == name);

        private static void SetText(FamilyManager fm, string name, string value)
        {
            var p = FindParam(fm, name);
            if (p != null) fm.Set(p, value);
        }

        private static void SetBool(FamilyManager fm, string name, bool value)
        {
            var p = FindParam(fm, name);
            if (p != null) fm.Set(p, value ? 1 : 0);
        }

        private static void SetLength(FamilyManager fm, string name, double feet)
        {
            var p = FindParam(fm, name);
            if (p != null) fm.Set(p, feet);
        }

        private static void SetDimension(FamilyManager fm, string name, double feet)
        {
            var p = FindParam(fm, name);
            if (p != null) fm.Set(p, feet);
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        private sealed class OverwriteLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = true;
                return true;
            }

            public bool OnSharedFamilyFound(Family family, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = true;
                return true;
            }
        }
    }
}
