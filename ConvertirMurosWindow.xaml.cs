using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using Path = System.IO.Path;

namespace RevitToSupabasePlugin
{
    public class BrickDisplayItem
    {
        public BrickType Brick { get; set; }
        public string DisplayName { get; set; }

        public BrickDisplayItem(BrickType brick)
        {
            Brick = brick;
            DisplayName = $"{brick.Name} ({brick.LengthCm}x{brick.WidthCm}x{brick.HeightCm} cm)";
        }
    }

    public enum WallIntersectionRole
    {
        Standard,        // Ladrillo normal de tramo recto
        CornerHeader,    // Ladrillo de cabecera que abraza la esquina exterior
        CornerAbutment,  // Ladrillo ajustado que muere a tope contra la esquina
        TieKey           // Ladrillo de enjarje que penetra la intersección en T
    }

    public class BrickLayoutItem
    {
        public int ZIndex { get; set; }
        public int XIndex { get; set; }
        public double PosX_Ft { get; set; }
        public double PosZ_Ft { get; set; }
        public double Length_Ft { get; set; }
        public double Height_Ft { get; set; }
        
        public double CutLeft_Ft { get; set; }
        public double CutRight_Ft { get; set; }
        public double CutTop_Ft { get; set; }
        
        public bool HasDovela1 { get; set; } // Dovela en la Mitad 1 (Izquierda / Celda 1)
        public bool HasDovela2 { get; set; } // Dovela en la Mitad 2 (Derecha / Celda 2)
        public bool HasDovela => HasDovela1 || HasDovela2;
        public bool IsCanchado { get; set; }

        public WallIntersectionRole Role { get; set; } = WallIntersectionRole.Standard;
    }

    public partial class ConvertirMurosWindow : Window
    {
        public BrickType? SelectedBrick { get; private set; }
        public string SelectedPattern { get; private set; } = "Soga";
        public double PegaCm { get; private set; } = 1.5;
        public double ReviteCm { get; private set; } = 0;
        public double DesfaseCm { get; private set; } = 0;
        public double DovelaWidthCm { get; private set; } = 2.5;
        public double DovelaDepthCm { get; private set; } = 12.5;
        public double DovelaStartDepthCm { get; private set; } = 17.5;
        public double CanchaStartHeightCm { get; private set; } = 0.0;
        public double CanchaAltoCm { get; private set; } = 12.0;
        public double CanchaProfCm { get; private set; } = 8.0;
        public string ReviteTipo { get; private set; } = "Plano";
        public Autodesk.Revit.DB.Material PegaMaterial { get; private set; }
        public double PegaCosto { get; private set; } = 0;
        public string PegaDescripcion { get; private set; } = "";
        public bool VerPega { get; private set; } = false;
        public bool IsConfirmed { get; private set; } = false;

        public List<BrickLayoutItem> LayoutItems { get; private set; } = new List<BrickLayoutItem>();
        
        private double wallLengthFt;
        private double wallHeightFt;

        private Autodesk.Revit.DB.Document _doc;
        
        // 3D Camera variables
        private System.Windows.Point _lastMousePosition;
        private double _cameraAngleX = 0;
        private double _cameraAngleY = 0;
        private double _cameraDistance = 100;
        private bool _isDragging = false;

        public ConvertirMurosWindow(int wallCount, double wallLenFt, double wallHgtFt, List<BrickType> availableBricks, Autodesk.Revit.DB.Document doc = null)
        {
            InitializeComponent();
            _doc = doc;

            wallLengthFt = wallLenFt;
            wallHeightFt = wallHgtFt;

            if (txtSummary != null) txtSummary.Text = $"Has seleccionado {wallCount} muro(s) para convertir.";
            if (txtWallDims != null) txtWallDims.Text = $"Dimensiones base: {(wallLenFt * 0.3048):F2}m x {(wallHgtFt * 0.3048):F2}m";

            LoadBricksFromList(availableBricks);
            LoadMaterials();

            this.Loaded += (s, e) =>
            {
                RebuildLayout();
            };
        }

        private void RebuildLayout()
        {
            GenerateBaseLayout();
        }

        private void LoadMaterials()
        {
            try
            {
                if (_doc == null) return;
                var materials = new Autodesk.Revit.DB.FilteredElementCollector(_doc)
                    .OfClass(typeof(Autodesk.Revit.DB.Material))
                    .Cast<Autodesk.Revit.DB.Material>()
                    .OrderBy(m => m.Name)
                    .ToList();
                cmbPegaMaterial.ItemsSource = materials;
                if (materials.Count > 0) cmbPegaMaterial.SelectedIndex = 0;
            }
            catch { }
        }

        private void LoadBricksFromList(List<BrickType> availableBricks)
        {
            var displayItems = availableBricks.Select(b => new BrickDisplayItem(b)).ToList();
            cmbLadrillos.ItemsSource = displayItems;
            if (displayItems.Count > 0)
            {
                cmbLadrillos.SelectedIndex = 0;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            GenerateBaseLayout();
        }

        private void Input_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (this.IsLoaded) GenerateBaseLayout();
        }

        private void Input_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (this.IsLoaded) GenerateBaseLayout();
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            GenerateBaseLayout();
        }

        private double ParseDoubleSafe(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Replace(',', '.'); // Fix culture parsing issues
            if (double.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double result))
                return result;
            return 0;
        }

        private void GenerateBaseLayout()
        {
            try
            {
                if (viewport3D == null) return;
                LayoutItems.Clear();

                if (cmbLadrillos.SelectedItem is not BrickDisplayItem selectedItem) return;
                
                BrickType brick = selectedItem.Brick;
                string pattern = (cmbPattern.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Soga";

                double pegaFt = 1.5 / 30.48;
                if (txtPega != null) pegaFt = ParseDoubleSafe(txtPega.Text) / 30.48;

                double brickLFt = pattern.Contains("Soga") ? (brick.LengthCm / 30.48) : (brick.WidthCm / 30.48);
                double brickHFt = brick.HeightCm / 30.48;

                double stepX = brickLFt + pegaFt;
                double stepZ = brickHFt + pegaFt;

                int zIndex = 0;
                bool hasPega = (chkVerPega?.IsChecked == true && pegaFt > 0);

                double currentZ = hasPega ? pegaFt : 0;
                double startXOffset = hasPega ? pegaFt : 0;
                double maxWallX = hasPega ? (wallLengthFt - pegaFt) : wallLengthFt;

                while (currentZ < wallHeightFt)
                {
                    double currentX = startXOffset;
                    int xIndex = 0;

                    double shift = 0;
                    if (pattern.Contains("Soga") || pattern.Contains("Tizón"))
                    {
                        if (zIndex % 2 != 0) shift = stepX / 2.0;
                    }
                    else if (pattern.Contains("Inglés"))
                    {
                        if (zIndex % 2 != 0)
                        {
                            brickLFt = brick.WidthCm / 30.48;
                            stepX = brickLFt + pegaFt;
                            shift = stepX / 2.0;
                        }
                        else
                        {
                            brickLFt = brick.LengthCm / 30.48;
                            stepX = brickLFt + pegaFt;
                            shift = 0;
                        }
                    }

                    // Primer medio ladrillo si hay shift
                    if (shift > 0)
                    {
                        double shiftBrickLen = shift - pegaFt;
                        if (shiftBrickLen > 0)
                            AddBrickToLayout(startXOffset, currentZ, shiftBrickLen, brickHFt, zIndex, -1, true, false, (currentZ + brickHFt > wallHeightFt));
                        currentX = startXOffset + shift;
                    }

                    while (currentX < maxWallX)
                    {
                        double remainingX = maxWallX - currentX;
                        double len = remainingX < brickLFt ? remainingX : brickLFt;
                        
                        bool isCutR = remainingX < brickLFt;
                        
                        AddBrickToLayout(currentX, currentZ, len, brickHFt, zIndex, xIndex, false, isCutR, (currentZ + brickHFt > wallHeightFt));
                        
                        currentX += stepX;
                        xIndex++;
                    }

                    currentZ += stepZ;
                    zIndex++;
                }

                DrawLayout();
            }
            catch { }
        }

        private void AddBrickToLayout(double xFt, double zFt, double lenFt, double hFt, int zIdx, int xIdx, bool cutL, bool cutR, bool cutT)
        {
            var item = new BrickLayoutItem
            {
                PosX_Ft = xFt,
                PosZ_Ft = zFt,
                Length_Ft = lenFt,
                Height_Ft = cutT ? (wallHeightFt - zFt) : hFt,
                CutLeft_Ft = cutL ? lenFt : 0, 
                CutRight_Ft = cutR ? lenFt : 0,
                CutTop_Ft = cutT ? (wallHeightFt - zFt) : 0,
                ZIndex = zIdx,
                XIndex = xIdx,
                HasDovela1 = false,
                HasDovela2 = false,
                IsCanchado = false
            };
            LayoutItems.Add(item);
        }

        private Dictionary<Model3D, BrickLayoutItem> _brickModels = new Dictionary<Model3D, BrickLayoutItem>();

        private void DrawLayout()
        {
            try
            {
                if (modelGroup == null) return;
                
                // Limpiar luces y modelos anteriores
                modelGroup.Children.Clear();
                _brickModels.Clear();

                // Iluminación de estudio 3D con luces ambiental, principal y de relleno
                modelGroup.Children.Add(new AmbientLight(System.Windows.Media.Color.FromRgb(120, 120, 130)));
                modelGroup.Children.Add(new DirectionalLight(System.Windows.Media.Color.FromRgb(240, 240, 248), new Vector3D(-1, -2, -1.5)));
                modelGroup.Children.Add(new DirectionalLight(System.Windows.Media.Color.FromRgb(140, 150, 170), new Vector3D(1, 1, 1)));

                bool verPega3D = chkVerPega.IsChecked == true;
                double pegaThickFt = (ParseDoubleSafe(txtPega.Text) > 0 ? ParseDoubleSafe(txtPega.Text) : 1.5) / 30.48;

                foreach (var item in LayoutItems)
                {
                    // Ladrillos canchados destacados en Ámbar/Dorado cálido, Estándar en terracota cerámico
                    System.Windows.Media.Color brickColor = item.IsCanchado 
                        ? System.Windows.Media.Color.FromRgb(245, 158, 11)  // Vibrant Amber / Gold
                        : System.Windows.Media.Color.FromRgb(217, 107, 39);  // Terracotta Orange

                    System.Windows.Media.Color wireColor = item.IsCanchado
                        ? System.Windows.Media.Color.FromRgb(251, 191, 36)  // Bright Gold Wireframe
                        : System.Windows.Media.Color.FromRgb(30, 41, 59);    // Slate Dark Wireframe

                    double depthFt = (SelectedBrick?.WidthCm ?? 11.5) / 30.48;

                    // Sólido del ladrillo con sombreado de caras nítido y especularidad
                    GeometryModel3D brickModel = CreateBoxModel(item.Length_Ft, depthFt, item.Height_Ft, brickColor, 1.0);
                    
                    Transform3DGroup t = new Transform3DGroup();
                    double cx = item.PosX_Ft + (item.Length_Ft / 2.0);
                    double cy = 0;
                    double cz = item.PosZ_Ft + (item.Height_Ft / 2.0);
                    t.Children.Add(new TranslateTransform3D(cx, cy, cz));
                    
                    brickModel.Transform = t;
                    modelGroup.Children.Add(brickModel);
                    
                    // Bordes finos y definidos (grosor 0.003 ft ~ 1mm)
                    double wireThickness = item.IsCanchado ? 0.004 : 0.003;
                    Model3DGroup edges = CreateWireframeModel(item.Length_Ft, depthFt, item.Height_Ft, wireColor, wireThickness);
                    edges.Transform = t;
                    modelGroup.Children.Add(edges);

                    _brickModels[brickModel] = item;

                    // Dibujar pega si aplica (semi-transparente elegante)
                    if (verPega3D)
                    {
                        GeometryModel3D pegaModel = CreateBoxModel(item.Length_Ft + pegaThickFt, depthFt + pegaThickFt, item.Height_Ft + pegaThickFt, System.Windows.Media.Color.FromRgb(226, 232, 240), 0.25);
                        Transform3DGroup pt = new Transform3DGroup();
                        pt.Children.Add(new TranslateTransform3D(cx + pegaThickFt/2, cy + pegaThickFt/2, cz + pegaThickFt/2));
                        pegaModel.Transform = pt;
                        modelGroup.Children.Add(pegaModel);
                    }

                    double startDepthFt = (DovelaStartDepthCm > 0 ? DovelaStartDepthCm : 17.5) / 30.48;
                    double dovelaW = (DovelaWidthCm > 0 ? DovelaWidthCm : 2.5) / 30.48;
                    double dovelaL = (DovelaDepthCm > 0 ? DovelaDepthCm : 12.5) / 30.48;

                    // Dovela Mitad 1 (Izquierda / Celda 1) - Azul cobalto destacado
                    if (item.HasDovela1)
                    {
                        double c1 = item.PosX_Ft + startDepthFt;
                        GeometryModel3D dovela1Model = CreateBoxModel(dovelaL, dovelaW, item.Height_Ft + 0.05, System.Windows.Media.Color.FromRgb(37, 99, 235), 1.0);
                        Transform3DGroup dt1 = new Transform3DGroup();
                        dt1.Children.Add(new TranslateTransform3D(c1, cy, cz));
                        dovela1Model.Transform = dt1;
                        modelGroup.Children.Add(dovela1Model);
                    }

                    // Dovela Mitad 2 (Derecha / Celda 2) - Azul cobalto destacado
                    if (item.HasDovela2)
                    {
                        double c2 = item.PosX_Ft + item.Length_Ft - startDepthFt;
                        GeometryModel3D dovela2Model = CreateBoxModel(dovelaL, dovelaW, item.Height_Ft + 0.05, System.Windows.Media.Color.FromRgb(37, 99, 235), 1.0);
                        Transform3DGroup dt2 = new Transform3DGroup();
                        dt2.Children.Add(new TranslateTransform3D(c2, cy, cz));
                        dovela2Model.Transform = dt2;
                        modelGroup.Children.Add(dovela2Model);
                    }
                }

                // Ajustar cámara para que vea todo el muro
                if (!_isDragging && wallLengthFt > 0)
                {
                    _cameraDistance = Math.Max(wallLengthFt, wallHeightFt) * 1.5;
                    _cameraAngleX = 45;
                    _cameraAngleY = 60;
                    UpdateCamera();
                }
            }
            catch { }
        }

        private GeometryModel3D CreateBoxModel(double l, double w, double h, System.Windows.Media.Color color, double opacity)
        {
            MeshGeometry3D mesh = new MeshGeometry3D();
            double x = l / 2.0;
            double y = w / 2.0;
            double z = h / 2.0;

            // 6 Caras con 4 vértices cada una para asegurar normales de cara planas e iluminadas limpiamente
            // Cara Frontal (-Y)
            mesh.Positions.Add(new Point3D(-x, -y, -z));
            mesh.Positions.Add(new Point3D( x, -y, -z));
            mesh.Positions.Add(new Point3D( x, -y,  z));
            mesh.Positions.Add(new Point3D(-x, -y,  z));
            for (int i = 0; i < 4; i++) mesh.Normals.Add(new Vector3D(0, -1, 0));

            // Cara Trasera (+Y)
            mesh.Positions.Add(new Point3D( x,  y, -z));
            mesh.Positions.Add(new Point3D(-x,  y, -z));
            mesh.Positions.Add(new Point3D(-x,  y,  z));
            mesh.Positions.Add(new Point3D( x,  y,  z));
            for (int i = 0; i < 4; i++) mesh.Normals.Add(new Vector3D(0, 1, 0));

            // Cara Superior (+Z)
            mesh.Positions.Add(new Point3D(-x, -y,  z));
            mesh.Positions.Add(new Point3D( x, -y,  z));
            mesh.Positions.Add(new Point3D( x,  y,  z));
            mesh.Positions.Add(new Point3D(-x,  y,  z));
            for (int i = 0; i < 4; i++) mesh.Normals.Add(new Vector3D(0, 0, 1));

            // Cara Inferior (-Z)
            mesh.Positions.Add(new Point3D(-x,  y, -z));
            mesh.Positions.Add(new Point3D( x,  y, -z));
            mesh.Positions.Add(new Point3D( x, -y, -z));
            mesh.Positions.Add(new Point3D(-x, -y, -z));
            for (int i = 0; i < 4; i++) mesh.Normals.Add(new Vector3D(0, 0, -1));

            // Cara Izquierda (-X)
            mesh.Positions.Add(new Point3D(-x,  y, -z));
            mesh.Positions.Add(new Point3D(-x, -y, -z));
            mesh.Positions.Add(new Point3D(-x, -y,  z));
            mesh.Positions.Add(new Point3D(-x,  y,  z));
            for (int i = 0; i < 4; i++) mesh.Normals.Add(new Vector3D(-1, 0, 0));

            // Cara Derecha (+X)
            mesh.Positions.Add(new Point3D( x, -y, -z));
            mesh.Positions.Add(new Point3D( x,  y, -z));
            mesh.Positions.Add(new Point3D( x,  y,  z));
            mesh.Positions.Add(new Point3D( x, -y,  z));
            for (int i = 0; i < 4; i++) mesh.Normals.Add(new Vector3D(1, 0, 0));

            for (int face = 0; face < 6; face++)
            {
                int baseIdx = face * 4;
                mesh.TriangleIndices.Add(baseIdx);
                mesh.TriangleIndices.Add(baseIdx + 1);
                mesh.TriangleIndices.Add(baseIdx + 2);

                mesh.TriangleIndices.Add(baseIdx);
                mesh.TriangleIndices.Add(baseIdx + 2);
                mesh.TriangleIndices.Add(baseIdx + 3);
            }

            SolidColorBrush brush = new SolidColorBrush(color) { Opacity = opacity };
            MaterialGroup matGroup = new MaterialGroup();
            matGroup.Children.Add(new DiffuseMaterial(brush));
            matGroup.Children.Add(new SpecularMaterial(new SolidColorBrush(System.Windows.Media.Color.FromArgb(140, 255, 255, 255)), 60));

            return new GeometryModel3D(mesh, matGroup);
        }

        private Model3DGroup CreateWireframeModel(double l, double w, double h, System.Windows.Media.Color color, double thickness)
        {
            Model3DGroup edgesGroup = new Model3DGroup();
            
            double x = l / 2.0;
            double y = w / 2.0;
            double z = h / 2.0;

            // 4 bordes paralelos a X
            edgesGroup.Children.Add(CreateEdge(l, thickness, thickness, 0, y, z, color));
            edgesGroup.Children.Add(CreateEdge(l, thickness, thickness, 0, -y, z, color));
            edgesGroup.Children.Add(CreateEdge(l, thickness, thickness, 0, y, -z, color));
            edgesGroup.Children.Add(CreateEdge(l, thickness, thickness, 0, -y, -z, color));

            // 4 bordes paralelos a Y
            edgesGroup.Children.Add(CreateEdge(thickness, w, thickness, x, 0, z, color));
            edgesGroup.Children.Add(CreateEdge(thickness, w, thickness, -x, 0, z, color));
            edgesGroup.Children.Add(CreateEdge(thickness, w, thickness, x, 0, -z, color));
            edgesGroup.Children.Add(CreateEdge(thickness, w, thickness, -x, 0, -z, color));

            // 4 bordes paralelos a Z
            edgesGroup.Children.Add(CreateEdge(thickness, thickness, h, x, y, 0, color));
            edgesGroup.Children.Add(CreateEdge(thickness, thickness, h, -x, y, 0, color));
            edgesGroup.Children.Add(CreateEdge(thickness, thickness, h, x, -y, 0, color));
            edgesGroup.Children.Add(CreateEdge(thickness, thickness, h, -x, -y, 0, color));

            return edgesGroup;
        }

        private GeometryModel3D CreateEdge(double l, double w, double h, double cx, double cy, double cz, System.Windows.Media.Color color)
        {
            GeometryModel3D edge = CreateBoxModel(l, w, h, color, 1.0);
            edge.Transform = new TranslateTransform3D(cx, cy, cz);
            return edge;
        }

        private void UpdateCamera()
        {
            if (camera3D == null) return;
            
            double radX = _cameraAngleX * Math.PI / 180.0;
            double radY = _cameraAngleY * Math.PI / 180.0;
            
            // Orbitando alrededor del centro del muro
            double targetX = wallLengthFt / 2.0;
            double targetZ = wallHeightFt / 2.0;

            double cx = targetX + _cameraDistance * Math.Sin(radY) * Math.Cos(radX);
            double cy = _cameraDistance * Math.Sin(radY) * Math.Sin(radX);
            double cz = targetZ + _cameraDistance * Math.Cos(radY);

            camera3D.Position = new Point3D(cx, cy, cz);
            
            Vector3D look = new Vector3D(targetX - cx, -cy, targetZ - cz);
            look.Normalize();
            camera3D.LookDirection = look;
            camera3D.UpDirection = (_cameraAngleY < 0) ? new Vector3D(0, 0, -1) : new Vector3D(0, 0, 1);

            if (lblViewOrientation != null)
            {
                double normY = (_cameraAngleY % 360 + 360) % 360;
                if (normY < 15 || normY > 345) lblViewOrientation.Text = "Vista Superior";
                else lblViewOrientation.Text = "Vista 3D";
            }
        }

        private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _lastMousePosition = e.GetPosition(this);
            Mouse.Capture((IInputElement)sender);

            // Hit Testing para edición
            PointHitTestParameters hitParams = new PointHitTestParameters(e.GetPosition(viewport3D));
            VisualTreeHelper.HitTest(viewport3D, null, HitTestCallback, hitParams);
        }

        private HitTestResultBehavior HitTestCallback(HitTestResult result)
        {
            if (result is RayMeshGeometry3DHitTestResult meshResult)
            {
                if (meshResult.ModelHit is GeometryModel3D hitModel && _brickModels.ContainsKey(hitModel))
                {
                    ApplyToolToBrick(_brickModels[hitModel], meshResult.PointHit);
                    return HitTestResultBehavior.Stop;
                }
            }
            return HitTestResultBehavior.Continue;
        }

        private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            Mouse.Capture(null);
        }

        private void Viewport_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isDragging)
            {
                System.Windows.Point currentPos = e.GetPosition(this);
                double deltaX = currentPos.X - _lastMousePosition.X;
                double deltaY = currentPos.Y - _lastMousePosition.Y;

                _cameraAngleX -= deltaX * 0.5;
                _cameraAngleY -= deltaY * 0.5;
                _lastMousePosition = currentPos;
                UpdateCamera();
            }
        }

        private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            _cameraDistance -= e.Delta * 0.1;
            if (_cameraDistance < 2) _cameraDistance = 2;
            UpdateCamera();
        }

        private void ApplyToolToBrick(BrickLayoutItem item, Point3D? hitPoint = null)
        {
            if (toolSelect.IsChecked == true) return;

            if (toolDovela.IsChecked == true)
            {
                double startDepthFt = (DovelaStartDepthCm > 0 ? DovelaStartDepthCm : 17.5) / 30.48;
                double c1 = item.PosX_Ft + startDepthFt;
                double c2 = item.PosX_Ft + item.Length_Ft - startDepthFt;
                double midPoint = (c1 + c2) / 2.0;

                if (hitPoint.HasValue)
                {
                    if (hitPoint.Value.X < midPoint)
                    {
                        item.HasDovela1 = !item.HasDovela1;
                    }
                    else
                    {
                        item.HasDovela2 = !item.HasDovela2;
                    }
                }
                else
                {
                    if (!item.HasDovela1 && !item.HasDovela2) { item.HasDovela1 = true; }
                    else if (item.HasDovela1 && !item.HasDovela2) { item.HasDovela1 = false; item.HasDovela2 = true; }
                    else if (!item.HasDovela1 && item.HasDovela2) { item.HasDovela1 = true; }
                    else { item.HasDovela1 = false; item.HasDovela2 = false; }
                }
            }
            else if (toolCanchado.IsChecked == true)
            {
                item.IsCanchado = !item.IsCanchado;
            }
            else if (toolTrimL.IsChecked == true)
            {
                double half = item.Length_Ft / 2.0;
                item.Length_Ft = half;
                item.PosX_Ft += half;
                item.CutLeft_Ft = half;
            }
            else if (toolTrimR.IsChecked == true)
            {
                double half = item.Length_Ft / 2.0;
                item.Length_Ft = half;
                item.CutRight_Ft = half;
            }
            
            DrawLayout();
        }

        private void BtnConvert_Click(object sender, RoutedEventArgs e)
        {
            if (cmbLadrillos.SelectedItem is BrickDisplayItem selectedItem)
            {
                SelectedBrick = selectedItem.Brick;
                SelectedPattern = (cmbPattern.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Soga";
                
                PegaCm = ParseDoubleSafe(txtPega.Text);
                ReviteCm = ParseDoubleSafe(txtRevite.Text);
                DesfaseCm = 0;
                DovelaWidthCm = ParseDoubleSafe(txtDovelaWidth.Text);
                DovelaDepthCm = ParseDoubleSafe(txtDovelaDepth.Text);
                DovelaStartDepthCm = ParseDoubleSafe(txtDovelaStartDepth.Text);
                CanchaStartHeightCm = ParseDoubleSafe(txtCanchaStartHeight.Text);
                CanchaAltoCm = ParseDoubleSafe(txtCanchaAlto.Text);
                CanchaProfCm = ParseDoubleSafe(txtCanchaProf.Text);
                
                ReviteTipo = "Recta";

                PegaMaterial = cmbPegaMaterial.SelectedItem as Autodesk.Revit.DB.Material;
                PegaCosto = ParseDoubleSafe(txtPegaCost.Text);
                PegaDescripcion = txtPegaDesc.Text;
                
                VerPega = chkVerPega.IsChecked == true;
                
                IsConfirmed = true;
                this.Close();
            }
            else
            {
                System.Windows.MessageBox.Show("Debes seleccionar una familia de ladrillos.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
