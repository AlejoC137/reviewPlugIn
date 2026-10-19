using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitToSupabasePlugin
{
    [Transaction(TransactionMode.Manual)]
    public class CommandPlumbingSheet : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null || uidoc.Document == null)
            {
                message = "No hay un documento activo en Revit.";
                return Result.Failed;
            }

            Document doc = uidoc.Document;

            try
            {
                using (Transaction t = new Transaction(doc, "Crear Tablas HIDRO - Plumbing Sheet"))
                {
                    t.Start();
                    CreatePlumbingSchedules(doc);
                    t.Commit();

                    Autodesk.Revit.UI.TaskDialog.Show("Plumbing Sheet - Tablas HIDRO Creadas", 
                        "Se han generado con éxito las tablas de cómputo hidráulico/plomería en el Navegador de Proyectos:\n\n" +
                        "• 🚰 Plumbing Sheet - Cómputo General HIDRO (Multicategoría)\n" +
                        "• 🚿 Cómputo de Aparatos Sanitarios (Plumbing Fixtures)\n" +
                        "• 🧪 Cómputo de Tuberías (Pipes)\n" +
                        "• 🔌 Cómputo de Uniones y Conexiones (Pipe Fittings)\n" +
                        "• 🚰 Cómputo de Válvulas y Accesorios (Pipe Accessories)\n" +
                        "• ⚙️ Cómputo de Equipos Hidro (Plumbing Equipment)");
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        public static void CreatePlumbingSchedules(Document doc)
        {
            // 1. Tabla Multicategoría General (Plumbing Sheet)
            ViewSchedule scheduleMaster = CreateOrGetSchedule(doc, ElementId.InvalidElementId, "🚰 Plumbing Sheet - Cómputo General HIDRO");
            PopulateMasterPlumbingScheduleFields(doc, scheduleMaster);

            // 2. Tabla Aparatos Sanitarios (Plumbing Fixtures)
            ViewSchedule scheduleFixtures = CreateOrGetSchedule(doc, new ElementId(BuiltInCategory.OST_PlumbingFixtures), "🚿 Cómputo de Aparatos Sanitarios (Plumbing Fixtures)");
            PopulateFixturesScheduleFields(doc, scheduleFixtures);

            // 3. Tabla Tuberías (Pipes / PipeCurves)
            ViewSchedule schedulePipes = CreateOrGetSchedule(doc, new ElementId(BuiltInCategory.OST_PipeCurves), "🧪 Cómputo de Tuberías (Pipes)");
            PopulatePipesScheduleFields(doc, schedulePipes);

            // 4. Tabla Uniones y Conexiones (Pipe Fittings)
            ViewSchedule scheduleFittings = CreateOrGetSchedule(doc, new ElementId(BuiltInCategory.OST_PipeFitting), "🔌 Cómputo de Uniones y Conexiones (Pipe Fittings)");
            PopulateFittingsScheduleFields(doc, scheduleFittings);

            // 5. Tabla Válvulas y Accesorios (Pipe Accessories)
            ViewSchedule scheduleAccessories = CreateOrGetSchedule(doc, new ElementId(BuiltInCategory.OST_PipeAccessory), "🚰 Cómputo de Válvulas y Accesorios (Pipe Accessories)");
            PopulateAccessoriesScheduleFields(doc, scheduleAccessories);

            // 6. Tabla Equipos Hidroneumáticos / Bombas (Plumbing Equipment)
            ViewSchedule scheduleEquipment = CreateOrGetSchedule(doc, new ElementId(BuiltInCategory.OST_PlumbingEquipment), "⚙️ Cómputo de Equipos Hidro (Plumbing Equipment)");
            PopulateEquipmentScheduleFields(doc, scheduleEquipment);
        }

        private static ViewSchedule CreateOrGetSchedule(Document doc, ElementId catId, string scheduleName)
        {
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .FirstOrDefault(vs => vs.Name.Equals(scheduleName, StringComparison.OrdinalIgnoreCase));

            if (existing != null) return existing;

            ViewSchedule vsNew = ViewSchedule.CreateSchedule(doc, catId);
            vsNew.Name = scheduleName;
            return vsNew;
        }

        private static void SafeAddField(Document doc, ScheduleDefinition def, IList<SchedulableField> schedulableFields, string heading, Func<SchedulableField, bool> predicate)
        {
            var targetField = schedulableFields.FirstOrDefault(predicate);
            if (targetField != null)
            {
                try
                {
                    ScheduleField f = def.AddField(targetField);
                    if (!string.IsNullOrEmpty(heading))
                    {
                        f.ColumnHeading = heading;
                    }
                }
                catch { }
            }
        }

        // --- 1. Multicategoría Master ---
        private static void PopulateMasterPlumbingScheduleFields(Document doc, ViewSchedule schedule)
        {
            ScheduleDefinition def = schedule.Definition;
            if (def.GetFieldOrder().Count > 0) return;

            IList<SchedulableField> fields = def.GetSchedulableFields();

            SafeAddField(doc, def, fields, "Categoría", sf => sf.ParameterId == new ElementId(BuiltInParameter.ELEM_CATEGORY_PARAM));
            SafeAddField(doc, def, fields, "Familia y Tipo", sf => sf.ParameterId == new ElementId(BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM));
            SafeAddField(doc, def, fields, "Cantidad", sf => sf.FieldType == ScheduleFieldType.Count);
            SafeAddField(doc, def, fields, "Comentarios", sf => sf.ParameterId == new ElementId(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS));
            SafeAddField(doc, def, fields, "Costo ($)", sf => sf.ParameterId == new ElementId(BuiltInParameter.ALL_MODEL_COST));

            def.IsItemized = true;
            def.ShowGrandTotal = true;
            def.ShowGrandTotalTitle = true;
            def.ShowGrandTotalCount = true;
        }

        // --- 2. Aparatos Sanitarios (Plumbing Fixtures) ---
        private static void PopulateFixturesScheduleFields(Document doc, ViewSchedule schedule)
        {
            ScheduleDefinition def = schedule.Definition;
            if (def.GetFieldOrder().Count > 0) return;

            IList<SchedulableField> fields = def.GetSchedulableFields();

            SafeAddField(doc, def, fields, "Familia y Tipo", sf => sf.ParameterId == new ElementId(BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM));
            SafeAddField(doc, def, fields, "Sistema", sf => sf.ParameterId == new ElementId(BuiltInParameter.RBS_SYSTEM_NAME_PARAM) || sf.GetName(doc).IndexOf("System", StringComparison.OrdinalIgnoreCase) >= 0 || sf.GetName(doc).IndexOf("Sistema", StringComparison.OrdinalIgnoreCase) >= 0);
            SafeAddField(doc, def, fields, "Cantidad", sf => sf.FieldType == ScheduleFieldType.Count);
            SafeAddField(doc, def, fields, "Marca / Código", sf => sf.ParameterId == new ElementId(BuiltInParameter.ALL_MODEL_MARK));
            SafeAddField(doc, def, fields, "Comentarios", sf => sf.ParameterId == new ElementId(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS));
            SafeAddField(doc, def, fields, "Costo ($)", sf => sf.ParameterId == new ElementId(BuiltInParameter.ALL_MODEL_COST));

            def.IsItemized = true;
            def.ShowGrandTotal = true;
            def.ShowGrandTotalTitle = true;
            def.ShowGrandTotalCount = true;
        }

        // --- 3. Tuberías (Pipes) ---
        private static void PopulatePipesScheduleFields(Document doc, ViewSchedule schedule)
        {
            ScheduleDefinition def = schedule.Definition;
            if (def.GetFieldOrder().Count > 0) return;

            IList<SchedulableField> fields = def.GetSchedulableFields();

            SafeAddField(doc, def, fields, "Tipo de Tubería", sf => sf.ParameterId == new ElementId(BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM));
            SafeAddField(doc, def, fields, "Sistema de Tuberías", sf => sf.ParameterId == new ElementId(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM) || sf.GetName(doc).IndexOf("System", StringComparison.OrdinalIgnoreCase) >= 0 || sf.GetName(doc).IndexOf("Sistema", StringComparison.OrdinalIgnoreCase) >= 0);
            SafeAddField(doc, def, fields, "Diámetro", sf => sf.ParameterId == new ElementId(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM) || sf.GetName(doc).IndexOf("Diameter", StringComparison.OrdinalIgnoreCase) >= 0 || sf.GetName(doc).IndexOf("Diámetro", StringComparison.OrdinalIgnoreCase) >= 0);
            SafeAddField(doc, def, fields, "Longitud (m)", sf => sf.ParameterId == new ElementId(BuiltInParameter.CURVE_ELEM_LENGTH) || sf.GetName(doc).IndexOf("Length", StringComparison.OrdinalIgnoreCase) >= 0 || sf.GetName(doc).IndexOf("Longitud", StringComparison.OrdinalIgnoreCase) >= 0);
            SafeAddField(doc, def, fields, "Cantidad Segmentos", sf => sf.FieldType == ScheduleFieldType.Count);
            SafeAddField(doc, def, fields, "Comentarios", sf => sf.ParameterId == new ElementId(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS));
            SafeAddField(doc, def, fields, "Costo ($)", sf => sf.ParameterId == new ElementId(BuiltInParameter.ALL_MODEL_COST));

            def.IsItemized = true;
            def.ShowGrandTotal = true;
            def.ShowGrandTotalTitle = true;
            def.ShowGrandTotalCount = true;
        }

        // --- 4. Uniones y Conexiones (Pipe Fittings) ---
        private static void PopulateFittingsScheduleFields(Document doc, ViewSchedule schedule)
        {
            ScheduleDefinition def = schedule.Definition;
            if (def.GetFieldOrder().Count > 0) return;

            IList<SchedulableField> fields = def.GetSchedulableFields();

            SafeAddField(doc, def, fields, "Familia y Tipo", sf => sf.ParameterId == new ElementId(BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM));
            SafeAddField(doc, def, fields, "Tamaño / Diámetro", sf => sf.GetName(doc).IndexOf("Size", StringComparison.OrdinalIgnoreCase) >= 0 || sf.GetName(doc).IndexOf("Tamaño", StringComparison.OrdinalIgnoreCase) >= 0);
            SafeAddField(doc, def, fields, "Cantidad", sf => sf.FieldType == ScheduleFieldType.Count);
            SafeAddField(doc, def, fields, "Comentarios", sf => sf.ParameterId == new ElementId(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS));
            SafeAddField(doc, def, fields, "Costo ($)", sf => sf.ParameterId == new ElementId(BuiltInParameter.ALL_MODEL_COST));

            def.IsItemized = true;
            def.ShowGrandTotal = true;
            def.ShowGrandTotalTitle = true;
            def.ShowGrandTotalCount = true;
        }

        // --- 5. Válvulas y Accesorios (Pipe Accessories) ---
        private static void PopulateAccessoriesScheduleFields(Document doc, ViewSchedule schedule)
        {
            ScheduleDefinition def = schedule.Definition;
            if (def.GetFieldOrder().Count > 0) return;

            IList<SchedulableField> fields = def.GetSchedulableFields();

            SafeAddField(doc, def, fields, "Familia y Tipo", sf => sf.ParameterId == new ElementId(BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM));
            SafeAddField(doc, def, fields, "Tamaño", sf => sf.GetName(doc).IndexOf("Size", StringComparison.OrdinalIgnoreCase) >= 0 || sf.GetName(doc).IndexOf("Tamaño", StringComparison.OrdinalIgnoreCase) >= 0);
            SafeAddField(doc, def, fields, "Cantidad", sf => sf.FieldType == ScheduleFieldType.Count);
            SafeAddField(doc, def, fields, "Comentarios", sf => sf.ParameterId == new ElementId(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS));
            SafeAddField(doc, def, fields, "Costo ($)", sf => sf.ParameterId == new ElementId(BuiltInParameter.ALL_MODEL_COST));

            def.IsItemized = true;
            def.ShowGrandTotal = true;
            def.ShowGrandTotalTitle = true;
            def.ShowGrandTotalCount = true;
        }

        // --- 6. Equipos Hidro (Plumbing Equipment) ---
        private static void PopulateEquipmentScheduleFields(Document doc, ViewSchedule schedule)
        {
            ScheduleDefinition def = schedule.Definition;
            if (def.GetFieldOrder().Count > 0) return;

            IList<SchedulableField> fields = def.GetSchedulableFields();

            SafeAddField(doc, def, fields, "Familia y Tipo", sf => sf.ParameterId == new ElementId(BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM));
            SafeAddField(doc, def, fields, "Sistema", sf => sf.ParameterId == new ElementId(BuiltInParameter.RBS_SYSTEM_NAME_PARAM) || sf.GetName(doc).IndexOf("System", StringComparison.OrdinalIgnoreCase) >= 0);
            SafeAddField(doc, def, fields, "Cantidad", sf => sf.FieldType == ScheduleFieldType.Count);
            SafeAddField(doc, def, fields, "Comentarios", sf => sf.ParameterId == new ElementId(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS));
            SafeAddField(doc, def, fields, "Costo ($)", sf => sf.ParameterId == new ElementId(BuiltInParameter.ALL_MODEL_COST));

            def.IsItemized = true;
            def.ShowGrandTotal = true;
            def.ShowGrandTotalTitle = true;
            def.ShowGrandTotalCount = true;
        }
    }
}
