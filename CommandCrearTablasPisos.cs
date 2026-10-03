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

                    // Asegurar que los parámetros compartidos de medición existan y estén vinculados en el proyecto
                    MeasurementManager.EnsureMeasurementParameters(doc);

                    // 1. Detectar en qué categorías existen elementos de enchape en el modelo actual
                    List<BuiltInCategory> targetCategories = DetectPluginCategories(doc);

                    // 2. Generar o actualizar las tablas para cada categoría activa (por defecto Suelos / OST_Floors)
                    foreach (BuiltInCategory cat in targetCategories)
                    {
                        ElementId categoryId = new ElementId(cat);
                        string suffix = cat == BuiltInCategory.OST_Walls ? " (Muros)" : "";

                        // Tabla 1: Enchapes y Baldosas
                        string tileSchedName = $"📊 Cómputo de Enchapes y Baldosas{suffix}";
                        ViewSchedule schedTiles = CreateOrGetSchedule(doc, categoryId, tileSchedName);
                        PopulateTileScheduleFields(doc, schedTiles);

                        // Tabla 2: Pega, Mortero y Fragua
                        string pegaSchedName = $"📊 Cómputo de Pega y Fragua{suffix}";
                        ViewSchedule schedPega = CreateOrGetSchedule(doc, categoryId, pegaSchedName);
                        PopulatePegaScheduleFields(doc, schedPega);

                        // Tabla 3: Zócalos y Rodapiés
                        string skirtingSchedName = $"📊 Cómputo de Zócalos y Rodapiés{suffix}";
                        ViewSchedule schedSkirting = CreateOrGetSchedule(doc, categoryId, skirtingSchedName);
                        PopulateSkirtingScheduleFields(doc, schedSkirting);
                    }

                    t.Commit();

                    Autodesk.Revit.UI.TaskDialog.Show(
                        "Tablas Creadas con Éxito",
                        "Se han generado y configurado con éxito las tablas de cómputo en el Navegador de Proyectos:\n\n" +
                        "• 📊 Cómputo de Enchapes y Baldosas (Área m² calculada por Medida_Area, piezas y cortes)\n" +
                        "• 📊 Cómputo de Pega y Fragua (Volumen m³ de mortero/fragua y tramos)\n" +
                        "• 📊 Cómputo de Zócalos y Rodapiés (Metros lineales de rodapié y despiece)\n\n" +
                        "Categorías vinculadas: " + string.Join(", ", targetCategories.Select(c => c.ToString())));
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private List<BuiltInCategory> DetectPluginCategories(Document doc)
        {
            HashSet<BuiltInCategory> detected = new HashSet<BuiltInCategory>();

            var collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType();

            foreach (Element e in collector)
            {
                string comm = e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? "";
                if (comm.StartsWith("ENCHAPE-", StringComparison.OrdinalIgnoreCase) ||
                    comm.StartsWith("BALDOSA-", StringComparison.OrdinalIgnoreCase) ||
                    comm.StartsWith("ZOCALO-", StringComparison.OrdinalIgnoreCase) ||
                    comm.StartsWith("PEGA-", StringComparison.OrdinalIgnoreCase))
                {
                    if (e.Category != null)
                    {
                        BuiltInCategory bic = (BuiltInCategory)e.Category.Id.Value;
                        detected.Add(bic);
                    }
                }
            }

            // Fallback arquitectónico: Por defecto los enchapes se modelan en Suelos (OST_Floors)
            if (detected.Count == 0)
            {
                detected.Add(BuiltInCategory.OST_Floors);
            }

            return detected.ToList();
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

            // 1. Tipo / Familia
            AddFieldByParam(def, schedulableFields, doc, BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM, "Formato de Baldosa / Tipo", BuiltInParameter.ALL_MODEL_TYPE_NAME);

            // 2. Comentarios (Rol: Entera / Cortada)
            ScheduleField? fComm = AddFieldByParam(def, schedulableFields, doc, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, "Estado / Tipo de Pieza");

            // 3. Cantidad / Recuento
            var fieldCount = schedulableFields.FirstOrDefault(sf => sf.FieldType == ScheduleFieldType.Count);
            if (fieldCount != null)
            {
                ScheduleField f = def.AddField(fieldCount);
                f.ColumnHeading = "Cantidad (Pzas)";
            }

            // 4. Área Real (Medida_Area de MeasurementManager con fallback a HOST_AREA_COMPUTED)
            AddMeasurementField(def, schedulableFields, doc, MeasurementManager.ParamArea, "Área Neta (m²)", BuiltInParameter.HOST_AREA_COMPUTED);

            // 5. Longitud y Ancho
            AddMeasurementField(def, schedulableFields, doc, MeasurementManager.ParamLongitud, "Largo (m)");
            AddMeasurementField(def, schedulableFields, doc, MeasurementManager.ParamAncho, "Ancho (m)");

            // 6. Espesor
            AddMeasurementField(def, schedulableFields, doc, MeasurementManager.ParamEspesor, "Espesor (cm/m)");

            // 7. Material
            AddFieldByParam(def, schedulableFields, doc, BuiltInParameter.MATERIAL_ID_PARAM, "Material Acabado", BuiltInParameter.STRUCTURAL_MATERIAL_PARAM);

            // 8. Filtro para aislar solo piezas de enchape del plugin
            if (fComm != null)
            {
                try
                {
                    ScheduleFilter filter = new ScheduleFilter(fComm.FieldId, ScheduleFilterType.Contains, "BALDOSA");
                    def.AddFilter(filter);
                }
                catch { }
            }

            def.ShowGrandTotal = true;
            def.ShowGrandTotalCount = true;
        }

        private void PopulatePegaScheduleFields(Document doc, ViewSchedule schedule)
        {
            ScheduleDefinition def = schedule.Definition;
            if (def.GetFieldOrder().Count > 0) return;

            IList<SchedulableField> schedulableFields = def.GetSchedulableFields();

            AddFieldByParam(def, schedulableFields, doc, BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM, "Elemento de Pega", BuiltInParameter.ALL_MODEL_TYPE_NAME);
            ScheduleField? fComm = AddFieldByParam(def, schedulableFields, doc, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, "Tipo de Capa / Fragua");

            var fieldCount = schedulableFields.FirstOrDefault(sf => sf.FieldType == ScheduleFieldType.Count);
            if (fieldCount != null)
            {
                ScheduleField f = def.AddField(fieldCount);
                f.ColumnHeading = "Tramos / Piezas";
            }

            // Volumen Real de Mortero / Fragua
            AddMeasurementField(def, schedulableFields, doc, MeasurementManager.ParamVolumen, "Volumen Mortero (m³)", BuiltInParameter.HOST_VOLUME_COMPUTED);
            AddMeasurementField(def, schedulableFields, doc, MeasurementManager.ParamEspesor, "Espesor Capa (cm/m)");
            AddFieldByParam(def, schedulableFields, doc, BuiltInParameter.MATERIAL_ID_PARAM, "Material Adhesivo / Mortero");

            if (fComm != null)
            {
                try
                {
                    ScheduleFilter filter = new ScheduleFilter(fComm.FieldId, ScheduleFilterType.Contains, "PEGA");
                    def.AddFilter(filter);
                }
                catch { }
            }

            def.ShowGrandTotal = true;
        }

        private void PopulateSkirtingScheduleFields(Document doc, ViewSchedule schedule)
        {
            ScheduleDefinition def = schedule.Definition;
            if (def.GetFieldOrder().Count > 0) return;

            IList<SchedulableField> schedulableFields = def.GetSchedulableFields();

            AddFieldByParam(def, schedulableFields, doc, BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM, "Tipo de Zócalo / Rodapié", BuiltInParameter.ALL_MODEL_TYPE_NAME);
            ScheduleField? fComm = AddFieldByParam(def, schedulableFields, doc, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, "Elemento");

            var fieldCount = schedulableFields.FirstOrDefault(sf => sf.FieldType == ScheduleFieldType.Count);
            if (fieldCount != null)
            {
                ScheduleField f = def.AddField(fieldCount);
                f.ColumnHeading = "Cantidad Tramos";
            }

            // Longitud acumulada en metros lineales
            AddMeasurementField(def, schedulableFields, doc, MeasurementManager.ParamLongitud, "Longitud Total (m)");
            AddMeasurementField(def, schedulableFields, doc, MeasurementManager.ParamEspesor, "Altura / Peralte (cm)");
            AddFieldByParam(def, schedulableFields, doc, BuiltInParameter.MATERIAL_ID_PARAM, "Material Zócalo");

            if (fComm != null)
            {
                try
                {
                    ScheduleFilter filter = new ScheduleFilter(fComm.FieldId, ScheduleFilterType.Contains, "ZOCALO");
                    def.AddFilter(filter);
                }
                catch { }
            }

            def.ShowGrandTotal = true;
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
            // Buscar por el nombre del Parámetro Compartido oficial de MeasurementManager
            var sf = schedulableFields.FirstOrDefault(f => f.GetName(doc).Equals(sharedParamName, StringComparison.OrdinalIgnoreCase));

            // Fallback a BuiltInParameter nativo si existiera
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
