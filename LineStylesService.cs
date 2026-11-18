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
        public string ScaleLabel => LineStylesService.ExtractScaleLabel(Name);

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
        public int NonPrintStylesCount { get; set; }
        public int VisibleNonPrintLines { get; set; }
        public int HiddenStylesCount { get; set; }
        public bool IsPrintModeActive => HiddenStylesCount > 0;
    }

    public static class LineStylesService
    {
        private static readonly Guid NonPrintStylesSchemaGuid = new Guid("A7C4E912-3B8D-4F1A-9C2E-8D5F6A1B3C4E");
        private static readonly Guid HiddenStyleBackupSchemaGuid = new Guid("C9E6A134-5D0F-4B3C-9E4A-0F7B8C3D5E6A");

        private const string NonPrintStorageName = "ARCA_NonPrintLineStyles_Config";
        private const string HiddenStyleStorageName = "ARCA_NonPrintLineStyles_PrintBackup";
        private const string FieldStyleIds = "NonPrintStyleCategoryIds";
        private const string FieldStyleBackups = "StyleColorWeightBackups";

        public const string DefaultAuxiliaryStyleName = "ARCA - Auxiliar No Imprimible";
        public const string GiantSpacePatternName = "ARCA - Espacio Gigante (No Imprime)";

        public static readonly int[] StandardScales = { 10, 20, 25, 50, 100, 200 };

        public static string GetScaleStyleName(int scale) => $"ARCA - Auxiliar [1:{scale}]";
        public static string GetScalePatternName(int scale) => $"ARCA - Espacio [1:{scale}]";

        public static string ExtractScaleLabel(string styleName)
        {
            if (string.IsNullOrWhiteSpace(styleName)) return "—";
            var match = System.Text.RegularExpressions.Regex.Match(styleName, @"1\s*:\s*(\d+)");
            return match.Success ? $"1:{match.Groups[1].Value}" : "Todas";
        }

        public static ElementId EnsureScaleAuxiliaryStyle(Document doc, int scale)
        {
            string styleName = GetScaleStyleName(scale);
            Category? linesCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            if (linesCat == null)
                throw new InvalidOperationException("No se encontró la categoría de líneas (OST_Lines).");

            foreach (Category existing in linesCat.SubCategories)
            {
                if (string.Equals(existing.Name, styleName, StringComparison.OrdinalIgnoreCase))
                {
                    var ids = GetNonPrintableStyleIds(doc);
                    if (!ids.Contains(existing.Id.Value))
                    {
                        using (var t = new Transaction(doc, $"Marcar {styleName} como No Imprimible"))
                        {
                            t.Start();
                            ids.Add(existing.Id.Value);
                            SaveNonPrintableStyleIds(doc, ids);
                            t.Commit();
                        }
                    }
                    return existing.Id;
                }
            }

            var dashPat = GetAllLinePatterns(doc).FirstOrDefault(p =>
                !p.IsSolid &&
                (p.Name.IndexOf("Dash", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 p.Name.IndexOf("Traz", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 p.Name.IndexOf("Hidden", StringComparison.OrdinalIgnoreCase) >= 0))
                ?? GetAllLinePatterns(doc).FirstOrDefault(p => !p.IsSolid);

            ElementId patId = dashPat?.Id ?? LinePatternElement.GetSolidPatternId();
            RevitColor color = GetScaleColor(scale);

            return CreateLineStyle(doc, styleName, 2, color, patId, true);
        }

        public static (int created, int existing) CreateStandardScaleKit(Document doc, IEnumerable<int> scales)
        {
            Category? linesCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            if (linesCat == null)
                throw new InvalidOperationException("No se encontró la categoría de líneas (OST_Lines).");

            var existingNames = new Dictionary<string, ElementId>(StringComparer.OrdinalIgnoreCase);
            foreach (Category c in linesCat.SubCategories)
            {
                existingNames[c.Name] = c.Id;
            }

            var dashPat = GetAllLinePatterns(doc).FirstOrDefault(p =>
                !p.IsSolid &&
                (p.Name.IndexOf("Dash", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 p.Name.IndexOf("Traz", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 p.Name.IndexOf("Hidden", StringComparison.OrdinalIgnoreCase) >= 0))
                ?? GetAllLinePatterns(doc).FirstOrDefault(p => !p.IsSolid);

            ElementId patId = dashPat?.Id ?? LinePatternElement.GetSolidPatternId();
            var nonPrintIds = GetNonPrintableStyleIds(doc);

            int created = 0;
            int existingCount = 0;

            using (var t = new Transaction(doc, "Crear Kit de Estilos No Imprimibles por Escala"))
            {
                t.Start();
                foreach (int scale in scales)
                {
                    string name = GetScaleStyleName(scale);
                    if (existingNames.TryGetValue(name, out ElementId? existId))
                    {
                        nonPrintIds.Add(existId.Value);
                        existingCount++;
                        continue;
                    }

                    Category newSub = doc.Settings.Categories.NewSubcategory(linesCat, name);
                    newSub.SetLineWeight(2, GraphicsStyleType.Projection);
                    newSub.LineColor = GetScaleColor(scale);
                    newSub.SetLinePatternId(patId, GraphicsStyleType.Projection);
                    nonPrintIds.Add(newSub.Id.Value);
                    created++;
                }
                SaveNonPrintableStyleIds(doc, nonPrintIds);
                t.Commit();
            }

            return (created, existingCount);
        }

        private static RevitColor GetScaleColor(int scale)
        {
            return scale switch
            {
                <= 10 => new RevitColor(236, 72, 153),  // Rosa
                <= 25 => new RevitColor(168, 85, 247),  // Violeta
                <= 50 => new RevitColor(217, 70, 239),  // Magenta
                <= 100 => new RevitColor(6, 182, 212),  // Cian
                _ => new RevitColor(249, 115, 22)       // Naranja
            };
        }

        public static ElementId GetOrCreateGiantSpacePatternId(Document doc)
        {
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(LinePatternElement))
                .Cast<LinePatternElement>()
                .FirstOrDefault(p => string.Equals(p.Name, GiantSpacePatternName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
                return existing.Id;

            double[] candidateSpaces = { 2.5, 1.0, 0.5 };
            foreach (double spaceFeet in candidateSpaces)
            {
                try
                {
                    var lp = new LinePattern(GiantSpacePatternName);
                    lp.SetSegments(new List<LinePatternSegment>
                    {
                        new LinePatternSegment(LinePatternSegmentType.Dot, 0.0),
                        new LinePatternSegment(LinePatternSegmentType.Space, spaceFeet)
                    });

                    LinePatternElement created = LinePatternElement.Create(doc, lp);
                    return created.Id;
                }
                catch
                {
                    // Try smaller space length if Revit rejects this length
                }
            }

            throw new InvalidOperationException("No se pudo crear el patrón de espacio gigante en Revit.");
        }

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
            status.NonPrintStylesCount = nonPrintCatIds.Count;

            var nonPrintGStyleIds = new HashSet<long>();
            foreach (long catIdVal in nonPrintCatIds)
            {
                Category? cat = FindSubcategoryById(doc, new ElementId(catIdVal));
                var gs = cat?.GetGraphicsStyle(GraphicsStyleType.Projection);
                if (gs != null) nonPrintGStyleIds.Add(gs.Id.Value);
            }

            if (nonPrintGStyleIds.Count > 0)
            {
                var curves = new FilteredElementCollector(doc)
                    .OfClass(typeof(CurveElement))
                    .Cast<CurveElement>();

                foreach (var c in curves)
                {
                    if (c.LineStyle != null && nonPrintGStyleIds.Contains(c.LineStyle.Id.Value))
                    {
                        status.VisibleNonPrintLines++;
                    }
                }
            }

            var backups = LoadStyleBackups(doc);
            status.HiddenStylesCount = backups.Count;

            return status;
        }

        public static (int stylesHidden, int linesAffected) ActivatePrintMode(Document doc)
        {
            var nonPrintCatIds = GetNonPrintableStyleIds(doc);
            if (nonPrintCatIds.Count == 0)
                return (0, 0);

            var backups = LoadStyleBackups(doc);
            int stylesHidden = 0;
            var affectedGStyleIds = new HashSet<long>();
            ElementId solidId = LinePatternElement.GetSolidPatternId();

            using (var t = new Transaction(doc, "Activar Modo Impresión (Patrón Espacio Gigante)"))
            {
                t.Start();
                ElementId giantPatId = GetOrCreateGiantSpacePatternId(doc);

                foreach (long catIdVal in nonPrintCatIds)
                {
                    Category? subCat = FindSubcategoryById(doc, new ElementId(catIdVal));
                    if (subCat == null) continue;

                    var gs = subCat.GetGraphicsStyle(GraphicsStyleType.Projection);
                    if (gs != null) affectedGStyleIds.Add(gs.Id.Value);

                    if (!backups.ContainsKey(catIdVal))
                    {
                        ElementId patId = subCat.GetLinePatternId(GraphicsStyleType.Projection);
                        if (patId == null || patId == ElementId.InvalidElementId)
                            patId = solidId;
                        int w = subCat.GetLineWeight(GraphicsStyleType.Projection) ?? 1;
                        backups[catIdVal] = (patId.Value, w);
                    }

                    subCat.SetLinePatternId(giantPatId, GraphicsStyleType.Projection);
                    subCat.SetLineWeight(1, GraphicsStyleType.Projection);
                    stylesHidden++;
                }

                SaveStyleBackups(doc, backups);
                t.Commit();
            }

            int linesAffected = 0;
            if (affectedGStyleIds.Count > 0)
            {
                linesAffected = new FilteredElementCollector(doc)
                    .OfClass(typeof(CurveElement))
                    .Cast<CurveElement>()
                    .Count(c => c.LineStyle != null && affectedGStyleIds.Contains(c.LineStyle.Id.Value));
            }

            return (stylesHidden, linesAffected);
        }

        public static (int stylesRestored, int skipped) ActivateWorkMode(Document doc)
        {
            var backups = LoadStyleBackups(doc);
            if (backups.Count == 0)
                return (0, 0);

            int restored = 0;
            int skipped = 0;

            using (var t = new Transaction(doc, "Activar Modo Trabajo (Mostrar Estilos No Imprimibles)"))
            {
                t.Start();
                foreach (var kvp in backups)
                {
                    Category? subCat = FindSubcategoryById(doc, new ElementId(kvp.Key));
                    if (subCat == null)
                    {
                        skipped++;
                        continue;
                    }

                    var (patIdVal, w) = kvp.Value;
                    ElementId patId = new ElementId(patIdVal);
                    if (patIdVal == LinePatternElement.GetSolidPatternId().Value || doc.GetElement(patId) is LinePatternElement)
                    {
                        subCat.SetLinePatternId(patId, GraphicsStyleType.Projection);
                    }
                    else
                    {
                        subCat.SetLinePatternId(LinePatternElement.GetSolidPatternId(), GraphicsStyleType.Projection);
                    }
                    subCat.SetLineWeight(Math.Max(1, Math.Min(16, w)), GraphicsStyleType.Projection);
                    restored++;
                }

                SaveStyleBackups(doc, new Dictionary<long, (long PatternId, int Weight)>());
                t.Commit();
            }

            return (restored, skipped);
        }

        public static string ExportViewToPdfWithoutNonPrint(Document doc, ElementId viewOrSheetId, string outputFolder, string fileNameWithoutExt)
        {
            bool wasAlreadyPrintMode = GetPrintModeStatus(doc).IsPrintModeActive;
            if (!wasAlreadyPrintMode)
            {
                ActivatePrintMode(doc);
            }

            try
            {
                var pdfOpts = new PDFExportOptions
                {
                    FileName = fileNameWithoutExt,
                    Combine = true,
                    ColorDepth = ColorDepthType.Color
                };

                doc.Export(outputFolder, new List<ElementId> { viewOrSheetId }, pdfOpts);
                return System.IO.Path.Combine(outputFolder, fileNameWithoutExt + ".pdf");
            }
            finally
            {
                if (!wasAlreadyPrintMode)
                {
                    ActivateWorkMode(doc);
                }
            }
        }

        #region ExtensibleStorage Helpers

        public static HashSet<long> GetNonPrintableStyleIds(Document doc)
        {
            var result = new HashSet<long>();
            try
            {
                Schema? schema = Schema.Lookup(NonPrintStylesSchemaGuid);
                if (schema == null) return result;

                DataStorage? storage = FindDataStorage(doc, NonPrintStorageName);
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
            DataStorage? storage = FindDataStorage(doc, NonPrintStorageName);
            if (storage == null)
            {
                storage = DataStorage.Create(doc);
                storage.Name = NonPrintStorageName;
            }

            var entity = new Entity(schema);
            entity.Set(FieldStyleIds, string.Join(",", ids));
            storage.SetEntity(entity);
        }

        private static Dictionary<long, (long PatternId, int Weight)> LoadStyleBackups(Document doc)
        {
            var result = new Dictionary<long, (long PatternId, int Weight)>();
            try
            {
                Schema? schema = Schema.Lookup(HiddenStyleBackupSchemaGuid);
                if (schema == null) return result;

                DataStorage? storage = FindDataStorage(doc, HiddenStyleStorageName);
                if (storage == null) return result;

                Entity ent = storage.GetEntity(schema);
                if (!ent.IsValid()) return result;

                string raw = ent.Get<string>(FieldStyleBackups) ?? string.Empty;
                foreach (var entry in raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = entry.Split(':');
                    if (parts.Length == 3 &&
                        long.TryParse(parts[0], out long id) &&
                        long.TryParse(parts[1], out long patId) &&
                        int.TryParse(parts[2], out int w))
                    {
                        result[id] = (patId, w);
                    }
                }
            }
            catch { }
            return result;
        }

        private static void SaveStyleBackups(Document doc, Dictionary<long, (long PatternId, int Weight)> backups)
        {
            Schema schema = GetOrCreateStyleBackupSchema();
            DataStorage? storage = FindDataStorage(doc, HiddenStyleStorageName);
            if (storage == null)
            {
                storage = DataStorage.Create(doc);
                storage.Name = HiddenStyleStorageName;
            }

            string serialized = string.Join(";", backups.Select(kvp =>
                $"{kvp.Key}:{kvp.Value.PatternId}:{kvp.Value.Weight}"));

            var entity = new Entity(schema);
            entity.Set(FieldStyleBackups, serialized);
            storage.SetEntity(entity);
        }

        private static DataStorage? FindDataStorage(Document doc, string name)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .FirstOrDefault(ds => ds.Name == name);
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

        private static Schema GetOrCreateStyleBackupSchema()
        {
            Schema? schema = Schema.Lookup(HiddenStyleBackupSchemaGuid);
            if (schema != null) return schema;

            var sb = new SchemaBuilder(HiddenStyleBackupSchemaGuid);
            sb.SetSchemaName("ARCA_NonPrintStyleColorBackup");
            sb.SetReadAccessLevel(AccessLevel.Public);
            sb.SetWriteAccessLevel(AccessLevel.Public);
            sb.AddSimpleField(FieldStyleBackups, typeof(string));
            return sb.Finish();
        }

        #endregion
    }
}
