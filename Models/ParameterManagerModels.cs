using System;
using System.Collections.Generic;
using System.ComponentModel;
using Autodesk.Revit.DB;

namespace RevitToSupabasePlugin.Models
{
    public enum ParameterOriginKind
    {
        Project,    // Parámetro de Proyecto (No-Shared, InternalDefinition)
        Shared,     // Parámetro Compartido (SharedParameterElement con GUID)
        Global,     // Parámetro Global (GlobalParameter)
        Family,     // Parámetro de Familia (.rfa)
        BuiltIn     // Parámetro Nativo del Sistema (BuiltInParameter)
    }

    public class ProjectParameterItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        private string _newName = string.Empty;
        private string _statusText = "Listo";

        public long ParameterElementId { get; set; } = -1;
        public string Name { get; set; } = string.Empty;
        public ParameterOriginKind Origin { get; set; } = ParameterOriginKind.Project;

        public string OriginText => Origin switch
        {
            ParameterOriginKind.Project => "Proyecto (No-Shared)",
            ParameterOriginKind.Shared => "Compartido (Shared)",
            ParameterOriginKind.Global => "Global",
            ParameterOriginKind.Family => "Familia (.rfa)",
            ParameterOriginKind.BuiltIn => "Nativo (Built-In)",
            _ => "Desconocido"
        };

        public string GuidText { get; set; } = "—";
        public bool IsShared => Origin == ParameterOriginKind.Shared;
        public bool IsInstance { get; set; } = true;
        public string ScopeText => Origin == ParameterOriginKind.Global
            ? "Global"
            : (IsInstance ? "Instancia (Ejemplar)" : "Tipo (ElementType)");

        public string DataTypeLabel { get; set; } = "Texto";
        public string SpecTypeIdRaw { get; set; } = string.Empty;
        public string GroupTypeIdRaw { get; set; } = string.Empty;
        public string GroupLabel { get; set; } = "Datos";
        public StorageType StorageType { get; set; } = StorageType.String;

        public List<long> BoundCategoryIds { get; set; } = new List<long>();
        public List<string> BoundCategoryNames { get; set; } = new List<string>();
        public string CategoriesSummary => BoundCategoryNames.Count == 0
            ? "—"
            : (BoundCategoryNames.Count <= 3
                ? string.Join(", ", BoundCategoryNames)
                : $"{string.Join(", ", BoundCategoryNames.GetRange(0, 3))} (+{BoundCategoryNames.Count - 3})");

        public int PopulatedElementsCount { get; set; }
        public int TotalElementsInBoundCategories { get; set; }
        public string UsageSummary => TotalElementsInBoundCategories > 0
            ? $"{PopulatedElementsCount}/{TotalElementsInBoundCategories} ({(PopulatedElementsCount * 100 / Math.Max(1, TotalElementsInBoundCategories))}%)"
            : $"{PopulatedElementsCount} elem.";

        /// <summary>
        /// True si es un parámetro compartido en el modelo cuyo GUID no existe en el archivo .txt activo.
        /// </summary>
        public bool IsOrphanInTxt { get; set; }

        public string TxtLinkStatus => Origin != ParameterOriginKind.Shared
            ? "No aplica"
            : (IsOrphanInTxt ? "⚠️ Huérfano (No en .TXT)" : "✅ En .TXT activo");

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

        public string NewName
        {
            get => string.IsNullOrWhiteSpace(_newName) ? Name : _newName;
            set
            {
                if (_newName != value)
                {
                    _newName = value ?? string.Empty;
                    OnPropertyChanged(nameof(NewName));
                }
            }
        }

        public string StatusText
        {
            get => _statusText;
            set
            {
                if (_statusText != value)
                {
                    _statusText = value ?? string.Empty;
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }

    public class SharedParamTxtItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        public string GroupName { get; set; } = "General";
        public string Name { get; set; } = string.Empty;
        public Guid Guid { get; set; } = Guid.Empty;
        public string GuidString => Guid == Guid.Empty ? "—" : Guid.ToString();
        public string DataTypeLabel { get; set; } = "TEXT";
        public string SpecTypeIdRaw { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool Visible { get; set; } = true;
        public bool UserModifiable { get; set; } = true;
        public bool HideWhenNoValue { get; set; } = false;
        public bool ExistsInActiveDoc { get; set; }

        public string DocPresenceText => ExistsInActiveDoc ? "✅ Cargado en Doc" : "⬜ Solo en .TXT";

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
        protected void OnPropertyChanged(string propName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }

    public class CloudSharedParamFileRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string FileName { get; set; } = "ARCA_SharedParameters.txt";
        public string ProfileTitle { get; set; } = "Estándar General ARCA";
        public string ProjectName { get; set; } = string.Empty;
        public string AuthorEmail { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        public string UpdatedAtText => UpdatedAt.ToString("yyyy-MM-dd HH:mm");
        public int ParamCount { get; set; }
        public int GroupCount { get; set; }
        public string ContentTxt { get; set; } = string.Empty;
        public string SourceKind { get; set; } = "Supabase Cloud";
        public override string ToString() => $"{ProfileTitle} ({FileName}) [{ParamCount} params]";
    }

    public class ElementParameterValueRow : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        private string _currentTargetValue = string.Empty;
        private string _previewNewValue = string.Empty;
        private string _status = "Sin cambios";

        public long ElementId { get; set; }
        public bool IsElementType { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string FamilyAndType { get; set; } = string.Empty;
        public string LevelName { get; set; } = "—";
        public string MarkOrCode { get; set; } = string.Empty;
        public string SourceValue { get; set; } = string.Empty;

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

        public string CurrentTargetValue
        {
            get => _currentTargetValue;
            set
            {
                if (_currentTargetValue != value)
                {
                    _currentTargetValue = value ?? string.Empty;
                    OnPropertyChanged(nameof(CurrentTargetValue));
                }
            }
        }

        public string PreviewNewValue
        {
            get => _previewNewValue;
            set
            {
                if (_previewNewValue != value)
                {
                    _previewNewValue = value ?? string.Empty;
                    OnPropertyChanged(nameof(PreviewNewValue));
                    OnPropertyChanged(nameof(HasPendingChange));
                }
            }
        }

        public bool HasPendingChange => !string.Equals(_currentTargetValue ?? "", _previewNewValue ?? "", StringComparison.Ordinal);

        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value ?? string.Empty;
                    OnPropertyChanged(nameof(Status));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }

    public class OpenRevitDocItem : INotifyPropertyChanged
    {
        private bool _isSelectedForSync;

        public Document? DocumentRef { get; set; }
        public string Title { get; set; } = string.Empty;
        public string PathName { get; set; } = string.Empty;
        public bool IsFamilyDocument { get; set; }
        public bool IsActiveDoc { get; set; }

        public string DocKindLabel => IsFamilyDocument ? "🧩 Familia (.rfa)" : "🏢 Proyecto (.rvt)";
        public string DisplayTitle => IsActiveDoc ? $"★ {Title} [ACTIVO]" : $"{Title} ({(IsFamilyDocument ? ".rfa" : ".rvt")})";

        public bool IsSelectedForSync
        {
            get => _isSelectedForSync;
            set
            {
                if (_isSelectedForSync != value)
                {
                    _isSelectedForSync = value;
                    OnPropertyChanged(nameof(IsSelectedForSync));
                }
            }
        }

        public override string ToString() => DisplayTitle;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }

    public class CategoryChoiceItem : INotifyPropertyChanged
    {
        private bool _isChecked;
        public long CategoryId { get; set; }
        public string Name { get; set; } = string.Empty;

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    OnPropertyChanged(nameof(IsChecked));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}
