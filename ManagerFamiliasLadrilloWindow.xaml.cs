using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Media.Media3D;
using System.Windows.Input;

namespace RevitToSupabasePlugin
{
    public partial class ManagerFamiliasLadrilloWindow : Window
    {
        private string? _selectedProfilePath = null;
        private string _jsonPath;
        private BricksDatabase _db;

        // Variables para la cámara 3D
        private System.Windows.Point _lastMousePosition;
        private double _cameraAngleX = 45;
        private double _cameraAngleY = 45;
        private double _cameraDistance = 100;
        private bool _isDragging = false;

        public ManagerFamiliasLadrilloWindow()
        {
            InitializeComponent();
            LoadBricks();
            UpdateCamera();
        }

        private void LoadBricks()
        {
            try
            {
                string assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string dirPath = System.IO.Path.GetDirectoryName(assemblyPath) ?? string.Empty;
                _jsonPath = System.IO.Path.Combine(dirPath, "BricksDB.json");

                if (!File.Exists(_jsonPath))
                {
                    string projRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(dirPath, "..", ".."));
                    _jsonPath = System.IO.Path.Combine(projRoot, "BricksDB.json");
                }

                if (File.Exists(_jsonPath))
                {
                    string jsonContent = File.ReadAllText(_jsonPath);
                    _db = JsonSerializer.Deserialize<BricksDatabase>(jsonContent) ?? new BricksDatabase { Bricks = new List<BrickType>() };
                }
                else
                {
                    _db = new BricksDatabase { Bricks = new List<BrickType>() };
                }
            }
            catch
            {
                _db = new BricksDatabase { Bricks = new List<BrickType>() };
            }
        }

        private void BtnBrowseProfile_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Seleccionar Perfil de Ladrillo",
                Filter = "Revit Family Files (*.rfa)|*.rfa|All Files (*.*)|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (openFileDialog.ShowDialog() == true)
            {
                _selectedProfilePath = openFileDialog.FileName;
                txtProfilePath.Text = System.IO.Path.GetFileName(_selectedProfilePath);
            }
        }

        private void CmbMaterial_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbMaterial == null || txtName == null || txtLength == null || txtWidth == null || txtHeight == null) return;

            string material = (cmbMaterial.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "";

            switch (material)
            {
                case "Ladrillo N°4 Rayado":
                    txtName.Text = "Ladrillo N°4 Rayado";
                    txtWidth.Text = "9";
                    txtHeight.Text = "19";
                    txtLength.Text = "38";
                    break;
                case "Ladrillo N°4 Barranquilla":
                    txtName.Text = "Ladrillo N°4 Barranquilla";
                    txtWidth.Text = "9";
                    txtHeight.Text = "20";
                    txtLength.Text = "40";
                    break;
                case "Bloque N°4 Tradicional Helios":
                    txtName.Text = "Bloque N°4 Tradicional Helios";
                    txtWidth.Text = "9";
                    txtHeight.Text = "23";
                    txtLength.Text = "33";
                    break;
                case "Bloque N°5 Tradicional Helios":
                    txtName.Text = "Bloque N°5 Tradicional Helios";
                    txtWidth.Text = "12";
                    txtHeight.Text = "23";
                    txtLength.Text = "33";
                    break;
                case "Bloquelón Santafe":
                    txtName.Text = "Bloquelón Santafe";
                    txtWidth.Text = "22.8";
                    txtHeight.Text = "7.7";
                    txtLength.Text = "80";
                    break;
                case "Personalizado...":
                    txtName.Text = "Ladrillo Personalizado";
                    break;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            UpdatePreview();
        }

        private void Input_Changed(object sender, RoutedEventArgs e)
        {
            if (this.IsLoaded)
            {
                UpdatePreview();
            }
        }

        private void UpdateCamera()
        {
            if (camera3D == null || gizmoCamera == null) return;
            
            double radX = _cameraAngleX * Math.PI / 180.0;
            double radY = _cameraAngleY * Math.PI / 180.0;
            
            double x = _cameraDistance * Math.Sin(radY) * Math.Cos(radX);
            double y = _cameraDistance * Math.Sin(radY) * Math.Sin(radX);
            double z = _cameraDistance * Math.Cos(radY);

            Point3D pos = new Point3D(x, y, z);
            Vector3D look = new Vector3D(-x, -y, -z);
            look.Normalize();

            camera3D.Position = pos;
            camera3D.LookDirection = look;

            // Para que la vista no se "vuelque" si pasamos por arriba/abajo
            if (_cameraAngleY < 0) camera3D.UpDirection = new Vector3D(0, 0, -1);
            else camera3D.UpDirection = new Vector3D(0, 0, 1);

            // Actualizar gizmo (distancia fija)
            double gx = 10 * Math.Sin(radY) * Math.Cos(radX);
            double gy = 10 * Math.Sin(radY) * Math.Sin(radX);
            double gz = 10 * Math.Cos(radY);
            
            gizmoCamera.Position = new Point3D(gx, gy, gz);
            gizmoCamera.LookDirection = new Vector3D(-gx, -gy, -gz);
            gizmoCamera.UpDirection = camera3D.UpDirection;

            UpdateOrientationLabel();
        }

        private void UpdateOrientationLabel()
        {
            if (lblViewOrientation == null) return;
            
            // Lógica simple para determinar nombre de vista basado en ángulos
            double normX = (_cameraAngleX % 360 + 360) % 360;
            double normY = (_cameraAngleY % 360 + 360) % 360;

            if (normY < 15 || normY > 345) lblViewOrientation.Text = "Vista Superior";
            else if (normY > 165 && normY < 195) lblViewOrientation.Text = "Vista Inferior";
            else
            {
                if (normX < 45 || normX > 315) lblViewOrientation.Text = "Vista Lateral (X)";
                else if (normX >= 45 && normX <= 135) lblViewOrientation.Text = "Vista Frontal (Y)";
                else if (normX > 135 && normX < 225) lblViewOrientation.Text = "Vista Lateral Trasera (-X)";
                else lblViewOrientation.Text = "Vista Trasera (-Y)";
                
                // Si está entre ángulos, decir isométrico
                if (normX % 90 > 15 && normX % 90 < 75)
                    lblViewOrientation.Text = "Vista Isométrica";
            }
        }

        private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _lastMousePosition = e.GetPosition(this);
            Mouse.Capture((IInputElement)sender);
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
            if (_cameraDistance < 10) _cameraDistance = 10;
            if (_cameraDistance > 500) _cameraDistance = 500;
            UpdateCamera();
        }

        private void UpdatePreview()
        {
            try
            {
                if (modelGroup == null || gizmoGroup == null) return;
                
                // Limpiar modelo y luces por defecto
                modelGroup.Children.Clear();
                modelGroup.Children.Add(new AmbientLight(System.Windows.Media.Color.FromRgb(100, 100, 100)));
                modelGroup.Children.Add(new DirectionalLight(System.Windows.Media.Colors.White, new Vector3D(-1, -1, -1)));
                modelGroup.Children.Add(new DirectionalLight(System.Windows.Media.Color.FromRgb(100, 100, 100), new Vector3D(1, 1, 1)));

                gizmoGroup.Children.Clear();
                gizmoGroup.Children.Add(new AmbientLight(System.Windows.Media.Colors.White));

                double.TryParse(txtLength.Text, out double length);
                double.TryParse(txtWidth.Text, out double width);
                double.TryParse(txtHeight.Text, out double height);

                length = Math.Max(1, length);
                width = Math.Max(1, width);
                height = Math.Max(1, height);

                if (lblBrickSize != null)
                    lblBrickSize.Text = $"L: {length}cm x W: {width}cm x H: {height}cm";

                System.Windows.Media.Color brickColor = (chkCanchado != null && chkCanchado.IsChecked == true) 
                    ? System.Windows.Media.Color.FromRgb(180, 100, 50) 
                    : System.Windows.Media.Color.FromRgb(79, 209, 197);

                // Dibujar Ladrillo
                modelGroup.Children.Add(CreateBoxModel(length, width, height, brickColor, 1.0));

                // Dibujar Pega
                if (chkPega != null && chkPega.IsChecked == true)
                {
                    double pegaThick = 1.5;
                    // Caja ligeramente más grande y transparente
                    GeometryModel3D pegaModel = CreateBoxModel(length + pegaThick, width + pegaThick, height + pegaThick, System.Windows.Media.Colors.White, 0.3);
                    
                    // Desplazamos la pega para que sobresalga
                    Transform3DGroup t = new Transform3DGroup();
                    t.Children.Add(new TranslateTransform3D(pegaThick/2, pegaThick/2, pegaThick/2));
                    pegaModel.Transform = t;
                    
                    modelGroup.Children.Add(pegaModel);
                }

                // Dibujar Dovela (Cilindro simulado/Prisma oscuro)
                if (chkDovela != null && chkDovela.IsChecked == true)
                {
                    // Prism interior oscuro simulando agujero
                    GeometryModel3D dovelaModel = CreateBoxModel(length * 0.4, width * 0.4, height + 0.1, System.Windows.Media.Colors.DarkBlue, 1.0);
                    modelGroup.Children.Add(dovelaModel);
                }

                // Gizmo
                gizmoGroup.Children.Add(CreateBoxModel(4, 0.2, 0.2, System.Windows.Media.Colors.Red, 1.0)); // X axis
                gizmoGroup.Children.Add(CreateBoxModel(0.2, 4, 0.2, System.Windows.Media.Colors.Green, 1.0)); // Y axis
                gizmoGroup.Children.Add(CreateBoxModel(0.2, 0.2, 4, System.Windows.Media.Colors.Blue, 1.0)); // Z axis
                
                // Centrar cámara en el objeto
                double maxDim = Math.Max(length, Math.Max(width, height));
                if (!_isDragging)
                {
                    _cameraDistance = maxDim * 2.5;
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

            // Vertices
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
                // Bottom
                0, 1, 2,  0, 2, 3,
                // Top
                4, 6, 5,  4, 7, 6,
                // Front
                0, 5, 1,  0, 4, 5,
                // Back
                3, 2, 6,  3, 6, 7,
                // Left
                0, 3, 7,  0, 7, 4,
                // Right
                1, 5, 6,  1, 6, 2
            };

            foreach (int i in indices)
            {
                mesh.TriangleIndices.Add(i);
            }

            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Opacity = opacity;
            DiffuseMaterial mat = new DiffuseMaterial(brush);

            return new GeometryModel3D(mesh, mat);
        }

        public BrickType? GeneratedBrick { get; private set; }

        private void BtnBuild_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Validate inputs
                if (string.IsNullOrWhiteSpace(txtName.Text))
                {
                    System.Windows.MessageBox.Show("Por favor, ingresa un nombre para la familia.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!double.TryParse(txtLength.Text, out double length) || 
                    !double.TryParse(txtWidth.Text, out double width) || 
                    !double.TryParse(txtHeight.Text, out double height))
                {
                    System.Windows.MessageBox.Show("Las dimensiones deben ser valores numéricos válidos.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string material = (cmbMaterial.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Desconocido";
                string profileOrientation = (cmbProfileOrientation.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Frontal";
                
                double rotation = 0;
                string rotString = (cmbProfileRotation.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "0°";
                rotString = rotString.Replace("°", "");
                double.TryParse(rotString, out rotation);

                double offsetX = 0;
                double.TryParse(txtOffsetX.Text, out offsetX);

                double offsetY = 0;
                double.TryParse(txtOffsetY.Text, out offsetY);

                bool hasMortar = chkPega.IsChecked ?? false;
                bool hasTrims = chkRecortes.IsChecked ?? false;
                bool hasDovela = chkDovela.IsChecked ?? false;
                bool hasCanchado = chkCanchado.IsChecked ?? false;

                // Create new brick type
                GeneratedBrick = new BrickType
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = txtName.Text,
                    LengthCm = length,
                    WidthCm = width,
                    HeightCm = height,
                    Material = material,
                    ProfilePath = _selectedProfilePath,
                    ProfileOrientation = profileOrientation,
                    ProfileOffsetX = offsetX,
                    ProfileOffsetY = offsetY,
                    ProfileRotation = rotation,
                    HasMortar = hasMortar,
                    HasTrims = hasTrims,
                    HasDovela = hasDovela,
                    HasCanchado = hasCanchado
                };

                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Ocurrió un error al configurar la familia: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
