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
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitToSupabasePlugin
{
    public partial class ManagerFamiliasPisoWindow : Window
    {
        private UIDocument? _uidoc;
        private Document? _doc;
        private List<TileType> _tiles = new List<TileType>();
        private TileType? _selectedTile;
        private bool _isUpdatingUI = false;

        // Variables de Cámara 3D
        private System.Windows.Point _lastMousePosition;
        private double _cameraAngleX = 45;
        private double _cameraAngleY = 30;
        private double _cameraDistance = 120;
        private bool _isDragging = false;

        public ManagerFamiliasPisoWindow(UIDocument? uidoc)
        {
            InitializeComponent();
            _uidoc = uidoc;
            _doc = uidoc?.Document;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadMaterials();
            LoadTilesFromDatabase();
            UpdateCamera();
        }

        private void LoadMaterials()
        {
            try
            {
                if (_doc == null) return;
                var materials = new FilteredElementCollector(_doc)
                    .OfClass(typeof(Autodesk.Revit.DB.Material))
                    .Cast<Autodesk.Revit.DB.Material>()
                    .OrderBy(m => m.Name)
                    .ToList();

                cmbMaterial.ItemsSource = materials;
            }
            catch { }
        }

        private string GetDatabasePath()
        {
            string assemblyDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
            string dbPath = Path.Combine(assemblyDir, "TilesDB.json");
            if (!File.Exists(dbPath))
            {
                // Fallback a la raíz del proyecto
                string altPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TilesDB.json");
                if (File.Exists(altPath)) return altPath;
            }
            return dbPath;
        }

        private void LoadTilesFromDatabase()
        {
            try
            {
                string dbPath = GetDatabasePath();
                if (File.Exists(dbPath))
                {
                    string json = File.ReadAllText(dbPath);
                    var db = JsonSerializer.Deserialize<TilesDatabase>(json);
                    if (db?.Tiles != null && db.Tiles.Count > 0)
                    {
                        _tiles = db.Tiles;
                    }
                }
            }
            catch { }

            if (_tiles.Count == 0)
            {
                _tiles = GetDefaultTiles();
            }

            RefreshTileList();
            if (lstTiles.Items.Count > 0)
            {
                lstTiles.SelectedIndex = 0;
            }
        }

        private List<TileType> GetDefaultTiles()
        {
            return new List<TileType>
            {
                new TileType { Id = "p_60x60", Name = "Porcelanato Pulido 60x60", WidthCm = 60, LengthCm = 60, ThicknessCm = 0.9, BevelMm = 1.0, Category = "Porcelanato", Material = "Porcelanato Blanco Pulido", CostM2 = 24.5 },
                new TileType { Id = "p_60x120", Name = "Porcelanato Gran Formato 60x120", WidthCm = 60, LengthCm = 120, ThicknessCm = 1.0, BevelMm = 1.0, Category = "Porcelanato", Material = "Porcelanato Calacatta", CostM2 = 38.0 },
                new TileType { Id = "m_20x120", Name = "Listón Madera 20x120", WidthCm = 20, LengthCm = 120, ThicknessCm = 0.9, BevelMm = 0.8, Category = "Madera / Parquet", Material = "Madera Roble", CostM2 = 32.5 },
                new TileType { Id = "c_30x30", Name = "Cerámica 30x30", WidthCm = 30, LengthCm = 30, ThicknessCm = 0.7, BevelMm = 1.5, Category = "Cerámica", Material = "Cerámica Esmaltada", CostM2 = 14.0 },
                new TileType { Id = "a_10x20", Name = "Adoquín Concreto 10x20", WidthCm = 10, LengthCm = 20, ThicknessCm = 6.0, BevelMm = 3.0, Category = "Adoquines / Exterior", Material = "Concreto Adoquín", CostM2 = 18.0 }
            };
        }

        private void RefreshTileList(string filter = "")
        {
            var filtered = string.IsNullOrWhiteSpace(filter)
                ? _tiles
                : _tiles.Where(t => (t.Name ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    (t.Category ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            lstTiles.ItemsSource = null;
            lstTiles.ItemsSource = filtered.Select(t => new { Tile = t, Display = $"{t.Name} ({t.LengthCm}x{t.WidthCm}x{t.ThicknessCm}cm)" }).ToList();
            lstTiles.DisplayMemberPath = "Display";
        }

        private void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshTileList(txtSearch.Text);
        }

        private void lstTiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstTiles.SelectedItem != null)
            {
                dynamic selected = lstTiles.SelectedItem;
                _selectedTile = selected.Tile as TileType;
                PopulateFieldsFromSelectedTile();
                Update3DPreview();
            }
        }

        private void PopulateFieldsFromSelectedTile()
        {
            if (_selectedTile == null) return;
            _isUpdatingUI = true;

            txtName.Text = _selectedTile.Name ?? "";
            txtLength.Text = _selectedTile.LengthCm.ToString("0.##");
            txtWidth.Text = _selectedTile.WidthCm.ToString("0.##");
            txtThickness.Text = _selectedTile.ThicknessCm.ToString("0.##");
            txtBevel.Text = _selectedTile.BevelMm.ToString("0.##");
            txtCost.Text = _selectedTile.CostM2.ToString("0.##");

            foreach (ComboBoxItem item in cmbCategory.Items)
            {
                if (string.Equals(item.Content.ToString(), _selectedTile.Category, StringComparison.OrdinalIgnoreCase))
                {
                    cmbCategory.SelectedItem = item;
                    break;
                }
            }

            if (cmbMaterial.ItemsSource is List<Autodesk.Revit.DB.Material> mats && !string.IsNullOrEmpty(_selectedTile.Material))
            {
                var mat = mats.FirstOrDefault(m => m.Name.IndexOf(_selectedTile.Material, StringComparison.OrdinalIgnoreCase) >= 0);
                if (mat != null) cmbMaterial.SelectedItem = mat;
            }

            _isUpdatingUI = false;
            UpdateMetrics();
        }

        private void PropertyField_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUI || _selectedTile == null) return;

            _selectedTile.Name = txtName.Text.Trim();
            if (cmbCategory.SelectedItem is ComboBoxItem cItem)
            {
                _selectedTile.Category = cItem.Content.ToString();
            }

            if (double.TryParse(txtLength.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double l))
                _selectedTile.LengthCm = Math.Max(1.0, l);

            if (double.TryParse(txtWidth.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double w))
                _selectedTile.WidthCm = Math.Max(1.0, w);

            if (double.TryParse(txtThickness.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double th))
                _selectedTile.ThicknessCm = Math.Max(0.2, th);

            if (double.TryParse(txtBevel.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double bv))
                _selectedTile.BevelMm = Math.Max(0.0, bv);

            if (double.TryParse(txtCost.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double c))
                _selectedTile.CostM2 = Math.Max(0.0, c);

            if (cmbMaterial.SelectedItem is Autodesk.Revit.DB.Material selMat)
            {
                _selectedTile.Material = selMat.Name;
            }

            UpdateMetrics();
            Update3DPreview();
        }

        private void UpdateMetrics()
        {
            if (_selectedTile == null) return;
            double areaM2 = (_selectedTile.LengthCm * _selectedTile.WidthCm) / 10000.0;
            double yieldPieces = areaM2 > 0 ? 1.0 / areaM2 : 0;
            double ratio = _selectedTile.WidthCm > 0 ? _selectedTile.LengthCm / _selectedTile.WidthCm : 1.0;

            lblTileArea.Text = $"{areaM2:0.000} m²";
            lblYield.Text = $"{yieldPieces:0.00} pzas/m²";
            lblAspectRatio.Text = $"{ratio:0.00} {(Math.Abs(ratio - 1.0) < 0.05 ? "(Cuadrada)" : "(Rectangular)")}";
        }

        private void btnNewTile_Click(object sender, RoutedEventArgs e)
        {
            var newTile = new TileType
            {
                Id = "tile_" + Guid.NewGuid().ToString().Substring(0, 8),
                Name = "Nueva Baldosa " + (_tiles.Count + 1),
                WidthCm = 60,
                LengthCm = 60,
                ThicknessCm = 1.0,
                BevelMm = 1.0,
                Category = "Porcelanato",
                Material = "Porcelanato",
                CostM2 = 25.0
            };
            _tiles.Add(newTile);
            RefreshTileList();
            lstTiles.SelectedIndex = _tiles.Count - 1;
        }

        private void btnDuplicate_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTile == null) return;
            var dup = new TileType
            {
                Id = "tile_" + Guid.NewGuid().ToString().Substring(0, 8),
                Name = _selectedTile.Name + " (Copia)",
                WidthCm = _selectedTile.WidthCm,
                LengthCm = _selectedTile.LengthCm,
                ThicknessCm = _selectedTile.ThicknessCm,
                BevelMm = _selectedTile.BevelMm,
                Category = _selectedTile.Category,
                Material = _selectedTile.Material,
                CostM2 = _selectedTile.CostM2
            };
            _tiles.Add(dup);
            RefreshTileList();
            lstTiles.SelectedIndex = _tiles.Count - 1;
        }

        private void btnDeleteTile_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTile == null) return;
            if (_tiles.Count <= 1)
            {
                System.Windows.MessageBox.Show("Debe existir al menos un formato en el catálogo.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _tiles.Remove(_selectedTile);
            RefreshTileList();
            if (lstTiles.Items.Count > 0) lstTiles.SelectedIndex = 0;
        }

        private void btnSaveDB_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string dbPath = GetDatabasePath();
                var db = new TilesDatabase { Tiles = _tiles };
                string json = JsonSerializer.Serialize(db, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(dbPath, json);
                System.Windows.MessageBox.Show("Catálogo de baldosas guardado exitosamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Error al guardar catálogo: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnSyncRevit_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null || _selectedTile == null) return;

            try
            {
                using (Transaction t = new Transaction(_doc, "Sincronizar Tipo de Piso"))
                {
                    t.Start();

                    // Buscar o duplicar FloorType en Revit
                    FloorType? baseFloorType = new FilteredElementCollector(_doc)
                        .OfClass(typeof(FloorType))
                        .Cast<FloorType>()
                        .FirstOrDefault();

                    if (baseFloorType != null)
                    {
                        string typeName = $"Piso Baldosa - {_selectedTile.Name}";
                        FloorType? existingType = new FilteredElementCollector(_doc)
                            .OfClass(typeof(FloorType))
                            .Cast<FloorType>()
                            .FirstOrDefault(ft => ft.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

                        if (existingType == null)
                        {
                            existingType = baseFloorType.Duplicate(typeName) as FloorType;
                        }

                        if (existingType != null)
                        {
                            // Asignar espesor en pies
                            double thickFeet = (_selectedTile.ThicknessCm / 100.0) * 3.28084;
                            CompoundStructure cs = existingType.GetCompoundStructure();
                            if (cs != null)
                            {
                                IList<CompoundStructureLayer> layers = cs.GetLayers();
                                if (layers.Count > 0)
                                {
                                    layers[0].Width = thickFeet;
                                    
                                    if (cmbMaterial.SelectedItem is Autodesk.Revit.DB.Material selMat)
                                    {
                                        layers[0].MaterialId = selMat.Id;
                                    }
                                    cs.SetLayers(layers);
                                    existingType.SetCompoundStructure(cs);
                                }
                            }
                        }
                    }

                    t.Commit();
                    System.Windows.MessageBox.Show($"Tipo de Piso '{_selectedTile.Name}' sincronizado y listo en Revit.", "Sincronizado", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Error al sincronizar con Revit: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #region Renderizado 3D y Controles de Cámara

        private void Update3DPreview()
        {
            if (_selectedTile == null) return;

            // Limpiar modelos anteriores excepto luces
            var lights = modelGroup.Children.Where(c => c is Light).ToList();
            modelGroup.Children.Clear();
            foreach (var light in lights) modelGroup.Children.Add(light);

            double L = _selectedTile.LengthCm;
            double W = _selectedTile.WidthCm;
            double H = _selectedTile.ThicknessCm;
            double bevel = (_selectedTile.BevelMm / 10.0); // mm a cm

            // Crear malla de la baldosa principal
            MeshGeometry3D mesh = CreateTileMesh(L, W, H, bevel);

            System.Windows.Media.Color tileColor = System.Windows.Media.Color.FromRgb(220, 225, 230);
            if (_selectedTile.Category == "Madera / Parquet") tileColor = System.Windows.Media.Color.FromRgb(180, 120, 70);
            else if (_selectedTile.Category == "Cerámica") tileColor = System.Windows.Media.Color.FromRgb(210, 140, 100);
            else if (_selectedTile.Category == "Adoquines / Exterior") tileColor = System.Windows.Media.Color.FromRgb(140, 90, 80);
            else if (_selectedTile.Category == "Terrazo / Hormigón") tileColor = System.Windows.Media.Color.FromRgb(160, 165, 170);
            else if (_selectedTile.Category == "Piedra Natural") tileColor = System.Windows.Media.Color.FromRgb(200, 195, 180);

            var material = new DiffuseMaterial(new SolidColorBrush(tileColor));
            var specular = new SpecularMaterial(new SolidColorBrush(System.Windows.Media.Color.FromRgb(80, 80, 80)), 40);
            var matGroup = new MaterialGroup();
            matGroup.Children.Add(material);
            matGroup.Children.Add(specular);

            var geomModel = new GeometryModel3D(mesh, matGroup);
            modelGroup.Children.Add(geomModel);

            // Agregar rejilla o plano de apoyo sutil
            MeshGeometry3D gridMesh = CreateGroundGrid(Math.Max(L, W) * 1.6);
            var gridMat = new DiffuseMaterial(new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 255, 255, 255)));
            modelGroup.Children.Add(new GeometryModel3D(gridMesh, gridMat));

            // Ajustar distancia de cámara adecuada al tamaño de la pieza
            double maxDim = Math.Max(L, Math.Max(W, H));
            _cameraDistance = maxDim * 2.2;
            UpdateCamera();
        }

        private MeshGeometry3D CreateTileMesh(double L, double W, double H, double bevel)
        {
            MeshGeometry3D mesh = new MeshGeometry3D();
            double hx = L / 2.0;
            double hy = W / 2.0;
            double hz = H / 2.0;

            // Box simple con vértices centrados en el origen
            Point3D[] p = new Point3D[]
            {
                new Point3D(-hx, -hy, -hz), // 0
                new Point3D( hx, -hy, -hz), // 1
                new Point3D( hx,  hy, -hz), // 2
                new Point3D(-hx,  hy, -hz), // 3
                new Point3D(-hx, -hy,  hz), // 4
                new Point3D( hx, -hy,  hz), // 5
                new Point3D( hx,  hy,  hz), // 6
                new Point3D(-hx,  hy,  hz)  // 7
            };

            // Caras
            AddQuad(mesh, p[4], p[5], p[6], p[7]); // Superior (+Z)
            AddQuad(mesh, p[1], p[0], p[3], p[2]); // Inferior (-Z)
            AddQuad(mesh, p[0], p[1], p[5], p[4]); // Frontal (-Y)
            AddQuad(mesh, p[2], p[3], p[7], p[6]); // Trasera (+Y)
            AddQuad(mesh, p[3], p[0], p[4], p[7]); // Izquierda (-X)
            AddQuad(mesh, p[1], p[2], p[6], p[5]); // Derecha (+X)

            return mesh;
        }

        private void AddQuad(MeshGeometry3D mesh, Point3D p0, Point3D p1, Point3D p2, Point3D p3)
        {
            int idx = mesh.Positions.Count;
            mesh.Positions.Add(p0);
            mesh.Positions.Add(p1);
            mesh.Positions.Add(p2);
            mesh.Positions.Add(p3);

            mesh.TriangleIndices.Add(idx);
            mesh.TriangleIndices.Add(idx + 1);
            mesh.TriangleIndices.Add(idx + 2);

            mesh.TriangleIndices.Add(idx);
            mesh.TriangleIndices.Add(idx + 2);
            mesh.TriangleIndices.Add(idx + 3);
        }

        private MeshGeometry3D CreateGroundGrid(double size)
        {
            MeshGeometry3D mesh = new MeshGeometry3D();
            double hs = size / 2.0;
            double z = -0.05;

            Point3D p0 = new Point3D(-hs, -hs, z);
            Point3D p1 = new Point3D(hs, -hs, z);
            Point3D p2 = new Point3D(hs, hs, z);
            Point3D p3 = new Point3D(-hs, hs, z);

            AddQuad(mesh, p0, p1, p2, p3);
            return mesh;
        }

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

                _cameraAngleY = Math.Max(5, Math.Min(85, _cameraAngleY));

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
            _cameraDistance -= e.Delta * 0.08;
            _cameraDistance = Math.Max(15, Math.Min(400, _cameraDistance));
            UpdateCamera();
        }

        #endregion
    }
}
