using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitToSupabasePlugin
{
    /// <summary>
    /// Automarcado de carpinterías: escribe en la Marca de cada elemento
    /// el Assembly Code del tipo, un guion y el Type Mark (ej. Assembly Code "P01" + Type Mark "A" = "P01-A"; ventanas: "V01-A").
    /// Así el código base (P02) y la variante (A) quedan en la marca y los cuadros por código la pueden filtrar.
    /// </summary>
    public static class DoorAutoMarkService
    {
        public static (int marked, List<string> logs) AutoMark(Document doc, IEnumerable<ElementId> elementIds)
        {
            var logs = new List<string>();
            int marked = 0;
            var ids = elementIds?.Distinct().ToList() ?? new List<ElementId>();

            if (ids.Count == 0)
            {
                logs.Add("No hay elementos para automarcar.");
                return (0, logs);
            }

            using (Transaction t = new Transaction(doc, "Automarcar carpinterías (Assembly Code + Type Mark)"))
            {
                t.Start();

                foreach (var id in ids)
                {
                    Element? e = doc.GetElement(id);
                    if (e == null) continue;

                    string assemblyCode = ReadAssemblyCode(doc, e);
                    string typeMark = ReadTypeMark(e);

                    if (string.IsNullOrWhiteSpace(assemblyCode))
                    {
                        logs.Add($"ID {id.Value}: sin Assembly Code en el tipo; no se marcó.");
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(typeMark))
                    {
                        logs.Add($"ID {id.Value}: sin Type Mark; no se marcó.");
                        continue;
                    }

                    string newMark = assemblyCode.Trim() + "-" + typeMark.Trim();
                    Parameter? mark = e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK) ?? e.LookupParameter("Marca") ?? e.LookupParameter("Mark");
                    if (mark == null || mark.IsReadOnly)
                    {
                        logs.Add($"ID {id.Value}: no tiene el parámetro Marca editable.");
                        continue;
                    }

                    if (mark.AsString() == newMark) continue;
                    mark.Set(newMark);
                    marked++;
                }

                t.Commit();
            }

            logs.Insert(0, $"Automarcado completado: {marked} elemento(s) con Marca = Assembly Code + Type Mark.");
            return (marked, logs);
        }

        /// <summary>Assembly Code del ejemplar, o del tipo si el ejemplar no lo tiene.</summary>
        private static string ReadAssemblyCode(Document doc, Element e)
        {
            string? v = ReadString(e.LookupParameter("Assembly Code"))
                        ?? ReadString(e.LookupParameter("Código de ensamblaje"));
            if (!string.IsNullOrWhiteSpace(v)) return v;

            if (e.GetTypeId() != ElementId.InvalidElementId && doc.GetElement(e.GetTypeId()) is ElementType type)
            {
                v = ReadString(type.LookupParameter("Assembly Code"))
                    ?? ReadString(type.LookupParameter("Código de ensamblaje"));
            }
            return v ?? string.Empty;
        }

        /// <summary>Type Mark del tipo del elemento.</summary>
        private static string ReadTypeMark(Element e)
        {
            if (e is not FamilyInstance fi || fi.Symbol == null) return string.Empty;
            FamilySymbol sym = fi.Symbol;
            return ReadString(sym.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK))
                   ?? ReadString(sym.LookupParameter("Type Mark"))
                   ?? ReadString(sym.LookupParameter("Marca de tipo"))
                   ?? string.Empty;
        }

        private static string? ReadString(Parameter? p)
        {
            if (p == null || !p.HasValue) return null;
            string? s = p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
            return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }
    }
}
