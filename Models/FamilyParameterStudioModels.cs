using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Autodesk.Revit.DB;

namespace RevitToSupabasePlugin.Models
{
    /// <summary>
    /// Resumen de una familia cargable en el proyecto (.rvt) para el listado lateral.
    /// </summary>
    public class LoadableFamilySummaryItem : INotifyPropertyChanged
    {
        private int _nestedCount = -1;
        private bool _isScanning;

        public long FamilyIdValue { get; set; }
        public string Name { get; set; } = string.Empty;
        public string CategoryName { get; set; } = "General";
        public bool IsShared { get; set; }
        public int TypeCount { get; set; }
        public int PlacedInstanceCount { get; set; }

        public int NestedCount
        {
            get => _nestedCount;
            set
            {
                if (_nestedCount != value)
                {
                    _nestedCount = value;
                    OnPropertyChanged(nameof(NestedCount));
                    OnPropertyChanged(nameof(NestedSummaryBadge));
                    OnPropertyChanged(nameof(HasNestedFamilies));
                }
            }
        }

        public bool IsScanning
        {
            get => _isScanning;
            set
            {
                if (_isScanning != value)
                {
                    _isScanning = value;
                    OnPropertyChanged(nameof(IsScanning));
                    OnPropertyChanged(nameof(NestedSummaryBadge));
                }
            }
        }

        public bool HasNestedFamilies => NestedCount > 0;

        public string NestedSummaryBadge
        {
            get
            {
                if (IsScanning) return "Escaneando...";
                if (NestedCount < 0) return "Sin escanear";
                if (NestedCount == 0) return "Sin anidadas";
                return $"{NestedCount} anidada(s)";
            }
        }

        public string MetaLine => $"{CategoryName} · {TypeCount} tipo(s) · {PlacedInstanceCount} ej.";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string prop) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    /// <summary>
    /// Nodo jerárquico dentro del árbol de una familia cargable y sus familias anidadas.
    /// </summary>
    public class NestedFamilyNode : INotifyPropertyChanged
    {
        private bool _isExpanded = true;
        private bool _isSelected;

        public string NodeId { get; set; } = Guid.NewGuid().ToString();
        public long FamilyIdValue { get; set; }
        public string Name { get; set; } = string.Empty;
        public string CategoryName { get; set; } = "General";
        public bool IsRoot { get; set; }
        public bool IsShared { get; set; }
        public int Depth { get; set; }
        public int InstanceCountInParent { get; set; }
        public string ActiveTypeName { get; set; } = string.Empty;
        public string PathBreadcrumb { get; set; } = string.Empty;
        public string? CycleWarning { get; set; }

        public List<long> SampleInstanceIds { get; set; } = new List<long>();
        public List<FamilyTypeSummaryItem> Types { get; set; } = new List<FamilyTypeSummaryItem>();
        public List<NativeParameterItem> Parameters { get; set; } = new List<NativeParameterItem>();
        public List<NestedAssociationItem> Associations { get; set; } = new List<NestedAssociationItem>();

        public ObservableCollection<NestedFamilyNode> Children { get; set; } = new ObservableCollection<NestedFamilyNode>();

        public string NodeIcon => IsRoot ? "📦" : (Children.Count > 0 ? "🧩" : "🔹");

        public string NodeBadgeText
        {
            get
            {
                if (IsRoot)
                    return $"{CategoryName} · Raíz .RFA · {Types.Count} tipo(s)";

                string sharedLabel = IsShared ? "Compartida" : "No compartida";
                return $"{CategoryName} · {sharedLabel} · {InstanceCountInParent} ej.";
            }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged(nameof(IsExpanded));
                }
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string prop) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    /// <summary>
    /// Tipo de familia y resumen de sus valores clave.
    /// </summary>
    public class FamilyTypeSummaryItem
    {
        public string TypeName { get; set; } = string.Empty;
        public bool IsCurrent { get; set; }
        public string CurrentBadge => IsCurrent ? "★ Activo" : "";
        public int ParameterValueCount { get; set; }
        public string PreviewValues { get; set; } = string.Empty;
    }

    /// <summary>
    /// Representación unificada y nativa de un parámetro en Revit (Built-In, Familia, Proyecto, Compartido, Global).
    /// </summary>
    public class NativeParameterItem : INotifyPropertyChanged
    {
        private string _currentValueDisplay = string.Empty;
        private string _formula = string.Empty;
        private string _groupLabel = string.Empty;
        private bool _isReporting;

        public long ParameterIdValue { get; set; } = -1;
        public int BuiltInParamInt { get; set; } = 0;
        public string Name { get; set; } = string.Empty;
        public ParameterOriginKind Origin { get; set; } = ParameterOriginKind.Project;

        public string OriginBadge => Origin switch
        {
            ParameterOriginKind.BuiltIn => "Nativo Revit",
            ParameterOriginKind.Family => "Familia (.rfa)",
            ParameterOriginKind.Project => "Proyecto",
            ParameterOriginKind.Shared => "Compartido",
            ParameterOriginKind.Global => "Global",
            _ => "Otro"
        };

        public bool IsInstance { get; set; }
        public string ScopeBadge => Origin == ParameterOriginKind.Global
            ? "Global"
            : (IsInstance ? "Ejemplar" : "Tipo");

        public string DataTypeLabel { get; set; } = "Texto";
        public string SpecTypeIdRaw { get; set; } = string.Empty;

        public string GroupLabel
        {
            get => _groupLabel;
            set
            {
                if (_groupLabel != value)
                {
                    _groupLabel = value;
                    OnPropertyChanged(nameof(GroupLabel));
                }
            }
        }

        public string GroupTypeIdRaw { get; set; } = string.Empty;
        public StorageType StorageType { get; set; } = StorageType.String;
        public bool IsShared => Origin == ParameterOriginKind.Shared;
        public string GuidText { get; set; } = "—";
        public bool IsReadOnly { get; set; }
        public bool HasFormula => !string.IsNullOrWhiteSpace(Formula);

        public string CurrentValueDisplay
        {
            get => _currentValueDisplay;
            set
            {
                if (_currentValueDisplay != value)
                {
                    _currentValueDisplay = value;
                    OnPropertyChanged(nameof(CurrentValueDisplay));
                }
            }
        }

        public string Formula
        {
            get => _formula;
            set
            {
                if (_formula != value)
                {
                    _formula = value ?? string.Empty;
                    OnPropertyChanged(nameof(Formula));
                    OnPropertyChanged(nameof(HasFormula));
                    OnPropertyChanged(nameof(FormulaOrValueSummary));
                }
            }
        }

        public bool IsReporting
        {
            get => _isReporting;
            set
            {
                if (_isReporting != value)
                {
                    _isReporting = value;
                    OnPropertyChanged(nameof(IsReporting));
                }
            }
        }

        public string FormulaOrValueSummary => HasFormula ? $"= {Formula}" : CurrentValueDisplay;

        public string AssociatedToHostParam { get; set; } = string.Empty;
        public int AssociatedNestedCount { get; set; }
        public string AssociationSummary => !string.IsNullOrWhiteSpace(AssociatedToHostParam)
            ? $"🔗 {AssociatedToHostParam}"
            : (AssociatedNestedCount > 0 ? $"⇄ {AssociatedNestedCount} vínculo(s)" : "—");

        // Capacidades nativas de edición según el contexto de Revit
        public bool CanEditValue { get; set; }
        public bool CanEditFormula { get; set; }
        public bool CanRename { get; set; }
        public bool CanToggleScope { get; set; }
        public bool CanEditGroup { get; set; }
        public bool CanToggleReporting { get; set; }
        public bool CanConvertToShared { get; set; }
        public bool CanDelete { get; set; }
        public string EditabilityHint { get; set; } = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string prop) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    /// <summary>
    /// Vínculo entre un parámetro de una familia anidada y un parámetro de la familia anfitriona (.rfa).
    /// </summary>
    public class NestedAssociationItem : INotifyPropertyChanged
    {
        private string _hostFamilyParamName = "— Sin asociar —";

        public long NestedInstanceIdValue { get; set; }
        public string NestedFamilyName { get; set; } = string.Empty;
        public string NestedParamName { get; set; } = string.Empty;
        public bool IsNestedTypeParam { get; set; }
        public string NestedScopeText => IsNestedTypeParam ? "Tipo" : "Ejemplar";
        public string DataTypeLabel { get; set; } = string.Empty;
        public string SpecTypeIdRaw { get; set; } = string.Empty;
        public string CurrentValue { get; set; } = string.Empty;

        public string HostFamilyParamName
        {
            get => _hostFamilyParamName;
            set
            {
                if (_hostFamilyParamName != value)
                {
                    _hostFamilyParamName = value ?? "— Sin asociar —";
                    OnPropertyChanged(nameof(HostFamilyParamName));
                    OnPropertyChanged(nameof(IsAssociated));
                    OnPropertyChanged(nameof(StatusBadge));
                }
            }
        }

        public bool IsAssociated => !string.IsNullOrWhiteSpace(HostFamilyParamName) && HostFamilyParamName != "— Sin asociar —";
        public string StatusBadge => IsAssociated ? "🔗 Asociado" : "○ Libre";

        public List<string> CompatibleHostParams { get; set; } = new List<string>();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string prop) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    /// <summary>
    /// Opción legible de Grupo o Tipo de Dato nativo de Revit.
    /// </summary>
    public class RevitForgeOption
    {
        public string Label { get; set; } = string.Empty;
        public ForgeTypeId TypeId { get; set; } = new ForgeTypeId();
        public string RawId => TypeId?.TypeId ?? string.Empty;

        public override string ToString() => Label;
    }
}
