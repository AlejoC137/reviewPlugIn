using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitToSupabasePlugin
{
    [Transaction(TransactionMode.Manual)]
    public class CommandCrearTablasSteelDecks : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                using (Transaction t = new Transaction(doc, "Crear Tablas de Cómputo de Steel Deck"))
                {
                    t.Start();

                    ElementId floorCatId = new ElementId(BuiltInCategory.OST_Floors);
                    ElementId studCatId = new ElementId(BuiltInCategory.OST_StructuralFraming);

                    // 1. Tabla de Lámina y Chapa Steel Deck
                    ViewSchedule schedDeck = CreateOrGetSchedule(doc, floorCatId, "📊 Cómputo de Lámina Steel Deck");
                    PopulateDeckScheduleFields(doc, schedDeck);

                    // 2. Tabla de Concreto en Losas Mixtas
                    ViewSchedule schedConcrete = CreateOrGetSchedule(doc, floorCatId, "📊 Cómputo de Concreto en Losas Steel Deck");
                    PopulateConcreteScheduleFields(doc, schedConcrete);

                    // 3. Tabla de Pernos de Cortante (Nelson Studs)
                    ViewSchedule schedStuds = CreateOrGetSchedule(doc, studCatId, "📊 Cómputo de Conectores de Cortante (Studs)");
                    PopulateStudScheduleFields(doc, schedStuds);

                    t.Commit();

                    Autodesk.Revit.UI.TaskDialog.Show(
                        "Tablas Creadas con Éxito",
                        "Se han generado con éxito las tablas de cómputo en el Navegador de Proyectos:\n\n" +
                        "• 📊 Cómputo de Lámina Steel Deck (Área m², tipo de chapa y calibre)\n" +
                        "• 📊 Cómputo de Concreto en Losas Steel Deck (Volumen m³ y capa de compresión)\n" +
                        "• 📊 Cómputo de Conectores de Cortante (Studs) (Cantidad de pernos y dimensiones)");
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

        private void PopulateDeckScheduleFields(Document doc, ViewSchedule schedule)
        {
            ScheduleDefinition def = schedule.Definition;
            if (def.GetFieldOrder().Count > 0) return;

            IList<SchedulableField> schedulableFields = def.GetSchedulableFields();

            // Nombre / Tipo
            var fieldFam = schedulableFields.FirstOrDefault(sf => sf.ParameterId == new ElementId(BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM) ||
                                                                  sf.ParameterId == new ElementId(BuiltInParameter.ALL_MODEL_TYPE_NAME));
            if (fieldFam != null)
            {
                ScheduleField f = def.AddField(fieldFam);
                f.ColumnHeading = "Perfil Steel Deck";
            }

            // Comentarios (STEELDECK-LAMINA)
            var fieldComm = schedulableFields.FirstOrDefault(sf => sf.ParameterId == new ElementId(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS));
            if (fieldComm != null)
            {
                ScheduleField f = def.AddField(fieldComm);
                f.ColumnHeading = "Tipo de Elemento";
            }

            // Cantidad
            var fieldCount = schedulableFields.FirstOrDefault(sf => sf.FieldType == ScheduleFieldType.Count);
            if (fieldCount != null)
            {
                ScheduleField f = def.AddField(fieldCount);
                f.ColumnHeading = "Cantidad";
            }

            // Área
            var fieldArea = schedulableFields.FirstOrDefault(sf => sf.ParameterId == new ElementId(BuiltInParameter.HOST_AREA_COMPUTED));
            if (fieldArea != null)
            {
                ScheduleField f = def.AddField(fieldArea);
                f.ColumnHeading = "Área Total (m²)";
            }
        }

        private void PopulateConcreteScheduleFields(Document doc, ViewSchedule schedule)
        {
            ScheduleDefinition def = schedule.Definition;
            if (def.GetFieldOrder().Count > 0) return;

            IList<SchedulableField> schedulableFields = def.GetSchedulableFields();

            // Nombre
            var fieldFam = schedulableFields.FirstOrDefault(sf => sf.ParameterId == new ElementId(BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM) ||
                                                                  sf.ParameterId == new ElementId(BuiltInParameter.ALL_MODEL_TYPE_NAME));
            if (fieldFam != null)
            {
                ScheduleField f = def.AddField(fieldFam);
                f.ColumnHeading = "Capa de Concreto";
            }

            // Material
            var fieldMat = schedulableFields.FirstOrDefault(sf => sf.ParameterId == new ElementId(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM) ||
                                                                  sf.ParameterId == new ElementId(BuiltInParameter.MATERIAL_ID_PARAM));
            if (fieldMat != null)
            {
                ScheduleField f = def.AddField(fieldMat);
                f.ColumnHeading = "Material";
            }

            // Volumen
            var fieldVol = schedulableFields.FirstOrDefault(sf => sf.ParameterId == new ElementId(BuiltInParameter.HOST_VOLUME_COMPUTED));
            if (fieldVol != null)
            {
                ScheduleField f = def.AddField(fieldVol);
                f.ColumnHeading = "Volumen (m³)";
            }
        }

        private void PopulateStudScheduleFields(Document doc, ViewSchedule schedule)
        {
            ScheduleDefinition def = schedule.Definition;
            if (def.GetFieldOrder().Count > 0) return;

            IList<SchedulableField> schedulableFields = def.GetSchedulableFields();

            // Nombre
            var fieldFam = schedulableFields.FirstOrDefault(sf => sf.ParameterId == new ElementId(BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM) ||
                                                                  sf.ParameterId == new ElementId(BuiltInParameter.ALL_MODEL_TYPE_NAME));
            if (fieldFam != null)
            {
                ScheduleField f = def.AddField(fieldFam);
                f.ColumnHeading = "Conector de Cortante";
            }

            // Comentarios
            var fieldComm = schedulableFields.FirstOrDefault(sf => sf.ParameterId == new ElementId(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS));
            if (fieldComm != null)
            {
                ScheduleField f = def.AddField(fieldComm);
                f.ColumnHeading = "Identificador";
            }

            // Cantidad
            var fieldCount = schedulableFields.FirstOrDefault(sf => sf.FieldType == ScheduleFieldType.Count);
            if (fieldCount != null)
            {
                ScheduleField f = def.AddField(fieldCount);
                f.ColumnHeading = "Cantidad (Pzas)";
            }
        }
    }
}
