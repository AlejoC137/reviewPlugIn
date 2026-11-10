using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitToSupabasePlugin
{
    /// <summary>
    /// Entrada única para crear carpinterías en lote desde la UI y desde el MCP.
    /// Flujo: (1) ensamblaje por código base (P01 con variantes P01A, P01B...),
    /// (2) cuadro por código = copia del cuadro maestro con el filtro cambiado (P01 -> P02),
    /// (3) vistas técnicas del ensamblaje.
    /// </summary>
    public class DoorBatchRequest
    {
        public IList<DoorItemViewModel> Items { get; set; } = new List<DoorItemViewModel>();
        public BatchAssemblyOptions Assembly { get; set; } = new BatchAssemblyOptions();
        /// <summary>Opciones de vistas. Null = no crear vistas.</summary>
        public AssemblyBatchViewOptions? Views { get; set; }
        /// <summary>Cuadro maestro (molde). Null = se usa el primer cuadro de la categoría.</summary>
        public ViewSchedule? MasterSchedule { get; set; }
    }

    public class DoorBatchResult
    {
        public List<AssemblyInstance> Assemblies { get; } = new List<AssemblyInstance>();
        public List<ViewSchedule> CodeSchedules { get; } = new List<ViewSchedule>();
        public int ViewsCreated { get; set; }
        public List<string> Logs { get; } = new List<string>();
    }

    public static class DoorAssemblyBatchPipeline
    {
        public static DoorBatchResult Run(Document doc, DoorBatchRequest request)
        {
            var result = new DoorBatchResult();
            var log = result.Logs;

            // 1) Ensamblajes: un ensamblaje por código base
            var (assemblies, assLogs) = DoorAssemblyManagerService.CreateBatchDoorAssemblies(doc, request.Items, request.Assembly);
            log.AddRange(assLogs);
            result.Assemblies.AddRange(assemblies);

            if (assemblies.Count == 0) return result;

            // 2) Cuadros por código (copias del maestro con el filtro cambiado)
            ViewSchedule? master = request.MasterSchedule ?? FindDefaultMaster(doc, request.Assembly.Category);
            CreateCodeSchedules(doc, master, assemblies, result, log);

            // 3) Vistas técnicas del ensamblaje
            if (request.Views != null)
            {
                var (vCount, vLogs) = DoorAssemblyManagerService.CreateBatchAssemblyViews(doc, assemblies, request.Views, master);
                result.ViewsCreated = vCount;
                log.AddRange(vLogs);
            }

            return result;
        }

        private static void CreateCodeSchedules(
            Document doc,
            ViewSchedule? master,
            IList<AssemblyInstance> assemblies,
            DoorBatchResult result,
            List<string> log)
        {
            if (master == null || !master.IsValidObject)
            {
                log.Add("[CUADROS] No hay cuadro maestro: se omiten las copias por código.");
                return;
            }

            string? masterCode = DoorAssemblyManagerService.DetectScheduleCode(master);
            if (masterCode == null)
            {
                log.Add($"[CUADROS] El cuadro '{master.Name}' no tiene filtro con código (ej. P01): se omiten las copias.");
                return;
            }

            foreach (var ai in assemblies)
            {
                string assemblyName = ai.AssemblyTypeName ?? ai.Name;
                string? code = DoorAssemblyManagerService.ExtractBaseCode(assemblyName);
                if (code == null) continue;

                if (string.Equals(code, masterCode, StringComparison.OrdinalIgnoreCase))
                {
                    // El propio maestro ya es el cuadro de este código
                    log.Add($"[CUADRO] '{master.Name}' ya corresponde a {code}.");
                    continue;
                }

                ViewSchedule? copy = CopyMasterForCode(doc, master, masterCode, code, log);
                if (copy != null)
                {
                    result.CodeSchedules.Add(copy);
                }
            }
        }

        /// <summary>
        /// Duplica el cuadro maestro y cambia el filtro del código de origen por el nuevo (P01 -> P02).
        /// Reutiliza el cuadro si ya existe con el nombre "CUADRO {código}".
        /// </summary>
        private static ViewSchedule? CopyMasterForCode(
            Document doc,
            ViewSchedule master,
            string fromCode,
            string toCode,
            List<string> log)
        {
            string desiredName = $"CUADRO {toCode}";

            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .FirstOrDefault(s => !s.IsTemplate && s.Name.Equals(desiredName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                log.Add($"[CUADRO] Se reutiliza '{existing.Name}' (ya existía).");
                return existing;
            }

            var (copies, copyLogs) = AssemblyScheduleService.DuplicateSchedules(
                doc,
                new List<ViewSchedule> { master },
                findName: fromCode,
                replaceName: toCode,
                modifyFilters: true,
                filterFindValue: fromCode,
                filterNewValue: toCode);

            log.AddRange(copyLogs.Select(l => "  " + l));

            ViewSchedule? copy = copies.FirstOrDefault();
            if (copy == null)
            {
                log.Add($"[AVISO] No se pudo copiar el cuadro '{master.Name}' para {toCode}.");
                return null;
            }

            if (!HasFilterWithCode(copy, toCode))
            {
                log.Add($"[AVISO] '{copy.Name}': ningún filtro contiene '{toCode}'. Revisa el filtro del cuadro maestro.");
            }

            if (!copy.Name.Equals(desiredName, StringComparison.OrdinalIgnoreCase))
            {
                var (ok, msg) = AssemblyScheduleService.RenameSchedule(doc, copy, desiredName);
                if (!ok) log.Add($"[AVISO] No se pudo renombrar a '{desiredName}': {msg}");
            }

            log.Add($"[CUADRO] '{copy.Name}' creado a partir de '{master.Name}' ({fromCode} -> {toCode}).");
            return copy;
        }

        private static bool HasFilterWithCode(ViewSchedule schedule, string code)
        {
            foreach (ScheduleFilter f in schedule.Definition.GetFilters())
            {
                string? value = null;
                try { value = f.GetStringValue(); } catch { }
                if (value != null && value.IndexOf(code, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static ViewSchedule? FindDefaultMaster(Document doc, BuiltInCategory category)
        {
            var catId = new ElementId(category);
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .FirstOrDefault(s => !s.IsTemplate && s.Definition.CategoryId == catId);
        }
    }
}
