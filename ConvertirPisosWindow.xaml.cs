using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitToSupabasePlugin
{
    public partial class ConvertirPisosWindow : Window
    {
        public bool IsConfirmed { get; private set; } = false;

        public TileType? SelectedTile { get; private set; }
        public FloorPatternType SelectedPattern { get; private set; } = FloorPatternType.OrthogonalGrid;

        // Capas a Modelar
        public bool ModelRevoque { get; private set; } = false;
        public double RevoqueThickCm { get; private set; } = 3.0;
        public Autodesk.Revit.DB.Material? RevoqueMaterial { get; private set; }

        public bool ModelBeddingMortar { get; private set; } = false;
        public double BeddingThickMm { get; private set; } = 5.0;
        public Autodesk.Revit.DB.Material? BeddingMaterial { get; private set; }

        public bool ModelTiles { get; private set; } = true;
        public double TileLengthCm { get; private set; } = 60.0;
        public double TileWidthCm { get; private set; } = 60.0;
        public double TileThickCm { get; private set; } = 1.0;
        public Autodesk.Revit.DB.Material? TileMaterial { get; private set; }

        public bool AutoAbsorbFloorThickness { get; private set; } = true;
        public bool DeleteOriginalFloor { get; private set; } = false;
        public double TotalFloorThicknessCm { get; private set; } = 10.0;

        public bool GenerateJointMesh { get; private set; } = true;
        public double JointInnerMm { get; private set; } = 3.0;
        public double JointPerimMm { get; private set; } = 5.0;
        public Autodesk.Revit.DB.Material? GroutMaterial { get; private set; }

        // Muros
        public bool FullWallHeight { get; private set; } = true;
        public double CustomWallHeightCm { get; private set; } = 210.0;
        public double WallBaseOffsetCm { get; private set; } = 0.0;

        // Zócalos
        public bool EnableSkirting { get; private set; } = false;
        public double SkirtingHeightCm { get; private set; } = 8.0;
        public double SkirtingThickCm { get; private set; } = 1.2;
        public double SkirtingBevelMm { get; private set; } = 1.5;
        public Autodesk.Revit.DB.Material? SkirtingMaterial { get; private set; }

        public double RotationDeg { get; private set; } = 0.0;
        public double OffsetXCm { get; private set; } = 0.0;
        public double OffsetYCm { get; private set; } = 0.0;

        public BuiltInCategory TargetSystemCategory { get; private set; } = BuiltInCategory.OST_Floors;
        public bool PreferDirectShapeGeometry { get; private set; } = false;

        public List<TileLayoutItem> LayoutTiles { get; private set; } = new List<TileLayoutItem>();
        public List<JointLayoutItem> LayoutJoints { get; private set; } = new List<JointLayoutItem>();
        public List<SkirtingItem> SkirtingSegments { get; private set; } = new List<SkirtingItem>();

        public List<TilingHostInfo> SelectedHosts { get; private set; } = new List<TilingHostInfo>();

        private Document? _doc;
        private List<TileType> _availableTiles = new List<TileType>();
        private List<Autodesk.Revit.DB.Material> _materials = new List<Autodesk.Revit.DB.Material>();

        // Variables de Cámara 3D
        private System.Windows.Point _lastMousePosition;
        private double _cameraAngleX = 45;
        private double _cameraAngleY = 35;
        private double _cameraDistance = 250;
        private bool _isDragging = false;
        private bool _isUpdating = false;

        public ConvertirPisosWindow(List<TilingHostInfo> selectedHosts, Document? doc)
        {
            InitializeComponent();
            _doc = doc;
            SelectedHosts = selectedHosts ?? new List<TilingHostInfo>();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadMaterials();
            LoadFaceBasedTileFamilies();
            AutoPopulateFromHostLayers();
            UpdateHostSummary();
            RecalculateLayoutAnd3D();
        }

        private void LoadMaterials()
        {
            try
            {
                if (_doc == null) return;
                _materials = new FilteredElementCollector(_doc)
                    .OfClass(typeof(Autodesk.Revit.DB.Material))
                    .Cast<Autodesk.Revit.DB.Material>()
                    .OrderBy(m => m.Name)
                    .ToList();

                cmbTileMaterial.ItemsSource = _materials;
                cmbRevoqueMaterial.ItemsSource = _materials;
                cmbBeddingMaterial.ItemsSource = _materials;
                cmbGroutMaterial.ItemsSource = _materials;
                cmbSkirtingMaterial.ItemsSource = _materials;

                if (_materials.Count > 0)
                {
                    cmbTileMaterial.SelectedItem = _materials.FirstOrDefault(m => m.Name.IndexOf("Porcelanato", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                                 m.Name.IndexOf("Ceram", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                                 m.Name.IndexOf("Piso", StringComparison.OrdinalIgnoreCase) >= 0) ?? _materials[0];

                    cmbRevoqueMaterial.SelectedItem = _materials.FirstOrDefault(m => m.Name.IndexOf("Revoque", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                                    m.Name.IndexOf("Yeso", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                                    m.Name.IndexOf("Mortero", StringComparison.OrdinalIgnoreCase) >= 0) ?? _materials[0];

                    cmbBeddingMaterial.SelectedItem = _materials.FirstOrDefault(m => m.Name.IndexOf("Pega", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                                    m.Name.IndexOf("Mortero", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                                    m.Name.IndexOf("Adhesivo", StringComparison.OrdinalIgnoreCase) >= 0) ?? _materials[0];

                    cmbGroutMaterial.SelectedItem = _materials.FirstOrDefault(m => m.Name.IndexOf("Fragua", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                                  m.Name.IndexOf("Grout", StringComparison.OrdinalIgnoreCase) >= 0) ?? _materials[0];

                    cmbSkirtingMaterial.SelectedItem = _materials.FirstOrDefault(m => m.Name.IndexOf("Madera", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                                     m.Name.IndexOf("Zocalo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                                     m.Name.IndexOf("Ceram", StringComparison.OrdinalIgnoreCase) >= 0) ?? _materials[0];
                }
            }
            catch { }
        }

        private void LoadFaceBasedTileFamilies()
        {
            _availableTiles.Clear();

            if (_doc != null)
            {
                // 1. Cargar exclusivamente 'Tiles Base.rfa' desde C:\Users\Alejandro\Documents\GitHub\reviewPlugIn\Familias\Floors-Tiles\
                var family = new FilteredElementCollector(_doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .FirstOrDefault(f => f.Name.Equals("Tiles Base", StringComparison.OrdinalIgnoreCase) ||
                                         f.Name.Equals("Base Tiles", StringComparison.OrdinalIgnoreCase));

                if (family == null)
                {
                    string explicitPath = @"C:\Users\Alejandro\Documents\GitHub\reviewPlugIn\Familias\Floors-Tiles\Tiles Base.rfa";
                    string addinFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
                    string[] searchPaths = new[]
                    {
                        explicitPath,
                        Path.Combine(addinFolder, "Familias", "Floors-Tiles", "Tiles Base.rfa"),
                        Path.Combine(addinFolder, "Tiles Base.rfa"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Autodesk\Revit\Addins\2027\Familias\Floors-Tiles", "Tiles Base.rfa")
                    };

                    string? foundPath = searchPaths.FirstOrDefault(File.Exists);
                    if (foundPath != null)
                    {
                        try
                        {
                            using (Transaction tLoad = new Transaction(_doc, "Cargar Tiles Base"))
                            {
                                tLoad.Start();
                                _doc.LoadFamily(foundPath, out family);
                                tLoad.Commit();
                            }
                        }
                        catch { }
                    }
                }

                // 2. Obtener exclusivamente los tipos (símbolos) de la familia 'Tiles Base'
                var tileSymbols = new FilteredElementCollector(_doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(s => s.FamilyName.Equals("Tiles Base", StringComparison.OrdinalIgnoreCase) ||
                                s.FamilyName.Equals("Base Tiles", StringComparison.OrdinalIgnoreCase) ||
                                (family != null && s.Family.Id.Value == family.Id.Value))
                    .ToList();

                foreach (var symbol in tileSymbols)
                {
                    if (!symbol.IsActive)
                    {
                        try
                        {
                            using (Transaction tAct = new Transaction(_doc, "Activar Símbolo Baldosa"))
                            {
                                tAct.Start();
                                symbol.Activate();
                                tAct.Commit();
                            }
                        }
                        catch { }
                    }

                    double l = GetSymbolParam(symbol, new[] { "Lenght", "Length", "Largo", "Depth", "Longitud", "Longitud_Baldosa" }, 60.0);
                    double w = GetSymbolParam(symbol, new[] { "Width", "Ancho", "Ancho_Baldosa" }, 60.0);
                    double th = GetSymbolParam(symbol, new[] { "Thickness", "Height", "Espesor", "Alto", "Espesor_Baldosa" }, 1.0);

                    _availableTiles.Add(new TileType
                    {
                        Id = symbol.Id.ToString(),
                        Name = $"{symbol.FamilyName} - {symbol.Name}",
                        LengthCm = l,
                        WidthCm = w,
                        ThicknessCm = th,
                        FamilySymbol = symbol
                    });
                }
            }

            // Si no se encuentra en disco, proveer el tipo base por defecto que enlazará con Tiles Base.rfa
            if (_availableTiles.Count == 0)
            {
                _availableTiles.Add(new TileType
                {
                    Id = "tiles_base_default",
                    Name = "Tiles Base (Face-Based Generic Model)",
                    LengthCm = 60.0,
                    WidthCm = 60.0,
                    ThicknessCm = 1.0
                });
            }

            cmbTileType.ItemsSource = null;
            cmbTileType.ItemsSource = _availableTiles.Select(t => new { Tile = t, Display = $"{t.Name} ({t.LengthCm}x{t.WidthCm}cm)" }).ToList();
            cmbTileType.DisplayMemberPath = "Display";
            if (cmbTileType.Items.Count > 0) cmbTileType.SelectedIndex = 0;
        }

        private double GetSymbolParam(FamilySymbol symbol, string[] names, double defaultCm)
        {
            foreach (var name in names)
            {
                Parameter p = symbol.LookupParameter(name);
                if (p != null && p.AsDouble() > 0)
                {
                    return p.AsDouble() * 30.48; // Pies a cm
                }
            }
            return defaultCm;
        }

        private void AutoPopulateFromHostLayers()
        {
            if (SelectedHosts.Count == 0) return;

            var firstHost = SelectedHosts[0];
            TotalFloorThicknessCm = firstHost.TotalHostThicknessCm > 0 ? firstHost.TotalHostThicknessCm : 10.0;
            var layers = firstHost.DetectedLayers;

            chkModelRevoque.IsChecked = layers.HasRevoque && layers.SuggestedRevoqueCm > 0;
            if (layers.HasRevoque && layers.SuggestedRevoqueCm > 0)
            {
                txtRevoqueThickCm.Text = layers.SuggestedRevoqueCm.ToString("0.##");
                if (!string.IsNullOrEmpty(layers.SuggestedRevoqueMaterial) && cmbRevoqueMaterial.ItemsSource is List<Autodesk.Revit.DB.Material> mats)
                {
                    var mat = mats.FirstOrDefault(m => m.Name.IndexOf(layers.SuggestedRevoqueMaterial, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (mat != null) cmbRevoqueMaterial.SelectedItem = mat;
                }
            }

            chkModelBeddingMortar.IsChecked = layers.HasPega && layers.SuggestedPegaMm > 0;
            if (layers.HasPega && layers.SuggestedPegaMm > 0)
            {
                txtBeddingThickMm.Text = layers.SuggestedPegaMm.ToString("0.##");
                if (!string.IsNullOrEmpty(layers.SuggestedPegaMaterial) && cmbBeddingMaterial.ItemsSource is List<Autodesk.Revit.DB.Material> mats)
                {
                    var mat = mats.FirstOrDefault(m => m.Name.IndexOf(layers.SuggestedPegaMaterial, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (mat != null) cmbBeddingMaterial.SelectedItem = mat;
                }
            }

            if (chkEnableSkirting != null) chkEnableSkirting.IsChecked = false;

            if (layers.HasFinish && layers.SuggestedFinishCm > 0)
            {
                txtTileThick.Text = layers.SuggestedFinishCm.ToString("0.##");
                if (!string.IsNullOrEmpty(layers.SuggestedFinishMaterial) && cmbTileMaterial.ItemsSource is List<Autodesk.Revit.DB.Material> mats)
                {
                    var mat = mats.FirstOrDefault(m => m.Name.IndexOf(layers.SuggestedFinishMaterial, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (mat != null) cmbTileMaterial.SelectedItem = mat;
                }
            }

            UpdateStratigraphy();
        }

        private void UpdateStratigraphy()
        {
            if (_isUpdating) return;
            _isUpdating = true;
            try
            {
                double tileThick = 1.0;
                if (txtTileThick != null && double.TryParse(txtTileThick.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double tt))
                    tileThick = Math.Max(0.2, tt);

                double beddingMm = 5.0;
                if (txtBeddingThickMm != null && double.TryParse(txtBeddingThickMm.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double bt))
                    beddingMm = Math.Max(0.5, bt);

                double beddingCm = beddingMm / 10.0;

                if (chkAutoAbsorbFloorThickness != null && chkAutoAbsorbFloorThickness.IsChecked == true)
                {
                    double morteroCm = Math.Max(0.1, TotalFloorThicknessCm - tileThick - beddingCm);
                    RevoqueThickCm = morteroCm;
                    if (txtRevoqueThickCm != null)
                    {
                        txtRevoqueThickCm.Text = morteroCm.ToString("0.##");
                        txtRevoqueThickCm.IsEnabled = false;
                    }
                }
                else
                {
                    if (txtRevoqueThickCm != null)
                    {
                        txtRevoqueThickCm.IsEnabled = true;
                        if (double.TryParse(txtRevoqueThickCm.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double rt))
                            RevoqueThickCm = Math.Max(0.1, rt);
                    }
                }

                double totalLayersCm = (chkModelRevoque?.IsChecked == true ? RevoqueThickCm : 0.0) +
                                       (chkModelBeddingMortar?.IsChecked == true ? beddingCm : 0.0) +
                                       (chkModelTiles?.IsChecked == true ? tileThick : 0.0);

                if (txtStratigraphyBreakdown != null)
                {
                    txtStratigraphyBreakdown.Text = $"Estratigrafía: Top Enchape en N.P.T. | Mortero: {RevoqueThickCm:0.##} cm | Pega: {beddingMm:0.#} mm | Baldosa: {tileThick:0.##} cm (Suelo: {TotalFloorThicknessCm:0.##} cm | Capas: {totalLayersCm:0.##} cm)";
                }
            }
            finally
            {
                _isUpdating = false;
            }
        }

        private void AutoAbsorb_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdating) return;
            UpdateStratigraphy();
            RecalculateLayoutAnd3D();
        }

        private void BeddingParam_Changed(object sender, TextChangedEventArgs e)
        {
            if (_isUpdating) return;
            UpdateStratigraphy();
            RecalculateLayoutAnd3D();
        }

        private void RevoqueParam_Changed(object sender, TextChangedEventArgs e)
        {
            if (_isUpdating) return;
            UpdateStratigraphy();
            RecalculateLayoutAnd3D();
        }

        private void UpdateHostSummary()
        {
            if (SelectedHosts.Count == 0)
            {
                txtFloorSummary.Text = "No hay elementos seleccionados.";
                txtFloorDimensions.Text = "Área: 0.00 m² | Longitud: 0.00 m";
                return;
            }

            int floorCount = SelectedHosts.Count(h => h.HostType == HostElementType.Floor);
            int wallCount = SelectedHosts.Count(h => h.HostType == HostElementType.Wall);

            string hostDesc = "";
            if (floorCount > 0 && wallCount > 0) hostDesc = $"{floorCount} Suelo(s) y {wallCount} Muro(s)";
            else if (floorCount > 0) hostDesc = $"{floorCount} Suelo(s)";
            else hostDesc = $"{wallCount} Muro(s)";

            double totalArea = SelectedHosts.Sum(f => f.AreaSqM);
            double totalPerimeter = SelectedHosts.Sum(f => f.PerimeterM);

            txtFloorSummary.Text = $"Anfitriones: {hostDesc}";
            txtFloorDimensions.Text = $"Área Total: {totalArea:0.00} m² | Longitud/Perímetro: {totalPerimeter:0.00} m";

            grpWallParams.Visibility = wallCount > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            grpSkirting.Visibility = floorCount > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        }

        private void cmbTileType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbTileType.SelectedItem != null)
            {
                dynamic item = cmbTileType.SelectedItem;
                SelectedTile = item.Tile as TileType;
                if (SelectedTile != null)
                {
                    txtTileLength.Text = SelectedTile.LengthCm.ToString("0.##");
                    txtTileWidth.Text = SelectedTile.WidthCm.ToString("0.##");
                    txtTileThick.Text = SelectedTile.ThicknessCm.ToString("0.##");
                }
            }
            UpdateStratigraphy();
            RecalculateLayoutAnd3D();
        }

        private void TileParam_Changed(object sender, TextChangedEventArgs e)
        {
            if (_isUpdating || SelectedTile == null) return;

            if (double.TryParse(txtTileLength.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double l))
                SelectedTile.LengthCm = Math.Max(5.0, l);

            if (double.TryParse(txtTileWidth.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double w))
                SelectedTile.WidthCm = Math.Max(5.0, w);

            if (double.TryParse(txtTileThick.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double th))
                SelectedTile.ThicknessCm = Math.Max(0.2, th);

            UpdateStratigraphy();
            RecalculateLayoutAnd3D();
        }

        private void WallParam_Changed(object sender, RoutedEventArgs e)
        {
            if (txtCustomWallHeight != null)
            {
                txtCustomWallHeight.IsEnabled = chkFullWallHeight.IsChecked != true;
            }
            ParamControl_Changed(sender, e);
        }

        private void ParamControl_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdating) return;
            RecalculateLayoutAnd3D();
        }

        private void ReadParametersFromUI()
        {
            if (cmbPattern.SelectedItem is ComboBoxItem pItem && pItem.Tag != null)
            {
                if (Enum.TryParse(pItem.Tag.ToString(), out FloorPatternType pat))
                    SelectedPattern = pat;
            }

            AutoAbsorbFloorThickness = chkAutoAbsorbFloorThickness.IsChecked == true;
            DeleteOriginalFloor = chkDeleteOriginalFloor.IsChecked == true;

            ModelRevoque = chkModelRevoque.IsChecked == true;
            if (double.TryParse(txtRevoqueThickCm.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double rt))
                RevoqueThickCm = Math.Max(0.1, rt);
            RevoqueMaterial = cmbRevoqueMaterial.SelectedItem as Autodesk.Revit.DB.Material;

            ModelBeddingMortar = chkModelBeddingMortar.IsChecked == true;
            if (double.TryParse(txtBeddingThickMm.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double bt))
                BeddingThickMm = Math.Max(0.5, bt);
            BeddingMaterial = cmbBeddingMaterial.SelectedItem as Autodesk.Revit.DB.Material;

            ModelTiles = chkModelTiles.IsChecked == true;
            if (double.TryParse(txtTileLength.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double tl))
                TileLengthCm = Math.Max(5.0, tl);
            if (double.TryParse(txtTileWidth.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double tw))
                TileWidthCm = Math.Max(5.0, tw);
            if (double.TryParse(txtTileThick.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double tt))
                TileThickCm = Math.Max(0.2, tt);
            TileMaterial = cmbTileMaterial.SelectedItem as Autodesk.Revit.DB.Material;

            GenerateJointMesh = chkGenerateJointMesh.IsChecked == true;
            if (double.TryParse(txtJointInner.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double ji))
                JointInnerMm = Math.Max(0.5, ji);
            if (double.TryParse(txtJointPerimeter.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double jp))
                JointPerimMm = Math.Max(0.0, jp);
            GroutMaterial = cmbGroutMaterial.SelectedItem as Autodesk.Revit.DB.Material;

            // Muros
            FullWallHeight = chkFullWallHeight.IsChecked == true;
            if (double.TryParse(txtCustomWallHeight.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double wh))
                CustomWallHeightCm = Math.Max(10.0, wh);

            if (double.TryParse(txtWallBaseOffset.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double wbo))
                WallBaseOffsetCm = Math.Max(0.0, wbo);

            // Zócalos
            EnableSkirting = chkEnableSkirting.IsChecked == true;
            if (double.TryParse(txtSkirtingHeight.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double sh))
                SkirtingHeightCm = Math.Max(1.0, sh);

            if (double.TryParse(txtSkirtingThickness.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double st))
                SkirtingThickCm = Math.Max(0.5, st);

            if (double.TryParse(txtSkirtingBevel.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double sb))
                SkirtingBevelMm = Math.Max(0.0, sb);

            SkirtingMaterial = cmbSkirtingMaterial.SelectedItem as Autodesk.Revit.DB.Material;

            if (double.TryParse(txtRotation.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double rot))
                RotationDeg = rot;

            if (double.TryParse(txtOffsetX.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double ox))
                OffsetXCm = ox;

            if (double.TryParse(txtOffsetY.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double oy))
                OffsetYCm = oy;

            PreferDirectShapeGeometry = rbDirectShape != null && rbDirectShape.IsChecked == true;
        }

        private void RecalculateLayoutAnd3D()
        {
            if (SelectedHosts.Count == 0) return;
            ReadParametersFromUI();

            LayoutTiles.Clear();
            LayoutJoints.Clear();
            SkirtingSegments.Clear();

            double tileLFt = (TileLengthCm / 100.0) * 3.28084;
            double tileWFt = (TileWidthCm / 100.0) * 3.28084;
            double tileHFt = (TileThickCm / 100.0) * 3.28084;

            double jointInnerFt = (JointInnerMm / 1000.0) * 3.28084;
            double jointPerimFt = (JointPerimMm / 1000.0) * 3.28084;

            double revoqueFt = ModelRevoque ? (RevoqueThickCm / 100.0) * 3.28084 : 0.0;
            double beddingFt = ModelBeddingMortar ? (BeddingThickMm / 1000.0) * 3.28084 : 0.0;

            double rotRad = RotationDeg * Math.PI / 180.0;
            double offXFt = (OffsetXCm / 100.0) * 3.28084;
            double offYFt = (OffsetYCm / 100.0) * 3.28084;

            double totalCutTilesArea = 0;
            double totalFullTilesArea = 0;
            int fullCount = 0;
            int cutCount = 0;

            foreach (var host in SelectedHosts)
            {
                if (host.OuterBoundaryUV.Count < 3) continue;

                List<XYZ> effectiveBoundaryUV = new List<XYZ>(host.OuterBoundaryUV);

                if (host.HostType == HostElementType.Wall && !FullWallHeight)
                {
                    double maxCustomV = host.MinV + (WallBaseOffsetCm / 100.0) * 3.28084 + (CustomWallHeightCm / 100.0) * 3.28084;
                    effectiveBoundaryUV = ClipPolygonToVMax(effectiveBoundaryUV, maxCustomV);
                }

                if (effectiveBoundaryUV.Count < 3) continue;

                // Para pisos: la cara superior del enchape queda en el N.P.T. (ElevationFt), por lo que el offset base es -tileHFt
                // Para muros: el enchape se coloca a partir del revoque + pega
                double normalTileOffset = (host.HostType == HostElementType.Floor) ? 0.0 : (revoqueFt + beddingFt);

                // Calcular contorno interior de baldosas (restando la junta perimetral)
                bool hasPerimJoint = GenerateJointMesh && jointPerimFt > 0.0005;
                List<XYZ> tilingBoundaryUV = hasPerimJoint ? InsetPolygon2D(effectiveBoundaryUV, jointPerimFt) : effectiveBoundaryUV;

                if (tilingBoundaryUV.Count < 3) tilingBoundaryUV = effectiveBoundaryUV;

                host.TilingBoundaryUV = tilingBoundaryUV;
                host.TilingBoundary3D = tilingBoundaryUV.Select(uv => host.LocalToWorld(uv, 0.0)).ToList();

                // Generar Zócalo perimetral si el anfitrión es un Piso (apoyado sobre N.P.T. / ElevationFt)
                if (host.HostType == HostElementType.Floor && EnableSkirting)
                {
                    double skHeightFt = (SkirtingHeightCm / 100.0) * 3.28084;
                    double skThickFt = (SkirtingThickCm / 100.0) * 3.28084;
                    double skBevelFt = (SkirtingBevelMm / 1000.0) * 3.28084;
                    double baseElevation = host.ElevationFt; // Apoyado exactamente sobre N.P.T.

                    for (int i = 0; i < host.OuterBoundary3D.Count; i++)
                    {
                        XYZ pt1 = host.OuterBoundary3D[i];
                        XYZ pt2 = host.OuterBoundary3D[(i + 1) % host.OuterBoundary3D.Count];
                        SkirtingSegments.Add(new SkirtingItem
                        {
                            StartPoint = pt1,
                            EndPoint = pt2,
                            HeightFt = skHeightFt,
                            ThicknessFt = skThickFt,
                            BevelFt = skBevelFt,
                            BaseElevationFt = baseElevation,
                            MaterialName = SkirtingMaterial?.Name ?? "Zócalo"
                        });
                    }
                }

                // Generar Junta Perimetral 3D (Banda exacta entre el borde del piso y el área de baldosas, CERO solapamiento)
                if (hasPerimJoint && tilingBoundaryUV.Count == effectiveBoundaryUV.Count)
                {
                    int nEdges = effectiveBoundaryUV.Count;
                    for (int i = 0; i < nEdges; i++)
                    {
                        XYZ p1_uv = effectiveBoundaryUV[i];
                        XYZ p2_uv = effectiveBoundaryUV[(i + 1) % nEdges];
                        XYZ q2_uv = tilingBoundaryUV[(i + 1) % nEdges];
                        XYZ q1_uv = tilingBoundaryUV[i];

                        XYZ p1 = host.LocalToWorld(p1_uv, normalTileOffset);
                        XYZ p2 = host.LocalToWorld(p2_uv, normalTileOffset);
                        XYZ q2 = host.LocalToWorld(q2_uv, normalTileOffset);
                        XYZ q1 = host.LocalToWorld(q1_uv, normalTileOffset);

                        List<XYZ> perimPoly3D = new List<XYZ> { p1, p2, q2, q1 };
                        XYZ centerPerim = (p1 + p2 + q2 + q1) / 4.0;
                        XYZ segDir = (p2 - p1);
                        double len = segDir.GetLength();

                        LayoutJoints.Add(new JointLayoutItem
                        {
                            IsPerimeter = true,
                            Center3D = centerPerim,
                            StartPoint3D = (p1 + q1) / 2.0,
                            EndPoint3D = (p2 + q2) / 2.0,
                            Normal3D = host.Normal3D,
                            RotationRad = Math.Atan2(segDir.Y, segDir.X),
                            LengthFt = len,
                            WidthFt = jointPerimFt,
                            DepthFt = tileHFt,
                            PolygonVertices3D = perimPoly3D,
                            MaterialName = GroutMaterial?.Name ?? "Fragua",
                            HostFaceRef = host.HostFaceReference
                        });
                    }
                }

                if (host.HostType == HostElementType.Floor)
                {
                    double spanU = host.MaxU - host.MinU;
                    double spanV = host.MaxV - host.MinV;
                    if (spanU <= 0.01 || spanV <= 0.01) continue;

                    bool hasPega = GenerateJointMesh && jointInnerFt > 0.0005;
                    bool hasPerim = hasPerimJoint;
                    double startOffsetU = (hasPerim ? jointPerimFt : 0.0) + offXFt;
                    double startOffsetV = (hasPerim ? jointPerimFt : 0.0) + offYFt;
                    double maxU = spanU - (hasPerim ? jointPerimFt : 0.0);
                    double maxV = spanV - (hasPerim ? jointPerimFt : 0.0);

                    double currentV = startOffsetV;
                    int rowIndex = 0;
                    var rowGroups = new List<List<TileLayoutItem>>();

                    while (currentV < maxV - 0.0001)
                    {
                        double remainingV = maxV - currentV;
                        double itemV = Math.Min(tileWFt, remainingV);
                        bool isCutTop = (remainingV < tileWFt - 0.001);
                        double topCut = isCutTop ? (tileWFt - itemV) : 0.0;
                        if (itemV <= 0.001) break;

                        var thisRow = new List<TileLayoutItem>();

                        // Traba / Desfase entre filas (RunningBond / ThirdBond)
                        double rowShiftU = 0.0;
                        if (SelectedPattern == FloorPatternType.RunningBond50)
                        {
                            if (rowIndex % 2 != 0) rowShiftU = (tileLFt - (hasPega ? jointInnerFt : 0.0)) / 2.0;
                        }
                        else if (SelectedPattern == FloorPatternType.ThirdBond33)
                        {
                            rowShiftU = (rowIndex % 3) * ((tileLFt - (hasPega ? jointInnerFt : 0.0)) / 3.0);
                        }

                        if (rowShiftU > 0.001)
                        {
                            double firstLen = Math.Min(rowShiftU, maxU - startOffsetU);
                            if (firstLen > 0.005)
                            {
                                double firstLatCut = tileLFt - firstLen;
                                XYZ corner3D = host.LocalToWorld(new XYZ(startOffsetU, currentV, 0.0), normalTileOffset);
                                XYZ center3D = host.LocalToWorld(new XYZ(startOffsetU + firstLen / 2.0, currentV + itemV / 2.0, 0.0), normalTileOffset);

                                XYZ ft_p0 = host.LocalToWorld(new XYZ(startOffsetU, currentV, 0.0), normalTileOffset);
                                XYZ ft_p1 = host.LocalToWorld(new XYZ(startOffsetU + firstLen, currentV, 0.0), normalTileOffset);
                                XYZ ft_p2 = host.LocalToWorld(new XYZ(startOffsetU + firstLen, currentV + itemV, 0.0), normalTileOffset);
                                XYZ ft_p3 = host.LocalToWorld(new XYZ(startOffsetU, currentV + itemV, 0.0), normalTileOffset);

                                var firstTile = new TileLayoutItem
                                {
                                    IndexX = -1,
                                    IndexY = rowIndex,
                                    CenterU_Ft = startOffsetU + firstLen / 2.0,
                                    CenterV_Ft = currentV + itemV / 2.0,
                                    Center3D = center3D,
                                    Corner3D = corner3D,
                                    Normal3D = host.Normal3D,
                                    DirectionU3D = host.BasisU,
                                    DirectionV3D = host.BasisV,
                                    Length_Ft = firstLen,
                                    Width_Ft = itemV,
                                    Thickness_Ft = tileHFt,
                                    RotationRad = 0.0,
                                    IsCut = true,
                                    CutLat_Ft = firstLatCut,
                                    CutTop_Ft = topCut,
                                    ActualLength_Ft = firstLen,
                                    ActualWidth_Ft = itemV,
                                    Role = TileRole.CutEdge,
                                    ActualAreaSqM = firstLen * itemV * 0.092903,
                                    PolygonVertices3D = new List<XYZ> { ft_p0, ft_p1, ft_p2, ft_p3 },
                                    HostFaceRef = host.HostFaceReference
                                };
                                LayoutTiles.Add(firstTile);
                                thisRow.Add(firstTile);
                                cutCount++;
                                totalCutTilesArea += (firstLen * itemV * 0.092903);
                            }
                        }

                        double currentU = startOffsetU + (rowShiftU > 0.001 ? (rowShiftU + (hasPega ? jointInnerFt : 0.0)) : 0.0);
                        int colIndex = 0;

                        while (currentU < maxU - 0.0001)
                        {
                            double remainingU = maxU - currentU;
                            double itemU = Math.Min(tileLFt, remainingU);
                            bool isCutLat = (remainingU < tileLFt - 0.001);
                            double latCut = isCutLat ? (tileLFt - itemU) : 0.0;
                            if (itemU <= 0.001) break;

                            XYZ corner3D = host.LocalToWorld(new XYZ(currentU, currentV, 0.0), normalTileOffset);
                            XYZ center3D = host.LocalToWorld(new XYZ(currentU + itemU / 2.0, currentV + itemV / 2.0, 0.0), normalTileOffset);

                            bool isCut = isCutLat || isCutTop;
                            TileRole role = isCut ? ((isCutLat && isCutTop) ? TileRole.Corner : TileRole.CutEdge) : TileRole.Full;

                            if (isCut)
                            {
                                cutCount++;
                                totalCutTilesArea += (itemU * itemV * 0.092903);
                            }
                            else
                            {
                                fullCount++;
                                totalFullTilesArea += (itemU * itemV * 0.092903);
                            }

                            XYZ ti_p0 = host.LocalToWorld(new XYZ(currentU, currentV, 0.0), normalTileOffset);
                            XYZ ti_p1 = host.LocalToWorld(new XYZ(currentU + itemU, currentV, 0.0), normalTileOffset);
                            XYZ ti_p2 = host.LocalToWorld(new XYZ(currentU + itemU, currentV + itemV, 0.0), normalTileOffset);
                            XYZ ti_p3 = host.LocalToWorld(new XYZ(currentU, currentV + itemV, 0.0), normalTileOffset);

                            var tileItem = new TileLayoutItem
                            {
                                IndexX = colIndex,
                                IndexY = rowIndex,
                                CenterU_Ft = currentU + itemU / 2.0,
                                CenterV_Ft = currentV + itemV / 2.0,
                                Center3D = center3D,
                                Corner3D = corner3D,
                                Normal3D = host.Normal3D,
                                DirectionU3D = host.BasisU,
                                DirectionV3D = host.BasisV,
                                Length_Ft = itemU,
                                Width_Ft = itemV,
                                Thickness_Ft = tileHFt,
                                RotationRad = 0.0,
                                IsCut = isCut,
                                CutLat_Ft = latCut,
                                CutTop_Ft = topCut,
                                ActualLength_Ft = itemU,
                                ActualWidth_Ft = itemV,
                                Role = role,
                                ActualAreaSqM = itemU * itemV * 0.092903,
                                PolygonVertices3D = new List<XYZ> { ti_p0, ti_p1, ti_p2, ti_p3 },
                                HostFaceRef = host.HostFaceReference
                            };
                            LayoutTiles.Add(tileItem);
                            thisRow.Add(tileItem);

                            currentU += (itemU + (hasPega ? jointInnerFt : 0.0));
                            colIndex++;
                        }

                        if (thisRow.Count > 0) rowGroups.Add(thisRow);

                        currentV += (itemV + (hasPega ? jointInnerFt : 0.0));
                        rowIndex++;
                    }

                    // Generar Fraguas / Juntas (Idéntico a CommandConvertirMurosALadrillos)
                    if (hasPega && rowGroups.Count > 0)
                    {
                        // 1. Juntas Horizontales entre filas
                        for (int r = 0; r < rowGroups.Count - 1; r++)
                        {
                            var rItems = rowGroups[r];
                            if (rItems.Count == 0) continue;

                            double vMaxOfRow = rItems.Max(t => t.CenterV_Ft + t.Width_Ft / 2.0);
                            double hLen = maxU - startOffsetU;
                            XYZ jStart3D = host.LocalToWorld(new XYZ(startOffsetU, vMaxOfRow, 0.0), normalTileOffset);
                            XYZ jEnd3D = host.LocalToWorld(new XYZ(maxU, vMaxOfRow, 0.0), normalTileOffset);

                            XYZ jh0 = host.LocalToWorld(new XYZ(startOffsetU, vMaxOfRow, 0.0), normalTileOffset);
                            XYZ jh1 = host.LocalToWorld(new XYZ(maxU, vMaxOfRow, 0.0), normalTileOffset);
                            XYZ jh2 = host.LocalToWorld(new XYZ(maxU, vMaxOfRow + jointInnerFt, 0.0), normalTileOffset);
                            XYZ jh3 = host.LocalToWorld(new XYZ(startOffsetU, vMaxOfRow + jointInnerFt, 0.0), normalTileOffset);

                            LayoutJoints.Add(new JointLayoutItem
                            {
                                IsVertical = false,
                                StartPoint3D = jStart3D,
                                EndPoint3D = jEnd3D,
                                Center3D = (jStart3D + jEnd3D) / 2.0,
                                Normal3D = host.Normal3D,
                                RotationRad = 0.0,
                                LengthFt = hLen,
                                WidthFt = jointInnerFt,
                                DepthFt = tileHFt,
                                PolygonVertices3D = new List<XYZ> { jh0, jh1, jh2, jh3 },
                                MaterialName = GroutMaterial?.Name ?? "Fragua",
                                HostFaceRef = host.HostFaceReference
                            });
                        }

                        // 2. Juntas Verticales entre baldosas de cada fila
                        for (int r = 0; r < rowGroups.Count; r++)
                        {
                            var rItems = rowGroups[r].OrderBy(t => t.CenterU_Ft).ToList();
                            for (int i = 0; i < rItems.Count - 1; i++)
                            {
                                var cur = rItems[i];
                                var nxt = rItems[i + 1];

                                double curRightU = cur.CenterU_Ft + cur.Length_Ft / 2.0;
                                double nxtLeftU = nxt.CenterU_Ft - nxt.Length_Ft / 2.0;
                                double vGap = nxtLeftU - curRightU;

                                if (vGap > 0.0001)
                                {
                                    double bottomV = cur.CenterV_Ft - cur.Width_Ft / 2.0;
                                    XYZ vStart3D = host.LocalToWorld(new XYZ(curRightU, bottomV, 0.0), normalTileOffset);
                                    XYZ vEnd3D = host.LocalToWorld(new XYZ(curRightU, bottomV + cur.Width_Ft, 0.0), normalTileOffset);

                                    XYZ jv0 = host.LocalToWorld(new XYZ(curRightU, bottomV, 0.0), normalTileOffset);
                                    XYZ jv1 = host.LocalToWorld(new XYZ(curRightU + vGap, bottomV, 0.0), normalTileOffset);
                                    XYZ jv2 = host.LocalToWorld(new XYZ(curRightU + vGap, bottomV + cur.Width_Ft, 0.0), normalTileOffset);
                                    XYZ jv3 = host.LocalToWorld(new XYZ(curRightU, bottomV + cur.Width_Ft, 0.0), normalTileOffset);

                                    LayoutJoints.Add(new JointLayoutItem
                                    {
                                        IsVertical = true,
                                        StartPoint3D = vStart3D,
                                        EndPoint3D = vEnd3D,
                                        Center3D = (vStart3D + vEnd3D) / 2.0,
                                        Normal3D = host.Normal3D,
                                        RotationRad = Math.PI / 2.0,
                                        LengthFt = cur.Width_Ft,
                                        WidthFt = vGap,
                                        DepthFt = tileHFt,
                                        PolygonVertices3D = new List<XYZ> { jv0, jv1, jv2, jv3 },
                                        MaterialName = GroutMaterial?.Name ?? "Fragua",
                                        HostFaceRef = host.HostFaceReference
                                    });
                                }
                            }
                        }
                    }
                }
                else
                {
                    // El conteo y trazado de baldosas se inicia a partir del límite interior de la junta
                    double minU = tilingBoundaryUV.Min(p => p.X);
                    double maxU = tilingBoundaryUV.Max(p => p.X);
                    double minV = tilingBoundaryUV.Min(p => p.Y);
                    double maxV = tilingBoundaryUV.Max(p => p.Y);

                    if (minU >= maxU || minV >= maxV) continue;

                double hu = tileLFt / 2.0;
                double hv = tileWFt / 2.0;

                double originU = minU + hu;
                double originV = minV + hv;
                if (cmbOrigin.SelectedIndex == 0) // Centro
                {
                    originU = (minU + maxU) / 2.0;
                    originV = (minV + maxV) / 2.0;
                }
                else if (cmbOrigin.SelectedIndex == 2) // Sup-Der
                {
                    originU = maxU - hu;
                    originV = maxV - hv;
                }

                originU += offXFt;
                originV += offYFt;

                double stepU = tileLFt + jointInnerFt;
                double stepV = tileWFt + jointInnerFt;

                double spanRadius = Math.Sqrt(Math.Pow(maxU - minU, 2) + Math.Pow(maxV - minV, 2)) * 1.3;
                int nu = (int)Math.Ceiling(spanRadius / stepU) + 2;
                int nv = (int)Math.Ceiling(spanRadius / stepV) + 2;

                for (int iv = -nv; iv <= nv; iv++)
                {
                    double rowShiftU = 0;
                    if (SelectedPattern == FloorPatternType.RunningBond50)
                    {
                        if (Math.Abs(iv) % 2 == 1) rowShiftU = stepU * 0.5;
                    }
                    else if (SelectedPattern == FloorPatternType.ThirdBond33)
                    {
                        rowShiftU = (Math.Abs(iv) % 3) * (stepU / 3.0);
                    }

                    for (int iu = -nu; iu <= nu; iu++)
                    {
                        double localU = iu * stepU + rowShiftU;
                        double localV = iv * stepV;

                        double ru = localU * Math.Cos(rotRad) - localV * Math.Sin(rotRad) + originU;
                        double rv = localU * Math.Sin(rotRad) + localV * Math.Cos(rotRad) + originV;

                        List<XYZ> tilePolyUV = new List<XYZ>
                        {
                            RotateAndTranslate2D(new XYZ(-hu, -hv, 0), rotRad, ru, rv),
                            RotateAndTranslate2D(new XYZ( hu, -hv, 0), rotRad, ru, rv),
                            RotateAndTranslate2D(new XYZ( hu,  hv, 0), rotRad, ru, rv),
                            RotateAndTranslate2D(new XYZ(-hu,  hv, 0), rotRad, ru, rv)
                        };

                        List<XYZ> clipResultUV;
                        bool isCut = false;
                        double fullAreaSqFt = tileLFt * tileWFt;

                        // Descarte rápido por caja envolvente (AABB) 2D
                        double tileMinU = tilePolyUV.Min(p => p.X);
                        double tileMaxU = tilePolyUV.Max(p => p.X);
                        double tileMinV = tilePolyUV.Min(p => p.Y);
                        double tileMaxV = tilePolyUV.Max(p => p.Y);

                        if (tileMaxU < minU - 1e-4 || tileMinU > maxU + 1e-4 || tileMaxV < minV - 1e-4 || tileMinV > maxV + 1e-4)
                        {
                            continue;
                        }

                        int insideCount = 0;
                        for (int k = 0; k < 4; k++)
                        {
                            if (IsPointInPolygon2D(tilePolyUV[k], tilingBoundaryUV)) insideCount++;
                        }

                        if (insideCount == 4)
                        {
                            // Baldosa completamente entera en el interior
                            clipResultUV = tilePolyUV;
                            isCut = false;
                        }
                        else
                        {
                            // Baldosa cortada en el perímetro interior o atravesando el borde
                            clipResultUV = ClipPolygonAgainstConvex(tilingBoundaryUV, tilePolyUV);
                            if (clipResultUV == null || clipResultUV.Count < 3) continue;

                            double clippedArea = CalculatePolygonArea(clipResultUV);
                            if (clippedArea < 0.005 * fullAreaSqFt) continue; // Descartar micro-astillas

                            isCut = Math.Abs(clippedArea - fullAreaSqFt) > 0.02 * fullAreaSqFt;
                        }

                        double clippedAreaSqFt = CalculatePolygonArea(clipResultUV);
                        double areaSqM = clippedAreaSqFt * 0.092903;

                        TileRole role = TileRole.Full;
                        if (isCut)
                        {
                            role = clipResultUV.Count > 4 ? TileRole.Trimmed : TileRole.CutEdge;
                            cutCount++;
                            totalCutTilesArea += (fullAreaSqFt * 0.092903);
                        }
                        else
                        {
                            fullCount++;
                            totalFullTilesArea += areaSqM;
                        }

                        List<XYZ> poly3D = clipResultUV.Select(pt => host.LocalToWorld(pt, normalTileOffset)).ToList();
                        XYZ polyCenter3D = host.LocalToWorld(new XYZ(ru, rv, 0), normalTileOffset);

                        XYZ dirU = (host.BasisU * Math.Cos(rotRad) + host.BasisV * Math.Sin(rotRad)).Normalize();
                        XYZ dirV = (-host.BasisU * Math.Sin(rotRad) + host.BasisV * Math.Cos(rotRad)).Normalize();

                        double cutLatFt = 0.0;
                        double cutTopFt = 0.0;
                        XYZ placementCenter3D = polyCenter3D;
                        double tileRotRad = rotRad;
                        XYZ tileDirU = dirU;

                        double actualLengthFt = tileLFt;
                        double actualWidthFt = tileWFt;

                        if (isCut)
                        {
                            double cosR = Math.Cos(rotRad);
                            double sinR = Math.Sin(rotRad);
                            double minLocalU = double.MaxValue, maxLocalU = double.MinValue;
                            double minLocalV = double.MaxValue, maxLocalV = double.MinValue;

                            foreach (var pt in clipResultUV)
                            {
                                double du =  (pt.X - ru) * cosR + (pt.Y - rv) * sinR;
                                double dv = -(pt.X - ru) * sinR + (pt.Y - rv) * cosR;

                                if (du < minLocalU) minLocalU = du;
                                if (du > maxLocalU) maxLocalU = du;
                                if (dv < minLocalV) minLocalV = dv;
                                if (dv > maxLocalV) maxLocalV = dv;
                            }

                            actualLengthFt = Math.Max(0.01, maxLocalU - minLocalU);
                            actualWidthFt = Math.Max(0.01, maxLocalV - minLocalV);

                            double cutU_pos = Math.Max(0.0, hu - maxLocalU);
                            double cutU_neg = Math.Max(0.0, minLocalU - (-hu));
                            double cutV_pos = Math.Max(0.0, hv - maxLocalV);
                            double cutV_neg = Math.Max(0.0, minLocalV - (-hv));

                            // Cut Lat (Recorte Lateral) corresponde a la longitud (eje U / Length)
                            cutLatFt = Math.Max(0.0, cutU_pos + cutU_neg);
                            // Cut Top (Recorte Superior) corresponde al ancho/altura (eje V / Width)
                            cutTopFt = Math.Max(0.0, cutV_pos + cutV_neg);

                            // El centro de inserción de la pieza cortada debe ubicarse en el centro geométrico del remanente dentro del piso
                            double midLocalU = (minLocalU + maxLocalU) / 2.0;
                            double midLocalV = (minLocalV + maxLocalV) / 2.0;
                            XYZ centerUV = RotateAndTranslate2D(new XYZ(midLocalU, midLocalV, 0), rotRad, ru, rv);
                            placementCenter3D = host.LocalToWorld(centerUV, normalTileOffset);
                        }

                        LayoutTiles.Add(new TileLayoutItem
                        {
                            IndexX = iu,
                            IndexY = iv,
                            CenterU_Ft = ru,
                            CenterV_Ft = rv,
                            Center3D = placementCenter3D,
                            Corner3D = host.LocalToWorld(RotateAndTranslate2D(new XYZ(-hu, -hv, 0), rotRad, ru, rv), normalTileOffset),
                            Normal3D = host.Normal3D,
                            DirectionU3D = tileDirU,
                            DirectionV3D = dirV,
                            Length_Ft = tileLFt,
                            Width_Ft = tileWFt,
                            Thickness_Ft = tileHFt,
                            RotationRad = tileRotRad,
                            IsCut = isCut,
                            CutLat_Ft = cutLatFt,
                            CutTop_Ft = cutTopFt,
                            ActualLength_Ft = actualLengthFt,
                            ActualWidth_Ft = actualWidthFt,
                            Role = role,
                            PolygonVerticesUV = clipResultUV,
                            PolygonVertices3D = poly3D,
                            ActualAreaSqM = areaSqM,
                            HostFaceRef = host.HostFaceReference
                        });
                    }
                }

                // Generar Juntas Interiores (Grid Bidireccional sin solapamientos, recortadas al contorno interior tilingBoundaryUV)
                // Dirección U: cintas continuas
                // Dirección V: piezas seccionadas entre las cintas de dirección U
                if (GenerateJointMesh && jointInnerFt > 0.0005)
                {
                    double halfSpan = spanRadius + 10.0;
                    double hw = jointInnerFt / 2.0;
                    double cosR = Math.Cos(rotRad);
                    double sinR = Math.Sin(rotRad);

                    // 1. Líneas de Junta Continuas en Dirección U (Transversales)
                    for (int iv = -nv; iv <= nv; iv++)
                    {
                        double vPos = iv * stepV + (tileWFt / 2.0 + jointInnerFt / 2.0);

                        List<XYZ> stripPolyUV = new List<XYZ>
                        {
                            RotateAndTranslate2D(new XYZ(-halfSpan, vPos - hw, 0), rotRad, originU, originV),
                            RotateAndTranslate2D(new XYZ( halfSpan, vPos - hw, 0), rotRad, originU, originV),
                            RotateAndTranslate2D(new XYZ( halfSpan, vPos + hw, 0), rotRad, originU, originV),
                            RotateAndTranslate2D(new XYZ(-halfSpan, vPos + hw, 0), rotRad, originU, originV)
                        };

                        var clippedStripUV = ClipPolygonAgainstConvex(tilingBoundaryUV, stripPolyUV);
                        if (clippedStripUV.Count >= 3)
                        {
                            double stripMinU = double.MaxValue;
                            double stripMaxU = double.MinValue;
                            foreach (var pt in clippedStripUV)
                            {
                                double du = (pt.X - originU) * cosR + (pt.Y - originV) * sinR;
                                if (du < stripMinU) stripMinU = du;
                                if (du > stripMaxU) stripMaxU = du;
                            }

                            double actualLen = stripMaxU - stripMinU;
                            if (actualLen < 0.005) continue;

                            double midU = (stripMinU + stripMaxU) / 2.0;
                            XYZ centerUV = RotateAndTranslate2D(new XYZ(midU, vPos, 0), rotRad, originU, originV);
                            XYZ centerStrip3D = host.LocalToWorld(centerUV, normalTileOffset);
                            XYZ dirU3D = (host.BasisU * cosR + host.BasisV * sinR).Normalize();
                            XYZ start3D = centerStrip3D - dirU3D * (actualLen / 2.0);
                            XYZ end3D = centerStrip3D + dirU3D * (actualLen / 2.0);

                            List<XYZ> strip3D = clippedStripUV.Select(pt => host.LocalToWorld(pt, normalTileOffset)).ToList();

                            LayoutJoints.Add(new JointLayoutItem
                            {
                                IsVertical = false,
                                Center3D = centerStrip3D,
                                StartPoint3D = start3D,
                                EndPoint3D = end3D,
                                Normal3D = host.Normal3D,
                                RotationRad = rotRad,
                                LengthFt = actualLen,
                                WidthFt = jointInnerFt,
                                DepthFt = tileHFt,
                                PolygonVertices3D = strip3D,
                                MaterialName = GroutMaterial?.Name ?? "Fragua",
                                HostFaceRef = host.HostFaceReference
                            });
                        }
                    }

                    // 2. Tramos de Junta Seccionados en Dirección V (entre filas iv e iv+1)
                    for (int iv = -nv; iv <= nv; iv++)
                    {
                        double vStart = (iv * stepV - tileWFt / 2.0);
                        double vEnd = (iv * stepV + tileWFt / 2.0);

                        double rowShiftU = 0;
                        if (SelectedPattern == FloorPatternType.RunningBond50)
                        {
                            if (Math.Abs(iv) % 2 == 1) rowShiftU = stepU * 0.5;
                        }
                        else if (SelectedPattern == FloorPatternType.ThirdBond33)
                        {
                            rowShiftU = (Math.Abs(iv) % 3) * (stepU / 3.0);
                        }

                        for (int iu = -nu; iu <= nu; iu++)
                        {
                            double uPos = iu * stepU + (tileLFt / 2.0 + jointInnerFt / 2.0) + rowShiftU;

                            List<XYZ> segPolyUV = new List<XYZ>
                            {
                                RotateAndTranslate2D(new XYZ(uPos - hw, vStart, 0), rotRad, originU, originV),
                                RotateAndTranslate2D(new XYZ(uPos + hw, vStart, 0), rotRad, originU, originV),
                                RotateAndTranslate2D(new XYZ(uPos + hw, vEnd,   0), rotRad, originU, originV),
                                RotateAndTranslate2D(new XYZ(uPos - hw, vEnd,   0), rotRad, originU, originV)
                            };

                            var clippedSegUV = ClipPolygonAgainstConvex(tilingBoundaryUV, segPolyUV);
                            if (clippedSegUV.Count >= 3)
                            {
                                double segMinV = double.MaxValue;
                                double segMaxV = double.MinValue;
                                foreach (var pt in clippedSegUV)
                                {
                                    double dv = -(pt.X - originU) * sinR + (pt.Y - originV) * cosR;
                                    if (dv < segMinV) segMinV = dv;
                                    if (dv > segMaxV) segMaxV = dv;
                                }

                                double actualLen = segMaxV - segMinV;
                                if (actualLen < 0.005) continue;

                                double midV = (segMinV + segMaxV) / 2.0;
                                XYZ centerUV = RotateAndTranslate2D(new XYZ(uPos, midV, 0), rotRad, originU, originV);
                                XYZ centerSeg3D = host.LocalToWorld(centerUV, normalTileOffset);
                                XYZ dirV3D = (-host.BasisU * sinR + host.BasisV * cosR).Normalize();
                                XYZ start3D = centerSeg3D - dirV3D * (actualLen / 2.0);
                                XYZ end3D = centerSeg3D + dirV3D * (actualLen / 2.0);

                                List<XYZ> seg3D = clippedSegUV.Select(pt => host.LocalToWorld(pt, normalTileOffset)).ToList();

                                LayoutJoints.Add(new JointLayoutItem
                                {
                                    IsVertical = true,
                                    Center3D = centerSeg3D,
                                    StartPoint3D = start3D,
                                    EndPoint3D = end3D,
                                    Normal3D = host.Normal3D,
                                    RotationRad = rotRad + Math.PI / 2.0,
                                    LengthFt = actualLen,
                                    WidthFt = jointInnerFt,
                                    DepthFt = tileHFt,
                                    PolygonVertices3D = seg3D,
                                    MaterialName = GroutMaterial?.Name ?? "Fragua",
                                    HostFaceRef = host.HostFaceReference
                                });
                            }
                        }
                    }
                }
            }
        }

            UpdateQuickCalculations(fullCount, cutCount, totalFullTilesArea + totalCutTilesArea);
            Render3DScene();
        }

        private XYZ RotateAndTranslate2D(XYZ pt, double angleRad, double tu, double tv)
        {
            double ru = pt.X * Math.Cos(angleRad) - pt.Y * Math.Sin(angleRad) + tu;
            double rv = pt.X * Math.Sin(angleRad) + pt.Y * Math.Cos(angleRad) + tv;
            return new XYZ(ru, rv, 0);
        }

        private List<XYZ> ClipPolygonToVMax(List<XYZ> poly, double maxV)
        {
            List<XYZ> clipBox = new List<XYZ>
            {
                new XYZ(-10000, -10000, 0),
                new XYZ( 10000, -10000, 0),
                new XYZ( 10000,  maxV, 0),
                new XYZ(-10000,  maxV, 0)
            };
            return ClipPolygonAgainstConvex(poly, clipBox);
        }

        private void UpdateQuickCalculations(int fullCount, int cutCount, double totalTileMaterialM2)
        {
            lblFullTiles.Text = $"{fullCount} pzas";
            lblCutTiles.Text = $"{cutCount} pzas";

            double netFloorArea = SelectedHosts.Sum(f => f.AreaSqM);
            double wastePercent = netFloorArea > 0 ? Math.Max(5.0, ((totalTileMaterialM2 - netFloorArea) / netFloorArea) * 100.0) : 10.0;
            double totalPurchasingArea = netFloorArea * (1.0 + (wastePercent / 100.0));

            lblTotalAreaWaste.Text = $"{totalPurchasingArea:0.00} m² (+{wastePercent:0.0}%)";

            double skirtingLinearM = SkirtingSegments.Sum(s => s.StartPoint.DistanceTo(s.EndPoint) * 0.3048);
            lblSkirtingLinearM.Text = $"{skirtingLinearM:0.00} ml";

            double perimeterPerTile = 2.0 * ((TileLengthCm) + (TileWidthCm)) / 100.0;
            double totalJointLinearM = (fullCount + cutCount) * (perimeterPerTile / 2.0);
            lblJointTotalM.Text = $"{totalJointLinearM:0.0} m";

            double costM2 = 25.0;
            double totalCost = totalPurchasingArea * costM2;
            lblEstimatedCost.Text = $"$ {totalCost:0.00}";
        }

        #region Renderizado 3D en WPF

        private void Render3DScene()
        {
            var lights = modelGroup.Children.Where(c => c is Light).ToList();
            modelGroup.Children.Clear();
            foreach (var light in lights) modelGroup.Children.Add(light);

            if (SelectedHosts.Count == 0) return;

            var fullTileMat = new MaterialGroup();
            fullTileMat.Children.Add(new DiffuseMaterial(new SolidColorBrush(System.Windows.Media.Color.FromRgb(49, 151, 149))));
            fullTileMat.Children.Add(new SpecularMaterial(new SolidColorBrush(System.Windows.Media.Color.FromRgb(60, 60, 60)), 30));

            var cutTileMat = new MaterialGroup();
            cutTileMat.Children.Add(new DiffuseMaterial(new SolidColorBrush(System.Windows.Media.Color.FromRgb(221, 107, 32))));
            cutTileMat.Children.Add(new SpecularMaterial(new SolidColorBrush(System.Windows.Media.Color.FromRgb(60, 60, 60)), 30));

            var revoqueMat = new DiffuseMaterial(new SolidColorBrush(System.Windows.Media.Color.FromRgb(160, 174, 192)));
            var beddingMat = new DiffuseMaterial(new SolidColorBrush(System.Windows.Media.Color.FromRgb(113, 128, 150)));
            var groutMat = new DiffuseMaterial(new SolidColorBrush(System.Windows.Media.Color.FromRgb(74, 85, 104)));

            var skirtingMat = new MaterialGroup();
            skirtingMat.Children.Add(new DiffuseMaterial(new SolidColorBrush(System.Windows.Media.Color.FromRgb(128, 90, 213))));
            skirtingMat.Children.Add(new SpecularMaterial(new SolidColorBrush(System.Windows.Media.Color.FromRgb(80, 80, 80)), 40));

            var baseSubstrateMat = new DiffuseMaterial(new SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 44, 52)));

            var allPts = SelectedHosts.SelectMany(h => h.OuterBoundary3D).ToList();
            if (allPts.Count == 0) return;

            double allMinX = allPts.Min(p => p.X);
            double allMaxX = allPts.Max(p => p.X);
            double allMinY = allPts.Min(p => p.Y);
            double allMaxY = allPts.Max(p => p.Y);
            double allMinZ = allPts.Min(p => p.Z);
            double allMaxZ = allPts.Max(p => p.Z);

            double centerX = (allMinX + allMaxX) / 2.0;
            double centerY = (allMinY + allMaxY) / 2.0;
            double centerZ = (allMinZ + allMaxZ) / 2.0;

            // 1. Dibujar anfitriones base y capas (Mortero, Pega) con estratigrafía top-to-bottom
            double tileHFt = (TileThickCm / 100.0) * 3.28084;
            double revoqueFt = (RevoqueThickCm / 100.0) * 3.28084;
            double beddingFt = (BeddingThickMm / 1000.0) * 3.28084;

            foreach (var host in SelectedHosts)
            {
                if (host.HostType == HostElementType.Floor)
                {
                    // Mortero de nivel (solado) en la base inferior
                    if (ModelRevoque)
                    {
                        double revOffset = -(tileHFt + beddingFt + revoqueFt);
                        MeshGeometry3D revMesh = CreateHostBaseMesh(host, centerX, centerY, centerZ, revOffset);
                        modelGroup.Children.Add(new GeometryModel3D(revMesh, revoqueMat));
                    }

                    // Cama de pega inmediatamente debajo de las baldosas (con setback perimetral)
                    if (ModelBeddingMortar)
                    {
                        double bedOffset = -(tileHFt + beddingFt);
                        MeshGeometry3D bedMesh = CreateBeddingMesh(host, centerX, centerY, centerZ, bedOffset);
                        modelGroup.Children.Add(new GeometryModel3D(bedMesh, beddingMat));
                    }

                    // Sustrato estructural bajo el solado
                    double subOffset = -(tileHFt + beddingFt + revoqueFt + 0.05);
                    MeshGeometry3D baseMesh = CreateHostBaseMesh(host, centerX, centerY, centerZ, subOffset);
                    modelGroup.Children.Add(new GeometryModel3D(baseMesh, baseSubstrateMat));
                }
                else
                {
                    // Para muros
                    MeshGeometry3D baseMesh = CreateHostBaseMesh(host, centerX, centerY, centerZ, -0.05);
                    modelGroup.Children.Add(new GeometryModel3D(baseMesh, baseSubstrateMat));

                    if (ModelRevoque)
                    {
                        MeshGeometry3D revMesh = CreateHostBaseMesh(host, centerX, centerY, centerZ, 0.0);
                        modelGroup.Children.Add(new GeometryModel3D(revMesh, revoqueMat));
                    }

                    if (ModelBeddingMortar)
                    {
                        double bedOffset = (ModelRevoque ? revoqueFt : 0.0);
                        MeshGeometry3D bedMesh = CreateHostBaseMesh(host, centerX, centerY, centerZ, bedOffset);
                        modelGroup.Children.Add(new GeometryModel3D(bedMesh, beddingMat));
                    }
                }
            }

            // 2. Dibujar Baldosas 3D si están activas
            if (ModelTiles)
            {
                MeshGeometry3D fullTilesMesh = new MeshGeometry3D();
                MeshGeometry3D cutTilesMesh = new MeshGeometry3D();

                foreach (var tile in LayoutTiles)
                {
                    MeshGeometry3D targetMesh = tile.IsCut ? cutTilesMesh : fullTilesMesh;
                    AddTileSolidToMesh(targetMesh, tile.PolygonVertices3D, centerX, centerY, centerZ, tile.Thickness_Ft);
                }

                if (fullTilesMesh.Positions.Count > 0)
                    modelGroup.Children.Add(new GeometryModel3D(fullTilesMesh, fullTileMat));

                if (cutTilesMesh.Positions.Count > 0)
                    modelGroup.Children.Add(new GeometryModel3D(cutTilesMesh, cutTileMat));
            }

            // 3. Dibujar Juntas / Fragua 3D entre baldosas si está activa
            if (GenerateJointMesh && LayoutJoints.Count > 0)
            {
                MeshGeometry3D jointsMesh = new MeshGeometry3D();
                foreach (var j in LayoutJoints)
                {
                    AddJointStripMesh(jointsMesh, j, centerX, centerY, centerZ);
                }
                if (jointsMesh.Positions.Count > 0)
                    modelGroup.Children.Add(new GeometryModel3D(jointsMesh, groutMat));
            }

            // 4. Dibujar Zócalos perimetrales sobre piso
            if (EnableSkirting && SkirtingSegments.Count > 0)
            {
                MeshGeometry3D skirtingMesh = new MeshGeometry3D();
                foreach (var seg in SkirtingSegments)
                {
                    AddSkirtingMesh(skirtingMesh, seg, centerX, centerY, centerZ);
                }
                if (skirtingMesh.Positions.Count > 0)
                    modelGroup.Children.Add(new GeometryModel3D(skirtingMesh, skirtingMat));
            }

            double spanX = (allMaxX - allMinX) * 30.48;
            double spanY = (allMaxY - allMinY) * 30.48;
            double spanZ = (allMaxZ - allMinZ) * 30.48;
            double maxSpan = Math.Max(spanX, Math.Max(spanY, spanZ));
            _cameraDistance = Math.Max(80, maxSpan * 1.6);
            UpdateCamera();
        }

        private MeshGeometry3D CreateBeddingMesh(TilingHostInfo host, double cx, double cy, double cz, double thickFt)
        {
            MeshGeometry3D mesh = new MeshGeometry3D();
            List<XYZ> pts3D = (host.TilingBoundary3D != null && host.TilingBoundary3D.Count >= 3) ? host.TilingBoundary3D : host.OuterBoundary3D;
            List<XYZ> ptsUV = (host.TilingBoundaryUV != null && host.TilingBoundaryUV.Count >= 3) ? host.TilingBoundaryUV : host.OuterBoundaryUV;
            if (pts3D.Count < 3) return mesh;

            double scale = 30.48;
            int baseIdx = mesh.Positions.Count;
            for (int i = 0; i < pts3D.Count; i++)
            {
                double px = (pts3D[i].X - cx) * scale;
                double py = (pts3D[i].Y - cy) * scale;
                double pz = (pts3D[i].Z - cz + thickFt) * scale;
                mesh.Positions.Add(new Point3D(px, py, pz));
            }

            List<int> tri = TriangulatePolygon2D(ptsUV);
            foreach (int idx in tri)
            {
                mesh.TriangleIndices.Add(baseIdx + idx);
            }

            return mesh;
        }

        private MeshGeometry3D CreateHostBaseMesh(TilingHostInfo host, double cx, double cy, double cz, double thickFt)
        {
            MeshGeometry3D mesh = new MeshGeometry3D();
            if (host.OuterBoundary3D.Count < 3) return mesh;

            double scale = 30.48;
            List<XYZ> pts3D = host.OuterBoundary3D;
            List<XYZ> ptsUV = host.OuterBoundaryUV;

            int baseIdx = mesh.Positions.Count;
            for (int i = 0; i < pts3D.Count; i++)
            {
                double px = (pts3D[i].X - cx) * scale;
                double py = (pts3D[i].Y - cy) * scale;
                double pz = (pts3D[i].Z - cz + thickFt) * scale;
                mesh.Positions.Add(new Point3D(px, py, pz));
            }

            List<int> tri = TriangulatePolygon2D(ptsUV);
            foreach (int idx in tri)
            {
                mesh.TriangleIndices.Add(baseIdx + idx);
            }

            return mesh;
        }

        private void AddTileSolidToMesh(MeshGeometry3D mesh, List<XYZ> poly3D, double cx, double cy, double cz, double thickFt)
        {
            if (poly3D == null || poly3D.Count < 3) return;

            double scale = 30.48;
            int baseIdx = mesh.Positions.Count;
            int n = poly3D.Count;

            // Vértices inferiores (z = pz)
            for (int i = 0; i < n; i++)
            {
                double px = (poly3D[i].X - cx) * scale;
                double py = (poly3D[i].Y - cy) * scale;
                double pz = (poly3D[i].Z - cz) * scale;
                mesh.Positions.Add(new Point3D(px, py, pz));
            }

            // Vértices superiores (z = pz + thickFt)
            for (int i = 0; i < n; i++)
            {
                double px = (poly3D[i].X - cx) * scale;
                double py = (poly3D[i].Y - cy) * scale;
                double pz = (poly3D[i].Z - cz + thickFt) * scale;
                mesh.Positions.Add(new Point3D(px, py, pz));
            }

            // Triangulación Cara Superior
            List<int> topTri = TriangulatePolygon2D(poly3D);
            foreach (int idx in topTri)
            {
                mesh.TriangleIndices.Add(baseIdx + n + idx);
            }

            // Triangulación Cara Inferior (orden inverso para normal hacia abajo)
            for (int i = 0; i < topTri.Count; i += 3)
            {
                mesh.TriangleIndices.Add(baseIdx + topTri[i + 2]);
                mesh.TriangleIndices.Add(baseIdx + topTri[i + 1]);
                mesh.TriangleIndices.Add(baseIdx + topTri[i]);
            }

            // Caras laterales
            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;
                int bCurr = baseIdx + i;
                int bNext = baseIdx + next;
                int tCurr = baseIdx + n + i;
                int tNext = baseIdx + n + next;

                mesh.TriangleIndices.Add(bCurr);
                mesh.TriangleIndices.Add(tCurr);
                mesh.TriangleIndices.Add(tNext);

                mesh.TriangleIndices.Add(bCurr);
                mesh.TriangleIndices.Add(tNext);
                mesh.TriangleIndices.Add(bNext);
            }
        }

        private void AddJointStripMesh(MeshGeometry3D mesh, JointLayoutItem j, double cx, double cy, double cz)
        {
            if (j.PolygonVertices3D != null && j.PolygonVertices3D.Count >= 3)
            {
                AddTileSolidToMesh(mesh, j.PolygonVertices3D, cx, cy, cz, j.DepthFt);
                return;
            }

            double scale = 30.48;
            XYZ segDir = j.EndPoint3D - j.StartPoint3D;
            double segLen = segDir.GetLength();
            if (segLen < 0.001) return;

            XYZ uDir = segDir.Normalize();
            XYZ side = j.Normal3D.CrossProduct(uDir);
            if (side.GetLength() < 0.01) side = XYZ.BasisZ.CrossProduct(uDir);
            if (side.GetLength() < 0.01) side = XYZ.BasisY.CrossProduct(uDir);
            side = side.Normalize();

            double w = j.WidthFt / 2.0;

            XYZ pt0 = j.StartPoint3D - side * w;
            XYZ pt1 = j.EndPoint3D - side * w;
            XYZ pt2 = j.EndPoint3D + side * w;
            XYZ pt3 = j.StartPoint3D + side * w;

            List<XYZ> pts = new List<XYZ> { pt0, pt1, pt2, pt3 };
            AddTileSolidToMesh(mesh, pts, cx, cy, cz, j.DepthFt);
        }

        private void AddSkirtingMesh(MeshGeometry3D mesh, SkirtingItem seg, double cx, double cy, double cz)
        {
            double scale = 30.48;
            double x1 = (seg.StartPoint.X - cx) * scale;
            double y1 = (seg.StartPoint.Y - cy) * scale;
            double zBase = (seg.BaseElevationFt - cz) * scale;

            double x2 = (seg.EndPoint.X - cx) * scale;
            double y2 = (seg.EndPoint.Y - cy) * scale;

            double dx = x2 - x1;
            double dy = y2 - y1;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.01) return;

            double nx = -dy / len;
            double ny = dx / len;

            double th = seg.ThicknessFt * scale;
            double h = seg.HeightFt * scale;

            Point3D p0 = new Point3D(x1, y1, zBase);
            Point3D p1 = new Point3D(x2, y2, zBase);
            Point3D p2 = new Point3D(x2 + nx * th, y2 + ny * th, zBase);
            Point3D p3 = new Point3D(x1 + nx * th, y1 + ny * th, zBase);

            Point3D p4 = new Point3D(x1, y1, zBase + h);
            Point3D p5 = new Point3D(x2, y2, zBase + h);
            Point3D p6 = new Point3D(x2 + nx * th, y2 + ny * th, zBase + h);
            Point3D p7 = new Point3D(x1 + nx * th, y1 + ny * th, zBase + h);

            int idx = mesh.Positions.Count;
            Point3D[] pts = { p0, p1, p2, p3, p4, p5, p6, p7 };
            foreach (var p in pts) mesh.Positions.Add(p);

            void AddQuad(int a, int b, int c, int d)
            {
                mesh.TriangleIndices.Add(idx + a);
                mesh.TriangleIndices.Add(idx + b);
                mesh.TriangleIndices.Add(idx + c);
                mesh.TriangleIndices.Add(idx + a);
                mesh.TriangleIndices.Add(idx + c);
                mesh.TriangleIndices.Add(idx + d);
            }

            AddQuad(4, 5, 6, 7); // Superior
            AddQuad(0, 4, 7, 3); // Cara externa
            AddQuad(1, 2, 6, 5); // Cara interna
            AddQuad(3, 7, 6, 2); // Cierre frontal
        }

        #endregion

        #region Polígonos

        private double CalculatePolygonArea(List<XYZ> poly)
        {
            if (poly.Count < 3) return 0;
            double area = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                XYZ p1 = poly[i];
                XYZ p2 = poly[(i + 1) % poly.Count];
                area += (p1.X * p2.Y - p2.X * p1.Y);
            }
            return Math.Abs(area) / 2.0;
        }

        private bool IsClockwise2D(List<XYZ> poly)
        {
            double area = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                XYZ p1 = poly[i];
                XYZ p2 = poly[(i + 1) % poly.Count];
                area += (p1.X * p2.Y - p2.X * p1.Y);
            }
            return area < 0;
        }

        public static List<int> TriangulatePolygon2D(List<XYZ> poly)
        {
            List<int> triangles = new List<int>();
            if (poly == null || poly.Count < 3) return triangles;

            int n = poly.Count;
            if (n == 3)
            {
                triangles.Add(0);
                triangles.Add(1);
                triangles.Add(2);
                return triangles;
            }

            List<int> indices = new List<int>(n);
            for (int i = 0; i < n; i++) indices.Add(i);

            // Verificar si los índices están en sentido antihorario
            double area = 0;
            for (int i = 0; i < n; i++)
            {
                XYZ p1 = poly[i];
                XYZ p2 = poly[(i + 1) % n];
                area += (p1.X * p2.Y - p2.X * p1.Y);
            }
            if (area < 0)
            {
                indices.Reverse();
            }

            int count = indices.Count;
            int timeout = count * 3;
            int curr = 0;

            while (count > 3 && timeout > 0)
            {
                timeout--;
                int prev = (curr + count - 1) % count;
                int next = (curr + 1) % count;

                int iPrev = indices[prev];
                int iCurr = indices[curr];
                int iNext = indices[next];

                XYZ a = poly[iPrev];
                XYZ b = poly[iCurr];
                XYZ c = poly[iNext];

                // Verificar si el vértice 'b' es convexo
                double cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
                if (cross > 1e-7)
                {
                    bool isEar = true;
                    for (int i = 0; i < count; i++)
                    {
                        if (i == prev || i == curr || i == next) continue;
                        XYZ p = poly[indices[i]];
                        if (IsPointInTriangle2D(p, a, b, c))
                        {
                            isEar = false;
                            break;
                        }
                    }

                    if (isEar)
                    {
                        triangles.Add(iPrev);
                        triangles.Add(iCurr);
                        triangles.Add(iNext);
                        indices.RemoveAt(curr);
                        count--;
                        curr = curr % Math.Max(1, count);
                        continue;
                    }
                }

                curr = (curr + 1) % count;
            }

            if (count == 3)
            {
                triangles.Add(indices[0]);
                triangles.Add(indices[1]);
                triangles.Add(indices[2]);
            }

            return triangles;
        }

        private static bool IsPointInTriangle2D(XYZ p, XYZ a, XYZ b, XYZ c)
        {
            double c1 = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
            double c2 = (c.X - b.X) * (p.Y - b.Y) - (c.Y - b.Y) * (p.X - b.X);
            double c3 = (a.X - c.X) * (p.Y - c.Y) - (a.Y - c.Y) * (p.X - c.X);

            return (c1 >= -1e-7 && c2 >= -1e-7 && c3 >= -1e-7) || (c1 <= 1e-7 && c2 <= 1e-7 && c3 <= 1e-7);
        }

        private bool IsPointInPolygon2D(XYZ pt, List<XYZ> poly)
        {
            if (poly.Count < 3) return false;
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                if (((poly[i].Y > pt.Y) != (poly[j].Y > pt.Y)) &&
                    (pt.X < (poly[j].X - poly[i].X) * (pt.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X))
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private bool DoPolygonsIntersect2D(List<XYZ> poly1, List<XYZ> poly2)
        {
            for (int i = 0; i < poly1.Count; i++)
            {
                XYZ a1 = poly1[i];
                XYZ a2 = poly1[(i + 1) % poly1.Count];
                for (int j = 0; j < poly2.Count; j++)
                {
                    XYZ b1 = poly2[j];
                    XYZ b2 = poly2[(j + 1) % poly2.Count];
                    if (DoLinesIntersect2D(a1, a2, b1, b2)) return true;
                }
            }
            return false;
        }

        private bool DoLinesIntersect2D(XYZ p1, XYZ p2, XYZ p3, XYZ p4)
        {
            double d1 = Direction2D(p3, p4, p1);
            double d2 = Direction2D(p3, p4, p2);
            double d3 = Direction2D(p1, p2, p3);
            double d4 = Direction2D(p1, p2, p4);

            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
                ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
                return true;

            return false;
        }

        private double Direction2D(XYZ pi, XYZ pj, XYZ pk)
        {
            return (pk.X - pi.X) * (pj.Y - pi.Y) - (pj.X - pi.X) * (pk.Y - pi.Y);
        }

        private List<XYZ> InsetPolygon2D(List<XYZ> poly, double insetDist)
        {
            if (poly == null || poly.Count < 3 || insetDist <= 0.0001)
                return poly != null ? new List<XYZ>(poly) : new List<XYZ>();

            List<XYZ> ccwPoly = new List<XYZ>(poly);
            if (IsClockwise2D(ccwPoly))
            {
                ccwPoly.Reverse();
            }

            int n = ccwPoly.Count;
            List<XYZ> insetVertices = new List<XYZ>();

            for (int i = 0; i < n; i++)
            {
                XYZ pPrev = ccwPoly[(i + n - 1) % n];
                XYZ pCurr = ccwPoly[i];
                XYZ pNext = ccwPoly[(i + 1) % n];

                XYZ vPrev = (pCurr - pPrev);
                double lenPrev = Math.Sqrt(vPrev.X * vPrev.X + vPrev.Y * vPrev.Y);
                if (lenPrev < 1e-6) { insetVertices.Add(pCurr); continue; }
                vPrev = new XYZ(vPrev.X / lenPrev, vPrev.Y / lenPrev, 0);

                XYZ vNext = (pNext - pCurr);
                double lenNext = Math.Sqrt(vNext.X * vNext.X + vNext.Y * vNext.Y);
                if (lenNext < 1e-6) { insetVertices.Add(pCurr); continue; }
                vNext = new XYZ(vNext.X / lenNext, vNext.Y / lenNext, 0);

                // Inward normal for CCW polygon: (-v.Y, v.X)
                XYZ nPrev = new XYZ(-vPrev.Y, vPrev.X, 0);
                XYZ nNext = new XYZ(-vNext.Y, vNext.X, 0);

                double dotNorm = nNext.X * nPrev.X + nNext.Y * nPrev.Y;
                double denom = nNext.X * vPrev.X + nNext.Y * vPrev.Y;

                if (Math.Abs(denom) < 1e-6)
                {
                    insetVertices.Add(pCurr + nNext * insetDist);
                }
                else
                {
                    double t = (insetDist * (1.0 - dotNorm)) / denom;
                    XYZ pA = pCurr + nPrev * insetDist;
                    XYZ q = pA + vPrev * t;
                    insetVertices.Add(new XYZ(q.X, q.Y, 0));
                }
            }

            double origArea = CalculatePolygonArea(ccwPoly);
            double insetArea = CalculatePolygonArea(insetVertices);
            if (insetVertices.Count >= 3 && insetArea > 0.001 && insetArea < origArea)
            {
                return insetVertices;
            }

            return ccwPoly;
        }

        private List<XYZ> ClipPolygonAgainstConvex(List<XYZ> subjectPoly, List<XYZ> convexClipBoundary)
        {
            if (subjectPoly == null || subjectPoly.Count < 3 || convexClipBoundary == null || convexClipBoundary.Count < 3)
                return new List<XYZ>();

            List<XYZ> clipBox = new List<XYZ>(convexClipBoundary);
            if (IsClockwise2D(clipBox))
            {
                clipBox.Reverse();
            }

            List<XYZ> outputList = new List<XYZ>(subjectPoly);

            for (int i = 0; i < clipBox.Count; i++)
            {
                XYZ cp1 = clipBox[i];
                XYZ cp2 = clipBox[(i + 1) % clipBox.Count];

                List<XYZ> inputList = new List<XYZ>(outputList);
                outputList.Clear();

                if (inputList.Count == 0) break;

                XYZ s = inputList[inputList.Count - 1];

                foreach (XYZ e in inputList)
                {
                    if (IsInsideEdge(e, cp1, cp2))
                    {
                        if (IsInsideEdge(s, cp1, cp2))
                        {
                            outputList.Add(e);
                        }
                        else
                        {
                            outputList.Add(Intersection(s, e, cp1, cp2));
                            outputList.Add(e);
                        }
                    }
                    else if (IsInsideEdge(s, cp1, cp2))
                    {
                        outputList.Add(Intersection(s, e, cp1, cp2));
                    }
                    s = e;
                }
            }

            List<XYZ> clean = new List<XYZ>();
            for (int i = 0; i < outputList.Count; i++)
            {
                XYZ p = outputList[i];
                if (clean.Count == 0 || clean[clean.Count - 1].DistanceTo(p) > 0.001)
                {
                    clean.Add(p);
                }
            }
            if (clean.Count >= 2 && clean[0].DistanceTo(clean[clean.Count - 1]) < 0.001)
            {
                clean.RemoveAt(clean.Count - 1);
            }

            return clean;
        }

        private bool IsInsideEdge(XYZ p, XYZ cp1, XYZ cp2)
        {
            return (cp2.X - cp1.X) * (p.Y - cp1.Y) - (cp2.Y - cp1.Y) * (p.X - cp1.X) >= -1e-7;
        }

        private XYZ Intersection(XYZ s, XYZ e, XYZ cp1, XYZ cp2)
        {
            double a1 = e.Y - s.Y;
            double b1 = s.X - e.X;
            double c1 = a1 * s.X + b1 * s.Y;

            double a2 = cp2.Y - cp1.Y;
            double b2 = cp1.X - cp2.X;
            double c2 = a2 * cp1.X + b2 * cp1.Y;

            double det = a1 * b2 - a2 * b1;
            if (Math.Abs(det) < 1e-9) return s;

            double x = (b2 * c1 - b1 * c2) / det;
            double y = (a1 * c2 - a2 * c1) / det;
            return new XYZ(x, y, (s.Z + e.Z) / 2.0);
        }

        #endregion

        #region Controles de Cámara

        private void UpdateCamera()
        {
            double radX = _cameraAngleX * Math.PI / 180.0;
            double radY = _cameraAngleY * Math.PI / 180.0;

            double x = _cameraDistance * Math.Cos(radY) * Math.Cos(radX);
            double y = _cameraDistance * Math.Cos(radY) * Math.Sin(radX);
            double z = _cameraDistance * Math.Sin(radY);

            camera.Position = new Point3D(x, y, z);
            camera.LookDirection = new Vector3D(-x, -y, -z);
        }

        private void btnTopView_Click(object sender, RoutedEventArgs e)
        {
            _cameraAngleX = 0;
            _cameraAngleY = 89;
            UpdateCamera();
        }

        private void btnIsoView_Click(object sender, RoutedEventArgs e)
        {
            _cameraAngleX = 45;
            _cameraAngleY = 35;
            UpdateCamera();
        }

        private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _isDragging = true;
                _lastMousePosition = e.GetPosition(viewport3D);
                viewport3D.CaptureMouse();
            }
        }

        private void Viewport_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isDragging)
            {
                System.Windows.Point currentPos = e.GetPosition(viewport3D);
                double deltaX = currentPos.X - _lastMousePosition.X;
                double deltaY = currentPos.Y - _lastMousePosition.Y;

                _cameraAngleX -= deltaX * 0.6;
                _cameraAngleY += deltaY * 0.6;
                _cameraAngleY = Math.Max(5, Math.Min(89, _cameraAngleY));

                _lastMousePosition = currentPos;
                UpdateCamera();
            }
        }

        private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            viewport3D.ReleaseMouseCapture();
        }

        private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            _cameraDistance -= e.Delta * 0.15;
            _cameraDistance = Math.Max(30, Math.Min(800, _cameraDistance));
            UpdateCamera();
        }

        #endregion

        private void btnGenerateRevit_Click(object sender, RoutedEventArgs e)
        {
            if (LayoutTiles.Count == 0 && !ModelRevoque && !ModelBeddingMortar)
            {
                System.Windows.MessageBox.Show("No hay elementos seleccionados para modelar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (cmbTargetCategory?.SelectedItem is ComboBoxItem selItem)
            {
                string tag = selItem.Tag?.ToString() ?? "";
                if (tag == "OST_Walls") TargetSystemCategory = BuiltInCategory.OST_Walls;
                else if (tag == "OST_GenericModel") TargetSystemCategory = BuiltInCategory.OST_GenericModel;
                else TargetSystemCategory = BuiltInCategory.OST_Floors;
            }
            else
            {
                TargetSystemCategory = BuiltInCategory.OST_Floors;
            }

            PreferDirectShapeGeometry = rbDirectShape != null && rbDirectShape.IsChecked == true;

            IsConfirmed = true;
            this.DialogResult = true;
            this.Close();
        }
    }
}
