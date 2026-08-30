using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitToSupabasePlugin
{
    public class SteelDeckType
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("rib_height_cm")]
        public double RibHeightCm { get; set; } = 5.08; // 2 pulgadas

        [JsonPropertyName("pitch_cm")]
        public double PitchCm { get; set; } = 30.48; // 12 pulgadas

        [JsonPropertyName("top_flute_width_cm")]
        public double TopFluteWidthCm { get; set; } = 15.24; // Cresta

        [JsonPropertyName("bottom_flute_width_cm")]
        public double BottomFluteWidthCm { get; set; } = 15.24; // Valle

        [JsonPropertyName("sheet_width_cm")]
        public double SheetWidthCm { get; set; } = 91.44; // Ancho útil

        [JsonPropertyName("thickness_mm")]
        public double ThicknessMm { get; set; } = 0.75; // Espesor chapa (mm)

        [JsonPropertyName("gauge")]
        public string? Gauge { get; set; } = "Calibre 22";

        [JsonPropertyName("default_concrete_topping_cm")]
        public double DefaultConcreteToppingCm { get; set; } = 5.0;

        [JsonPropertyName("steel_material")]
        public string? SteelMaterial { get; set; } = "Acero Galvanizado ASTM A653";

        [JsonPropertyName("concrete_material")]
        public string? ConcreteMaterial { get; set; } = "Hormigón - Estructural 210 kg/cm²";

        [JsonPropertyName("stud_diameter_in")]
        public double StudDiameterIn { get; set; } = 0.75; // 3/4"

        [JsonPropertyName("stud_height_cm")]
        public double StudHeightCm { get; set; } = 10.0;

        [JsonPropertyName("stud_spacing_cm")]
        public double StudSpacingCm { get; set; } = 30.48;

        [JsonPropertyName("cost_m2")]
        public double CostM2 { get; set; } = 20.0;

        [JsonPropertyName("weight_kg_m2")]
        public double WeightKgM2 { get; set; } = 9.0;
    }

    public class SteelDecksDatabase
    {
        [JsonPropertyName("decks")]
        public List<SteelDeckType>? Decks { get; set; }
    }

    [Transaction(TransactionMode.Manual)]
    public class CommandGenerarFamiliasSteelDeck : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                var window = new ManagerFamiliasSteelDeckWindow(uidoc);
                window.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
