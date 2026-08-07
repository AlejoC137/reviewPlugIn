using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitToSupabasePlugin
{
    public class BrickType
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        
        [JsonPropertyName("width_cm")]
        public double WidthCm { get; set; }
        
        [JsonPropertyName("height_cm")]
        public double HeightCm { get; set; }
        
        [JsonPropertyName("length_cm")]
        public double LengthCm { get; set; }

        [JsonPropertyName("material")]
        public string? Material { get; set; }
        
        [JsonPropertyName("has_mortar")]
        public bool HasMortar { get; set; }
        
        [JsonPropertyName("has_trims")]
        public bool HasTrims { get; set; }
        
        [JsonPropertyName("has_dovela")]
        public bool HasDovela { get; set; }
        
        [JsonPropertyName("extrusion_template")]
        public string? ExtrusionTemplate { get; set; } // "H" for Horizontal, "V" for Vertical

        [JsonPropertyName("has_canchado")]
        public bool HasCanchado { get; set; }

        [JsonPropertyName("profile_path")]
        public string? ProfilePath { get; set; }

        [JsonPropertyName("has_dovelas")]
        public bool HasDovelas { get; set; }

        [JsonPropertyName("has_esquinas")]
        public bool HasEsquinas { get; set; }

        [JsonPropertyName("profile_orientation")]
        public string? ProfileOrientation { get; set; }

        [JsonPropertyName("profile_offset_x")]
        public double ProfileOffsetX { get; set; }

        [JsonPropertyName("profile_offset_y")]
        public double ProfileOffsetY { get; set; }

        [JsonPropertyName("profile_rotation")]
        public double ProfileRotation { get; set; }
    }

    public class BricksDatabase
    {
        [JsonPropertyName("bricks")]
        public List<BrickType>? Bricks { get; set; }
    }

    [Transaction(TransactionMode.Manual)]
    public class CommandGenerarFamiliasLadrillos : IExternalCommand
    {
        public Result Execute(
          ExternalCommandData commandData,
          ref string message,
          ElementSet elements)
        {
            try
            {
                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                var window = new ManagerFamiliasLadrilloWindow(uidoc);
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
