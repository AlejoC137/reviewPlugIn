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

                    // Asegurar que los parámetros compartidos de medición existan y estén vinculados en el proyecto
                    MeasurementManager.EnsureMeasurementParameters(doc);

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
                        "• 📊 Cómputo de Lámina Steel Deck (Área m² calculada por Medida_Area y peralte de greca)\n" +
                        "• 📊 Cómputo de Concreto en Losas Steel Deck (Volumen m³ real por Medida_Volumen y espesor de capa)\n" +
                        "• 📊 Cómputo de Conectores de Cortante (Studs) (Conteo unitario y altura de pernos)");
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

            // 1. Perfil / Tipo
            AddFieldByParam(def, schedulableFields, doc, BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM, "Perfil Steel Deck", BuiltInParameter.ALL_MODEL_TYPE_NAME);

            // 2. Comentarios
            ScheduleField? fComm = AddFieldByParam(def, schedulableFields, doc, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, "Tipo de Elemento");

            // 3. Cantidad de Planchas / Tramos
            var fieldCount = schedulableFields.FirstOrDefault(sf => sf.FieldType == ScheduleFieldType.Count);
            if (fieldCount != null)
            {
                ScheduleField f = def.AddField(fieldCount);
                f.ColumnHeading = "Cantidad";
            }

            // 4. Área Total Real de Lámina (Medida_Area de DirectShape)
            AddMeasurementField(def, schedulableFields, doc, MeasurementManager.ParamArea, "Área Total Desarrollada (m²)", BuiltInParameter.HOST_AREA_COMPUTED);

            // 5. Peralte de Cresta
            AddMeasurementField(def, schedulableFields, doc, MeasurementManager.ParamEspesor, "Peralte de Greca (cm/m)");

            // 6. Material de Acero Galvanizado
            AddFieldByParam(def, schedulableFields, doc, BuiltInParameter.MATERIAL_ID_PARAM, "Material Lámina");

            // Filtro exclusivo para no mezclar losas convencionales de piso
            if (fComm != null)
            {
                try
                {
                    ScheduleFilter filter = new ScheduleFilter(fComm.FieldId, ScheduleFilterType.Contains, "STEELDECK-LAMINA");
                    def.AddFilter(filter);
                }
                catch { }
            }

            def.ShowGrandTotal = true;
            def.ShowGrandTotalCount = true;
        }

        private void PopulateConcreteScheduleFields(Document doc, ViewSchedule schedule)
        {
            ScheduleDefinition def = schedule.Definition;
            if (def.GetFieldOrder().Count > 0) return;

            IList<SchedulableField> schedulableFields = def.GetSchedulableFields();

            AddFieldByParam(def, schedulableFields, doc, BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM, "Capa de Concreto", BuiltInParameter.ALL_MODEL_TYPE_NAME);
            ScheduleField? fComm = AddFieldByParam(def, schedulableFields, doc, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, "Identificador");

            AddFieldByParam(def, schedulableFields, doc, BuiltInParameter.STRUCTURAL_MATERIAL_PARAM, "Material Concreto", BuiltInParameter.MATERIAL_ID_PARAM);

            // Volumen Real de Concreto (Medida_Volumen de DirectShape)
            AddMeasurementField(def, schedulableFields, doc, MeasurementManager.ParamVolumen, "Volumen Real Concreto (m³)", BuiltInParameter.HOST_VOLUME_COMPUTED);

            // Espesor y Área
            AddMeasurementField(def, schedulableFields, doc, MeasurementManager.ParamEspesor, "Espesor Capa Compresión (cm)");
            AddMeasurementField(def, schedulableFields, doc, MeasurementManager.ParamArea, "Área Huella (m²)", BuiltInParameter.HOST_AREA_COMPUTED);

            // Filtro exclusivo para concreto de losa colaborante
            if (fComm != null)
            {
                try
                {
                    ScheduleFilter filter = new ScheduleFilter(fComm.FieldId, ScheduleFilterType.Contains, "STEELDECK-CONCRETO");
                    def.AddFilter(filter);
                }
                catch { }
            }

            def.ShowGrandTotal = true;
        }

        private void PopulateStudScheduleFields(Document doc, ViewSchedule schedule)
        {
            ScheduleDefinition def = schedule.Definition;
            if (def.GetFieldOrder().Count > 0) return;

            IList<SchedulableField> schedulableFields = def.GetSchedulableFields();

            AddFieldByParam(def, schedulableFields, doc, BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM, "Conector de Cortante", BuiltInParameter.ALL_MODEL_TYPE_NAME);
            ScheduleField? fComm = AddFieldByParam(def, schedulableFields, doc, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, "Identificador");

            var fieldCount = schedulableFields.FirstOrDefault(sf => sf.FieldType == ScheduleFieldType.Count);
            if (fieldCount != null)
            {
                ScheduleField f = def.AddField(fieldCount);
                f.ColumnHeading = "Cantidad (Pzas)";
            }

            // Dimensiones del Perno
            AddMeasurementField(def, schedulableFields, doc, MeasurementManager.ParamLongitud, "Altura Perno (cm)");
            AddMeasurementField(def, schedulableFields, doc, MeasurementManager.ParamAncho, "Diámetro Perno (pulg)");
            AddFieldByParam(def, schedulableFields, doc, BuiltInParameter.MATERIAL_ID_PARAM, "Material Perno");

            // Filtro exclusivo para pernos stud
            if (fComm != null)
            {
                try
                {
                    ScheduleFilter filter = new ScheduleFilter(fComm.FieldId, ScheduleFilterType.Contains, "STEELDECK-STUD");
                    def.AddFilter(filter);
                }
                catch { }
            }

            def.ShowGrandTotal = true;
            def.ShowGrandTotalCount = true;
        }

        private static ScheduleField? AddFieldByParam(ScheduleDefinition def, IList<SchedulableField> schedulableFields, Document doc, BuiltInParameter bip, string heading, BuiltInParameter? fallbackBip = null)
        {
            ElementId pid = new ElementId(bip);
            var sf = schedulableFields.FirstOrDefault(f => f.ParameterId == pid);
            if (sf == null && fallbackBip.HasValue)
            {
                ElementId fallbackId = new ElementId(fallbackBip.Value);
                sf = schedulableFields.FirstOrDefault(f => f.ParameterId == fallbackId);
            }

            if (sf != null)
            {
                ScheduleField field = def.AddField(sf);
                field.ColumnHeading = heading;
                return field;
            }
            return null;
        }

        private static ScheduleField? AddMeasurementField(ScheduleDefinition def, IList<SchedulableField> schedulableFields, Document doc, string sharedParamName, string heading, BuiltInParameter? fallbackBip = null)
        {
            var sf = schedulableFields.FirstOrDefault(f => f.GetName(doc).Equals(sharedParamName, StringComparison.OrdinalIgnoreCase));

            if (sf == null && fallbackBip.HasValue)
            {
                ElementId fid = new ElementId(fallbackBip.Value);
                sf = schedulableFields.FirstOrDefault(f => f.ParameterId == fid);
            }

            if (sf != null)
            {
                ScheduleField field = def.AddField(sf);
                field.ColumnHeading = heading;
                return field;
            }
            return null;
        }
    }
}
