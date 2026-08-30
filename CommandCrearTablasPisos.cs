using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitToSupabasePlugin
{
    [Transaction(TransactionMode.Manual)]
    public class CommandCrearTablasPisos : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                using (Transaction t = new Transaction(doc, "Crear Tablas de Cómputos de Enchapes"))
                {
                    t.Start();

                    ElementId categoryId = new ElementId(BuiltInCategory.OST_GenericModel);

                    // 1. Crear / Obtener Tabla de Enchapes (Pisos y Muros)
                    ViewSchedule schedTiles = CreateOrGetSchedule(doc, categoryId, "📊 Cómputo de Enchapes y Baldosas");
                    PopulateTileScheduleFields(doc, schedTiles);

                    // 2. Crear / Obtener Tabla de Pega y Fragua
                    ViewSchedule schedPega = CreateOrGetSchedule(doc, categoryId, "📊 Cómputo de Pega y Fragua");
                    PopulatePegaScheduleFields(doc, schedPega);

                    // 3. Crear / Obtener Tabla de Zócalos y Rodapiés
                    ViewSchedule schedSkirting = CreateOrGetSchedule(doc, categoryId, "📊 Cómputo de Zócalos y Rodapiés");
                    PopulateSkirtingScheduleFields(doc, schedSkirting);

                    t.Commit();

                    Autodesk.Revit.UI.TaskDialog.Show(
                        "Tablas Creadas con Éxito",
                        "Se han generado con éxito las tablas de cómputo en el Navegador de Proyectos:\n\n" +
                        "• 📊 Cómputo de Enchapes y Baldosas (Piezas enteras, cortes y áreas)\n" +
                        "• 📊 Cómputo de Pega y Fragua (Cama base y juntas 3D)\n" +
                        "• 📊 Cómputo de Zócalos y Rodapiés (Metros lineales y materiales)");
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private ViewSchedule CreateOrGetSchedule(Document doc, ElementId catId, string scheduleName)
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

        private void PopulateTileScheduleFields(Document doc, ViewSchedule schedule)
        {
            ScheduleDefinition def = schedule.Definition;
            if (def.GetFieldOrder().Count > 0) return;

            IList<SchedulableField> schedulableFields = def.GetSchedulableFields();

            // 1. Familia y Tipo
            var fieldFam = schedulableFields.FirstOrDefault(sf => sf.ParameterId == new ElementId(BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM));
            if (fieldFam != null)
            {
                ScheduleField f = def.AddField(fieldFam);
                f.ColumnHeading = "Formato de Baldosa / Enchape";
            }

            // 2. Comentarios
            var fieldComm = schedulableFields.FirstOrDefault(sf => sf.ParameterId == new ElementId(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS));
            if (fieldComm != null)
            {
                ScheduleField f = def.AddField(fieldComm);
                f.ColumnHeading = "Tipo de Pieza";
            }

            // 3. Cantidad
            var fieldCount = schedulableFields.FirstOrDefault(sf => sf.FieldType == ScheduleFieldType.Count);
            if (fieldCount != null)
            {
                ScheduleField f = def.AddField(fieldCount);
                f.ColumnHeading = "Cantidad (Pzas)";
            }

            // 4. Volumen / Área
            var fieldVol = schedulableFields.FirstOrDefault(sf => sf.ParameterId == new ElementId(BuiltInParameter.HOST_VOLUME_COMPUTED) ||
                                                                  sf.GetName(doc).IndexOf("Volume", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                  sf.GetName(doc).IndexOf("Volumen", StringComparison.OrdinalIgnoreCase) >= 0);
            if (fieldVol != null)
            {
                ScheduleField f = def.AddField(fieldVol);
                f.ColumnHeading = "Volumen (m³)";
            }
        }

        private void PopulatePegaScheduleFields(Document doc, ViewSchedule schedule)
        {
            ScheduleDefinition def = schedule.Definition;
            if (def.GetFieldOrder().Count > 0) return;

            IList<SchedulableField> schedulableFields = def.GetSchedulableFields();

            var fieldFam = schedulableFields.FirstOrDefault(sf => sf.ParameterId == new ElementId(BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM));
            if (fieldFam != null)
            {
                ScheduleField f = def.AddField(fieldFam);
                f.ColumnHeading = "Elemento de Pega";
            }

            var fieldComm = schedulableFields.FirstOrDefault(sf => sf.ParameterId == new ElementId(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS));
            if (fieldComm != null)
            {
                ScheduleField f = def.AddField(fieldComm);
                f.ColumnHeading = "Tipo de Pega / Fragua";
            }

            var fieldCount = schedulableFields.FirstOrDefault(sf => sf.FieldType == ScheduleFieldType.Count);
            if (fieldCount != null)
            {
                ScheduleField f = def.AddField(fieldCount);
                f.ColumnHeading = "Tramos";
            }
        }

        private void PopulateSkirtingScheduleFields(Document doc, ViewSchedule schedule)
        {
            ScheduleDefinition def = schedule.Definition;
            if (def.GetFieldOrder().Count > 0) return;

            IList<SchedulableField> schedulableFields = def.GetSchedulableFields();

            var fieldFam = schedulableFields.FirstOrDefault(sf => sf.ParameterId == new ElementId(BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM));
            if (fieldFam != null)
            {
                ScheduleField f = def.AddField(fieldFam);
                f.ColumnHeading = "Tipo de Zócalo";
            }

            var fieldComm = schedulableFields.FirstOrDefault(sf => sf.ParameterId == new ElementId(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS));
            if (fieldComm != null)
            {
                ScheduleField f = def.AddField(fieldComm);
                f.ColumnHeading = "Elemento";
            }

            var fieldCount = schedulableFields.FirstOrDefault(sf => sf.FieldType == ScheduleFieldType.Count);
            if (fieldCount != null)
            {
                ScheduleField f = def.AddField(fieldCount);
                f.ColumnHeading = "Tramos";
            }
        }
    }
}
