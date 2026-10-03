using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace RevitToSupabasePlugin
{
    // ==========================================
    // 1. PAYLOAD DE SALIDA: META-PROMPT PARA IA
    // ==========================================
    public class DetailMetaPrompt
    {
        [JsonProperty("contexto_proyecto")]
        public ProjectContext Context { get; set; } = new ProjectContext();

        [JsonProperty("elementos_cortados_detectados")]
        public List<DetectedElementData> CutElements { get; set; } = new List<DetectedElementData>();

        [JsonProperty("nodos_criticos_identificados")]
        public List<string> CriticalNodesDetected { get; set; } = new List<string>();

        [JsonProperty("condiciones_opcionales_usuario")]
        public UserOptionalInputs OptionalInputs { get; set; } = new UserOptionalInputs();

        [JsonProperty("instrucciones_sistema_ia")]
        public string SystemInstructions { get; set; } = 
            "Evalúa la física constructiva del nodo (continuidad térmica, estanqueidad hidrófuga, barrera de vapor, " +
            "fijación mecánica y tolerancias). Devuelve un JSON conforme al esquema estandarizado con technical_compliance " +
            "y parametric_actions para aplicar en Revit.";
    }

    public class ProjectContext
    {
        [JsonProperty("nombre_vista")]
        public string ViewName { get; set; } = string.Empty;

        [JsonProperty("tipo_vista")]
        public string ViewType { get; set; } = string.Empty;

        [JsonProperty("escala_vista")]
        public int ViewScale { get; set; } = 20;

        [JsonProperty("disciplina")]
        public string Discipline { get; set; } = "Architectural";
    }

    public class DetectedElementData
    {
        [JsonProperty("element_id")]
        public long ElementId { get; set; }

        [JsonProperty("categoria")]
        public string Category { get; set; } = string.Empty;

        [JsonProperty("nombre_tipo")]
        public string TypeName { get; set; } = string.Empty;

        [JsonProperty("es_estructural")]
        public bool IsStructural { get; set; }

        [JsonProperty("capas_estratigrafia")]
        public List<LayerData> Layers { get; set; } = new List<LayerData>();
    }

    public class LayerData
    {
        [JsonProperty("funcion")]
        public string Function { get; set; } = string.Empty;

        [JsonProperty("material")]
        public string MaterialName { get; set; } = string.Empty;

        [JsonProperty("espesor_mm")]
        public double ThicknessMm { get; set; }
    }

    public class UserOptionalInputs
    {
        [JsonProperty("exposicion_climatica")]
        public string ClimateExposure { get; set; } = string.Empty;

        [JsonProperty("criterio_desempeno")]
        public string PerformanceTarget { get; set; } = string.Empty;

        [JsonProperty("notas_adicionales")]
        public string CustomNotes { get; set; } = string.Empty;
    }

    // ==========================================
    // 2. PAYLOAD DE ENTRADA: RESPUESTA DE LA IA
    // ==========================================
    public class DetailAiResponse
    {
        [JsonProperty("node_identification")]
        public NodeIdentification? NodeIdentification { get; set; }

        [JsonProperty("technical_compliance")]
        public TechnicalCompliance? TechnicalCompliance { get; set; }

        [JsonProperty("parametric_actions")]
        public ParametricActions? ParametricActions { get; set; }
    }

    public class NodeIdentification
    {
        [JsonProperty("node_type")]
        public string NodeType { get; set; } = string.Empty;

        [JsonProperty("standard_archetype_id")]
        public string StandardArchetypeId { get; set; } = string.Empty;

        [JsonProperty("confidence_score")]
        public double ConfidenceScore { get; set; } = 1.0;
    }

    public class TechnicalCompliance
    {
        [JsonProperty("summary")]
        public string Summary { get; set; } = string.Empty;

        [JsonProperty("thermal_bridge_status")]
        public string ThermalBridgeStatus { get; set; } = string.Empty;

        [JsonProperty("waterproofing_strategy")]
        public string WaterproofingStrategy { get; set; } = string.Empty;

        [JsonProperty("code_references")]
        public List<string> CodeReferences { get; set; } = new List<string>();

        [JsonProperty("site_warnings")]
        public List<string> SiteWarnings { get; set; } = new List<string>();
    }

    public class ParametricActions
    {
        [JsonProperty("native_adjustments")]
        public List<NativeElementAdjustment> NativeAdjustments { get; set; } = new List<NativeElementAdjustment>();

        [JsonProperty("detail_components")]
        public List<DetailComponentInjection> DetailComponents { get; set; } = new List<DetailComponentInjection>();
    }

    public class NativeElementAdjustment
    {
        [JsonProperty("element_id")]
        public long ElementId { get; set; }

        [JsonProperty("action")]
        public string Action { get; set; } = "ASSIGN_TYPE";

        [JsonProperty("target_type_name")]
        public string TargetTypeName { get; set; } = string.Empty;

        [JsonProperty("parameters_to_set")]
        public Dictionary<string, object> ParametersToSet { get; set; } = new Dictionary<string, object>();
    }

    public class DetailComponentInjection
    {
        [JsonProperty("family_name")]
        public string FamilyName { get; set; } = string.Empty;

        [JsonProperty("reference_element_id")]
        public long ReferenceElementId { get; set; }

        [JsonProperty("anchor_point")]
        public string AnchorPoint { get; set; } = "TOP_EXTERIOR";

        [JsonProperty("offset_x_mm")]
        public double OffsetXMm { get; set; }

        [JsonProperty("offset_y_mm")]
        public double OffsetYMm { get; set; }

        [JsonProperty("dimensions_mm")]
        public Dictionary<string, double> DimensionsMm { get; set; } = new Dictionary<string, double>();
    }
}
