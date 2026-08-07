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
        
        public bool HasDovela { get; set; }
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
        public double DovelaDiamCm { get; private set; } = 5.0;
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
            txtSummary.Text = $"Has seleccionado {wallCount} muro(s) para convertir.";
            wallLengthFt = wallLenFt;
            wallHeightFt = wallHgtFt;
            
            txtWallDims.Text = $"Dimensiones base: {(wallLenFt * 0.3048):F2}m x {(wallHgtFt * 0.3048):F2}m";
            
            LoadBricksFromList(availableBricks);
            LoadMaterials();
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
                double currentZ = 0;

                while (currentZ < wallHeightFt)
                {
                    double currentX = 0;
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
                            AddBrickToLayout(0, currentZ, shiftBrickLen, brickHFt, zIndex, -1, true, false, (currentZ + brickHFt > wallHeightFt));
                        currentX = shift;
                    }

                    while (currentX < wallLengthFt)
                    {
                        double remainingX = wallLengthFt - currentX;
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
                HasDovela = false,
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

                modelGroup.Children.Add(new AmbientLight(System.Windows.Media.Color.FromRgb(100, 100, 100)));
                modelGroup.Children.Add(new DirectionalLight(System.Windows.Media.Colors.White, new Vector3D(-1, -1, -1)));
                modelGroup.Children.Add(new DirectionalLight(System.Windows.Media.Color.FromRgb(80, 80, 80), new Vector3D(1, 1, 1)));

                bool verPega3D = chkVerPega.IsChecked == true;
                double pegaThickFt = (ParseDoubleSafe(txtPega.Text) > 0 ? ParseDoubleSafe(txtPega.Text) : 1.5) / 30.48;

                foreach (var item in LayoutItems)
                {
                    System.Windows.Media.Color brickColor = item.IsCanchado 
                        ? System.Windows.Media.Color.FromRgb(180, 100, 50) 
                        : System.Windows.Media.Color.FromRgb(221, 107, 32);

                    double depthFt = (SelectedBrick?.WidthCm ?? 11.5) / 30.48;

                    // El cubo se crea centrado en 0,0,0, así que lo trasladamos a su posición
                    GeometryModel3D brickModel = CreateBoxModel(item.Length_Ft, depthFt, item.Height_Ft, brickColor, 1.0);
                    
                    Transform3DGroup t = new Transform3DGroup();
                    // Posicionamos usando el centro del ladrillo
                    double cx = item.PosX_Ft + (item.Length_Ft / 2.0);
                    double cy = 0; // profundidad
                    double cz = item.PosZ_Ft + (item.Height_Ft / 2.0);
                    t.Children.Add(new TranslateTransform3D(cx, cy, cz));
                    
                    brickModel.Transform = t;
                    modelGroup.Children.Add(brickModel);
                    
                    Model3DGroup edges = CreateWireframeModel(item.Length_Ft, depthFt, item.Height_Ft, System.Windows.Media.Colors.LightBlue, 0.02);
                    edges.Transform = t;
                    modelGroup.Children.Add(edges);

                    _brickModels[brickModel] = item;

                    // Dibujar pega si aplica
                    if (verPega3D)
                    {
                        GeometryModel3D pegaModel = CreateBoxModel(item.Length_Ft + pegaThickFt, depthFt + pegaThickFt, item.Height_Ft + pegaThickFt, System.Windows.Media.Colors.White, 0.3);
                        Transform3DGroup pt = new Transform3DGroup();
                        pt.Children.Add(new TranslateTransform3D(cx + pegaThickFt/2, cy + pegaThickFt/2, cz + pegaThickFt/2));
                        pegaModel.Transform = pt;
                        modelGroup.Children.Add(pegaModel);
                    }

                    // Dovela (marcador cilíndrico / hueco)
                    if (item.HasDovela)
                    {
                        GeometryModel3D dovelaModel = CreateBoxModel(item.Length_Ft * 0.3, depthFt * 0.4, item.Height_Ft + 0.1, System.Windows.Media.Colors.DarkBlue, 1.0);
                        Transform3DGroup dt = new Transform3DGroup();
                        dt.Children.Add(new TranslateTransform3D(cx, cy, cz));
                        dovelaModel.Transform = dt;
                        modelGroup.Children.Add(dovelaModel);
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

            mesh.Positions.Add(new Point3D(-x, -y, -z)); // 0
            mesh.Positions.Add(new Point3D( x, -y, -z)); // 1
            mesh.Positions.Add(new Point3D( x,  y, -z)); // 2
            mesh.Positions.Add(new Point3D(-x,  y, -z)); // 3
            mesh.Positions.Add(new Point3D(-x, -y,  z)); // 4
            mesh.Positions.Add(new Point3D( x, -y,  z)); // 5
            mesh.Positions.Add(new Point3D( x,  y,  z)); // 6
            mesh.Positions.Add(new Point3D(-x,  y,  z)); // 7

            int[] indices = new int[]
            {
                0, 1, 2,  0, 2, 3, // Bottom
                4, 6, 5,  4, 7, 6, // Top
                0, 5, 1,  0, 4, 5, // Front
                3, 2, 6,  3, 6, 7, // Back
                0, 3, 7,  0, 7, 4, // Left
                1, 5, 6,  1, 6, 2  // Right
            };

            foreach (int i in indices) mesh.TriangleIndices.Add(i);

            SolidColorBrush brush = new SolidColorBrush(color) { Opacity = opacity };
            return new GeometryModel3D(mesh, new DiffuseMaterial(brush));
        }

        private Model3DGroup CreateWireframeModel(double l, double w, double h, System.Windows.Media.Color color, double thickness)
        {
            Model3DGroup edgesGroup = new Model3DGroup();
            
            double x = l / 2.0;
            double y = w / 2.0;
            double z = h / 2.0;

            // 4 edges parallel to X
            edgesGroup.Children.Add(CreateEdge(l, thickness, thickness, 0, y, z, color));
            edgesGroup.Children.Add(CreateEdge(l, thickness, thickness, 0, -y, z, color));
            edgesGroup.Children.Add(CreateEdge(l, thickness, thickness, 0, y, -z, color));
            edgesGroup.Children.Add(CreateEdge(l, thickness, thickness, 0, -y, -z, color));

            // 4 edges parallel to Y
            edgesGroup.Children.Add(CreateEdge(thickness, w, thickness, x, 0, z, color));
            edgesGroup.Children.Add(CreateEdge(thickness, w, thickness, -x, 0, z, color));
            edgesGroup.Children.Add(CreateEdge(thickness, w, thickness, x, 0, -z, color));
            edgesGroup.Children.Add(CreateEdge(thickness, w, thickness, -x, 0, -z, color));

            // 4 edges parallel to Z
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
                    ApplyToolToBrick(_brickModels[hitModel]);
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

        private void ApplyToolToBrick(BrickLayoutItem item)
        {
            if (toolSelect.IsChecked == true) return;

            if (toolDovela.IsChecked == true)
            {
                item.HasDovela = !item.HasDovela;
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
                DesfaseCm = ParseDoubleSafe(txtPegaOffset.Text);
                DovelaDiamCm = ParseDoubleSafe(txtDovelaDiam.Text);
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
