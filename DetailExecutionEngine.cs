using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using View = Autodesk.Revit.DB.View;

namespace RevitToSupabasePlugin
{
    public class DetailExecutionReport
    {
        public int TypesAdjusted { get; set; }
        public int ComponentsPlaced { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
        public List<string> Messages { get; set; } = new List<string>();
    }

    public static class DetailExecutionEngine
    {
        public static DetailExecutionReport ApplyJsonActions(Document doc, Autodesk.Revit.DB.View view, DetailAiResponse response)
        {
            var report = new DetailExecutionReport();
            if (response?.ParametricActions == null)
            {
                report.Warnings.Add("El JSON de respuesta no contiene 'parametric_actions'.");
                return report;
            }

            using (TransactionGroup tg = new TransactionGroup(doc, "Aplicar Estandarización de Detalle IA"))
            {
                tg.Start();

                // 1. Ajustes en Elementos Nativos (3D / Tipo del Sistema)
                if (response.ParametricActions.NativeAdjustments != null && response.ParametricActions.NativeAdjustments.Count > 0)
                {
                    using (Transaction t1 = new Transaction(doc, "Actualizar Tipos y Parámetros Nativos"))
                    {
                        t1.Start();

                        foreach (var adj in response.ParametricActions.NativeAdjustments)
                        {
                            Element elem = doc.GetElement(new ElementId(adj.ElementId));
                            if (elem == null)
                            {
                                report.Warnings.Add($"Elemento con Id {adj.ElementId} no encontrado en el proyecto.");
                                continue;
                            }

                            if (adj.Action == "ASSIGN_TYPE" && !string.IsNullOrWhiteSpace(adj.TargetTypeName))
                            {
                                ElementType? targetType = new FilteredElementCollector(doc)
                                    .OfCategoryId(elem.Category.Id)
                                    .WhereElementIsElementType()
                                    .Cast<ElementType>()
                                    .FirstOrDefault(x => x.Name.Equals(adj.TargetTypeName, StringComparison.OrdinalIgnoreCase));

                                if (targetType != null)
                                {
                                    elem.ChangeTypeId(targetType.Id);
                                    report.TypesAdjusted++;
                                    report.Messages.Add($"Elemento {elem.Id.Value} ({elem.Category.Name}) actualizado a tipo: {targetType.Name}");
                                }
                                else
                                {
                                    report.Warnings.Add($"No se encontró en el proyecto el tipo '{adj.TargetTypeName}' para la categoría {elem.Category.Name}. Se mantuvo el tipo actual.");
                                }
                            }

                            // Aplicar parámetros si existen
                            if (adj.ParametersToSet != null)
                            {
                                foreach (var kvp in adj.ParametersToSet)
                                {
                                    Parameter p = elem.LookupParameter(kvp.Key);
                                    if (p != null && !p.IsReadOnly)
                                    {
                                        if (kvp.Value is double dVal)
                                            p.Set(dVal / 304.8);
                                        else if (kvp.Value is int iVal)
                                            p.Set(iVal);
                                        else if (kvp.Value is long lVal)
                                            p.Set((int)lVal);
                                        else if (kvp.Value is bool bVal)
                                            p.Set(bVal ? 1 : 0);
                                        else if (kvp.Value is string sVal)
                                            p.Set(sVal);
                                    }
                                }
                            }
                        }

                        t1.Commit();
                    }
                }

                // 2. Inyección de Componentes de Detalle 2D en la Vista
                if (response.ParametricActions.DetailComponents != null && response.ParametricActions.DetailComponents.Count > 0)
                {
                    using (Transaction t2 = new Transaction(doc, "Inyectar Componentes 2D de Detalle"))
                    {
                        t2.Start();

                        foreach (var comp in response.ParametricActions.DetailComponents)
                        {
                            Element refElem = doc.GetElement(new ElementId(comp.ReferenceElementId));
                            if (refElem == null)
                            {
                                report.Warnings.Add($"Elemento de referencia Id {comp.ReferenceElementId} no encontrado.");
                                continue;
                            }

                            FamilySymbol? symbol = new FilteredElementCollector(doc)
                                .OfCategory(BuiltInCategory.OST_DetailComponents)
                                .WhereElementIsElementType()
                                .Cast<FamilySymbol>()
                                .FirstOrDefault(s => s.FamilyName.Equals(comp.FamilyName, StringComparison.OrdinalIgnoreCase));

                            if (symbol != null)
                            {
                                if (!symbol.IsActive) symbol.Activate();

                                BoundingBoxXYZ bbox = refElem.get_BoundingBox(view);
                                XYZ anchor = bbox != null ? bbox.Max : XYZ.Zero;

                                XYZ placePt = new XYZ(
                                    anchor.X + (comp.OffsetXMm / 304.8),
                                    anchor.Y,
                                    anchor.Z + (comp.OffsetYMm / 304.8)
                                );

                                try
                                {
                                    FamilyInstance detailInst = doc.Create.NewFamilyInstance(placePt, symbol, view);

                                    if (comp.DimensionsMm != null)
                                    {
                                        foreach (var paramEntry in comp.DimensionsMm)
                                        {
                                            Parameter p = detailInst.LookupParameter(paramEntry.Key);
                                            if (p != null && !p.IsReadOnly)
                                            {
                                                p.Set(paramEntry.Value / 304.8);
                                            }
                                        }
                                    }

                                    report.ComponentsPlaced++;
                                    report.Messages.Add($"Componente de detalle colocado: {comp.FamilyName} en ({comp.OffsetXMm}mm, {comp.OffsetYMm}mm).");
                                }
                                catch (Exception ex)
                                {
                                    report.Warnings.Add($"Error al instanciar componente '{comp.FamilyName}': {ex.Message}");
                                }
                            }
                            else
                            {
                                report.Warnings.Add($"Familia de componente de detalle '{comp.FamilyName}' no está cargada en el proyecto.");
                            }
                        }

                        t2.Commit();
                    }
                }

                tg.Assimilate();
            }

            return report;
        }
    }
}
