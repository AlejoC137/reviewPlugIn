using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using RevitToSupabasePlugin.Models;
using Application = Autodesk.Revit.ApplicationServices.Application;

namespace RevitToSupabasePlugin.Services
{
    public static class ParameterTransformationService
    {
        /// <summary>
        /// Escanea todos los parámetros del documento activo (.rvt o .rfa):
        /// Parámetros de Proyecto (No-Shared), Parámetros Compartidos, Parámetros Globales y Parámetros de Familia.
        /// </summary>
        public static List<ProjectParameterItem> ScanDocumentParameters(
            Application app,
            Document doc,
            bool computeUsageStats = true)
        {
            var results = new List<ProjectParameterItem>();
            if (doc == null) return results;

            // Obtener GUIDs presentes en el archivo .txt activo para detectar parámetros compartidos huérfanos
            var activeTxtItems = SharedParameterCloudService.ReadSharedParameterFileItems(app, null);
            var activeTxtGuids = new HashSet<Guid>(activeTxtItems.Where(x => x.Guid != Guid.Empty).Select(x => x.Guid));

            // Caso 1: Documento de Familia (.rfa)
            if (doc.IsFamilyDocument)
            {
                try
                {
                    foreach (FamilyParameter fp in doc.FamilyManager.Parameters)
                    {
                        if (fp?.Definition == null) continue;
                        bool isShared = fp.IsShared;
                        Guid g = Guid.Empty;
                        if (isShared)
                        {
                            try { g = fp.GUID; } catch { }
                        }

                        var dt = fp.Definition.GetDataType();
                        var grp = fp.Definition.GetGroupTypeId();

                        results.Add(new ProjectParameterItem
                        {
                            ParameterElementId = fp.Id.Value,
                            Name = fp.Definition.Name,
                            NewName = fp.Definition.Name,
                            Origin = isShared ? ParameterOriginKind.Shared : ParameterOriginKind.Family,
                            GuidText = isShared && g != Guid.Empty ? g.ToString() : "—",
                            IsInstance = fp.IsInstance,
                            DataTypeLabel = SafeGetSpecLabel(dt),
                            SpecTypeIdRaw = dt?.TypeId ?? "",
                            GroupTypeIdRaw = grp?.TypeId ?? "",
                            GroupLabel = SafeGetGroupLabel(grp),
                            StorageType = fp.StorageType,
                            BoundCategoryNames = new List<string> { doc.OwnerFamily?.FamilyCategory?.Name ?? "Familia" },
                            PopulatedElementsCount = doc.FamilyManager.Types.Size,
                            TotalElementsInBoundCategories = doc.FamilyManager.Types.Size,
                            IsOrphanInTxt = isShared && g != Guid.Empty && !activeTxtGuids.Contains(g),
                            StatusText = string.IsNullOrWhiteSpace(fp.Formula) ? "Activo en .rfa" : $"Fórmula: {fp.Formula}"
                        });
                    }
                }
                catch { }

                return results.OrderBy(r => r.Name).ToList();
            }

            // Caso 2: Documento de Proyecto (.rvt) -> Recorrer ParameterBindings
            BindingMap map = doc.ParameterBindings;
            DefinitionBindingMapIterator it = map.ForwardIterator();
            it.Reset();

            while (it.MoveNext())
            {
                Definition? def = it.Key;
                ElementBinding? binding = it.Current as ElementBinding;
                if (def == null || binding == null) continue;

                long paramElemId = -1;
                ParameterOriginKind origin = ParameterOriginKind.Project;
                string guidStr = "—";
                bool isOrphan = false;
                StorageType storageType = StorageType.String;

                if (def is InternalDefinition intDef)
                {
                    paramElemId = intDef.Id.Value;
                    Element? paramElem = doc.GetElement(intDef.Id);
                    if (paramElem is SharedParameterElement spElem)
                    {
                        origin = ParameterOriginKind.Shared;
                        try
                        {
                            Guid g = spElem.GuidValue;
                            guidStr = g.ToString();
                            isOrphan = !activeTxtGuids.Contains(g);
                        }
                        catch { }
                    }
                }

                var catIds = new List<long>();
                var catNames = new List<string>();
                if (binding.Categories != null)
                {
                    foreach (Category cat in binding.Categories)
                    {
                        if (cat == null) continue;
                        catIds.Add(cat.Id.Value);
                        catNames.Add(cat.Name);
                    }
                }
                catNames.Sort();

                var dt = def.GetDataType();
                var grp = def.GetGroupTypeId();
                storageType = InferStorageTypeFromSpec(dt);

                var item = new ProjectParameterItem
                {
                    ParameterElementId = paramElemId,
                    Name = def.Name,
                    NewName = def.Name,
                    Origin = origin,
                    GuidText = guidStr,
                    IsInstance = binding is InstanceBinding,
                    DataTypeLabel = SafeGetSpecLabel(dt),
                    SpecTypeIdRaw = dt?.TypeId ?? "",
                    GroupTypeIdRaw = grp?.TypeId ?? "",
                    GroupLabel = SafeGetGroupLabel(grp),
                    StorageType = storageType,
                    BoundCategoryIds = catIds,
                    BoundCategoryNames = catNames,
                    IsOrphanInTxt = isOrphan,
                    StatusText = origin == ParameterOriginKind.Shared
                        ? (isOrphan ? "⚠️ Compartido Huérfano" : "✅ Compartido Vinculado")
                        : "🔶 Proyecto (No-Shared)"
                };

                results.Add(item);
            }

            // Escanear Parámetros Globales si están disponibles
            try
            {
                if (GlobalParametersManager.AreGlobalParametersAllowed(doc))
                {
                    var gpIds = GlobalParametersManager.GetAllGlobalParameters(doc);
                    foreach (ElementId gpId in gpIds)
                    {
                        if (doc.GetElement(gpId) is GlobalParameter gp && gp.GetDefinition() != null)
                        {
                            var def = gp.GetDefinition();
                            var dt = def.GetDataType();
                            var grp = def.GetGroupTypeId();

                            results.Add(new ProjectParameterItem
                            {
                                ParameterElementId = gp.Id.Value,
                                Name = def.Name,
                                NewName = def.Name,
                                Origin = ParameterOriginKind.Global,
                                GuidText = "GLOBAL",
                                IsInstance = false,
                                DataTypeLabel = SafeGetSpecLabel(dt),
                                SpecTypeIdRaw = dt?.TypeId ?? "",
                                GroupTypeIdRaw = grp?.TypeId ?? "",
                                GroupLabel = SafeGetGroupLabel(grp),
                                BoundCategoryNames = new List<string> { "Global del Proyecto" },
                                PopulatedElementsCount = gp.GetValue() != null ? 1 : 0,
                                TotalElementsInBoundCategories = 1,
                                StatusText = string.IsNullOrWhiteSpace(gp.GetFormula()) ? "Global Activo" : $"Fórmula: {gp.GetFormula()}"
                            });
                        }
                    }
                }
            }
            catch { }

            // Calcular estadísticas de uso de parámetros en los elementos del modelo
            if (computeUsageStats && results.Count > 0)
            {
                ComputeParameterUsageStatistics(doc, results);
            }

            return results
                .OrderBy(r => r.Origin)
                .ThenBy(r => r.Name)
                .ToList();
        }

        private static void ComputeParameterUsageStatistics(Document doc, List<ProjectParameterItem> items)
        {
            try
            {
                var instanceParamsByCat = new Dictionary<long, List<ProjectParameterItem>>();
                var typeParamsByCat = new Dictionary<long, List<ProjectParameterItem>>();

                foreach (var pItem in items)
                {
                    if (pItem.ParameterElementId <= 0 || pItem.Origin == ParameterOriginKind.Global)
                        continue;

                    var targetDict = pItem.IsInstance ? instanceParamsByCat : typeParamsByCat;
                    foreach (long catId in pItem.BoundCategoryIds)
                    {
                        if (!targetDict.TryGetValue(catId, out var list))
                        {
                            list = new List<ProjectParameterItem>();
                            targetDict[catId] = list;
                        }
                        list.Add(pItem);
                    }
                }

                if (instanceParamsByCat.Count > 0)
                {
                    var instanceCollector = new FilteredElementCollector(doc)
                        .WhereElementIsNotElementType()
                        .WhereElementIsViewIndependent();

                    foreach (Element el in instanceCollector)
                    {
                        Category? cat = el.Category;
                        if (cat == null) continue;
                        if (!instanceParamsByCat.TryGetValue(cat.Id.Value, out var relevantParams)) continue;

                        foreach (var pItem in relevantParams)
                        {
                            pItem.TotalElementsInBoundCategories++;
                            Parameter? p = el.LookupParameter(pItem.Name);
                            if (p != null && HasMeaningfulValue(p))
                            {
                                pItem.PopulatedElementsCount++;
                            }
                        }
                    }
                }

                if (typeParamsByCat.Count > 0)
                {
                    var typeCollector = new FilteredElementCollector(doc).WhereElementIsElementType();
                    foreach (Element elType in typeCollector)
                    {
                        Category? cat = elType.Category;
                        if (cat == null) continue;
                        if (!typeParamsByCat.TryGetValue(cat.Id.Value, out var relevantParams)) continue;

                        foreach (var pItem in relevantParams)
                        {
                            pItem.TotalElementsInBoundCategories++;
                            Parameter? p = elType.LookupParameter(pItem.Name);
                            if (p != null && HasMeaningfulValue(p))
                            {
                                pItem.PopulatedElementsCount++;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        // ══════════════════════════════════════════════════════════════════════════
        // MOTOR TRANSFORMATIVO: PROYECTO (NO-SHARED) -> COMPARTIDO (SHARED)
        // Y CONVERSIÓN INSTANCIA <-> TIPO PRESERVANDO TODOS LOS VALORES
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Transforma uno o varios Parámetros de Proyecto (No-Shared) en Parámetros Compartidos (Shared)
        /// registrándolos en el archivo .txt activo y preservando todos los valores de los elementos y tipos del modelo.
        /// También permite cambiar opcionalmente su ámbito (Mantener, Forzar Instancia o Forzar Tipo).
        /// </summary>
        public static (int convertedCount, int preservedValuesCount, List<string> logs) ConvertProjectParametersToShared(
            Application app,
            Document doc,
            List<ProjectParameterItem> parametersToConvert,
            string targetSharedGroupName = "01_ARCA_Transformados",
            string scopeOverride = "KEEP") // "KEEP", "INSTANCE", "TYPE"
        {
            var logs = new List<string>();
            int convertedCount = 0;
            int totalPreservedValues = 0;

            if (doc == null || doc.IsFamilyDocument)
            {
                logs.Add("❌ La transformación de ParameterBindings requiere un documento de proyecto (.rvt) activo.");
                return (0, 0, logs);
            }

            string spFile = SharedParameterCloudService.EnsureDefaultSharedParamFile(app, true);
            DefinitionFile? defFile = app.OpenSharedParameterFile();
            if (defFile == null)
            {
                logs.Add($"❌ No se pudo abrir el archivo de parámetros compartidos: {spFile}");
                return (0, 0, logs);
            }

            DefinitionGroup group = defFile.Groups.get_Item(targetSharedGroupName)
                                    ?? defFile.Groups.Create(targetSharedGroupName);

            using (var tg = new TransactionGroup(doc, "ARCA: Transformar Parámetros a Compartidos"))
            {
                tg.Start();

                foreach (var item in parametersToConvert)
                {
                    try
                    {
                        // 1. Localizar la definición y el binding actual en el documento
                        var found = FindBindingByNameOrId(doc, item.Name, item.ParameterElementId);
                        if (found.def == null || found.binding == null)
                        {
                            logs.Add($"⚠️ No se encontró el binding para '{item.Name}'.");
                            continue;
                        }

                        Definition oldDef = found.def;
                        ElementBinding oldBinding = found.binding;
                        bool oldIsInstance = oldBinding is InstanceBinding;
                        bool targetIsInstance = scopeOverride == "INSTANCE" ? true :
                                                scopeOverride == "TYPE" ? false : oldIsInstance;

                        string targetParamName = string.IsNullOrWhiteSpace(item.NewName) ? item.Name : item.NewName.Trim();
                        ForgeTypeId dataType = oldDef.GetDataType();
                        if (dataType == null || dataType.Empty())
                            dataType = SpecTypeId.String.Text;

                        ForgeTypeId groupType = oldDef.GetGroupTypeId();
                        if (groupType == null || groupType.Empty())
                            groupType = GroupTypeId.Data;

                        // Copiar CategorySet
                        CategorySet catSet = app.Create.NewCategorySet();
                        foreach (Category c in oldBinding.Categories)
                        {
                            if (c != null && c.AllowsBoundParameters)
                                catSet.Insert(c);
                        }

                        if (catSet.IsEmpty)
                        {
                            logs.Add($"⚠️ '{item.Name}' no tiene categorías válidas que permitan parámetros vinculados.");
                            continue;
                        }

                        // 2. Respaldar en memoria TODOS los valores actuales de los elementos o tipos
                        var snapshotValues = CaptureParameterValuesSnapshot(doc, item.Name, oldIsInstance, targetIsInstance, catSet);
                        logs.Add($"📦 '{item.Name}': {snapshotValues.Count} valores respaldados en memoria antes de transformar.");

                        // 3. Obtener o crear la ExternalDefinition en el archivo .txt activo
                        ExternalDefinition? extDef = group.Definitions.get_Item(targetParamName) as ExternalDefinition;
                        if (extDef == null)
                        {
                            // Buscar en cualquier otro grupo del .txt para no duplicar nombres si ya existía
                            foreach (DefinitionGroup g in defFile.Groups)
                            {
                                if (g.Definitions.get_Item(targetParamName) is ExternalDefinition existingInOtherGroup)
                                {
                                    extDef = existingInOtherGroup;
                                    break;
                                }
                            }
                        }

                        if (extDef == null)
                        {
                            var opts = new ExternalDefinitionCreationOptions(targetParamName, dataType)
                            {
                                Visible = true,
                                UserModifiable = true,
                                Description = $"Transformado a Parámetro Compartido por ARCA Review ({DateTime.Now:yyyy-MM-dd})"
                            };
                            if (Guid.TryParse(item.GuidText, out Guid existingGuid) && existingGuid != Guid.Empty)
                            {
                                opts.GUID = existingGuid;
                            }
                            extDef = group.Definitions.Create(opts) as ExternalDefinition;
                        }

                        if (extDef == null)
                        {
                            logs.Add($"❌ No se pudo crear la ExternalDefinition para '{targetParamName}'.");
                            continue;
                        }

                        // 4. Transacción Paso A: Eliminar el ParameterElement No-Shared antiguo
                        using (var txRemove = new Transaction(doc, $"Eliminar parámetro {item.Name}"))
                        {
                            txRemove.Start();
                            if (oldDef is InternalDefinition intDef && intDef.Id != ElementId.InvalidElementId)
                            {
                                doc.Delete(intDef.Id);
                            }
                            else
                            {
                                doc.ParameterBindings.Remove(oldDef);
                            }
                            txRemove.Commit();
                        }

                        // 5. Transacción Paso B: Insertar el nuevo Binding como Parámetro Compartido y restaurar todos los valores
                        int restoredForThisParam = 0;
                        using (var txBind = new Transaction(doc, $"Vincular parámetro compartido {targetParamName}"))
                        {
                            txBind.Start();

                            ElementBinding newBinding = targetIsInstance
                                ? (ElementBinding)app.Create.NewInstanceBinding(catSet)
                                : (ElementBinding)app.Create.NewTypeBinding(catSet);

                            bool boundOk = doc.ParameterBindings.Insert(extDef, newBinding, groupType);
                            if (!boundOk)
                            {
                                boundOk = doc.ParameterBindings.ReInsert(extDef, newBinding, groupType);
                            }

                            doc.Regenerate();

                            // Restaurar los valores respaldados
                            restoredForThisParam = RestoreParameterValuesSnapshot(doc, targetParamName, extDef.GUID, snapshotValues);

                            txBind.Commit();
                        }

                        convertedCount++;
                        totalPreservedValues += restoredForThisParam;
                        item.Name = targetParamName;
                        item.Origin = ParameterOriginKind.Shared;
                        item.GuidText = extDef.GUID.ToString();
                        item.IsInstance = targetIsInstance;
                        item.IsOrphanInTxt = false;
                        item.StatusText = $"✅ Transformado a Shared ({restoredForThisParam} vals)";
                        logs.Add($"✅ '{item.Name}' [GUID: {extDef.GUID}] convertido exitosamente ({restoredForThisParam} valores restaurados).");
                    }
                    catch (Exception ex)
                    {
                        item.StatusText = $"❌ Error: {ex.Message}";
                        logs.Add($"❌ Error transformando '{item.Name}': {ex.Message}");
                    }
                }

                tg.Assimilate();
            }

            return (convertedCount, totalPreservedValues, logs);
        }

        /// <summary>
        /// Cambia el ámbito de una lista de parámetros entre Instancia (InstanceBinding) y Tipo (TypeBinding)
        /// preservando y migrando los valores entre ejemplares y tipos.
        /// </summary>
        public static (int toggledCount, int migratedValues, List<string> logs) ToggleParameterScopeInstanceType(
            Application app,
            Document doc,
            List<ProjectParameterItem> parametersToToggle)
        {
            var logs = new List<string>();
            int toggledCount = 0;
            int migratedValues = 0;

            if (doc == null || doc.IsFamilyDocument) return (0, 0, logs);

            using (var tg = new TransactionGroup(doc, "ARCA: Cambiar Ámbito Instancia <-> Tipo"))
            {
                tg.Start();

                foreach (var item in parametersToToggle)
                {
                    try
                    {
                        var found = FindBindingByNameOrId(doc, item.Name, item.ParameterElementId);
                        if (found.def == null || found.binding == null)
                        {
                            logs.Add($"⚠️ No se halló el parámetro '{item.Name}'.");
                            continue;
                        }

                        Definition def = found.def;
                        ElementBinding oldBinding = found.binding;
                        bool oldIsInstance = oldBinding is InstanceBinding;
                        bool newIsInstance = !oldIsInstance;

                        CategorySet catSet = app.Create.NewCategorySet();
                        foreach (Category c in oldBinding.Categories)
                        {
                            if (c != null && c.AllowsBoundParameters)
                                catSet.Insert(c);
                        }

                        ForgeTypeId groupType = def.GetGroupTypeId();
                        if (groupType == null || groupType.Empty())
                            groupType = GroupTypeId.Data;

                        // Respaldar valores mapeados hacia el nuevo ámbito (Instancia->Tipo o Tipo->Instancia)
                        var snapshot = CaptureParameterValuesSnapshot(doc, item.Name, oldIsInstance, newIsInstance, catSet);

                        using (var tx = new Transaction(doc, $"Cambiar ámbito {item.Name}"))
                        {
                            tx.Start();

                            ElementBinding newBinding = newIsInstance
                                ? (ElementBinding)app.Create.NewInstanceBinding(catSet)
                                : (ElementBinding)app.Create.NewTypeBinding(catSet);

                            bool ok = doc.ParameterBindings.ReInsert(def, newBinding, groupType);
                            if (!ok)
                            {
                                doc.ParameterBindings.Remove(def);
                                doc.ParameterBindings.Insert(def, newBinding, groupType);
                            }

                            doc.Regenerate();

                            Guid.TryParse(item.GuidText, out Guid g);
                            int restored = RestoreParameterValuesSnapshot(doc, item.Name, g, snapshot);
                            migratedValues += restored;

                            tx.Commit();
                        }

                        item.IsInstance = newIsInstance;
                        item.StatusText = $"✅ Cambiado a {(newIsInstance ? "Instancia" : "Tipo")}";
                        toggledCount++;
                        logs.Add($"🔄 '{item.Name}' cambiado de {(oldIsInstance ? "Instancia" : "Tipo")} a {(newIsInstance ? "Instancia" : "Tipo")} ({snapshot.Count} elementos procesados).");
                    }
                    catch (Exception ex)
                    {
                        logs.Add($"❌ Error cambiando ámbito de '{item.Name}': {ex.Message}");
                    }
                }

                tg.Assimilate();
            }

            return (toggledCount, migratedValues, logs);
        }

        private class RawParamSnapshot
        {
            public StorageType StorageType { get; set; }
            public string? StringVal { get; set; }
            public double DoubleVal { get; set; }
            public int IntVal { get; set; }
            public ElementId? ElemIdVal { get; set; }
        }

        private static Dictionary<long, RawParamSnapshot> CaptureParameterValuesSnapshot(
            Document doc,
            string paramName,
            bool oldIsInstance,
            bool targetIsInstance,
            CategorySet categories)
        {
            var result = new Dictionary<long, RawParamSnapshot>();
            var allowedCatIds = new HashSet<long>();
            foreach (Category c in categories)
            {
                if (c != null) allowedCatIds.Add(c.Id.Value);
            }

            if (oldIsInstance && targetIsInstance)
            {
                // Instancia -> Instancia
                var elems = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                foreach (Element el in elems)
                {
                    if (el.Category == null || !allowedCatIds.Contains(el.Category.Id.Value)) continue;
                    Parameter? p = el.LookupParameter(paramName);
                    var snap = TryExtractSnapshot(p);
                    if (snap != null) result[el.Id.Value] = snap;
                }
            }
            else if (!oldIsInstance && !targetIsInstance)
            {
                // Tipo -> Tipo
                var types = new FilteredElementCollector(doc).WhereElementIsElementType();
                foreach (Element et in types)
                {
                    if (et.Category == null || !allowedCatIds.Contains(et.Category.Id.Value)) continue;
                    Parameter? p = et.LookupParameter(paramName);
                    var snap = TryExtractSnapshot(p);
                    if (snap != null) result[et.Id.Value] = snap;
                }
            }
            else if (oldIsInstance && !targetIsInstance)
            {
                // Instancia -> Tipo: Tomar el primer valor no vacío de los ejemplares de cada tipo
                var elems = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                foreach (Element el in elems)
                {
                    if (el.Category == null || !allowedCatIds.Contains(el.Category.Id.Value)) continue;
                    ElementId typeId = el.GetTypeId();
                    if (typeId == ElementId.InvalidElementId || result.ContainsKey(typeId.Value)) continue;

                    Parameter? p = el.LookupParameter(paramName);
                    var snap = TryExtractSnapshot(p);
                    if (snap != null) result[typeId.Value] = snap;
                }
            }
            else if (!oldIsInstance && targetIsInstance)
            {
                // Tipo -> Instancia: Leer el valor del ElementType y asignarlo a cada ejemplar
                var typeSnaps = new Dictionary<long, RawParamSnapshot>();
                var types = new FilteredElementCollector(doc).WhereElementIsElementType();
                foreach (Element et in types)
                {
                    if (et.Category == null || !allowedCatIds.Contains(et.Category.Id.Value)) continue;
                    Parameter? p = et.LookupParameter(paramName);
                    var snap = TryExtractSnapshot(p);
                    if (snap != null) typeSnaps[et.Id.Value] = snap;
                }

                var elems = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                foreach (Element el in elems)
                {
                    if (el.Category == null || !allowedCatIds.Contains(el.Category.Id.Value)) continue;
                    long tid = el.GetTypeId().Value;
                    if (typeSnaps.TryGetValue(tid, out var snap))
                    {
                        result[el.Id.Value] = snap;
                    }
                }
            }

            return result;
        }

        private static RawParamSnapshot? TryExtractSnapshot(Parameter? p)
        {
            if (p == null || !p.HasValue) return null;
            switch (p.StorageType)
            {
                case StorageType.String:
                    string? s = p.AsString();
                    if (string.IsNullOrEmpty(s)) return null;
                    return new RawParamSnapshot { StorageType = StorageType.String, StringVal = s };
                case StorageType.Double:
                    return new RawParamSnapshot { StorageType = StorageType.Double, DoubleVal = p.AsDouble() };
                case StorageType.Integer:
                    return new RawParamSnapshot { StorageType = StorageType.Integer, IntVal = p.AsInteger() };
                case StorageType.ElementId:
                    ElementId eid = p.AsElementId();
                    if (eid == ElementId.InvalidElementId) return null;
                    return new RawParamSnapshot { StorageType = StorageType.ElementId, ElemIdVal = eid };
                default:
                    return null;
            }
        }

        private static int RestoreParameterValuesSnapshot(
            Document doc,
            string paramName,
            Guid sharedGuid,
            Dictionary<long, RawParamSnapshot> snapshots)
        {
            int count = 0;
            foreach (var kv in snapshots)
            {
                Element? el = doc.GetElement(new ElementId(kv.Key));
                if (el == null) continue;

                Parameter? p = null;
                if (sharedGuid != Guid.Empty)
                {
                    try { p = el.get_Parameter(sharedGuid); } catch { }
                }
                p ??= el.LookupParameter(paramName);

                if (p == null || p.IsReadOnly) continue;

                try
                {
                    var snap = kv.Value;
                    switch (snap.StorageType)
                    {
                        case StorageType.String:
                            if (snap.StringVal != null) { p.Set(snap.StringVal); count++; }
                            break;
                        case StorageType.Double:
                            p.Set(snap.DoubleVal);
                            count++;
                            break;
                        case StorageType.Integer:
                            p.Set(snap.IntVal);
                            count++;
                            break;
                        case StorageType.ElementId:
                            if (snap.ElemIdVal != null) { p.Set(snap.ElemIdVal); count++; }
                            break;
                    }
                }
                catch { }
            }
            return count;
        }

        // ══════════════════════════════════════════════════════════════════════════
        // OPERACIONES MASIVAS DE VALORES EN ELEMENTOS (TAB 3)
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Obtiene la lista de categorías con elementos modelados en el documento.
        /// </summary>
        public static List<CategoryChoiceItem> GetModelCategoriesWithParameters(Document doc)
        {
            var list = new List<CategoryChoiceItem>();
            if (doc == null || doc.IsFamilyDocument) return list;

            foreach (Category cat in doc.Settings.Categories)
            {
                if (cat == null || !cat.AllowsBoundParameters) continue;
                if (cat.CategoryType != CategoryType.Model && cat.Id.Value != (long)BuiltInCategory.OST_Sheets && cat.Id.Value != (long)BuiltInCategory.OST_Rooms)
                    continue;

                list.Add(new CategoryChoiceItem
                {
                    CategoryId = cat.Id.Value,
                    Name = cat.Name,
                    IsChecked = cat.Id.Value == (long)BuiltInCategory.OST_Walls ||
                                cat.Id.Value == (long)BuiltInCategory.OST_Doors ||
                                cat.Id.Value == (long)BuiltInCategory.OST_Windows ||
                                cat.Id.Value == (long)BuiltInCategory.OST_Floors ||
                                cat.Id.Value == (long)BuiltInCategory.OST_StructuralFraming ||
                                cat.Id.Value == (long)BuiltInCategory.OST_StructuralColumns
                });
            }

            return list.OrderBy(c => c.Name).ToList();
        }

        /// <summary>
        /// Construye la matriz de elementos para previsualizar y editar masivamente los valores de un parámetro origen y destino.
        /// </summary>
        public static List<ElementParameterValueRow> LoadElementParameterMatrix(
            Document doc,
            long categoryId,
            string sourceParamName,
            string targetParamName,
            bool inspectElementTypes = false)
        {
            var rows = new List<ElementParameterValueRow>();
            if (doc == null || doc.IsFamilyDocument) return rows;

            FilteredElementCollector collector = new FilteredElementCollector(doc);
            if (categoryId != 0)
            {
                collector.OfCategoryId(new ElementId(categoryId));
            }

            collector = inspectElementTypes
                ? collector.WhereElementIsElementType()
                : collector.WhereElementIsNotElementType();

            foreach (Element el in collector.Take(1500))
            {
                if (el.Category == null) continue;

                string famAndType = GetElementFamilyAndTypeLabel(doc, el);
                string levelName = GetElementLevelLabel(doc, el);
                string mark = el.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString()
                              ?? el.get_Parameter(BuiltInParameter.DOOR_NUMBER)?.AsString()
                              ?? "";

                string srcVal = ReadParameterOrTokenAsString(doc, el, sourceParamName);
                string tgtVal = ReadParameterOrTokenAsString(doc, el, targetParamName);

                rows.Add(new ElementParameterValueRow
                {
                    ElementId = el.Id.Value,
                    IsElementType = inspectElementTypes,
                    CategoryName = el.Category.Name,
                    FamilyAndType = famAndType,
                    LevelName = levelName,
                    MarkOrCode = mark,
                    SourceValue = srcVal,
                    CurrentTargetValue = tgtVal,
                    PreviewNewValue = tgtVal,
                    Status = "En espera"
                });
            }

            return rows.OrderBy(r => r.CategoryName).ThenBy(r => r.FamilyAndType).ToList();
        }

        /// <summary>
        /// Calcula en memoria la transformación masiva sobre las filas seleccionadas antes de escribir en Revit.
        /// Modos soportados:
        /// - "COPY": Copia el valor de Origen al Destino.
        /// - "TEMPLATE": Evalúa una plantilla con tokens como {Origen}, {Actual}, {Categoria}, {Familia}, {Nivel}, {Marca}, {Id}, {#}.
        /// - "REPLACE": Busca y reemplaza texto sobre el valor actual (o de origen).
        /// - "PREFIX_SUFFIX": Agrega prefijo y/o sufijo.
        /// - "MATH": Aplica operación matemática (multiplicador y suma) si el valor es numérico.
        /// </summary>
        public static int PreviewMassOperationOnRows(
            List<ElementParameterValueRow> rows,
            string mode,
            string arg1,
            string arg2,
            bool onlyIfTargetEmpty = false)
        {
            int changed = 0;
            int seq = 1;

            foreach (var row in rows.Where(r => r.IsSelected))
            {
                if (onlyIfTargetEmpty && !string.IsNullOrWhiteSpace(row.CurrentTargetValue))
                {
                    row.PreviewNewValue = row.CurrentTargetValue;
                    row.Status = "Omitido (Ya tiene valor)";
                    continue;
                }

                string baseText = (!string.IsNullOrEmpty(row.SourceValue) ? row.SourceValue : row.CurrentTargetValue) ?? string.Empty;
                string newVal = row.CurrentTargetValue ?? string.Empty;

                switch (mode)
                {
                    case "COPY":
                        newVal = row.SourceValue ?? string.Empty;
                        break;

                    case "TEMPLATE":
                        string tpl = string.IsNullOrWhiteSpace(arg1) ? "{Categoria}-{Nivel}-{Marca}" : arg1;
                        newVal = tpl
                            .Replace("{Origen}", row.SourceValue ?? "")
                            .Replace("{Actual}", row.CurrentTargetValue ?? "")
                            .Replace("{Categoria}", row.CategoryName ?? "")
                            .Replace("{Familia}", row.FamilyAndType ?? "")
                            .Replace("{Nivel}", row.LevelName ?? "")
                            .Replace("{Marca}", row.MarkOrCode ?? "")
                            .Replace("{Id}", row.ElementId.ToString())
                            .Replace("{#}", seq.ToString("D3"));
                        seq++;
                        break;

                    case "REPLACE":
                        string findStr = arg1 ?? "";
                        string repStr = arg2 ?? "";
                        if (!string.IsNullOrEmpty(findStr))
                        {
                            newVal = (row.CurrentTargetValue ?? "").Replace(findStr, repStr);
                        }
                        break;

                    case "PREFIX_SUFFIX":
                        string prefix = arg1 ?? "";
                        string suffix = arg2 ?? "";
                        newVal = $"{prefix}{baseText}{suffix}";
                        break;

                    case "UPPERCASE":
                        newVal = baseText.ToUpperInvariant();
                        break;

                    case "MATH":
                        double.TryParse(baseText.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out double num);
                        if (!double.TryParse((arg1 ?? "1").Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out double factor))
                            factor = 1.0;
                        double.TryParse((arg2 ?? "0").Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out double offset);
                        double calc = Math.Round((num * factor) + offset, 3);
                        newVal = calc.ToString(CultureInfo.InvariantCulture);
                        break;
                }

                row.PreviewNewValue = newVal ?? string.Empty;
                if (row.HasPendingChange)
                {
                    row.Status = "⚡ Pendiente aplicar";
                    changed++;
                }
                else
                {
                    row.Status = "Sin cambios";
                }
            }

            return changed;
        }

        /// <summary>
        /// Escribe todos los valores previsualizados (PreviewNewValue) en el parámetro destino de los elementos en Revit.
        /// </summary>
        public static (int updatedCount, int errorCount, List<string> logs) ApplyElementParameterMatrixToDocument(
            Document doc,
            List<ElementParameterValueRow> rows,
            string targetParamName)
        {
            var logs = new List<string>();
            int updated = 0;
            int errors = 0;

            if (doc == null || string.IsNullOrWhiteSpace(targetParamName))
                return (0, 0, logs);

            var rowsToApply = rows.Where(r => r.IsSelected && r.HasPendingChange).ToList();
            if (rowsToApply.Count == 0)
            {
                logs.Add("ℹ️ No hay filas seleccionadas con cambios pendientes para aplicar.");
                return (0, 0, logs);
            }

            using (var tx = new Transaction(doc, $"ARCA: Edición Masiva de '{targetParamName}'"))
            {
                tx.Start();

                foreach (var row in rowsToApply)
                {
                    Element? el = doc.GetElement(new ElementId(row.ElementId));
                    if (el == null) continue;

                    Parameter? p = ResolveParameterOnElement(el, targetParamName);
                    if (p == null)
                    {
                        // Si el parámetro está en el ElementType y estamos editando ejemplares, intentar en el tipo
                        ElementId tid = el.GetTypeId();
                        if (tid != ElementId.InvalidElementId)
                        {
                            Element? et = doc.GetElement(tid);
                            if (et != null) p = ResolveParameterOnElement(et, targetParamName);
                        }
                    }

                    if (p == null || p.IsReadOnly)
                    {
                        row.Status = "❌ Solo lectura / No hallado";
                        errors++;
                        continue;
                    }

                    try
                    {
                        bool ok = SetParameterFromString(p, row.PreviewNewValue);
                        if (ok)
                        {
                            row.CurrentTargetValue = row.PreviewNewValue;
                            row.Status = "✅ Aplicado";
                            updated++;
                        }
                        else
                        {
                            row.Status = "⚠️ Formato inválido";
                            errors++;
                        }
                    }
                    catch (Exception ex)
                    {
                        row.Status = $"❌ {ex.Message}";
                        errors++;
                    }
                }

                tx.Commit();
            }

            logs.Add($"✅ Edición masiva sobre '{targetParamName}' completada: {updated} elementos actualizados, {errors} omitidos/errores.");
            return (updated, errors, logs);
        }

        // ══════════════════════════════════════════════════════════════════════════
        // INYECCIÓN DE PARÁMETROS DESDE .TXT Y PROPAGACIÓN MULTI-DOCUMENTO (TAB 4 & 5)
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Vincula una lista de definiciones del archivo .txt activo hacia un documento (.rvt o .rfa).
        /// </summary>
        public static (int boundCount, List<string> logs) BindTxtSharedParametersToDocument(
            Application app,
            Document targetDoc,
            List<SharedParamTxtItem> txtItems,
            List<long> categoryIds,
            bool asInstance,
            ForgeTypeId? targetGroup = null)
        {
            var logs = new List<string>();
            int boundCount = 0;
            if (targetDoc == null || txtItems.Count == 0) return (0, logs);

            ForgeTypeId groupType = targetGroup ?? GroupTypeId.Data;
            DefinitionFile? defFile = app.OpenSharedParameterFile();
            if (defFile == null)
            {
                logs.Add("❌ No hay un archivo .txt de Parámetros Compartidos abierto en Revit.");
                return (0, logs);
            }

            // Si el documento destino es una Familia (.rfa)
            if (targetDoc.IsFamilyDocument)
            {
                using (var tx = new Transaction(targetDoc, "ARCA: Inyectar Parámetros Compartidos en Familia"))
                {
                    tx.Start();
                    foreach (var item in txtItems)
                    {
                        try
                        {
                            ExternalDefinition? extDef = FindExternalDefInFile(defFile, item.Name, item.Guid);
                            if (extDef == null) continue;

                            FamilyParameter? existing = targetDoc.FamilyManager.get_Parameter(extDef.Name);
                            if (existing != null)
                            {
                                logs.Add($"ℹ️ [{targetDoc.Title}] '{item.Name}' ya existe en la familia.");
                                continue;
                            }

                            targetDoc.FamilyManager.AddParameter(extDef, groupType, asInstance);
                            item.ExistsInActiveDoc = true;
                            boundCount++;
                            logs.Add($"✅ [{targetDoc.Title}] '{item.Name}' agregado como FamilyParameter ({(asInstance ? "Instancia" : "Tipo")}).");
                        }
                        catch (Exception ex)
                        {
                            logs.Add($"❌ [{targetDoc.Title}] Error agregando '{item.Name}': {ex.Message}");
                        }
                    }
                    tx.Commit();
                }
                return (boundCount, logs);
            }

            // Si el documento destino es un Proyecto (.rvt)
            CategorySet catSet = app.Create.NewCategorySet();
            foreach (long cid in categoryIds)
            {
                try
                {
                    Category? c = Category.GetCategory(targetDoc, new ElementId(cid));
                    if (c != null && c.AllowsBoundParameters)
                        catSet.Insert(c);
                }
                catch { }
            }

            if (catSet.IsEmpty)
            {
                logs.Add("⚠️ Debes seleccionar al menos una categoría válida para vincular los parámetros en el proyecto.");
                return (0, logs);
            }

            using (var tx = new Transaction(targetDoc, "ARCA: Vincular Parámetros Compartidos desde .TXT"))
            {
                tx.Start();

                foreach (var item in txtItems)
                {
                    try
                    {
                        ExternalDefinition? extDef = FindExternalDefInFile(defFile, item.Name, item.Guid);
                        if (extDef == null)
                        {
                            logs.Add($"⚠️ No se halló la ExternalDefinition '{item.Name}' en el archivo .txt activo.");
                            continue;
                        }

                        ElementBinding binding = asInstance
                            ? (ElementBinding)app.Create.NewInstanceBinding(catSet)
                            : (ElementBinding)app.Create.NewTypeBinding(catSet);

                        bool ok = targetDoc.ParameterBindings.Insert(extDef, binding, groupType);
                        if (!ok)
                        {
                            ok = targetDoc.ParameterBindings.ReInsert(extDef, binding, groupType);
                        }

                        if (ok)
                        {
                            item.ExistsInActiveDoc = true;
                            boundCount++;
                            logs.Add($"✅ [{targetDoc.Title}] '{item.Name}' vinculado a {catSet.Size} categorías ({(asInstance ? "Instancia" : "Tipo")}).");
                        }
                    }
                    catch (Exception ex)
                    {
                        logs.Add($"❌ [{targetDoc.Title}] Error vinculando '{item.Name}': {ex.Message}");
                    }
                }

                tx.Commit();
            }

            return (boundCount, logs);
        }

        /// <summary>
        /// Propaga una lista de parámetros del proyecto activo hacia múltiples documentos (.rvt o .rfa) abiertos en la sesión de Revit.
        /// Si el parámetro origen no es compartido, primero lo registra automáticamente en el .txt activo para garantizar que viaje con GUID único.
        /// </summary>
        public static (int docsUpdated, int totalBindingsCreated, List<string> logs) PropagateParametersToOpenDocuments(
            Application app,
            Document sourceDoc,
            List<ProjectParameterItem> selectedParams,
            List<OpenRevitDocItem> targetDocs)
        {
            var logs = new List<string>();
            int docsUpdated = 0;
            int totalBindings = 0;

            if (selectedParams.Count == 0 || targetDocs.Count == 0)
            {
                logs.Add("⚠️ Selecciona al menos un parámetro y un documento destino.");
                return (0, 0, logs);
            }

            string spFile = SharedParameterCloudService.EnsureDefaultSharedParamFile(app, true);
            // Rescatar huérfanos primero para que todos los Shared existan en el .txt
            if (sourceDoc != null && !sourceDoc.IsFamilyDocument)
            {
                SharedParameterCloudService.RescueOrphanSharedParametersToTxt(app, sourceDoc, "01_ARCA_Sincronizados");
            }

            DefinitionFile? defFile = app.OpenSharedParameterFile();
            if (defFile == null)
            {
                logs.Add("❌ No se pudo abrir el archivo .txt de Parámetros Compartidos.");
                return (0, 0, logs);
            }

            DefinitionGroup syncGroup = defFile.Groups.get_Item("01_ARCA_Sincronizados")
                                        ?? defFile.Groups.Create("01_ARCA_Sincronizados");

            foreach (var docItem in targetDocs.Where(d => d.DocumentRef != null))
            {
                Document destDoc = docItem.DocumentRef!;
                int createdInThisDoc = 0;

                using (var tx = new Transaction(destDoc, "ARCA: Sincronizar Parámetros Multi-Doc"))
                {
                    tx.Start();

                    foreach (var pItem in selectedParams)
                    {
                        try
                        {
                            Guid.TryParse(pItem.GuidText, out Guid g);
                            ExternalDefinition? extDef = FindExternalDefInFile(defFile, pItem.Name, g);

                            if (extDef == null)
                            {
                                ForgeTypeId dt = SharedParameterCloudService.ConvertTxtTokenToForgeDataType(pItem.DataTypeLabel);
                                var opts = new ExternalDefinitionCreationOptions(pItem.Name, dt)
                                {
                                    Visible = true,
                                    UserModifiable = true,
                                    Description = "Sincronizado entre documentos por ARCA Review"
                                };
                                extDef = syncGroup.Definitions.Create(opts) as ExternalDefinition;
                            }

                            if (extDef == null) continue;

                            if (destDoc.IsFamilyDocument)
                            {
                                FamilyParameter? existingFp = destDoc.FamilyManager.get_Parameter(extDef.Name);
                                if (existingFp == null)
                                {
                                    destDoc.FamilyManager.AddParameter(extDef, GroupTypeId.Data, pItem.IsInstance);
                                    createdInThisDoc++;
                                }
                            }
                            else
                            {
                                CategorySet catSet = app.Create.NewCategorySet();
                                foreach (long cid in pItem.BoundCategoryIds)
                                {
                                    Category? c = Category.GetCategory(destDoc, new ElementId(cid));
                                    if (c != null && c.AllowsBoundParameters)
                                        catSet.Insert(c);
                                }

                                // Si venía de una familia o no tenía categorías, asignar GenericModel y Walls por defecto
                                if (catSet.IsEmpty)
                                {
                                    Category? gm = Category.GetCategory(destDoc, BuiltInCategory.OST_GenericModel);
                                    if (gm != null) catSet.Insert(gm);
                                }

                                ElementBinding newBind = pItem.IsInstance
                                    ? (ElementBinding)app.Create.NewInstanceBinding(catSet)
                                    : (ElementBinding)app.Create.NewTypeBinding(catSet);

                                bool ok = destDoc.ParameterBindings.Insert(extDef, newBind, GroupTypeId.Data);
                                if (!ok) ok = destDoc.ParameterBindings.ReInsert(extDef, newBind, GroupTypeId.Data);
                                if (ok) createdInThisDoc++;
                            }
                        }
                        catch (Exception ex)
                        {
                            logs.Add($"⚠️ [{destDoc.Title}] Aviso en '{pItem.Name}': {ex.Message}");
                        }
                    }

                    tx.Commit();
                }

                if (createdInThisDoc > 0)
                {
                    docsUpdated++;
                    totalBindings += createdInThisDoc;
                }
                logs.Add($"📄 Documento '{destDoc.Title}': {createdInThisDoc} parámetros sincronizados.");
            }

            return (docsUpdated, totalBindings, logs);
        }

        // ══════════════════════════════════════════════════════════════════════════
        // UTILIDADES INTERNAS DE SOPORTE
        // ══════════════════════════════════════════════════════════════════════════

        private static ExternalDefinition? FindExternalDefInFile(DefinitionFile defFile, string name, Guid guid)
        {
            foreach (DefinitionGroup grp in defFile.Groups)
            {
                foreach (Definition d in grp.Definitions)
                {
                    if (d is ExternalDefinition ed)
                    {
                        if (guid != Guid.Empty && ed.GUID == guid) return ed;
                        if (ed.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return ed;
                    }
                }
            }
            return null;
        }

        private static (Definition? def, ElementBinding? binding) FindBindingByNameOrId(Document doc, string name, long paramElemId)
        {
            BindingMap map = doc.ParameterBindings;
            DefinitionBindingMapIterator it = map.ForwardIterator();
            it.Reset();
            while (it.MoveNext())
            {
                Definition? d = it.Key;
                ElementBinding? b = it.Current as ElementBinding;
                if (d == null || b == null) continue;

                if (d is InternalDefinition id && paramElemId > 0 && id.Id.Value == paramElemId)
                    return (d, b);

                if (d.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return (d, b);
            }
            return (null, null);
        }

        private static string ReadParameterOrTokenAsString(Document doc, Element el, string paramName)
        {
            if (string.IsNullOrWhiteSpace(paramName)) return "";
            Parameter? p = ResolveParameterOnElement(el, paramName);
            if (p == null)
            {
                ElementId tid = el.GetTypeId();
                if (tid != ElementId.InvalidElementId)
                {
                    Element? et = doc.GetElement(tid);
                    if (et != null) p = ResolveParameterOnElement(et, paramName);
                }
            }

            if (p == null || !p.HasValue) return "";

            return p.StorageType switch
            {
                StorageType.String => p.AsString() ?? "",
                StorageType.Double => p.AsValueString() ?? p.AsDouble().ToString("0.###", CultureInfo.InvariantCulture),
                StorageType.Integer => p.AsInteger().ToString(CultureInfo.InvariantCulture),
                StorageType.ElementId => p.AsValueString() ?? p.AsElementId().Value.ToString(),
                _ => ""
            };
        }

        private static Parameter? ResolveParameterOnElement(Element el, string paramName)
        {
            Parameter? p = el.LookupParameter(paramName);
            if (p != null) return p;

            if (paramName.Equals("Marca", StringComparison.OrdinalIgnoreCase) || paramName.Equals("Mark", StringComparison.OrdinalIgnoreCase))
                return el.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
            if (paramName.Equals("Comentarios", StringComparison.OrdinalIgnoreCase) || paramName.Equals("Comments", StringComparison.OrdinalIgnoreCase))
                return el.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            if (paramName.Equals("Comentarios de tipo", StringComparison.OrdinalIgnoreCase) || paramName.Equals("Type Comments", StringComparison.OrdinalIgnoreCase))
                return el.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_COMMENTS);
            if (paramName.Equals("Descripción", StringComparison.OrdinalIgnoreCase) || paramName.Equals("Description", StringComparison.OrdinalIgnoreCase))
                return el.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION);

            return null;
        }

        private static bool SetParameterFromString(Parameter p, string newVal)
        {
            switch (p.StorageType)
            {
                case StorageType.String:
                    return p.Set(newVal ?? "");
                case StorageType.Integer:
                    if (newVal.Equals("Sí", StringComparison.OrdinalIgnoreCase) || newVal.Equals("Si", StringComparison.OrdinalIgnoreCase) || newVal.Equals("True", StringComparison.OrdinalIgnoreCase))
                        return p.Set(1);
                    if (newVal.Equals("No", StringComparison.OrdinalIgnoreCase) || newVal.Equals("False", StringComparison.OrdinalIgnoreCase))
                        return p.Set(0);
                    if (int.TryParse(newVal, out int iv))
                        return p.Set(iv);
                    return false;
                case StorageType.Double:
                    if (p.SetValueString(newVal)) return true;
                    if (double.TryParse(newVal.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out double dv))
                        return p.Set(dv);
                    return false;
                default:
                    return false;
            }
        }

        private static string GetElementFamilyAndTypeLabel(Document doc, Element el)
        {
            if (el is ElementType et)
                return $"{et.FamilyName} : {et.Name}";

            ElementId tid = el.GetTypeId();
            if (tid != ElementId.InvalidElementId && doc.GetElement(tid) is ElementType elemType)
                return $"{elemType.FamilyName} : {elemType.Name}";

            return el.Name ?? $"Elem #{el.Id.Value}";
        }

        private static string GetElementLevelLabel(Document doc, Element el)
        {
            if (el.LevelId != ElementId.InvalidElementId && doc.GetElement(el.LevelId) is Level lvl)
                return lvl.Name;

            Parameter? pLvl = el.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)
                              ?? el.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT)
                              ?? el.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);
            if (pLvl != null && pLvl.StorageType == StorageType.ElementId && doc.GetElement(pLvl.AsElementId()) is Level l2)
                return l2.Name;

            return "—";
        }

        private static bool HasMeaningfulValue(Parameter p)
        {
            if (!p.HasValue) return false;
            return p.StorageType switch
            {
                StorageType.String => !string.IsNullOrWhiteSpace(p.AsString()),
                StorageType.Double => Math.Abs(p.AsDouble()) > 1e-9,
                StorageType.Integer => true,
                StorageType.ElementId => p.AsElementId() != ElementId.InvalidElementId,
                _ => false
            };
        }

        private static StorageType InferStorageTypeFromSpec(ForgeTypeId? dt)
        {
            if (dt == null || dt.Empty()) return StorageType.String;
            if (dt == SpecTypeId.String.Text || dt == SpecTypeId.String.MultilineText || dt == SpecTypeId.String.Url)
                return StorageType.String;
            if (dt == SpecTypeId.Int.Integer || dt == SpecTypeId.Boolean.YesNo)
                return StorageType.Integer;
            if (dt == SpecTypeId.Reference.Material || dt == SpecTypeId.Reference.Image)
                return StorageType.ElementId;
            return StorageType.Double;
        }

        private static string SafeGetSpecLabel(ForgeTypeId? dt)
        {
            if (dt == null || dt.Empty()) return "Texto";
            try { return LabelUtils.GetLabelForSpec(dt); }
            catch { return SharedParameterCloudService.ConvertForgeDataTypeToTxtToken(dt); }
        }

        private static string SafeGetGroupLabel(ForgeTypeId? grp)
        {
            if (grp == null || grp.Empty()) return "Datos";
            try { return LabelUtils.GetLabelForGroup(grp); }
            catch { return "Datos"; }
        }
    }
}
