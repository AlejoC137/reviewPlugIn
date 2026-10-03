using System;
using System.ComponentModel;
using Autodesk.Revit.DB;

namespace RevitToSupabasePlugin
{
    public enum ConversionMode
    {
        DirectShapeSystemCategory, // Reasigna la categoría de sistema manteniendo el sólido B-Rep exacto (cortes, juntas, chaflanes)
        NativeFloorElement         // Convierte el contorno horizontal a una entidad de suelo nativa de Revit (Autodesk.Revit.DB.Floor)
    }

    public class SystemCategoryItem
    {
        public string DisplayName { get; set; } = string.Empty;
        public BuiltInCategory Category { get; set; }
        public string Description { get; set; } = string.Empty;
        public string IconText { get; set; } = string.Empty;

        public override string ToString() => DisplayName;
    }

    public class ScannedElementItem : INotifyPropertyChanged
    {
        private bool _isSelected = true;

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }

        public ElementId Id { get; set; } = ElementId.InvalidElementId;
        public string IdString => Id.Value.ToString();
        public string Name { get; set; } = string.Empty;
        public string ElementTypeKind { get; set; } = string.Empty; // "DirectShape" o "FamilyInstance"
        public string CurrentCategoryName { get; set; } = string.Empty;
        public BuiltInCategory CurrentBuiltInCategory { get; set; } = BuiltInCategory.INVALID;
        public string Comments { get; set; } = string.Empty;
        public string MaterialName { get; set; } = string.Empty;
        public ElementId MaterialId { get; set; } = ElementId.InvalidElementId;
        public string LevelName { get; set; } = string.Empty;
        public ElementId LevelId { get; set; } = ElementId.InvalidElementId;
        public double VolumeM3 { get; set; } = 0.0;
        public double AreaM2 { get; set; } = 0.0;
        public bool IsPluginElement { get; set; } = false;
        public string PluginRole { get; set; } = string.Empty; // "Baldosa Entera", "Baldosa Cortada", "Mortero Base", "Cama de Pega", "Fragua / Junta", "Zócalo"

        public Element? ElementRef { get; set; }
    }
}
