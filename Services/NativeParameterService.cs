using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using RevitToSupabasePlugin.Models;
using Application = Autodesk.Revit.ApplicationServices.Application;

namespace RevitToSupabasePlugin.Services
{
    /// <summary>
    /// Servicio unificado de inspección y edición nativa de parámetros en Revit (.rvt y .rfa).
    /// Respeta las lógicas nativas de Revit: Built-In, Familia, Proyecto, Compartido, Global,
    /// Fórmulas, Reporting, Grupos de Propiedades, Ámbito (Ejemplar/Tipo) y Asociaciones.
    /// </summary>
    public static class NativeParameterService
    {
        public static List<RevitForgeOption> GetCommonRevitGroups()
        {
            return new List<RevitForgeOption>
            {
                new RevitForgeOption { Label = "Cotas (Dimensions)", TypeId = GroupTypeId.Geometry },
                new RevitForgeOption { Label = "Datos de identidad (Identity Data)", TypeId = GroupTypeId.IdentityData },
                new RevitForgeOption { Label = "Materiales y acabados (Materials)", TypeId = GroupTypeId.Materials },
                new RevitForgeOption { Label = "Restricciones (Constraints)", TypeId = GroupTypeId.Constraints },
                new RevitForgeOption { Label = "Visibilidad / Gráficos (Visibility)", TypeId = GroupTypeId.Visibility },
                new RevitForgeOption { Label = "Datos (Data)", TypeId = GroupTypeId.Data },
                new RevitForgeOption { Label = "Texto (Text)", TypeId = GroupTypeId.Text },
                new RevitForgeOption { Label = "General", TypeId = GroupTypeId.General },
                new RevitForgeOption { Label = "Fases (Phasing)", TypeId = GroupTypeId.Phasing },
                new RevitForgeOption { Label = "Otros (Other)", TypeId = GroupTypeId.Title }
            };
        }

        public static List<RevitForgeOption> GetCommonRevitDataTypes()
        {
            return new List<RevitForgeOption>
            {
                new RevitForgeOption { Label = "Texto (Text)", TypeId = SpecTypeId.String.Text },
                new RevitForgeOption { Label = "Longitud (Length)", TypeId = SpecTypeId.Length },
                new RevitForgeOption { Label = "Área (Area)", TypeId = SpecTypeId.Area },
                new RevitForgeOption { Label = "Volumen (Volume)", TypeId = SpecTypeId.Volume },
                new RevitForgeOption { Label = "Ángulo (Angle)", TypeId = SpecTypeId.Angle },
                new RevitForgeOption { Label = "Sí / No (Yes/No)", TypeId = SpecTypeId.Boolean.YesNo },
                new RevitForgeOption { Label = "Entero (Integer)", TypeId = SpecTypeId.Int.Integer },
                new RevitForgeOption { Label = "Número (Number)", TypeId = SpecTypeId.Number },
                new RevitForgeOption { Label = "Moneda (Currency)", TypeId = SpecTypeId.Currency },
                new RevitForgeOption { Label = "Material", TypeId = SpecTypeId.Reference.Material }
            };
        }

        public static string SafeGetSpecLabel(ForgeTypeId? specTypeId)
        {
            if (specTypeId == null || string.IsNullOrEmpty(specTypeId.TypeId)) return "Texto";
            try { return LabelUtils.GetLabelForSpec(specTypeId); }
            catch { return specTypeId.TypeId; }
        }

        public static string SafeGetGroupLabel(ForgeTypeId? groupTypeId)
        {
            if (groupTypeId == null || string.IsNullOrEmpty(groupTypeId.TypeId)) return "Otros";
            try { return LabelUtils.GetLabelForGroup(groupTypeId); }
            catch { return groupTypeId.TypeId; }
        }

        /// <summary>
        /// Inspecciona los parámetros nativos de un elemento seleccionado (Ejemplar + Tipo).
        /// </summary>
        public static List<NativeParameterItem> InspectElementParameters(Document doc, Element elem)
        {
            var list = new List<NativeParameterItem>();
            if (doc == null || elem == null) return list;

            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Parámetros de Ejemplar
            CollectElementParams(doc, elem, isTypeScope: false, list, seenKeys);

            // 2. Parámetros de Tipo (si tiene ElementType)
            ElementId typeId = elem.GetTypeId();
            if (typeId != ElementId.InvalidElementId)
            {
                Element? typeElem = doc.GetElement(typeId);
                if (typeElem != null)
                {
                    CollectElementParams(doc, typeElem, isTypeScope: true, list, seenKeys);
                }
            }

            return list.OrderBy(p => p.IsInstance ? 0 : 1).ThenBy(p => p.GroupLabel).ThenBy(p => p.Name).ToList();
        }

        private static void CollectElementParams(
            Document doc,
            Element target,
            bool isTypeScope,
            List<NativeParameterItem> list,
            HashSet<string> seenKeys)
        {
            foreach (Parameter p in target.Parameters)
            {
                if (p?.Definition == null) continue;
                string name = p.Definition.Name;
                if (string.IsNullOrWhiteSpace(name)) continue;

                string key = $"{(isTypeScope ? "T" : "I")}::{name}::{p.Id.Value}";
                if (!seenKeys.Add(key)) continue;

                ParameterOriginKind origin = ClassifyParameterOrigin(doc, p);
                ForgeTypeId? dt = null;
                ForgeTypeId? grp = null;
                try { dt = p.Definition.GetDataType(); } catch { }
                try { grp = p.Definition.GetGroupTypeId(); } catch { }

                string guidStr = "—";
                if (p.IsShared)
                {
                    try { guidStr = p.GUID.ToString(); } catch { }
                }

                string valDisplay = ReadParameterDisplayValue(doc, p);
                int builtInInt = 0;
                if (p.Definition is InternalDefinition intDef)
                {
                    builtInInt = (int)intDef.BuiltInParameter;
                }

                string assocHost = string.Empty;
                if (doc.IsFamilyDocument && doc.FamilyManager != null)
                {
                    try
                    {
                        FamilyParameter? assoc = doc.FamilyManager.GetAssociatedFamilyParameter(p);
                        if (assoc?.Definition != null)
                            assocHost = assoc.Definition.Name;
                    }
                    catch { }
                }

                bool canEditVal = !p.IsReadOnly && p.StorageType != StorageType.None;
                string hint = p.IsReadOnly
                    ? "Solo lectura en Revit (calculado o bloqueado por fórmula/sistema)."
                    : "Editable directamente desde Propiedades o desde este inspector.";

                list.Add(new NativeParameterItem
                {
                    ParameterIdValue = p.Id.Value,
                    BuiltInParamInt = builtInInt,
                    Name = name,
                    Origin = origin,
                    IsInstance = !isTypeScope,
                    DataTypeLabel = SafeGetSpecLabel(dt),
                    SpecTypeIdRaw = dt?.TypeId ?? string.Empty,
                    GroupLabel = SafeGetGroupLabel(grp),
                    GroupTypeIdRaw = grp?.TypeId ?? string.Empty,
                    StorageType = p.StorageType,
                    GuidText = guidStr,
                    IsReadOnly = p.IsReadOnly,
                    CurrentValueDisplay = valDisplay,
                    AssociatedToHostParam = assocHost,
                    CanEditValue = canEditVal,
                    CanEditFormula = false,
                    CanRename = false,
                    CanToggleScope = false,
                    CanEditGroup = false,
                    CanToggleReporting = false,
                    CanConvertToShared = origin == ParameterOriginKind.Project,
                    CanDelete = false,
                    EditabilityHint = hint
                });
            }
        }

        /// <summary>
        /// Inspecciona los parámetros propios de un documento (.rfa via FamilyManager o .rvt via ParameterBindings + GlobalParameters).
        /// </summary>
        public static List<NativeParameterItem> InspectDocumentParameters(Application app, Document doc)
        {
            var results = new List<NativeParameterItem>();
            if (doc == null) return results;

            if (doc.IsFamilyDocument && doc.FamilyManager != null)
            {
                FamilyManager fm = doc.FamilyManager;
                FamilyType? currentType = fm.CurrentType;

                foreach (FamilyParameter fp in fm.Parameters)
                {
                    if (fp?.Definition == null) continue;

                    bool isBuiltIn = false;
                    if (fp.Definition is InternalDefinition intDef)
                        isBuiltIn = intDef.BuiltInParameter != BuiltInParameter.INVALID;

                    ParameterOriginKind origin = fp.IsShared
                        ? ParameterOriginKind.Shared
                        : (isBuiltIn ? ParameterOriginKind.BuiltIn : ParameterOriginKind.Family);

                    ForgeTypeId? dt = null;
                    ForgeTypeId? grp = null;
                    try { dt = fp.Definition.GetDataType(); } catch { }
                    try { grp = fp.Definition.GetGroupTypeId(); } catch { }

                    string guidStr = "—";
                    if (fp.IsShared)
                    {
                        try { guidStr = fp.GUID.ToString(); } catch { }
                    }

                    string valDisplay = ReadFamilyParameterDisplayValue(doc, currentType, fp);
                    string formula = fp.Formula ?? string.Empty;
                    bool hasFormula = !string.IsNullOrWhiteSpace(formula);
                    int assocCount = 0;
                    try { assocCount = fp.AssociatedParameters?.Size ?? 0; } catch { }

                    bool canRename = !isBuiltIn && !fp.IsShared;
                    bool canToggleScope = !isBuiltIn;
                    bool canEditGroup = !isBuiltIn;
                    bool canDelete = !isBuiltIn;
                    bool canConvertShared = !isBuiltIn && !fp.IsShared;
                    bool canReport = !isBuiltIn && fp.CanAssignFormula && fp.StorageType == StorageType.Double;

                    string hint = isBuiltIn
                        ? "Parámetro nativo de la categoría de familia: puedes editar su valor y fórmula, pero no renombrarlo ni borrarlo."
                        : (fp.IsShared
                            ? "Parámetro Compartido (GUID): puedes editar valor, fórmula, ámbito y grupo. El nombre viene del .TXT."
                            : "Parámetro de Familia (.rfa): editable en todas sus propiedades (nombre, valor, fórmula, ámbito, grupo o convertir a compartido).");

                    results.Add(new NativeParameterItem
                    {
                        ParameterIdValue = fp.Id.Value,
                        Name = fp.Definition.Name,
                        Origin = origin,
                        IsInstance = fp.IsInstance,
                        DataTypeLabel = SafeGetSpecLabel(dt),
                        SpecTypeIdRaw = dt?.TypeId ?? string.Empty,
                        GroupLabel = SafeGetGroupLabel(grp),
                        GroupTypeIdRaw = grp?.TypeId ?? string.Empty,
                        StorageType = fp.StorageType,
                        GuidText = guidStr,
                        IsReadOnly = fp.IsReadOnly || hasFormula,
                        CurrentValueDisplay = valDisplay,
                        Formula = formula,
                        IsReporting = fp.IsReporting,
                        AssociatedNestedCount = assocCount,
                        CanEditValue = !hasFormula && !fp.IsReporting && currentType != null,
                        CanEditFormula = fp.CanAssignFormula,
                        CanRename = canRename,
                        CanToggleScope = canToggleScope,
                        CanEditGroup = canEditGroup,
                        CanToggleReporting = canReport,
                        CanConvertToShared = canConvertShared,
                        CanDelete = canDelete,
                        EditabilityHint = hint
                    });
                }

                return results.OrderBy(r => r.Origin == ParameterOriginKind.BuiltIn ? 1 : 0)
                              .ThenBy(r => r.GroupLabel)
                              .ThenBy(r => r.Name)
                              .ToList();
            }

            // Documento de Proyecto (.rvt)
            var projParams = ParameterTransformationService.ScanDocumentParameters(app, doc, computeUsageStats: false);
            foreach (var p in projParams)
            {
                bool isProjectOrShared = p.Origin == ParameterOriginKind.Project || p.Origin == ParameterOriginKind.Shared;
                results.Add(new NativeParameterItem
                {
                    ParameterIdValue = p.ParameterElementId,
                    Name = p.Name,
                    Origin = p.Origin,
                    IsInstance = p.IsInstance,
                    DataTypeLabel = p.DataTypeLabel,
                    SpecTypeIdRaw = p.SpecTypeIdRaw,
                    GroupLabel = p.GroupLabel,
                    GroupTypeIdRaw = p.GroupTypeIdRaw,
                    StorageType = p.StorageType,
                    GuidText = p.GuidText,
                    CurrentValueDisplay = p.CategoriesSummary,
                    CanEditValue = false,
                    CanEditFormula = false,
                    CanRename = false,
                    CanToggleScope = isProjectOrShared,
                    CanEditGroup = isProjectOrShared,
                    CanToggleReporting = false,
                    CanConvertToShared = p.Origin == ParameterOriginKind.Project,
                    CanDelete = isProjectOrShared,
                    EditabilityHint = p.Origin == ParameterOriginKind.Project
                        ? "Parámetro de Proyecto (No-Shared): puedes cambiar Instancia ↔ Tipo, grupo, o convertirlo a Compartido."
                        : (p.Origin == ParameterOriginKind.Shared
                            ? "Parámetro Compartido en Proyecto: puedes cambiar Instancia ↔ Tipo o grupo."
                            : "Parámetro Global del Proyecto.")
                });
            }

            return results.OrderBy(r => r.Name).ToList();
        }

        public static ParameterOriginKind ClassifyParameterOrigin(Document doc, Parameter p)
        {
            if (p.IsShared) return ParameterOriginKind.Shared;
            if (p.Definition is InternalDefinition intDef)
            {
                if (intDef.BuiltInParameter != BuiltInParameter.INVALID)
                    return ParameterOriginKind.BuiltIn;
                if (doc.IsFamilyDocument)
                    return ParameterOriginKind.Family;
                return ParameterOriginKind.Project;
            }
            return doc.IsFamilyDocument ? ParameterOriginKind.Family : ParameterOriginKind.Project;
        }

        public static string ReadParameterDisplayValue(Document doc, Parameter p)
        {
            try
            {
                if (!p.HasValue) return "—";
                string? vs = p.AsValueString();
                if (!string.IsNullOrEmpty(vs)) return vs;

                return p.StorageType switch
                {
                    StorageType.String => p.AsString() ?? "—",
                    StorageType.Integer => p.AsInteger().ToString(),
                    StorageType.Double => Math.Round(p.AsDouble(), 4).ToString(),
                    StorageType.ElementId => ReadElementIdName(doc, p.AsElementId()),
                    _ => "—"
                };
            }
            catch
            {
                return "—";
            }
        }

        public static string ReadFamilyParameterDisplayValue(Document doc, FamilyType? fType, FamilyParameter fp)
        {
            if (fType == null || fp == null) return "—";
            try
            {
                if (!fType.HasValue(fp)) return "—";
                string? vs = fType.AsValueString(fp);
                if (!string.IsNullOrEmpty(vs)) return vs;

                return fp.StorageType switch
                {
                    StorageType.String => fType.AsString(fp) ?? "—",
                    StorageType.Integer => fp.Definition.GetDataType() == SpecTypeId.Boolean.YesNo
                        ? (fType.AsInteger(fp) == 1 ? "Sí" : "No")
                        : fType.AsInteger(fp)?.ToString() ?? "—",
                    StorageType.Double => fType.AsDouble(fp).HasValue ? Math.Round(fType.AsDouble(fp)!.Value, 4).ToString() : "—",
                    StorageType.ElementId => ReadElementIdName(doc, fType.AsElementId(fp)),
                    _ => "—"
                };
            }
            catch
            {
                return "—";
            }
        }

        private static string ReadElementIdName(Document doc, ElementId? id)
        {
            if (id == null || id == ElementId.InvalidElementId) return "<Ninguno>";
            Element? el = doc.GetElement(id);
            return el != null ? el.Name : $"Id:{id.Value}";
        }

        // =========================================================================
        // OPERACIONES DE EDICIÓN NATIVA
        // =========================================================================

        public static (bool ok, string message) SetParameterValueNative(
            Document doc,
            Element? selectedElement,
            NativeParameterItem item,
            string newValueText)
        {
            if (doc == null || item == null) return (false, "Documento o parámetro inválido.");

            using (var tx = new Transaction(doc, $"ARCA: Editar valor de {item.Name}"))
            {
                tx.Start();
                try
                {
                    if (doc.IsFamilyDocument && selectedElement == null)
                    {
                        FamilyManager fm = doc.FamilyManager;
                        FamilyParameter? fp = fm.get_Parameter(item.Name);
                        if (fp == null) { tx.RollBack(); return (false, $"No se encontró el parámetro '{item.Name}' en la familia."); }
                        if (fm.CurrentType == null) fm.NewType("Tipo 1");

                        bool setOk = TrySetFamilyManagerValue(doc, fm, fp, newValueText);
                        if (!setOk) { tx.RollBack(); return (false, $"No se pudo asignar '{newValueText}' a '{item.Name}'."); }

                        tx.Commit();
                        return (true, $"Valor de '{item.Name}' actualizado a '{newValueText}'.");
                    }
                    else if (selectedElement != null)
                    {
                        Element target = selectedElement;
                        if (!item.IsInstance)
                        {
                            ElementId tid = selectedElement.GetTypeId();
                            if (tid != ElementId.InvalidElementId)
                                target = doc.GetElement(tid) ?? selectedElement;
                        }

                        Parameter? p = target.LookupParameter(item.Name);
                        if (p == null) { tx.RollBack(); return (false, $"Parámetro '{item.Name}' no encontrado en el elemento."); }
                        if (p.IsReadOnly) { tx.RollBack(); return (false, $"'{item.Name}' es de solo lectura en Revit."); }

                        bool setOk = TrySetElementParameterValue(p, newValueText);
                        if (!setOk) { tx.RollBack(); return (false, $"Formato incompatible para '{item.Name}'."); }

                        tx.Commit();
                        return (true, $"Valor de '{item.Name}' actualizado en '{target.Name}'.");
                    }

                    tx.RollBack();
                    return (false, "Selecciona un elemento o abre una familia .rfa para editar valores directamente.");
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted()) tx.RollBack();
                    return (false, $"Error al editar valor: {ex.Message}");
                }
            }
        }

        private static bool TrySetFamilyManagerValue(Document doc, FamilyManager fm, FamilyParameter fp, string valText)
        {
            switch (fp.StorageType)
            {
                case StorageType.String:
                    fm.Set(fp, valText ?? string.Empty);
                    return true;
                case StorageType.Integer:
                    if (fp.Definition.GetDataType() == SpecTypeId.Boolean.YesNo)
                    {
                        bool b = valText.Equals("1") || valText.Equals("si", StringComparison.OrdinalIgnoreCase) ||
                                 valText.Equals("sí", StringComparison.OrdinalIgnoreCase) || valText.Equals("true", StringComparison.OrdinalIgnoreCase);
                        fm.Set(fp, b ? 1 : 0);
                        return true;
                    }
                    if (int.TryParse(valText, out int iv)) { fm.Set(fp, iv); return true; }
                    return false;
                case StorageType.Double:
                    if (double.TryParse(valText, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double dv))
                    {
                        // Si el usuario escribe un número sin unidad en familia, intentar SetValueString primero si tiene unidad o usar UnitUtils
                        if (UnitFormatUtils.TryParse(doc.GetUnits(), fp.Definition.GetDataType(), valText, out double parsedInternal))
                        {
                            fm.Set(fp, parsedInternal);
                            return true;
                        }
                        fm.Set(fp, dv);
                        return true;
                    }
                    if (UnitFormatUtils.TryParse(doc.GetUnits(), fp.Definition.GetDataType(), valText, out double parsedWithUnit))
                    {
                        fm.Set(fp, parsedWithUnit);
                        return true;
                    }
                    return false;
                default:
                    return false;
            }
        }

        private static bool TrySetElementParameterValue(Parameter p, string valText)
        {
            switch (p.StorageType)
            {
                case StorageType.String:
                    return p.Set(valText ?? string.Empty);
                case StorageType.Integer:
                    if (p.Definition.GetDataType() == SpecTypeId.Boolean.YesNo)
                    {
                        bool b = valText.Equals("1") || valText.Equals("si", StringComparison.OrdinalIgnoreCase) ||
                                 valText.Equals("sí", StringComparison.OrdinalIgnoreCase) || valText.Equals("true", StringComparison.OrdinalIgnoreCase);
                        return p.Set(b ? 1 : 0);
                    }
                    return int.TryParse(valText, out int iv) && p.Set(iv);
                case StorageType.Double:
                    if (p.SetValueString(valText)) return true;
                    if (double.TryParse(valText, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double dv))
                        return p.Set(dv);
                    return false;
                default:
                    return false;
            }
        }

        public static (bool ok, string message) SetFamilyFormulaNative(Document rfaDoc, string paramName, string? formula)
        {
            if (rfaDoc == null || !rfaDoc.IsFamilyDocument)
                return (false, "Las fórmulas solo se editan dentro de un documento de Familia (.rfa).");

            FamilyManager fm = rfaDoc.FamilyManager;
            FamilyParameter? fp = fm.get_Parameter(paramName);
            if (fp == null) return (false, $"No se encontró '{paramName}'.");
            if (!fp.CanAssignFormula) return (false, $"'{paramName}' no admite fórmulas en Revit.");

            using (var tx = new Transaction(rfaDoc, $"ARCA: Fórmula en {paramName}"))
            {
                tx.Start();
                try
                {
                    string? cleanFormula = string.IsNullOrWhiteSpace(formula) ? null : formula.Trim();
                    fm.SetFormula(fp, cleanFormula);
                    tx.Commit();
                    return (true, cleanFormula == null
                        ? $"Fórmula eliminada de '{paramName}'."
                        : $"Fórmula aplicada a '{paramName}': = {cleanFormula}");
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted()) tx.RollBack();
                    return (false, $"Fórmula inválida para Revit: {ex.Message}");
                }
            }
        }

        public static (bool ok, string message) RenameFamilyParameterNative(Document rfaDoc, string oldName, string newName)
        {
            if (rfaDoc == null || !rfaDoc.IsFamilyDocument)
                return (false, "El renombrado directo nativo aplica a parámetros de Familia (.rfa).");
            if (string.IsNullOrWhiteSpace(newName))
                return (false, "El nuevo nombre no puede estar vacío.");

            FamilyManager fm = rfaDoc.FamilyManager;
            FamilyParameter? fp = fm.get_Parameter(oldName);
            if (fp == null) return (false, $"No se encontró '{oldName}'.");
            if (fp.IsShared) return (false, "Un parámetro Compartido toma su nombre del archivo .TXT (no se renombra directo).");

            using (var tx = new Transaction(rfaDoc, $"ARCA: Renombrar {oldName}"))
            {
                tx.Start();
                try
                {
                    fm.RenameParameter(fp, newName.Trim());
                    tx.Commit();
                    return (true, $"Renombrado '{oldName}' ➔ '{newName.Trim()}'.");
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted()) tx.RollBack();
                    return (false, $"No se pudo renombrar: {ex.Message}");
                }
            }
        }

        public static (bool ok, string message) ToggleScopeNative(Application app, Document doc, string paramName)
        {
            if (doc == null) return (false, "Sin documento activo.");

            if (doc.IsFamilyDocument)
            {
                FamilyManager fm = doc.FamilyManager;
                FamilyParameter? fp = fm.get_Parameter(paramName);
                if (fp == null) return (false, $"No se encontró '{paramName}'.");

                using (var tx = new Transaction(doc, $"ARCA: Cambiar ámbito {paramName}"))
                {
                    tx.Start();
                    try
                    {
                        if (fp.IsInstance) fm.MakeType(fp);
                        else fm.MakeInstance(fp);
                        tx.Commit();
                        return (true, $"'{paramName}' ahora es de {(fp.IsInstance ? "Ejemplar (Instancia)" : "Tipo")}.");
                    }
                    catch (Exception ex)
                    {
                        if (tx.HasStarted()) tx.RollBack();
                        return (false, $"Revit no permitió cambiar ámbito: {ex.Message}");
                    }
                }
            }

            // En .rvt usar el motor que preserva valores
            var all = ParameterTransformationService.ScanDocumentParameters(app, doc, computeUsageStats: false);
            var match = all.FirstOrDefault(x => x.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase));
            if (match == null) return (false, $"No se encontró '{paramName}' en ParameterBindings.");

            var (toggledCount, migratedValues, logs) = ParameterTransformationService.ToggleParameterScopeInstanceType(
                app, doc, new List<ProjectParameterItem> { match });
            return toggledCount > 0
                ? (true, $"'{paramName}' cambiado a {(match.IsInstance ? "Tipo" : "Ejemplar")} ({migratedValues} valores preservados).")
                : (false, logs.LastOrDefault() ?? "No se pudo cambiar el ámbito.");
        }

        public static (bool ok, string message) SetParameterGroupNative(Document doc, string paramName, ForgeTypeId newGroup)
        {
            if (doc == null || newGroup == null) return (false, "Datos inválidos.");

            using (var tx = new Transaction(doc, $"ARCA: Grupo de {paramName}"))
            {
                tx.Start();
                try
                {
                    if (doc.IsFamilyDocument)
                    {
                        FamilyParameter? fp = doc.FamilyManager.get_Parameter(paramName);
                        if (fp?.Definition is InternalDefinition intDef)
                        {
                            intDef.SetGroupTypeId(newGroup);
                            tx.Commit();
                            return (true, $"Grupo de '{paramName}' cambiado a '{SafeGetGroupLabel(newGroup)}'.");
                        }
                    }
                    else
                    {
                        var it = doc.ParameterBindings.ForwardIterator();
                        it.Reset();
                        while (it.MoveNext())
                        {
                            if (it.Key is InternalDefinition intDef && intDef.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase))
                            {
                                intDef.SetGroupTypeId(newGroup);
                                tx.Commit();
                                return (true, $"Grupo de '{paramName}' cambiado a '{SafeGetGroupLabel(newGroup)}'.");
                            }
                        }
                    }

                    tx.RollBack();
                    return (false, $"No se pudo cambiar el grupo de '{paramName}'.");
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted()) tx.RollBack();
                    return (false, $"Error cambiando grupo: {ex.Message}");
                }
            }
        }

        public static (bool ok, string message) ToggleReportingNative(Document rfaDoc, string paramName, bool makeReporting)
        {
            if (rfaDoc == null || !rfaDoc.IsFamilyDocument)
                return (false, "Reporting solo aplica en Familias (.rfa).");

            FamilyManager fm = rfaDoc.FamilyManager;
            FamilyParameter? fp = fm.get_Parameter(paramName);
            if (fp == null) return (false, $"No se encontró '{paramName}'.");

            using (var tx = new Transaction(rfaDoc, $"ARCA: Reporting {paramName}"))
            {
                tx.Start();
                try
                {
                    if (makeReporting) fm.MakeReporting(fp);
                    else fm.MakeNonReporting(fp);
                    tx.Commit();
                    return (true, $"'{paramName}' marcado como {(makeReporting ? "Parámetro de Informe (Reporting)" : "Normal")}.");
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted()) tx.RollBack();
                    return (false, $"Revit requiere que esté acotado/ejemplar para ser Reporting: {ex.Message}");
                }
            }
        }

        private static ExternalDefinition? GetOrCreateExternalDefinition(
            Application app,
            string groupName,
            string paramName,
            ForgeTypeId dataType,
            string description)
        {
            SharedParameterCloudService.EnsureDefaultSharedParamFile(app, true);
            DefinitionFile? defFile = app.OpenSharedParameterFile();
            if (defFile == null) return null;

            DefinitionGroup? defGroup = defFile.Groups.get_Item(groupName) ?? defFile.Groups.Create(groupName);
            if (defGroup == null) return null;

            if (defGroup.Definitions.get_Item(paramName) is ExternalDefinition existing)
                return existing;

            var opts = new ExternalDefinitionCreationOptions(paramName, dataType)
            {
                Description = description ?? string.Empty
            };
            return defGroup.Definitions.Create(opts) as ExternalDefinition;
        }

        public static (bool ok, string message) ConvertToSharedNative(Application app, Document doc, string paramName)
        {
            if (doc == null) return (false, "Sin documento.");

            if (doc.IsFamilyDocument)
            {
                FamilyManager fm = doc.FamilyManager;
                FamilyParameter? fp = fm.get_Parameter(paramName);
                if (fp == null) return (false, $"No se encontró '{paramName}'.");
                if (fp.IsShared) return (true, $"'{paramName}' ya es Compartido.");

                ForgeTypeId dt = fp.Definition.GetDataType();
                ForgeTypeId grp = fp.Definition.GetGroupTypeId();
                bool isInst = fp.IsInstance;

                var extDef = GetOrCreateExternalDefinition(
                    app, "01_ARCA_Familia", paramName, dt,
                    "Convertido a parámetro compartido desde ARCA Familias & Parámetros");

                if (extDef == null)
                    return (false, "No se pudo crear la definición compartida en el archivo .TXT activo.");

                using (var tx = new Transaction(doc, $"ARCA: Convertir {paramName} a Shared"))
                {
                    tx.Start();
                    try
                    {
                        fm.ReplaceParameter(fp, extDef, grp, isInst);
                        tx.Commit();
                        return (true, $"'{paramName}' convertido a Compartido (GUID: {extDef.GUID}) preservando fórmulas y vínculos.");
                    }
                    catch (Exception ex)
                    {
                        if (tx.HasStarted()) tx.RollBack();
                        return (false, $"Error al convertir en familia: {ex.Message}");
                    }
                }
            }

            // En .rvt
            var all = ParameterTransformationService.ScanDocumentParameters(app, doc, computeUsageStats: false);
            var match = all.FirstOrDefault(x => x.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase));
            if (match == null) return (false, $"No se encontró '{paramName}' en el proyecto.");

            var (convertedCount, preservedValuesCount, logs) = ParameterTransformationService.ConvertProjectParametersToShared(
                app, doc, new List<ProjectParameterItem> { match }, "01_ARCA_Proyecto");

            return convertedCount > 0
                ? (true, $"'{paramName}' convertido a Compartido ({preservedValuesCount} valores preservados).")
                : (false, logs.LastOrDefault() ?? "No se pudo convertir.");
        }

        public static (bool ok, string message) CreateNativeParameter(
            Application app,
            Document doc,
            string name,
            ForgeTypeId dataType,
            ForgeTypeId groupType,
            bool isInstance,
            bool asShared,
            List<long>? categoryIds = null)
        {
            if (doc == null) return (false, "Sin documento.");
            string cleanName = (name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(cleanName)) return (false, "Escribe un nombre para el parámetro.");

            ExternalDefinition? extDef = null;
            if (asShared)
            {
                extDef = GetOrCreateExternalDefinition(
                    app, "01_ARCA_Parametros", cleanName, dataType, "Creado desde ARCA Familias & Parámetros");
                if (extDef == null)
                    return (false, "No se pudo registrar el parámetro compartido en el archivo .TXT.");
            }

            if (doc.IsFamilyDocument)
            {
                FamilyManager fm = doc.FamilyManager;
                if (fm.get_Parameter(cleanName) != null)
                    return (false, $"Ya existe un parámetro llamado '{cleanName}' en la familia.");

                using (var tx = new Transaction(doc, $"ARCA: Crear parámetro {cleanName}"))
                {
                    tx.Start();
                    try
                    {
                        if (asShared && extDef != null)
                            fm.AddParameter(extDef, groupType, isInstance);
                        else
                            fm.AddParameter(cleanName, groupType, dataType, isInstance);

                        tx.Commit();
                        return (true, $"Parámetro '{cleanName}' creado en la familia ({(asShared ? "Compartido" : "Familia")}, {(isInstance ? "Ejemplar" : "Tipo")}).");
                    }
                    catch (Exception ex)
                    {
                        if (tx.HasStarted()) tx.RollBack();
                        return (false, $"Error creando parámetro en familia: {ex.Message}");
                    }
                }
            }

            // Proyecto (.rvt) -> Por ahora creación compartida directa en categorías seleccionadas (o todas las model categories si no se especifican)
            if (extDef == null)
            {
                extDef = GetOrCreateExternalDefinition(
                    app, "01_ARCA_Proyecto", cleanName, dataType, "Creado desde ARCA Familias & Parámetros");
                if (extDef == null)
                    return (false, "En proyectos (.rvt), la creación automática vía API requiere definición Compartida (.TXT) y falló la creación.");
            }

            CategorySet catSet = app.Create.NewCategorySet();
            if (categoryIds != null && categoryIds.Count > 0)
            {
                var idSet = new HashSet<long>(categoryIds);
                foreach (Category cat in doc.Settings.Categories)
                {
                    if (cat != null && cat.AllowsBoundParameters && idSet.Contains(cat.Id.Value))
                        catSet.Insert(cat);
                }
            }
            else
            {
                foreach (Category cat in doc.Settings.Categories)
                {
                    if (cat != null && cat.AllowsBoundParameters && cat.CategoryType == CategoryType.Model)
                        catSet.Insert(cat);
                }
            }

            if (catSet.IsEmpty)
                return (false, "Selecciona al menos una categoría válida para vincular el parámetro.");

            using (var tx = new Transaction(doc, $"ARCA: Vincular {cleanName}"))
            {
                tx.Start();
                try
                {
                    ElementBinding binding = isInstance
                        ? (ElementBinding)app.Create.NewInstanceBinding(catSet)
                        : app.Create.NewTypeBinding(catSet);

                    bool inserted = doc.ParameterBindings.Insert(extDef, binding, groupType);
                    if (!inserted) inserted = doc.ParameterBindings.ReInsert(extDef, binding, groupType);

                    tx.Commit();
                    return inserted
                        ? (true, $"Parámetro '{cleanName}' creado y vinculado a {catSet.Size} categoría(s).")
                        : (false, "No se pudo insertar el binding en el proyecto.");
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted()) tx.RollBack();
                    return (false, $"Error vinculando parámetro: {ex.Message}");
                }
            }
        }

        public static (bool ok, string message) DeleteParameterNative(Document doc, string paramName)
        {
            if (doc == null) return (false, "Sin documento.");

            using (var tx = new Transaction(doc, $"ARCA: Eliminar parámetro {paramName}"))
            {
                tx.Start();
                try
                {
                    if (doc.IsFamilyDocument)
                    {
                        FamilyParameter? fp = doc.FamilyManager.get_Parameter(paramName);
                        if (fp == null) { tx.RollBack(); return (false, $"No se encontró '{paramName}'."); }
                        doc.FamilyManager.RemoveParameter(fp);
                        tx.Commit();
                        return (true, $"Parámetro '{paramName}' eliminado de la familia.");
                    }
                    else
                    {
                        Definition? targetDef = null;
                        var it = doc.ParameterBindings.ForwardIterator();
                        it.Reset();
                        while (it.MoveNext())
                        {
                            if (it.Key != null && it.Key.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase))
                            {
                                targetDef = it.Key;
                                break;
                            }
                        }

                        if (targetDef == null) { tx.RollBack(); return (false, $"No se encontró '{paramName}' en el proyecto."); }
                        doc.ParameterBindings.Remove(targetDef);
                        tx.Commit();
                        return (true, $"Parámetro '{paramName}' desvinculado del proyecto.");
                    }
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted()) tx.RollBack();
                    return (false, $"No se pudo eliminar '{paramName}': {ex.Message}");
                }
            }
        }
    }
}
