using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;

namespace RevitToSupabasePlugin
{
    /// <summary>
    /// Servicio de puente para ensamblajes en Revit.
    /// Permite sincronizar cálculos globales del modelo hacia elementos que viven dentro de
    /// ensamblajes (AssemblyInstance) aislados, resolviendo la limitación nativa de scope de Revit
    /// mediante parámetros puente automáticos (ARCA_TotalTipo_Obra y ARCA_Ubicaciones_Obra).
    /// </summary>
    public static class AssemblyBridgeService
    {
        public const string ParamTotalObra = "ARCA_TotalTipo_Obra";
        public const string ParamUbicacionesObra = "ARCA_Ubicaciones_Obra";

        public static readonly Guid GuidTotalObra = new Guid("9A7B3C10-2222-4444-8888-A00000000001");
        public static readonly Guid GuidUbicacionesObra = new Guid("9A7B3C10-2222-4444-8888-A00000000002");

        /// <summary>
        /// Asegura que los parámetros puente compartidos existan y estén vinculados al proyecto
        /// para las categorías especificadas (Puertas, Ventanas y Ensamblajes).
        /// </summary>
        public static void EnsureBridgeParameters(Document doc, BuiltInCategory category = BuiltInCategory.OST_Doors)
        {
            if (doc == null || doc.IsReadOnly) return;
            Autodesk.Revit.ApplicationServices.Application app = doc.Application;

            string originalSpFile = app.SharedParametersFilename;
            string bridgeSpFile = Path.Combine(Path.GetTempPath(), "ARCA_AssemblyBridge_SharedParams.txt");

            try
            {
                CreateSharedParamFileIfNotExists(bridgeSpFile);
                app.SharedParametersFilename = bridgeSpFile;

                DefinitionFile defFile = app.OpenSharedParameterFile();
                if (defFile == null) return;

                DefinitionGroup group = defFile.Groups.get_Item("ArcaBridge") ?? defFile.Groups.Create("ArcaBridge");
                if (group == null) return;

                Definition defTotal = GetOrCreateDef(group, ParamTotalObra, SpecTypeId.Int.Integer, GuidTotalObra, "Cantidad total de ejemplares de este tipo en todo el modelo del proyecto");
                Definition defUbicaciones = GetOrCreateDef(group, ParamUbicacionesObra, SpecTypeId.String.Text, GuidUbicacionesObra, "Resumen de niveles y ubicaciones del tipo en el proyecto");

                var definitions = new[] { defTotal, defUbicaciones }.Where(d => d != null).ToList();

                CategorySet catSet = app.Create.NewCategorySet();
                var catsToBind = new[] { category, BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows, BuiltInCategory.OST_Assemblies };

                foreach (var bic in catsToBind.Distinct())
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

                    if (bindingMap.Contains(def))
                    {
                        if (bindingMap.get_Item(def) is InstanceBinding existingBinding)
                        {
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
                                bindingMap.ReInsert(def, existingBinding, GroupTypeId.Data);
                            }
                        }
                    }
                    else
                    {
                        InstanceBinding newBinding = app.Create.NewInstanceBinding(catSet);
                        bindingMap.Insert(def, newBinding, GroupTypeId.Data);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AssemblyBridgeService] Aviso al asegurar parámetros puente: {ex.Message}");
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

            try
            {
                using (StreamWriter sw = File.CreateText(path))
                {
                    sw.WriteLine("# Archivo de Parámetros Compartidos para Ensamblajes ARCA REVIEW");
                    sw.WriteLine("*META\tVERSION\tMINVERSION");
                    sw.WriteLine("META\t2\t1");
                    sw.WriteLine("*GROUP\tID\tNAME");
                    sw.WriteLine("GROUP\t1\tArcaBridge");
                    sw.WriteLine("*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE");
                    sw.WriteLine($"PARAM\t{GuidTotalObra}\t{ParamTotalObra}\tINTEGER\t\t1\t1\tCantidad total de ejemplares de este tipo en todo el modelo del proyecto\t1\t0");
                    sw.WriteLine($"PARAM\t{GuidUbicacionesObra}\t{ParamUbicacionesObra}\tTEXT\t\t1\t1\tResumen de niveles y ubicaciones del tipo en el proyecto\t1\t0");
                }
            }
            catch { }
        }

        /// <summary>
        /// Recorre todo el modelo del proyecto, cuenta el total de ejemplares por cada Tipo de familia,
        /// calcula el resumen de ubicaciones/niveles y estampa estos valores en los elementos de los ensamblajes.
        /// </summary>
        public static (int updatedCount, List<string> logs) SyncBridgeData(Document doc, BuiltInCategory category = BuiltInCategory.OST_Doors)
        {
            var logs = new List<string>();
            int updatedCount = 0;

            string catLabel = category == BuiltInCategory.OST_Windows ? "Ventanas" : "Puertas";

            using (Transaction t = new Transaction(doc, $"Sincronizar Parámetros Puente de {catLabel}"))
            {
                t.Start();

                // 1. Asegurar parámetros vinculados al proyecto
                EnsureBridgeParameters(doc, category);

                // 2. Obtener todos los elementos de la categoría en el proyecto
                var allElements = new FilteredElementCollector(doc)
                    .OfCategory(category)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .ToList();

                if (allElements.Count == 0)
                {
                    logs.Add($"No se encontraron elementos de {catLabel} en el modelo.");
                    t.RollBack();
                    return (0, logs);
                }

                // 3. Agrupar por Tipo de Familia y calcular conteos y ubicaciones
                var typeGroups = allElements.GroupBy(e => e.GetTypeId()).ToList();

                foreach (var g in typeGroups)
                {
                    int totalCount = g.Count();

                    // Resumen de niveles (ej: "Piso 1 (3), Piso 2 (5)")
                    var levelSummaries = g
                        .GroupBy(e => doc.GetElement(e.LevelId)?.Name ?? "Sin Nivel")
                        .Select(lg => $"{lg.Key} ({lg.Count()})");
                    string locationsStr = string.Join(", ", levelSummaries);

                    string typeName = g.First().Symbol?.Name ?? "Tipo";
                    string famName = g.First().Symbol?.FamilyName ?? catLabel;

                    // 4. Actualizar todos los elementos de este tipo (en especial los que están en ensamblajes)
                    foreach (var elem in g)
                    {
                        bool isAssMember = elem.AssemblyInstanceId != ElementId.InvalidElementId;

                        var pCount = elem.LookupParameter(ParamTotalObra);
                        if (pCount != null && !pCount.IsReadOnly)
                        {
                            pCount.Set(totalCount);
                            updatedCount++;
                        }

                        var pLoc = elem.LookupParameter(ParamUbicacionesObra);
                        if (pLoc != null && !pLoc.IsReadOnly)
                        {
                            pLoc.Set(locationsStr);
                        }

                        // Si el elemento está dentro de un ensamblaje, registrar en log
                        if (isAssMember)
                        {
                            var ass = doc.GetElement(elem.AssemblyInstanceId) as AssemblyInstance;
                            string assName = ass?.AssemblyTypeName ?? ass?.Name ?? $"ID {elem.AssemblyInstanceId}";
                            logs.Add($"  [PUENTE] Ensamblaje '{assName}' ({famName} - {typeName}): Total Obra = {totalCount} uds | {locationsStr}");
                        }
                    }
                }

                t.Commit();
            }

            logs.Insert(0, $"Sincronización completada: {updatedCount} elemento(s) actualizados con datos globales de {catLabel}.");
            return (updatedCount, logs);
        }

        /// <summary>
        /// Añade el campo de Parámetro Puente (ARCA_TotalTipo_Obra) a una tabla existente o de ensamblaje si aún no lo tiene.
        /// </summary>
        public static bool InjectBridgeFieldToSchedule(Document doc, ViewSchedule schedule, string customHeading = "Total en Obra (Global)")
        {
            if (schedule == null || !schedule.IsValidObject) return false;

            ScheduleDefinition def = schedule.Definition;
            var schedulable = def.GetSchedulableFields();

            var match = schedulable.FirstOrDefault(f =>
                f.GetName(doc).Equals(ParamTotalObra, StringComparison.OrdinalIgnoreCase) ||
                f.GetName(doc).Contains("TotalTipo_Obra"));

            if (match == null) return false;

            // Verificar si ya está en la tabla
            var existingFields = def.GetFieldOrder().Select(id => def.GetField(id)).ToList();
            if (existingFields.Any(f => f.ParameterId == match.ParameterId))
            {
                return true; // Ya está presente
            }

            try
            {
                ScheduleField field = def.AddField(match);
                if (field != null)
                {
                    field.ColumnHeading = customHeading;
                    return true;
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Clona cualquier tabla (schedule) existente del proyecto hacia los ensamblajes indicados,
        /// replicando formato, cabeceras, orden y agregando opcionalmente el parámetro puente de total global.
        /// </summary>
        public static (int createdSchedulesCount, List<string> logs) CloneScheduleToAssemblies(
            Document doc,
            ViewSchedule sourceSchedule,
            IList<AssemblyInstance> targetAssemblies,
            bool injectBridgeParam = true)
        {
            var logs = new List<string>();
            int createdCount = 0;

            if (sourceSchedule == null)
            {
                logs.Add("No se especificó la tabla de origen (molde).");
                return (0, logs);
            }

            if (targetAssemblies == null || targetAssemblies.Count == 0)
            {
                logs.Add("No se especificaron ensamblajes de destino.");
                return (0, logs);
            }

            var snapshot = AssemblyScheduleService.TakeSnapshot(doc, sourceSchedule);
            ElementId catId = snapshot.CategoryId;

            using (Transaction t = new Transaction(doc, "Clonar Tabla a Ensamblajes con Parámetro Puente"))
            {
                t.Start();

                foreach (var ai in targetAssemblies)
                {
                    if (ai == null || !ai.IsValidObject) continue;

                    string aName = ai.AssemblyTypeName ?? ai.Name;

                    try
                    {
                        // 1. Crear tabla nativa en el ensamblaje
                        ViewSchedule? newSched = null;
                        try
                        {
                            newSched = AssemblyViewUtils.CreateSingleCategorySchedule(doc, ai.Id, catId);
                        }
                        catch
                        {
                            newSched = AssemblyViewUtils.CreatePartList(doc, ai.Id);
                        }

                        if (newSched != null)
                        {
                            string sName = $"{aName} - {sourceSchedule.Name}";
                            newSched.Name = AssemblyScheduleService.GetUniqueScheduleName(doc, sName);

                            // 2. Aplicar snapshot exacto de formato, orden y títulos
                            AssemblyScheduleService.ApplySnapshotToSchedule(
                                doc,
                                newSched,
                                snapshot,
                                replicateFormat: true,
                                replicateSorting: true,
                                replicateTotals: true,
                                replicateFilters: false, // Sin filtros para permitir ver los miembros
                                replicateTemplate: true);

                            // 3. Inyectar parámetro puente si solicitado
                            if (injectBridgeParam)
                            {
                                InjectBridgeFieldToSchedule(doc, newSched);
                            }

                            createdCount++;
                            logs.Add($"Tabla clonada para '{aName}': '{newSched.Name}' con parámetro puente.");
                        }
                    }
                    catch (Exception ex)
                    {
                        logs.Add($"Error al clonar tabla para '{aName}': {ex.Message}");
                    }
                }

                t.Commit();
            }

            logs.Insert(0, $"Operación completada: {createdCount} tabla(s) de ensamblaje generadas a partir de '{sourceSchedule.Name}'.");
            return (createdCount, logs);
        }
    }
}
