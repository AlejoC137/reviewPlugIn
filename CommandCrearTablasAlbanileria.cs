using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitToSupabasePlugin
{
    [Transaction(TransactionMode.Manual)]
    public class CommandCrearTablasAlbanileria : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                using (Transaction t = new Transaction(doc, "Crear Tablas de Cómputos de Albañilería"))
                {
                    t.Start();
                    CreateAlbanileriaSchedules(doc);
                    t.Commit();

                    Autodesk.Revit.UI.TaskDialog.Show("Tablas Creadas", "Se han generado con éxito las tablas de cómputo en el Navegador de Proyectos:\n\n• 📊 Cómputo de Pegas (Mortero)\n• 📊 Cómputo de Ladrillos y Bloques");
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        public static void CreateAlbanileriaSchedules(Document doc)
        {
            ElementId categoryId = new ElementId(BuiltInCategory.OST_GenericModel);

            // 1. Crear / Obtener Tabla de Pegas (Mortero)
            ViewSchedule schedulePegas = CreateOrGetSchedule(doc, categoryId, "📊 Cómputo de Pegas (Mortero)");
            PopulatePegaScheduleFields(doc, schedulePegas);

            // 2. Crear / Obtener Tabla de Ladrillos
            ViewSchedule scheduleLadrillos = CreateOrGetSchedule(doc, categoryId, "📊 Cómputo de Ladrillos y Bloques");
            PopulateBrickScheduleFields(doc, scheduleLadrillos);
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

        private static void PopulatePegaScheduleFields(Document doc, ViewSchedule schedule)
        {
            ScheduleDefinition def = schedule.Definition;
            if (def.GetFieldOrder().Count > 0) return; // Ya tiene campos configurados

            IList<SchedulableField> schedulableFields = def.GetSchedulableFields();

            // 1. Familia y Tipo
            var fieldFam = schedulableFields.FirstOrDefault(sf => sf.ParameterId == new ElementId(BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM));
            if (fieldFam != null)
            {
                ScheduleField f = def.AddField(fieldFam);
                f.ColumnHeading = "Familia de Pega";
            }

            // 2. Comentarios / Rol (PEGA-HORIZ / PEGA-VERT)
            var fieldComm = schedulableFields.FirstOrDefault(sf => sf.ParameterId == new ElementId(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS));
            ScheduleField? fComments = null;
            if (fieldComm != null)
            {
                fComments = def.AddField(fieldComm);
                fComments.ColumnHeading = "Tipo Junta";
            }

            // 3. Cantidad (Count)
            var fieldCount = schedulableFields.FirstOrDefault(sf => sf.FieldType == ScheduleFieldType.Count);
            if (fieldCount != null)
            {
                ScheduleField f = def.AddField(fieldCount);
                f.ColumnHeading = "Cantidad Juntas";
            }

            // 4. Volumen Total (m³)
            var fieldVol = schedulableFields.FirstOrDefault(sf => sf.ParameterId == new ElementId(BuiltInParameter.HOST_VOLUME_COMPUTED) ||
                                                                  sf.GetName(doc).IndexOf("Volume", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                  sf.GetName(doc).IndexOf("Volumen", StringComparison.OrdinalIgnoreCase) >= 0);
            if (fieldVol != null)
            {
                ScheduleField f = def.AddField(fieldVol);
                f.ColumnHeading = "Volumen Mortero (m³)";
            }

            // 5. Costo Estimado ($)
            var fieldCost = schedulableFields.FirstOrDefault(sf => sf.ParameterId == new ElementId(BuiltInParameter.ALL_MODEL_COST));
            if (fieldCost != null)
            {
                ScheduleField f = def.AddField(fieldCost);
                f.ColumnHeading = "Costo ($)";
            }

            // Filtrar solo elementos que contengan "PEGA" en comentarios
            if (fComments != null)
            {
                ScheduleFilter filter = new ScheduleFilter(fComments.FieldId, ScheduleFilterType.Contains, "PEGA");
                def.AddFilter(filter);
            }

            def.IsItemized = true;
            def.ShowGrandTotal = true;
            def.ShowGrandTotalTitle = true;
            def.ShowGrandTotalCount = true;
        }

        private static void PopulateBrickScheduleFields(Document doc, ViewSchedule schedule)
        {
            ScheduleDefinition def = schedule.Definition;
            if (def.GetFieldOrder().Count > 0) return; // Ya tiene campos configurados

            IList<SchedulableField> schedulableFields = def.GetSchedulableFields();

            // 1. Familia y Tipo
            var fieldFam = schedulableFields.FirstOrDefault(sf => sf.ParameterId == new ElementId(BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM));
            if (fieldFam != null)
            {
                ScheduleField f = def.AddField(fieldFam);
                f.ColumnHeading = "Familia y Tipo de Ladrillo";
            }

            // 2. Cantidad (Count)
            var fieldCount = schedulableFields.FirstOrDefault(sf => sf.FieldType == ScheduleFieldType.Count);
            if (fieldCount != null)
            {
                ScheduleField f = def.AddField(fieldCount);
                f.ColumnHeading = "Cantidad Unidades";
            }

            // 3. Volumen Total (m³)
            var fieldVol = schedulableFields.FirstOrDefault(sf => sf.ParameterId == new ElementId(BuiltInParameter.HOST_VOLUME_COMPUTED) ||
                                                                  sf.GetName(doc).IndexOf("Volume", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                  sf.GetName(doc).IndexOf("Volumen", StringComparison.OrdinalIgnoreCase) >= 0);
            if (fieldVol != null)
            {
                ScheduleField f = def.AddField(fieldVol);
                f.ColumnHeading = "Volumen Total (m³)";
            }

            // 4. Costo ($)
            var fieldCost = schedulableFields.FirstOrDefault(sf => sf.ParameterId == new ElementId(BuiltInParameter.ALL_MODEL_COST));
            if (fieldCost != null)
            {
                ScheduleField f = def.AddField(fieldCost);
                f.ColumnHeading = "Costo ($)";
            }

            def.IsItemized = true;
            def.ShowGrandTotal = true;
            def.ShowGrandTotalTitle = true;
            def.ShowGrandTotalCount = true;
        }
    }
}
