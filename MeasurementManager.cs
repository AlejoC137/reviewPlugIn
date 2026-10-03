using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;

namespace RevitToSupabasePlugin
{
    /// <summary>
    /// Administrador unificado de mediciones para Revit.
    /// Garantiza que TODO elemento generado por la app (DirectShape o Familia)
    /// sea 100% medible en Tablas de Planificación (Schedules) y propiedades por:
    /// - Medida_Longitud (m)
    /// - Medida_Area (m²)
    /// - Medida_Volumen (m³)
    /// - Medida_Espesor (m/cm)
    /// - Medida_Ancho (m/cm)
    /// - Medida_Unidades (Entero/Recuento)
    /// </summary>
    public static class MeasurementManager
    {
        // GUIDs fijos para consistencia en todo el ecosistema BIM
        public static readonly Guid GuidLongitud = new Guid("9A7B3C10-1111-4444-8888-A00000000001");
        public static readonly Guid GuidArea = new Guid("9A7B3C10-1111-4444-8888-A00000000002");
        public static readonly Guid GuidVolumen = new Guid("9A7B3C10-1111-4444-8888-A00000000003");
        public static readonly Guid GuidEspesor = new Guid("9A7B3C10-1111-4444-8888-A00000000004");
        public static readonly Guid GuidAncho = new Guid("9A7B3C10-1111-4444-8888-A00000000005");
        public static readonly Guid GuidUnidades = new Guid("9A7B3C10-1111-4444-8888-A00000000006");

        public const string ParamLongitud = "Medida_Longitud";
        public const string ParamArea = "Medida_Area";
        public const string ParamVolumen = "Medida_Volumen";
        public const string ParamEspesor = "Medida_Espesor";
        public const string ParamAncho = "Medida_Ancho";
        public const string ParamUnidades = "Medida_Unidades";

        private static readonly BuiltInCategory[] SupportedCategories = new[]
        {
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_GenericModel,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_Parts,
            BuiltInCategory.OST_Roofs,
            BuiltInCategory.OST_Ceilings,
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_PlumbingFixtures,
            BuiltInCategory.OST_SpecialityEquipment
        };

        /// <summary>
        /// Asegura que los parámetros compartidos de medición existan y estén vinculados al proyecto
        /// para todas las categorías soportadas. Debe llamarse dentro de una Transacción activa.
        /// </summary>
        public static void EnsureMeasurementParameters(Document doc)
        {
            if (doc == null || doc.IsReadOnly) return;
            Autodesk.Revit.ApplicationServices.Application app = doc.Application;

            string originalSpFile = app.SharedParametersFilename;
            string pluginSpFile = Path.Combine(Path.GetTempPath(), "ARCA_Medidas_SharedParams.txt");

            try
            {
                // Crear archivo temporal de parámetros compartidos si no existe
                CreateSharedParamFileIfNotExists(pluginSpFile);

                app.SharedParametersFilename = pluginSpFile;
                DefinitionFile defFile = app.OpenSharedParameterFile();
                if (defFile == null) return;

                DefinitionGroup group = defFile.Groups.get_Item("MedicionesBIM") ?? defFile.Groups.Create("MedicionesBIM");
                if (group == null) return;

                // Definiciones
                Definition defLong = GetOrCreateDef(group, ParamLongitud, SpecTypeId.Length, GuidLongitud, "Longitud neta desarrollada");
                Definition defArea = GetOrCreateDef(group, ParamArea, SpecTypeId.Area, GuidArea, "Área superficial o neta de huella");
                Definition defVol = GetOrCreateDef(group, ParamVolumen, SpecTypeId.Volume, GuidVolumen, "Volumen cúbico real del sólido");
                Definition defEsp = GetOrCreateDef(group, ParamEspesor, SpecTypeId.Length, GuidEspesor, "Espesor o peralte del elemento");
                Definition defAncho = GetOrCreateDef(group, ParamAncho, SpecTypeId.Length, GuidAncho, "Ancho transversal de la pieza");
                Definition defUni = GetOrCreateDef(group, ParamUnidades, SpecTypeId.Int.Integer, GuidUnidades, "Cantidad unitaria para cómputo");

                var definitions = new[] { defLong, defArea, defVol, defEsp, defAncho, defUni }.Where(d => d != null).ToList();

                // Construir CategorySet con las categorías soportadas
                CategorySet catSet = app.Create.NewCategorySet();
                foreach (BuiltInCategory bic in SupportedCategories)
                {
                    try
                    {
                        Category cat = doc.Settings.Categories.get_Item(bic);
                        if (cat != null && cat.AllowsBoundParameters)
                        {
                            catSet.Insert(cat);
                        }
                    }
                    catch { }
                }

                BindingMap bindingMap = doc.ParameterBindings;

                foreach (Definition def in definitions)
                {
                    if (def == null) continue;

                    // Si ya está vinculado, revisar si necesita re-vincularse
                    if (bindingMap.Contains(def))
                    {
                        if (bindingMap.get_Item(def) is InstanceBinding existingBinding)
                        {
                            // Expandir categorías si falta alguna
                            bool needsUpdate = false;
                            foreach (Category c in catSet)
                            {
                                if (!existingBinding.Categories.Contains(c))
                                {
                                    existingBinding.Categories.Insert(c);
                                    needsUpdate = true;
                                }
                            }
                            if (needsUpdate)
                            {
                                bindingMap.ReInsert(def, existingBinding, GroupTypeId.Geometry);
                            }
                        }
                    }
                    else
                    {
                        InstanceBinding newBinding = app.Create.NewInstanceBinding(catSet);
                        bindingMap.Insert(def, newBinding, GroupTypeId.Geometry);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MeasurementManager] Advertencia asegurando parámetros: {ex.Message}");
            }
            finally
            {
                try { app.SharedParametersFilename = originalSpFile; } catch { }
            }
        }

        private static Definition GetOrCreateDef(DefinitionGroup group, string name, ForgeTypeId specTypeId, Guid guid, string description)
        {
            Definition def = group.Definitions.get_Item(name);
            if (def != null) return def;

            try
            {
                ExternalDefinitionCreationOptions opt = new ExternalDefinitionCreationOptions(name, specTypeId)
                {
                    GUID = guid,
                    Description = description,
                    UserModifiable = true
                };
                return group.Definitions.Create(opt);
            }
            catch
            {
                return group.Definitions.get_Item(name);
            }
        }

        private static void CreateSharedParamFileIfNotExists(string path)
        {
            if (File.Exists(path)) return;

            using (StreamWriter sw = File.CreateText(path))
            {
                sw.WriteLine("# This is a Revit shared parameter file created by ARCA REVIEW Plugin.");
                sw.WriteLine("*META\tVERSION\tMINVERSION");
                sw.WriteLine("META\t2\t1");
                sw.WriteLine("*GROUP\tID\tNAME");
                sw.WriteLine("GROUP\t1\tMedicionesBIM");
                sw.WriteLine("*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE");
                sw.WriteLine($"PARAM\t{GuidLongitud}\t{ParamLongitud}\tLENGTH\t\t1\t1\tLongitud neta desarrollada\t1\t0");
                sw.WriteLine($"PARAM\t{GuidArea}\t{ParamArea}\tAREA\t\t1\t1\tÁrea neta o de huella\t1\t0");
                sw.WriteLine($"PARAM\t{GuidVolumen}\t{ParamVolumen}\tVOLUME\t\t1\t1\tVolumen cúbico real\t1\t0");
                sw.WriteLine($"PARAM\t{GuidEspesor}\t{ParamEspesor}\tLENGTH\t\t1\t1\tEspesor o peralte\t1\t0");
                sw.WriteLine($"PARAM\t{GuidAncho}\t{ParamAncho}\tLENGTH\t\t1\t1\tAncho transversal\t1\t0");
                sw.WriteLine($"PARAM\t{GuidUnidades}\t{ParamUnidades}\tINTEGER\t\t1\t1\tCantidad de unidades\t1\t0");
            }
        }

        /// <summary>
        /// Mide exhaustivamente un sólido y aplica los parámetros de medición (Longitud, Área, Volumen, Espesor, Ancho, Unidades)
        /// directamente al Elemento (DirectShape o FamilyInstance).
        /// </summary>
        public static void MeasureAndApply(
            Element elem, 
            Solid? solid = null, 
            double? customLengthFt = null, 
            double? customAreaSqFt = null, 
            double? customVolumeCuFt = null, 
            double? customThicknessFt = null,
            double? customWidthFt = null,
            int units = 1)
        {
            if (elem == null) return;

            double volumeFt3 = customVolumeCuFt ?? 0.0;
            double areaFt2 = customAreaSqFt ?? 0.0;
            double lengthFt = customLengthFt ?? 0.0;
            double thicknessFt = customThicknessFt ?? 0.0;
            double widthFt = customWidthFt ?? 0.0;

            if (solid != null && solid.Faces.Size > 0)
            {
                if (volumeFt3 <= 0.000001) volumeFt3 = solid.Volume;

                // Calcular BoundingBox del sólido
                BoundingBoxXYZ? bb = null;
                try { bb = solid.GetBoundingBox(); } catch { }

                double dx = bb != null ? Math.Abs(bb.Max.X - bb.Min.X) : 0;
                double dy = bb != null ? Math.Abs(bb.Max.Y - bb.Min.Y) : 0;
                double dz = bb != null ? Math.Abs(bb.Max.Z - bb.Min.Z) : 0;

                // Espesor por defecto es la dimensión menor o la altura Z
                if (thicknessFt <= 0.0001)
                {
                    thicknessFt = dz > 0.0001 ? dz : Math.Min(dx, dy);
                }

                // Longitud y Ancho
                if (lengthFt <= 0.0001)
                {
                    lengthFt = Math.Max(dx, dy);
                    if (lengthFt <= 0.0001) lengthFt = dz;
                }
                if (widthFt <= 0.0001)
                {
                    widthFt = Math.Min(dx, dy);
                    if (widthFt <= 0.0001) widthFt = Math.Min(Math.Max(dx, dy), dz);
                }

                // Área: Buscar la cara superior planar o la mayor cara horizontal
                if (areaFt2 <= 0.00001)
                {
                    double topArea = 0;
                    foreach (Face f in solid.Faces)
                    {
                        if (f is PlanarFace pf && pf.FaceNormal.Z > 0.4)
                        {
                            topArea += pf.Area;
                        }
                    }

                    if (topArea > 0.0001)
                    {
                        areaFt2 = topArea;
                    }
                    else if (dx > 0.0001 && dy > 0.0001)
                    {
                        areaFt2 = dx * dy;
                    }
                    else
                    {
                        areaFt2 = solid.SurfaceArea / 2.0;
                    }
                }
            }

            // Aplicar parámetros oficiales de ARCA
            SetParameterValue(elem, ParamLongitud, lengthFt);
            SetParameterValue(elem, ParamArea, areaFt2);
            SetParameterValue(elem, ParamVolumen, volumeFt3);
            SetParameterValue(elem, ParamEspesor, thicknessFt);
            SetParameterValue(elem, ParamAncho, widthFt);
            SetParameterValue(elem, ParamUnidades, units);

            // Respaldo de compatibilidad con parámetros comunes existentes
            SetParameterValue(elem, "Longitud", lengthFt);
            SetParameterValue(elem, "Área", areaFt2);
            SetParameterValue(elem, "Area", areaFt2);
            SetParameterValue(elem, "Volumen", volumeFt3);
            SetParameterValue(elem, "Espesor", thicknessFt);
            SetParameterValue(elem, "Grosor", thicknessFt);
            SetParameterValue(elem, "Largo", lengthFt);
            SetParameterValue(elem, "Ancho", widthFt);
            SetParameterValue(elem, "Alto", thicknessFt);
            SetParameterValue(elem, "Unidades", units);
            SetParameterValue(elem, "Cantidad", units);
        }

        private static void SetParameterValue(Element elem, string paramName, double val)
        {
            if (val <= 0.0000001) return;
            try
            {
                Parameter p = elem.LookupParameter(paramName);
                if (p != null && !p.IsReadOnly)
                {
                    p.Set(val);
                }
            }
            catch { }
        }

        private static void SetParameterValue(Element elem, string paramName, int val)
        {
            try
            {
                Parameter p = elem.LookupParameter(paramName);
                if (p != null && !p.IsReadOnly)
                {
                    p.Set(val);
                }
            }
            catch { }
        }
    }
}
