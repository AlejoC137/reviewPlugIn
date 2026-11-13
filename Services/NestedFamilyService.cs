using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitToSupabasePlugin.Models;
using Application = Autodesk.Revit.ApplicationServices.Application;

namespace RevitToSupabasePlugin.Services
{
    /// <summary>
    /// Servicio para inspeccionar, recorrer recursivamente y gestionar Familias Cargables (.rfa)
    /// y sus Familias Anidadas (Nested Families), tanto desde un Proyecto (.rvt) como dentro del Editor de Familias (.rfa).
    /// </summary>
    public static class NestedFamilyService
    {
        /// <summary>
        /// Lista todas las familias cargables editables en un proyecto (.rvt) con conteo de tipos y ejemplares colocados.
        /// </summary>
        public static List<LoadableFamilySummaryItem> GetLoadableFamiliesInProject(Document doc)
        {
            var result = new List<LoadableFamilySummaryItem>();
            if (doc == null || doc.IsFamilyDocument) return result;

            // Contar ejemplares colocados por FamilyId en una sola pasada
            var instanceCounts = new Dictionary<long, int>();
            var allInstances = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>();

            foreach (var fi in allInstances)
            {
                long famId = fi.Symbol?.Family?.Id.Value ?? -1;
                if (famId <= 0) continue;
                instanceCounts[famId] = instanceCounts.TryGetValue(famId, out int c) ? c + 1 : 1;
            }

            var families = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(f => f != null && !f.IsInPlace && f.IsEditable);

            foreach (var fam in families)
            {
                long fid = fam.Id.Value;
                int typeCount = 0;
                try { typeCount = fam.GetFamilySymbolIds()?.Count ?? 0; } catch { }

                bool isShared = false;
                try { isShared = fam.get_Parameter(BuiltInParameter.FAMILY_SHARED)?.AsInteger() == 1; } catch { }

                result.Add(new LoadableFamilySummaryItem
                {
                    FamilyIdValue = fid,
                    Name = fam.Name,
                    CategoryName = fam.FamilyCategory?.Name ?? "General",
                    IsShared = isShared,
                    TypeCount = typeCount,
                    PlacedInstanceCount = instanceCounts.TryGetValue(fid, out int cnt) ? cnt : 0,
                    NestedCount = -1
                });
            }

            return result.OrderBy(f => f.CategoryName).ThenBy(f => f.Name).ToList();
        }

        /// <summary>
        /// Construye el árbol completo de una familia cargable y todas sus familias anidadas (apertura parcial en memoria).
        /// Si activeDoc ya es un .rfa y targetFamily es null, inspecciona directamente el .rfa abierto.
        /// </summary>
        public static NestedFamilyNode? InspectFamilyTree(
            Application app,
            Document activeDoc,
            Family? targetFamily,
            int maxDepth = 5)
        {
            if (activeDoc == null) return null;

            if (activeDoc.IsFamilyDocument && targetFamily == null)
            {
                string rootName = activeDoc.OwnerFamily?.Name ?? Path.GetFileNameWithoutExtension(activeDoc.Title);
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootName };
                return BuildNodeFromFamilyDoc(app, activeDoc, familyRef: null, rootName, isRoot: true, depth: 0,
                    instanceCountInParent: 1, sampleInstanceIds: new List<long>(), parentHostDoc: null,
                    breadcrumb: rootName, visitedBranch: visited, maxDepth: maxDepth);
            }

            if (targetFamily == null || targetFamily.IsInPlace || !targetFamily.IsEditable)
                return null;

            Document? famDoc = null;
            try
            {
                famDoc = activeDoc.EditFamily(targetFamily);
                if (famDoc == null) return null;

                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { targetFamily.Name };
                return BuildNodeFromFamilyDoc(app, famDoc, targetFamily, targetFamily.Name, isRoot: true, depth: 0,
                    instanceCountInParent: 1, sampleInstanceIds: new List<long>(), parentHostDoc: activeDoc,
                    breadcrumb: targetFamily.Name, visitedBranch: visited, maxDepth: maxDepth);
            }
            finally
            {
                if (famDoc != null)
                {
                    try { famDoc.Close(false); } catch { }
                }
            }
        }

        private static NestedFamilyNode BuildNodeFromFamilyDoc(
            Application app,
            Document famDoc,
            Family? familyRef,
            string familyName,
            bool isRoot,
            int depth,
            int instanceCountInParent,
            List<long> sampleInstanceIds,
            Document? parentHostDoc,
            string breadcrumb,
            HashSet<string> visitedBranch,
            int maxDepth)
        {
            bool isShared = false;
            try
            {
                Family? owner = famDoc.OwnerFamily ?? familyRef;
                if (owner != null)
                    isShared = owner.get_Parameter(BuiltInParameter.FAMILY_SHARED)?.AsInteger() == 1;
            }
            catch { }

            string catName = famDoc.OwnerFamily?.FamilyCategory?.Name
                             ?? familyRef?.FamilyCategory?.Name
                             ?? "Familia";

            var node = new NestedFamilyNode
            {
                FamilyIdValue = familyRef?.Id.Value ?? -1,
                Name = familyName,
                CategoryName = catName,
                IsRoot = isRoot,
                IsShared = isShared,
                Depth = depth,
                InstanceCountInParent = instanceCountInParent,
                SampleInstanceIds = sampleInstanceIds,
                PathBreadcrumb = breadcrumb,
                ActiveTypeName = famDoc.FamilyManager?.CurrentType?.Name ?? "—"
            };

            // 1. Leer Tipos de Familia
            if (famDoc.FamilyManager != null)
            {
                FamilyManager fm = famDoc.FamilyManager;
                string activeTName = fm.CurrentType?.Name ?? "";
                foreach (FamilyType ft in fm.Types)
                {
                    if (ft == null) continue;
                    var previewParts = new List<string>();
                    int valCount = 0;
                    foreach (FamilyParameter fp in fm.Parameters)
                    {
                        if (fp == null || !ft.HasValue(fp)) continue;
                        valCount++;
                        if (previewParts.Count < 3 && fp.Definition != null)
                        {
                            string v = NativeParameterService.ReadFamilyParameterDisplayValue(famDoc, ft, fp);
                            if (v != "—") previewParts.Add($"{fp.Definition.Name}: {v}");
                        }
                    }

                    node.Types.Add(new FamilyTypeSummaryItem
                    {
                        TypeName = string.IsNullOrWhiteSpace(ft.Name) ? "(Predeterminado)" : ft.Name,
                        IsCurrent = string.Equals(ft.Name, activeTName, StringComparison.Ordinal),
                        ParameterValueCount = valCount,
                        PreviewValues = previewParts.Count > 0 ? string.Join(" | ", previewParts) : "Sin valores explícitos"
                    });
                }

                // 2. Leer Parámetros Nativos de esta familia
                node.Parameters = NativeParameterService.InspectDocumentParameters(app, famDoc);
            }

            // 3. Leer Asociaciones de este nodo con su documento padre (si parentHostDoc es un .rfa y hay sampleInstanceIds)
            if (!isRoot && parentHostDoc != null && parentHostDoc.IsFamilyDocument && sampleInstanceIds.Count > 0)
            {
                ElementId firstInstId = new ElementId(sampleInstanceIds[0]);
                if (parentHostDoc.GetElement(firstInstId) is FamilyInstance fi)
                {
                    node.Associations = ExtractAssociationsForNestedInstance(parentHostDoc, fi);
                }
            }

            // 4. Recorrer Familias Anidadas dentro de famDoc
            if (depth < maxDepth)
            {
                var nestedInstances = new FilteredElementCollector(famDoc)
                    .OfClass(typeof(FamilyInstance))
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .ToList();

                var groupedByFamily = nestedInstances
                    .Where(fi => fi.Symbol?.Family != null)
                    .GroupBy(fi => fi.Symbol.Family.Id.Value)
                    .OrderBy(g => g.First().Symbol.Family.Name);

                foreach (var group in groupedByFamily)
                {
                    FamilyInstance firstInst = group.First();
                    Family nestedFam = firstInst.Symbol.Family;
                    var instIds = group.Select(x => x.Id.Value).ToList();
                    string childBreadcrumb = $"{breadcrumb} ▸ {nestedFam.Name}";

                    if (!nestedFam.IsEditable || nestedFam.IsInPlace)
                    {
                        node.Children.Add(new NestedFamilyNode
                        {
                            FamilyIdValue = nestedFam.Id.Value,
                            Name = nestedFam.Name,
                            CategoryName = nestedFam.FamilyCategory?.Name ?? "Anidada",
                            IsRoot = false,
                            Depth = depth + 1,
                            InstanceCountInParent = instIds.Count,
                            SampleInstanceIds = instIds,
                            PathBreadcrumb = childBreadcrumb,
                            Associations = famDoc.IsFamilyDocument
                                ? ExtractAssociationsForNestedInstance(famDoc, firstInst)
                                : new List<NestedAssociationItem>()
                        });
                        continue;
                    }

                    if (!visitedBranch.Add(nestedFam.Name))
                    {
                        node.Children.Add(new NestedFamilyNode
                        {
                            FamilyIdValue = nestedFam.Id.Value,
                            Name = $"{nestedFam.Name} (Referencia circular)",
                            CategoryName = nestedFam.FamilyCategory?.Name ?? "Anidada",
                            IsRoot = false,
                            Depth = depth + 1,
                            InstanceCountInParent = instIds.Count,
                            SampleInstanceIds = instIds,
                            PathBreadcrumb = childBreadcrumb,
                            CycleWarning = "Ciclo detectado en jerarquía."
                        });
                        continue;
                    }

                    Document? childDoc = null;
                    try
                    {
                        childDoc = famDoc.EditFamily(nestedFam);
                        if (childDoc != null)
                        {
                            var childNode = BuildNodeFromFamilyDoc(
                                app, childDoc, nestedFam, nestedFam.Name,
                                isRoot: false, depth: depth + 1,
                                instanceCountInParent: instIds.Count,
                                sampleInstanceIds: instIds,
                                parentHostDoc: famDoc,
                                breadcrumb: childBreadcrumb,
                                visitedBranch: visitedBranch,
                                maxDepth: maxDepth);
                            node.Children.Add(childNode);
                        }
                    }
                    catch
                    {
                        node.Children.Add(new NestedFamilyNode
                        {
                            FamilyIdValue = nestedFam.Id.Value,
                            Name = nestedFam.Name,
                            CategoryName = nestedFam.FamilyCategory?.Name ?? "Anidada",
                            IsRoot = false,
                            Depth = depth + 1,
                            InstanceCountInParent = instIds.Count,
                            SampleInstanceIds = instIds,
                            PathBreadcrumb = childBreadcrumb
                        });
                    }
                    finally
                    {
                        visitedBranch.Remove(nestedFam.Name);
                        if (childDoc != null)
                        {
                            try { childDoc.Close(false); } catch { }
                        }
                    }
                }
            }

            return node;
        }

        /// <summary>
        /// Extrae los parámetros asociables de un FamilyInstance anidado dentro de su familia anfitriona (.rfa).
        /// </summary>
        public static List<NestedAssociationItem> ExtractAssociationsForNestedInstance(Document hostRfaDoc, FamilyInstance nestedInst)
        {
            var list = new List<NestedAssociationItem>();
            if (hostRfaDoc == null || !hostRfaDoc.IsFamilyDocument || nestedInst == null) return list;

            FamilyManager fm = hostRfaDoc.FamilyManager;
            if (fm == null) return list;

            // Agrupar FamilyParameters del anfitrión por SpecTypeId para sugerir solo los compatibles
            var hostParamsBySpec = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (FamilyParameter hp in fm.Parameters)
            {
                if (hp?.Definition == null) continue;
                string spec = hp.Definition.GetDataType()?.TypeId ?? "";
                if (!hostParamsBySpec.TryGetValue(spec, out var bucket))
                {
                    bucket = new List<string>();
                    hostParamsBySpec[spec] = bucket;
                }
                bucket.Add(hp.Definition.Name);
            }

            void ReadTargetParams(Element target, bool isTypeParam)
            {
                foreach (Parameter p in target.Parameters)
                {
                    if (p?.Definition == null) continue;
                    bool canAssoc = false;
                    try { canAssoc = fm.CanElementParameterBeAssociated(p); } catch { }
                    if (!canAssoc) continue;

                    string spec = p.Definition.GetDataType()?.TypeId ?? "";
                    FamilyParameter? linked = null;
                    try { linked = fm.GetAssociatedFamilyParameter(p); } catch { }

                    var compat = new List<string> { "— Sin asociar —" };
                    if (hostParamsBySpec.TryGetValue(spec, out var matching))
                        compat.AddRange(matching.OrderBy(x => x));

                    list.Add(new NestedAssociationItem
                    {
                        NestedInstanceIdValue = nestedInst.Id.Value,
                        NestedFamilyName = nestedInst.Symbol?.Family?.Name ?? nestedInst.Name,
                        NestedParamName = p.Definition.Name,
                        IsNestedTypeParam = isTypeParam,
                        DataTypeLabel = NativeParameterService.SafeGetSpecLabel(p.Definition.GetDataType()),
                        SpecTypeIdRaw = spec,
                        CurrentValue = NativeParameterService.ReadParameterDisplayValue(hostRfaDoc, p),
                        HostFamilyParamName = linked?.Definition?.Name ?? "— Sin asociar —",
                        CompatibleHostParams = compat
                    });
                }
            }

            ReadTargetParams(nestedInst, isTypeParam: false);
            if (nestedInst.Symbol != null)
                ReadTargetParams(nestedInst.Symbol, isTypeParam: true);

            return list.OrderBy(a => a.IsNestedTypeParam ? 1 : 0).ThenBy(a => a.NestedParamName).ToList();
        }

        /// <summary>
        /// Asocia o desasocia un parámetro de una familia anidada con un parámetro de la familia anfitriona (.rfa).
        /// </summary>
        public static (bool ok, string message) AssociateNestedParameter(
            Document hostRfaDoc,
            long nestedInstanceIdValue,
            string nestedParamName,
            bool isNestedTypeParam,
            string? hostFamilyParamName)
        {
            if (hostRfaDoc == null || !hostRfaDoc.IsFamilyDocument)
                return (false, "Solo disponible al editar una Familia (.rfa).");

            if (hostRfaDoc.GetElement(new ElementId(nestedInstanceIdValue)) is not FamilyInstance fi)
                return (false, "No se encontró el ejemplar de la familia anidada.");

            Element target = isNestedTypeParam && fi.Symbol != null ? fi.Symbol : fi;
            Parameter? nestedParam = target.LookupParameter(nestedParamName);
            if (nestedParam == null)
                return (false, $"No se encontró el parámetro '{nestedParamName}' en la familia anidada.");

            FamilyManager fm = hostRfaDoc.FamilyManager;
            FamilyParameter? hostFp = null;
            bool unlinking = string.IsNullOrWhiteSpace(hostFamilyParamName) || hostFamilyParamName == "— Sin asociar —";
            if (!unlinking)
            {
                hostFp = fm.get_Parameter(hostFamilyParamName);
                if (hostFp == null)
                    return (false, $"No se encontró el parámetro anfitrión '{hostFamilyParamName}'.");
            }

            using (var tx = new Transaction(hostRfaDoc, $"ARCA: Asociar {nestedParamName}"))
            {
                tx.Start();
                try
                {
                    fm.AssociateElementParameterToFamilyParameter(nestedParam, hostFp);
                    tx.Commit();
                    return (true, unlinking
                        ? $"Parámetro '{nestedParamName}' desasociado."
                        : $"Parámetro '{nestedParamName}' asociado a '{hostFamilyParamName}'.");
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted()) tx.RollBack();
                    return (false, $"No se pudo asociar '{nestedParamName}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Cuenta cuántas familias anidadas directas tiene una familia (escaneo rápido).
        /// </summary>
        public static int CountDirectNestedFamilies(Document doc, Family fam)
        {
            if (doc == null || fam == null || fam.IsInPlace || !fam.IsEditable) return 0;
            Document? famDoc = null;
            try
            {
                famDoc = doc.EditFamily(fam);
                if (famDoc == null) return 0;

                return new FilteredElementCollector(famDoc)
                    .OfClass(typeof(FamilyInstance))
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .Select(fi => fi.Symbol?.Family?.Id.Value ?? -1)
                    .Where(id => id > 0)
                    .Distinct()
                    .Count();
            }
            catch
            {
                return 0;
            }
            finally
            {
                if (famDoc != null)
                {
                    try { famDoc.Close(false); } catch { }
                }
            }
        }

        /// <summary>
        /// Abre una familia (raíz o anidada por nombre de ruta) en el Editor de Familias visible de Revit.
        /// </summary>
        public static (bool ok, string message) OpenFamilyNodeInEditor(
            UIApplication uiApp,
            Document activeDoc,
            long rootFamilyIdValue,
            string targetFamilyName)
        {
            if (uiApp == null || activeDoc == null)
                return (false, "No hay documento activo.");

            try
            {
                Document? currentDoc = activeDoc;
                var openedTempDocs = new List<Document>();

                try
                {
                    // Si estamos en .rvt y tenemos rootFamilyIdValue, abrir primero la familia raíz
                    if (!activeDoc.IsFamilyDocument && rootFamilyIdValue > 0)
                    {
                        if (activeDoc.GetElement(new ElementId(rootFamilyIdValue)) is not Family rootFam)
                            return (false, "No se encontró la familia raíz en el proyecto.");

                        currentDoc = activeDoc.EditFamily(rootFam);
                        openedTempDocs.Add(currentDoc);

                        if (string.Equals(rootFam.Name, targetFamilyName, StringComparison.OrdinalIgnoreCase))
                        {
                            return SaveAndActivateFamilyDoc(uiApp, currentDoc, rootFam.Name, openedTempDocs);
                        }
                    }

                    // Buscar la familia anidada por nombre dentro de currentDoc (BFS en caso de multinivel)
                    Family? foundNested = FindFamilyByNameInDoc(currentDoc, targetFamilyName);
                    if (foundNested == null)
                        return (false, $"No se pudo localizar '{targetFamilyName}' para abrirla en el editor.");

                    Document nestedDoc = currentDoc.EditFamily(foundNested);
                    openedTempDocs.Add(nestedDoc);
                    return SaveAndActivateFamilyDoc(uiApp, nestedDoc, foundNested.Name, openedTempDocs);
                }
                finally
                {
                    foreach (var d in openedTempDocs)
                    {
                        try { if (d.IsValidObject) d.Close(false); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                return (false, $"Error abriendo familia en editor: {ex.Message}");
            }
        }

        private static Family? FindFamilyByNameInDoc(Document doc, string targetName)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .FirstOrDefault(f => f != null && !f.IsInPlace && f.IsEditable &&
                                     string.Equals(f.Name, targetName, StringComparison.OrdinalIgnoreCase));
        }

        private static (bool ok, string message) SaveAndActivateFamilyDoc(
            UIApplication uiApp,
            Document famDoc,
            string familyName,
            List<Document> openedTempDocs)
        {
            string existingPath = famDoc.PathName;
            if (!string.IsNullOrWhiteSpace(existingPath) && File.Exists(existingPath))
            {
                foreach (var d in openedTempDocs)
                {
                    try { if (d.IsValidObject) d.Close(false); } catch { }
                }
                openedTempDocs.Clear();
                uiApp.OpenAndActivateDocument(existingPath);
                return (true, $"Abierta en Editor de Familias: {existingPath}");
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "ARCA_NestedPreview");
            Directory.CreateDirectory(tempDir);
            string safeName = string.Concat(familyName.Split(Path.GetInvalidFileNameChars()));
            string tempPath = Path.Combine(tempDir, $"{safeName}.rfa");

            famDoc.SaveAs(tempPath, new SaveAsOptions { OverwriteExistingFile = true });

            foreach (var d in openedTempDocs)
            {
                try { if (d.IsValidObject) d.Close(false); } catch { }
            }
            openedTempDocs.Clear();

            uiApp.OpenAndActivateDocument(tempPath);
            return (true, $"Familia '{familyName}' extraída y abierta en el Editor de Familias.");
        }

        private static readonly char[] RevitForbiddenChars = { '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~' };

        /// <summary>
        /// Renombra una familia (raíz en .rvt o cualquier familia anidada en .rvt / .rfa) directamente desde el árbol.
        /// Si es una anidada profunda o desde un proyecto, abre en memoria la cadena padre, renombra y recarga automáticamente.
        /// </summary>
        public static (bool ok, string message) RenameFamilyNode(
            Document activeDoc,
            long rootFamilyIdValue,
            NestedFamilyNode targetNode,
            string newName)
        {
            if (activeDoc == null || targetNode == null)
                return (false, "Documento o nodo inválido.");

            string cleanName = (newName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(cleanName))
                return (false, "El nombre de la familia no puede estar vacío.");

            if (cleanName.IndexOfAny(RevitForbiddenChars) >= 0)
                return (false, "El nombre contiene caracteres prohibidos por Revit (\\ : { } [ ] | ; < > ? ` ~).");

            if (string.Equals(cleanName, targetNode.Name, StringComparison.Ordinal))
                return (true, "El nombre no cambió.");

            if (activeDoc.IsFamilyDocument && targetNode.IsRoot)
                return (false, "La familia raíz abierta se renombra al guardarla (Guardar como .RFA). Selecciona una familia anidada para renombrarla.");

            // Caso directo: raíz en .rvt (Depth 0) o anidada directa en .rfa (Depth 1)
            bool isDirectChildInActiveDoc = (!activeDoc.IsFamilyDocument && targetNode.IsRoot)
                                            || (activeDoc.IsFamilyDocument && targetNode.Depth == 1);

            if (isDirectChildInActiveDoc)
            {
                Family? fam = targetNode.FamilyIdValue > 0
                    ? activeDoc.GetElement(new ElementId(targetNode.FamilyIdValue)) as Family
                    : FindFamilyByNameInDoc(activeDoc, targetNode.Name);

                if (fam == null)
                    return (false, $"No se encontró la familia '{targetNode.Name}' en el documento activo.");

                return RenameFamilyDirectInDoc(activeDoc, fam, cleanName);
            }

            // Caso anidado en cadena (.rvt con Depth >= 1, o .rfa con Depth >= 2)
            string[] segments = targetNode.PathBreadcrumb
                .Split(new[] { " ▸ " }, StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length < 2)
                return (false, "No se pudo resolver la ruta jerárquica de la familia anidada.");

            // Construir lista de familias intermedias desde activeDoc hasta el padre inmediato de targetNode
            var parentChainNames = new List<string>();
            if (!activeDoc.IsFamilyDocument)
            {
                // En .rvt, segments[0] es la raíz cargable en activeDoc
                for (int i = 0; i < segments.Length - 1; i++)
                    parentChainNames.Add(segments[i]);
            }
            else
            {
                // En .rfa, segments[0] es el propio activeDoc; empezamos desde segments[1] hasta el padre inmediato
                for (int i = 1; i < segments.Length - 1; i++)
                    parentChainNames.Add(segments[i]);
            }

            return RenameNestedRecursive(activeDoc, rootFamilyIdValue, parentChainNames, 0, targetNode.Name, cleanName);
        }

        private static (bool ok, string message) RenameFamilyDirectInDoc(Document doc, Family fam, string cleanName)
        {
            bool duplicate = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Any(f => f != null && f.Id != fam.Id && string.Equals(f.Name, cleanName, StringComparison.OrdinalIgnoreCase));

            if (duplicate)
                return (false, $"Ya existe una familia llamada '{cleanName}' en '{doc.Title}'.");

            string oldName = fam.Name;
            using (var tx = new Transaction(doc, $"ARCA: Renombrar familia {oldName}"))
            {
                tx.Start();
                try
                {
                    fam.Name = cleanName;
                    tx.Commit();
                    return (true, $"Familia renombrada: '{oldName}' ➔ '{cleanName}'.");
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted()) tx.RollBack();
                    return (false, $"Revit no permitió renombrar '{oldName}': {ex.Message}");
                }
            }
        }

        private static (bool ok, string message) RenameNestedRecursive(
            Document currentParentDoc,
            long rootFamilyIdValue,
            List<string> parentChainNames,
            int chainIndex,
            string targetNestedName,
            string cleanNewName)
        {
            if (chainIndex >= parentChainNames.Count)
            {
                Family? targetFam = FindFamilyByNameInDoc(currentParentDoc, targetNestedName);
                if (targetFam == null)
                    return (false, $"No se encontró la familia anidada '{targetNestedName}'.");

                return RenameFamilyDirectInDoc(currentParentDoc, targetFam, cleanNewName);
            }

            string nextParentName = parentChainNames[chainIndex];
            Family? nextParentFam = (chainIndex == 0 && !currentParentDoc.IsFamilyDocument && rootFamilyIdValue > 0)
                ? currentParentDoc.GetElement(new ElementId(rootFamilyIdValue)) as Family
                : FindFamilyByNameInDoc(currentParentDoc, nextParentName);

            if (nextParentFam == null || !nextParentFam.IsEditable)
                return (false, $"No se pudo abrir la familia padre '{nextParentName}'.");

            Document? childDoc = null;
            try
            {
                childDoc = currentParentDoc.EditFamily(nextParentFam);
                if (childDoc == null)
                    return (false, $"No se pudo editar '{nextParentName}' en memoria.");

                var (ok, msg) = RenameNestedRecursive(childDoc, -1, parentChainNames, chainIndex + 1, targetNestedName, cleanNewName);
                if (!ok) return (false, msg);

                childDoc.LoadFamily(currentParentDoc, new ModularFamilyLoadOptions());
                return (true, msg);
            }
            catch (Exception ex)
            {
                return (false, $"Error recargando '{nextParentName}': {ex.Message}");
            }
            finally
            {
                if (childDoc != null)
                {
                    try { childDoc.Close(false); } catch { }
                }
            }
        }
    }
}
