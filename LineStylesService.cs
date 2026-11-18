using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using RevitColor = Autodesk.Revit.DB.Color;
using RevitView = Autodesk.Revit.DB.View;

namespace RevitToSupabasePlugin
{
    public class LinePatternOption
    {
        public ElementId Id { get; set; } = ElementId.InvalidElementId;
        public string Name { get; set; } = "Sólido";
        public bool IsSolid { get; set; }
        public double[] DashArray { get; set; } = Array.Empty<double>();

        public override string ToString() => Name;
    }

    public class LineStyleItem
    {
        public ElementId CategoryId { get; set; } = ElementId.InvalidElementId;
        public ElementId GraphicsStyleId { get; set; } = ElementId.InvalidElementId;
        public string Name { get; set; } = string.Empty;
        public int LineWeight { get; set; } = 1;
        public byte ColorR { get; set; }
        public byte ColorG { get; set; }
        public byte ColorB { get; set; }
        public ElementId LinePatternId { get; set; } = ElementId.InvalidElementId;
        public string LinePatternName { get; set; } = "Sólido";
        public double[] DashArray { get; set; } = Array.Empty<double>();
        public bool IsBuiltIn { get; set; }
        public bool IsNonPrintable { get; set; }

        public string TipoLabel => IsBuiltIn ? "Sistema" : "Personalizado";
        public string NonPrintLabel => IsNonPrintable ? "NO IMPRIME" : "Imprimible";

        public System.Windows.Media.Brush PreviewBrush =>
            new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(ColorR, ColorG, ColorB));

        public System.Windows.Media.DoubleCollection PreviewDashCollection =>
            DashArray != null && DashArray.Length > 0
                ? new System.Windows.Media.DoubleCollection(DashArray)
                : new System.Windows.Media.DoubleCollection();

        public double PreviewThickness => Math.Max(1.0, Math.Min(8.0, LineWeight * 0.5 + 0.5));
    }

    public class PrintModeStatus
    {
        public int VisibleNonPrintLines { get; set; }
        public int HiddenNonPrintLines { get; set; }
        public bool IsPrintModeActive => HiddenNonPrintLines > 0 && VisibleNonPrintLines == 0;
    }

    public static class LineStylesService
    {
        private static readonly Guid NonPrintStylesSchemaGuid = new Guid("A7C4E912-3B8D-4F1A-9C2E-8D5F6A1B3C4E");
        private static readonly Guid HiddenLineBackupSchemaGuid = new Guid("B8D5F023-4C9E-4A2B-8D3F-9E6A7B2C4D5F");

        private const string NonPrintStorageName = "ARCA_NonPrintLineStyles_Config";
        private const string FieldStyleIds = "NonPrintStyleCategoryIds";
        private const string FieldOriginalGStyleId = "OriginalGraphicsStyleId";

        public const string DefaultAuxiliaryStyleName = "ARCA - Auxiliar No Imprimible";

        public static List<LinePatternOption> GetAllLinePatterns(Document doc)
        {
            var result = new List<LinePatternOption>();
            ElementId solidId = LinePatternElement.GetSolidPatternId();

            result.Add(new LinePatternOption
            {
                Id = solidId,
                Name = "Sólido (Solid)",
                IsSolid = true,
                DashArray = Array.Empty<double>()
            });

            var patterns = new FilteredElementCollector(doc)
                .OfClass(typeof(LinePatternElement))
                .Cast<LinePatternElement>()
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase);

            foreach (var p in patterns)
            {
                if (p.Id == solidId) continue;
                result.Add(new LinePatternOption
                {
                    Id = p.Id,
                    Name = p.Name,
                    IsSolid = false,
                    DashArray = ExtractDashArray(p)
                });
            }

            return result;
        }

        private static double[] ExtractDashArray(LinePatternElement patternElem)
        {
            try
            {
                LinePattern lp = patternElem.GetLinePattern();
                if (lp == null) return new double[] { 4, 2 };

                var segments = lp.GetSegments();
                if (segments == null || segments.Count == 0) return Array.Empty<double>();

                var dashes = new List<double>();
                foreach (var seg in segments)
                {
                    if (seg.Type == LinePatternSegmentType.Dot)
                    {
                        dashes.Add(1.5);
                    }
                    else
                    {
                        // Convert feet to approximate screen dash units (clamped for clean UI preview)
                        double len = Math.Max(2.0, Math.Min(14.0, seg.Length * 400.0));
                        dashes.Add(len);
                    }
                }
                return dashes.Count > 0 ? dashes.ToArray() : new double[] { 4, 2 };
            }
            catch
            {
                return new double[] { 4, 2 };
            }
        }

        public static List<LineStyleItem> GetAllLineStyles(Document doc)
        {
            var result = new List<LineStyleItem>();
            Category? linesCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            if (linesCat == null) return result;

            var patterns = GetAllLinePatterns(doc).ToDictionary(p => p.Id.Value, p => p);
            var nonPrintIds = GetNonPrintableStyleIds(doc);
            ElementId solidId = LinePatternElement.GetSolidPatternId();

            foreach (Category subCat in linesCat.SubCategories)
            {
                GraphicsStyle? gs = subCat.GetGraphicsStyle(GraphicsStyleType.Projection);
                if (gs == null) continue;

                int weight = subCat.GetLineWeight(GraphicsStyleType.Projection) ?? 1;
                RevitColor color = subCat.LineColor;
                byte r = color != null && color.IsValid ? color.Red : (byte)0;
                byte g = color != null && color.IsValid ? color.Green : (byte)0;
                byte b = color != null && color.IsValid ? color.Blue : (byte)0;

                ElementId patId = subCat.GetLinePatternId(GraphicsStyleType.Projection);
                if (patId == null || patId == ElementId.InvalidElementId)
                    patId = solidId;

                string patName = "Sólido (Solid)";
                double[] dashArr = Array.Empty<double>();
                if (patterns.TryGetValue(patId.Value, out var patOpt))
                {
                    patName = patOpt.Name;
                    dashArr = patOpt.DashArray;
                }

                result.Add(new LineStyleItem
                {
                    CategoryId = subCat.Id,
                    GraphicsStyleId = gs.Id,
                    Name = subCat.Name,
                    LineWeight = Math.Max(1, Math.Min(16, weight)),
                    ColorR = r,
                    ColorG = g,
                    ColorB = b,
                    LinePatternId = patId,
                    LinePatternName = patName,
                    DashArray = dashArr,
                    IsBuiltIn = subCat.Id.Value < 0,
                    IsNonPrintable = nonPrintIds.Contains(subCat.Id.Value)
                });
            }

            return result
                .OrderByDescending(x => x.IsNonPrintable)
                .ThenBy(x => x.IsBuiltIn)
                .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public static Category? FindSubcategoryById(Document doc, ElementId categoryId)
        {
            Category? linesCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            if (linesCat == null) return null;

            foreach (Category subCat in linesCat.SubCategories)
            {
                if (subCat.Id == categoryId)
                    return subCat;
            }
            return null;
        }

        public static GraphicsStyle? GetInvisibleLinesGraphicsStyle(Document doc)
        {
            Category? invCat = Category.GetCategory(doc, BuiltInCategory.OST_InvisibleLines);
            if (invCat != null)
            {
                var gs = invCat.GetGraphicsStyle(GraphicsStyleType.Projection);
                if (gs != null) return gs;
            }

            Category? linesCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            if (linesCat != null)
            {
                foreach (Category subCat in linesCat.SubCategories)
                {
                    if (subCat.Id.Value == (long)BuiltInCategory.OST_InvisibleLines ||
                        subCat.Name.IndexOf("Invisible", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var gs = subCat.GetGraphicsStyle(GraphicsStyleType.Projection);
                        if (gs != null) return gs;
                    }
                }
            }
            return null;
        }

        public static GraphicsStyle? GetThinLinesGraphicsStyle(Document doc)
        {
            Category? thinCat = Category.GetCategory(doc, BuiltInCategory.OST_GenericLines);
            if (thinCat != null)
            {
                var gs = thinCat.GetGraphicsStyle(GraphicsStyleType.Projection);
                if (gs != null) return gs;
            }

            Category? linesCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            if (linesCat != null)
            {
                foreach (Category subCat in linesCat.SubCategories)
                {
                    if (subCat.Id.Value == (long)BuiltInCategory.OST_GenericLines ||
                        subCat.Name.IndexOf("Thin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        subCat.Name.IndexOf("Fina", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var gs = subCat.GetGraphicsStyle(GraphicsStyleType.Projection);
                        if (gs != null) return gs;
                    }
                }
                foreach (Category subCat in linesCat.SubCategories)
                {
                    var gs = subCat.GetGraphicsStyle(GraphicsStyleType.Projection);
                    if (gs != null) return gs;
                }
            }
            return null;
        }

        public static ElementId CreateLineStyle(
            Document doc,
            string name,
            int weight,
            RevitColor color,
            ElementId patternId,
            bool markNonPrintable)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("El nombre del estilo de línea no puede estar vacío.");

            Category? linesCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            if (linesCat == null)
                throw new InvalidOperationException("No se encontró la categoría de líneas (OST_Lines) en el documento.");

            string trimmedName = name.Trim();
            foreach (Category existing in linesCat.SubCategories)
            {
                if (string.Equals(existing.Name, trimmedName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Ya existe un estilo de línea llamado '{trimmedName}'.");
            }

            ElementId createdId = ElementId.InvalidElementId;
            using (var t = new Transaction(doc, $"Crear Estilo de Línea: {trimmedName}"))
            {
                t.Start();
                Category newSub = doc.Settings.Categories.NewSubcategory(linesCat, trimmedName);
                newSub.SetLineWeight(Math.Max(1, Math.Min(16, weight)), GraphicsStyleType.Projection);
                newSub.LineColor = color;
                if (patternId != null && patternId != ElementId.InvalidElementId)
                {
                    newSub.SetLinePatternId(patternId, GraphicsStyleType.Projection);
                }
                createdId = newSub.Id;

                if (markNonPrintable)
                {
                    var ids = GetNonPrintableStyleIds(doc);
                    ids.Add(createdId.Value);
                    SaveNonPrintableStyleIds(doc, ids);
                }
                t.Commit();
            }

            return createdId;
        }

        public static void UpdateLineStyle(
            Document doc,
            ElementId categoryId,
            int weight,
            RevitColor color,
            ElementId patternId,
            bool markNonPrintable)
        {
            Category? subCat = FindSubcategoryById(doc, categoryId);
            if (subCat == null)
                throw new InvalidOperationException("No se encontró el estilo de línea seleccionado.");

            using (var t = new Transaction(doc, $"Actualizar Estilo de Línea: {subCat.Name}"))
            {
                t.Start();
                subCat.SetLineWeight(Math.Max(1, Math.Min(16, weight)), GraphicsStyleType.Projection);
                subCat.LineColor = color;
                if (patternId != null && patternId != ElementId.InvalidElementId)
                {
                    subCat.SetLinePatternId(patternId, GraphicsStyleType.Projection);
                }

                var ids = GetNonPrintableStyleIds(doc);
                if (markNonPrintable)
                    ids.Add(categoryId.Value);
                else
                    ids.Remove(categoryId.Value);
                SaveNonPrintableStyleIds(doc, ids);

                t.Commit();
            }
        }

        public static int DeleteLineStyle(Document doc, ElementId categoryId)
        {
            if (categoryId.Value < 0)
                throw new InvalidOperationException("No se pueden eliminar los estilos de línea integrados de Revit.");

            Category? subCat = FindSubcategoryById(doc, categoryId);
            if (subCat == null)
                throw new InvalidOperationException("No se encontró el estilo de línea.");

            GraphicsStyle? targetGs = subCat.GetGraphicsStyle(GraphicsStyleType.Projection);
            GraphicsStyle? fallbackGs = GetThinLinesGraphicsStyle(doc);
            int reassigned = 0;

            using (var t = new Transaction(doc, $"Eliminar Estilo de Línea: {subCat.Name}"))
            {
                t.Start();
                if (targetGs != null && fallbackGs != null && targetGs.Id != fallbackGs.Id)
                {
                    var curves = new FilteredElementCollector(doc)
                        .OfClass(typeof(CurveElement))
                        .Cast<CurveElement>()
                        .Where(c => c.LineStyle != null && c.LineStyle.Id == targetGs.Id)
                        .ToList();

                    foreach (var c in curves)
                    {
                        try
                        {
                            if (c.GroupId == ElementId.InvalidElementId)
                            {
                                c.LineStyle = fallbackGs;
                                reassigned++;
                            }
                        }
                        catch { }
                    }
                }

                var ids = GetNonPrintableStyleIds(doc);
                if (ids.Remove(categoryId.Value))
                {
                    SaveNonPrintableStyleIds(doc, ids);
                }

                doc.Delete(categoryId);
                t.Commit();
            }

            return reassigned;
        }

        public static (int converted, int skipped) ConvertElementsToLineStyle(
            Document doc,
            IEnumerable<ElementId> elementIds,
            ElementId targetGraphicsStyleId)
        {
            GraphicsStyle? targetGs = doc.GetElement(targetGraphicsStyleId) as GraphicsStyle;
            if (targetGs == null)
                throw new InvalidOperationException("No se encontró el estilo de línea destino.");

            int converted = 0;
            int skipped = 0;

            using (var t = new Transaction(doc, "Convertir Líneas a Estilo"))
            {
                t.Start();
                foreach (var id in elementIds)
                {
                    if (doc.GetElement(id) is CurveElement curve)
                    {
                        try
                        {
                            if (curve.GroupId != ElementId.InvalidElementId)
                            {
                                skipped++;
                                continue;
                            }
                            curve.LineStyle = targetGs;
                            converted++;
                        }
                        catch
                        {
                            skipped++;
                        }
                    }
                }
                t.Commit();
            }

            return (converted, skipped);
        }

        public static (int converted, int skipped) ConvertBySourceStyle(
            Document doc,
            ElementId sourceGraphicsStyleId,
            ElementId targetGraphicsStyleId,
            ElementId? activeViewId)
        {
            GraphicsStyle? targetGs = doc.GetElement(targetGraphicsStyleId) as GraphicsStyle;
            if (targetGs == null)
                throw new InvalidOperationException("No se encontró el estilo de línea destino.");

            FilteredElementCollector collector = activeViewId != null && activeViewId != ElementId.InvalidElementId
                ? new FilteredElementCollector(doc, activeViewId)
                : new FilteredElementCollector(doc);

            var curves = collector
                .OfClass(typeof(CurveElement))
                .Cast<CurveElement>()
                .Where(c => c.LineStyle != null && c.LineStyle.Id == sourceGraphicsStyleId)
                .ToList();

            int converted = 0;
            int skipped = 0;

            using (var t = new Transaction(doc, "Conversión Masiva de Líneas"))
            {
                t.Start();
                foreach (var curve in curves)
                {
                    try
                    {
                        if (curve.GroupId != ElementId.InvalidElementId)
                        {
                            skipped++;
                            continue;
                        }
                        curve.LineStyle = targetGs;
                        converted++;
                    }
                    catch
                    {
                        skipped++;
                    }
                }
                t.Commit();
            }

            return (converted, skipped);
        }

        public static List<ElementId> FindLinesByStyle(Document doc, ElementId graphicsStyleId, ElementId? activeViewId)
        {
            FilteredElementCollector collector = activeViewId != null && activeViewId != ElementId.InvalidElementId
                ? new FilteredElementCollector(doc, activeViewId)
                : new FilteredElementCollector(doc);

            return collector
                .OfClass(typeof(CurveElement))
                .Cast<CurveElement>()
                .Where(c => c.LineStyle != null && c.LineStyle.Id == graphicsStyleId)
                .Select(c => c.Id)
                .ToList();
        }

        public static PrintModeStatus GetPrintModeStatus(Document doc)
        {
            var status = new PrintModeStatus();
            var nonPrintCatIds = GetNonPrintableStyleIds(doc);
            var nonPrintGStyleIds = new HashSet<long>();

            foreach (long catIdVal in nonPrintCatIds)
            {
                Category? cat = FindSubcategoryById(doc, new ElementId(catIdVal));
                var gs = cat?.GetGraphicsStyle(GraphicsStyleType.Projection);
                if (gs != null) nonPrintGStyleIds.Add(gs.Id.Value);
            }

            Schema? backupSchema = Schema.Lookup(HiddenLineBackupSchemaGuid);

            var curves = new FilteredElementCollector(doc)
                .OfClass(typeof(CurveElement))
                .Cast<CurveElement>();

            foreach (var c in curves)
            {
                if (c.LineStyle != null && nonPrintGStyleIds.Contains(c.LineStyle.Id.Value))
                {
                    status.VisibleNonPrintLines++;
                }
                else if (backupSchema != null)
                {
                    Entity ent = c.GetEntity(backupSchema);
                    if (ent.IsValid())
                    {
                        status.HiddenNonPrintLines++;
                    }
                }
            }

            return status;
        }

        public static (int hiddenCount, int skippedCount) ActivatePrintMode(Document doc)
        {
            var nonPrintCatIds = GetNonPrintableStyleIds(doc);
            if (nonPrintCatIds.Count == 0)
                return (0, 0);

            var nonPrintGStyleIds = new HashSet<long>();
            foreach (long catIdVal in nonPrintCatIds)
            {
                Category? cat = FindSubcategoryById(doc, new ElementId(catIdVal));
                var gs = cat?.GetGraphicsStyle(GraphicsStyleType.Projection);
                if (gs != null) nonPrintGStyleIds.Add(gs.Id.Value);
            }

            if (nonPrintGStyleIds.Count == 0)
                return (0, 0);

            GraphicsStyle? invisibleGs = GetInvisibleLinesGraphicsStyle(doc);
            if (invisibleGs == null)
                throw new InvalidOperationException("No se encontró el estilo '<Invisible lines>' en Revit.");

            Schema backupSchema = GetOrCreateBackupSchema();

            var curves = new FilteredElementCollector(doc)
                .OfClass(typeof(CurveElement))
                .Cast<CurveElement>()
                .Where(c => c.LineStyle != null && nonPrintGStyleIds.Contains(c.LineStyle.Id.Value))
                .ToList();

            int hidden = 0;
            int skipped = 0;

            using (var t = new Transaction(doc, "Activar Modo Impresión (Ocultar Líneas Auxiliares)"))
            {
                t.Start();
                foreach (var c in curves)
                {
                    try
                    {
                        if (c.GroupId != ElementId.InvalidElementId)
                        {
                            skipped++;
                            continue;
                        }

                        long origGsId = c.LineStyle.Id.Value;
                        var entity = new Entity(backupSchema);
                        entity.Set(FieldOriginalGStyleId, origGsId);
                        c.SetEntity(entity);

                        c.LineStyle = invisibleGs;
                        hidden++;
                    }
                    catch
                    {
                        skipped++;
                    }
                }
                t.Commit();
            }

            return (hidden, skipped);
        }

        public static (int restoredCount, int skippedCount) ActivateWorkMode(Document doc)
        {
            Schema? backupSchema = Schema.Lookup(HiddenLineBackupSchemaGuid);
            if (backupSchema == null)
                return (0, 0);

            var curves = new FilteredElementCollector(doc)
                .OfClass(typeof(CurveElement))
                .Cast<CurveElement>()
                .ToList();

            int restored = 0;
            int skipped = 0;

            using (var t = new Transaction(doc, "Activar Modo Trabajo (Mostrar Líneas Auxiliares)"))
            {
                t.Start();
                foreach (var c in curves)
                {
                    Entity ent = c.GetEntity(backupSchema);
                    if (!ent.IsValid()) continue;

                    try
                    {
                        if (c.GroupId != ElementId.InvalidElementId)
                        {
                            skipped++;
                            continue;
                        }

                        long origIdVal = ent.Get<long>(FieldOriginalGStyleId);
                        if (doc.GetElement(new ElementId(origIdVal)) is GraphicsStyle origGs)
                        {
                            c.LineStyle = origGs;
                            c.DeleteEntity(backupSchema);
                            restored++;
                        }
                        else
                        {
                            skipped++;
                        }
                    }
                    catch
                    {
                        skipped++;
                    }
                }
                t.Commit();
            }

            return (restored, skipped);
        }

        #region ExtensibleStorage Helpers

        public static HashSet<long> GetNonPrintableStyleIds(Document doc)
        {
            var result = new HashSet<long>();
            try
            {
                Schema? schema = Schema.Lookup(NonPrintStylesSchemaGuid);
                if (schema == null) return result;

                DataStorage? storage = FindNonPrintStorage(doc);
                if (storage == null) return result;

                Entity ent = storage.GetEntity(schema);
                if (!ent.IsValid()) return result;

                string raw = ent.Get<string>(FieldStyleIds) ?? string.Empty;
                foreach (var part in raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (long.TryParse(part.Trim(), out long val))
                        result.Add(val);
                }
            }
            catch { }
            return result;
        }

        private static void SaveNonPrintableStyleIds(Document doc, HashSet<long> ids)
        {
            Schema schema = GetOrCreateNonPrintSchema();
            DataStorage? storage = FindNonPrintStorage(doc);
            if (storage == null)
            {
                storage = DataStorage.Create(doc);
                storage.Name = NonPrintStorageName;
            }

            var entity = new Entity(schema);
            entity.Set(FieldStyleIds, string.Join(",", ids));
            storage.SetEntity(entity);
        }

        private static DataStorage? FindNonPrintStorage(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .FirstOrDefault(ds => ds.Name == NonPrintStorageName);
        }

        private static Schema GetOrCreateNonPrintSchema()
        {
            Schema? schema = Schema.Lookup(NonPrintStylesSchemaGuid);
            if (schema != null) return schema;

            var sb = new SchemaBuilder(NonPrintStylesSchemaGuid);
            sb.SetSchemaName("ARCA_NonPrintLineStyles");
            sb.SetReadAccessLevel(AccessLevel.Public);
            sb.SetWriteAccessLevel(AccessLevel.Public);
            sb.AddSimpleField(FieldStyleIds, typeof(string));
            return sb.Finish();
        }

        private static Schema GetOrCreateBackupSchema()
        {
            Schema? schema = Schema.Lookup(HiddenLineBackupSchemaGuid);
            if (schema != null) return schema;

            var sb = new SchemaBuilder(HiddenLineBackupSchemaGuid);
            sb.SetSchemaName("ARCA_HiddenLineStyleBackup");
            sb.SetReadAccessLevel(AccessLevel.Public);
            sb.SetWriteAccessLevel(AccessLevel.Public);
            sb.AddSimpleField(FieldOriginalGStyleId, typeof(long));
            return sb.Finish();
        }

        #endregion
    }
}
