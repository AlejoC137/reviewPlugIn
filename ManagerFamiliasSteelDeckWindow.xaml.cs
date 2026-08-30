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
using Microsoft.Win32;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitToSupabasePlugin
{
    public partial class ManagerFamiliasSteelDeckWindow : Window
    {
        private UIDocument _uidoc;
        private Document _doc;
        private List<SteelDeckType> _allDecks = new List<SteelDeckType>();
        private SteelDeckType? _selectedDeck;
        private List<Autodesk.Revit.DB.Material> _materials = new List<Autodesk.Revit.DB.Material>();
        private bool _isUpdatingUi = false;

        // Variables de interacción de cámara 3D
        private System.Windows.Point _lastMousePos;
        private double _cameraAngleX = 45;
        private double _cameraAngleY = 30;
        private double _cameraDistance = 120;
        private bool _isOrbiting = false;

        public ManagerFamiliasSteelDeckWindow(UIDocument uidoc)
        {
            InitializeComponent();
            _uidoc = uidoc;
            _doc = uidoc.Document;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadMaterialsFromDoc();
            LoadDecksDatabase();
            UpdateCamera();
            if (lstDecks.Items.Count > 0)
            {
                lstDecks.SelectedIndex = 0;
            }
        }

        private void LoadMaterialsFromDoc()
        {
            try
            {
                if (_doc == null) return;
                _materials = new FilteredElementCollector(_doc)
                    .OfClass(typeof(Autodesk.Revit.DB.Material))
                    .Cast<Autodesk.Revit.DB.Material>()
                    .OrderBy(m => m.Name)
                    .ToList();

                cmbConcreteMat.ItemsSource = _materials;
                cmbSteelMat.ItemsSource = _materials;
            }
            catch { }
        }

        private string GetDbPath()
        {
            string addinDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
            string localDb = Path.Combine(addinDir, "SteelDecksDB.json");
            if (File.Exists(localDb)) return localDb;

            string projectDb = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Autodesk\Revit\Addins\2027\SteelDecksDB.json");
            if (File.Exists(projectDb)) return projectDb;

            return Path.Combine(@"C:\Users\Alejandro\Documents\GitHub\reviewPlugIn", "SteelDecksDB.json");
        }

        private void LoadDecksDatabase()
        {
            try
            {
                string dbPath = GetDbPath();
                if (File.Exists(dbPath))
                {
                    string json = File.ReadAllText(dbPath);
                    var db = JsonSerializer.Deserialize<SteelDecksDatabase>(json);
                    if (db?.Decks != null)
                    {
                        _allDecks = db.Decks;
                    }
                }

                if (_allDecks.Count == 0)
                {
                    _allDecks = GetDefaultDecks();
                }

                RefreshList();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Error cargando base de datos de Steel Deck: " + ex.Message);
            }
        }

        private List<SteelDeckType> GetDefaultDecks()
        {
            return new List<SteelDeckType>
            {
                new SteelDeckType
                {
                    Id = "steeldeck_2_cal22",
                    Name = "Steel Deck 2\" - Calibre 22 (0.75mm)",
                    Category = "Steel Deck 2 Pulgadas",
                    RibHeightCm = 5.08,
                    PitchCm = 30.48,
                    TopFluteWidthCm = 15.24,
                    BottomFluteWidthCm = 15.24,
                    SheetWidthCm = 91.44,
                    ThicknessMm = 0.75,
                    Gauge = "Calibre 22",
                    DefaultConcreteToppingCm = 5.0,
                    SteelMaterial = "Acero Galvanizado ASTM A653",
                    ConcreteMaterial = "Hormigón - Estructural 210 kg/cm²",
                    CostM2 = 18.50,
                    WeightKgM2 = 8.45
                },
                new SteelDeckType
                {
                    Id = "steeldeck_3_cal22",
                    Name = "Steel Deck 3\" - Calibre 22 (0.75mm)",
                    Category = "Steel Deck 3 Pulgadas",
                    RibHeightCm = 7.62,
                    PitchCm = 30.48,
                    TopFluteWidthCm = 15.24,
                    BottomFluteWidthCm = 15.24,
                    SheetWidthCm = 91.44,
                    ThicknessMm = 0.75,
                    Gauge = "Calibre 22",
                    DefaultConcreteToppingCm = 6.5,
                    SteelMaterial = "Acero Galvanizado ASTM A653",
                    ConcreteMaterial = "Hormigón - Estructural 280 kg/cm²",
                    CostM2 = 21.50,
                    WeightKgM2 = 9.80
                }
            };
        }

        private void RefreshList()
        {
            string filter = (txtSearch.Text ?? "").Trim().ToLower();
            var filtered = _allDecks
                .Where(d => string.IsNullOrEmpty(filter) ||
                            (d.Name != null && d.Name.ToLower().Contains(filter)) ||
                            (d.Category != null && d.Category.ToLower().Contains(filter)) ||
                            (d.Gauge != null && d.Gauge.ToLower().Contains(filter)))
                .ToList();

            lstDecks.ItemsSource = null;
            lstDecks.ItemsSource = filtered;

            if (_selectedDeck != null && filtered.Contains(_selectedDeck))
            {
                lstDecks.SelectedItem = _selectedDeck;
            }
            else if (filtered.Count > 0)
            {
                lstDecks.SelectedIndex = 0;
            }
        }

        private void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshList();
        }

        private void lstDecks_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedDeck = lstDecks.SelectedItem as SteelDeckType;
            if (_selectedDeck != null)
            {
                pnlEditor.IsEnabled = true;
                btnDuplicate.IsEnabled = true;
                PopulateFields(_selectedDeck);
                Render3DPreview();
            }
            else
            {
                pnlEditor.IsEnabled = false;
                btnDuplicate.IsEnabled = false;
            }
        }

        private void PopulateFields(SteelDeckType deck)
        {
            _isUpdatingUi = true;
            try
            {
                txtName.Text = deck.Name ?? "";
                txtCategory.Text = deck.Category ?? "";
                txtGauge.Text = deck.Gauge ?? "";
                txtThicknessMm.Text = deck.ThicknessMm.ToString("0.##");

                txtRibHeight.Text = deck.RibHeightCm.ToString("0.##");
                txtPitch.Text = deck.PitchCm.ToString("0.##");
                txtTopFlute.Text = deck.TopFluteWidthCm.ToString("0.##");
                txtBottomFlute.Text = deck.BottomFluteWidthCm.ToString("0.##");
                txtSheetWidth.Text = deck.SheetWidthCm.ToString("0.##");

                txtToppingCm.Text = deck.DefaultConcreteToppingCm.ToString("0.##");
                txtWeight.Text = deck.WeightKgM2.ToString("0.##");
                txtCost.Text = deck.CostM2.ToString("0.##");

                txtStudDiameter.Text = deck.StudDiameterIn.ToString("0.##");
                txtStudHeight.Text = deck.StudHeightCm.ToString("0.##");
                txtStudSpacing.Text = deck.StudSpacingCm.ToString("0.##");

                if (_materials.Count > 0)
                {
                    cmbConcreteMat.SelectedItem = _materials.FirstOrDefault(m => m.Name.Equals(deck.ConcreteMaterial, StringComparison.OrdinalIgnoreCase))
                                               ?? _materials.FirstOrDefault(m => m.Name.IndexOf("Hormigón", StringComparison.OrdinalIgnoreCase) >= 0 || m.Name.IndexOf("Concreto", StringComparison.OrdinalIgnoreCase) >= 0);

                    cmbSteelMat.SelectedItem = _materials.FirstOrDefault(m => m.Name.Equals(deck.SteelMaterial, StringComparison.OrdinalIgnoreCase))
                                            ?? _materials.FirstOrDefault(m => m.Name.IndexOf("Metal", StringComparison.OrdinalIgnoreCase) >= 0 || m.Name.IndexOf("Acero", StringComparison.OrdinalIgnoreCase) >= 0);
                }
            }
            finally
            {
                _isUpdatingUi = false;
            }
        }

        private void OnParameterChanged(object sender, EventArgs e)
        {
            if (_isUpdatingUi || _selectedDeck == null) return;

            ReadFieldsIntoDeck(_selectedDeck);
            Render3DPreview();
        }

        private void ReadFieldsIntoDeck(SteelDeckType deck)
        {
            deck.Name = txtName.Text;
            deck.Category = txtCategory.Text;
            deck.Gauge = txtGauge.Text;

            if (double.TryParse(txtThicknessMm.Text, out double thk)) deck.ThicknessMm = thk;
            if (double.TryParse(txtRibHeight.Text, out double rh)) deck.RibHeightCm = rh;
            if (double.TryParse(txtPitch.Text, out double pitch)) deck.PitchCm = pitch;
            if (double.TryParse(txtTopFlute.Text, out double tf)) deck.TopFluteWidthCm = tf;
            if (double.TryParse(txtBottomFlute.Text, out double bf)) deck.BottomFluteWidthCm = bf;
            if (double.TryParse(txtSheetWidth.Text, out double sw)) deck.SheetWidthCm = sw;
            if (double.TryParse(txtToppingCm.Text, out double topCm)) deck.DefaultConcreteToppingCm = topCm;
            if (double.TryParse(txtWeight.Text, out double w)) deck.WeightKgM2 = w;
            if (double.TryParse(txtCost.Text, out double c)) deck.CostM2 = c;

            if (double.TryParse(txtStudDiameter.Text, out double sd)) deck.StudDiameterIn = sd;
            if (double.TryParse(txtStudHeight.Text, out double sh)) deck.StudHeightCm = sh;
            if (double.TryParse(txtStudSpacing.Text, out double ss)) deck.StudSpacingCm = ss;

            if (cmbConcreteMat.SelectedItem is Autodesk.Revit.DB.Material cMat) deck.ConcreteMaterial = cMat.Name;
            if (cmbSteelMat.SelectedItem is Autodesk.Revit.DB.Material sMat) deck.SteelMaterial = sMat.Name;
        }

        private void btnNew_Click(object sender, RoutedEventArgs e)
        {
            var newDeck = new SteelDeckType
            {
                Id = "steeldeck_" + Guid.NewGuid().ToString().Substring(0, 8),
                Name = "Nuevo Perfil Steel Deck",
                Category = "Steel Deck Personalizado",
                RibHeightCm = 5.0,
                PitchCm = 30.0,
                TopFluteWidthCm = 15.0,
                BottomFluteWidthCm = 15.0,
                SheetWidthCm = 90.0,
                ThicknessMm = 0.75,
                Gauge = "Calibre 22",
                DefaultConcreteToppingCm = 5.0,
                CostM2 = 20.0,
                WeightKgM2 = 9.0
            };

            _allDecks.Add(newDeck);
            RefreshList();
            lstDecks.SelectedItem = newDeck;
        }

        private void btnDuplicate_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDeck == null) return;

            var copy = new SteelDeckType
            {
                Id = "steeldeck_" + Guid.NewGuid().ToString().Substring(0, 8),
                Name = _selectedDeck.Name + " (Copia)",
                Category = _selectedDeck.Category,
                RibHeightCm = _selectedDeck.RibHeightCm,
                PitchCm = _selectedDeck.PitchCm,
                TopFluteWidthCm = _selectedDeck.TopFluteWidthCm,
                BottomFluteWidthCm = _selectedDeck.BottomFluteWidthCm,
                SheetWidthCm = _selectedDeck.SheetWidthCm,
                ThicknessMm = _selectedDeck.ThicknessMm,
                Gauge = _selectedDeck.Gauge,
                DefaultConcreteToppingCm = _selectedDeck.DefaultConcreteToppingCm,
                SteelMaterial = _selectedDeck.SteelMaterial,
                ConcreteMaterial = _selectedDeck.ConcreteMaterial,
                StudDiameterIn = _selectedDeck.StudDiameterIn,
                StudHeightCm = _selectedDeck.StudHeightCm,
                StudSpacingCm = _selectedDeck.StudSpacingCm,
                CostM2 = _selectedDeck.CostM2,
                WeightKgM2 = _selectedDeck.WeightKgM2
            };

            _allDecks.Add(copy);
            RefreshList();
            lstDecks.SelectedItem = copy;
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDeck == null) return;
            ReadFieldsIntoDeck(_selectedDeck);
            SaveDatabase();
            RefreshList();
            System.Windows.MessageBox.Show("¡Perfil guardado correctamente en la base de datos!", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void btnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDeck == null) return;

            var res = System.Windows.MessageBox.Show($"¿Estás seguro de eliminar el perfil '{_selectedDeck.Name}'?", "Confirmar Eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                _allDecks.Remove(_selectedDeck);
                SaveDatabase();
                _selectedDeck = null;
                RefreshList();
            }
        }

        private void SaveDatabase()
        {
            try
            {
                string dbPath = GetDbPath();
                var db = new SteelDecksDatabase { Decks = _allDecks };
                string json = JsonSerializer.Serialize(db, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(dbPath, json);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Error guardando base de datos: " + ex.Message);
            }
        }

        private void btnExport_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.SaveFileDialog sfd = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Archivo JSON (*.json)|*.json",
                FileName = "SteelDecksDB_Export.json"
            };
            if (sfd.ShowDialog() == true)
            {
                var db = new SteelDecksDatabase { Decks = _allDecks };
                string json = JsonSerializer.Serialize(db, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(sfd.FileName, json);
                System.Windows.MessageBox.Show("Catálogo exportado exitosamente.");
            }
        }

        private void btnImport_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog ofd = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Archivo JSON (*.json)|*.json"
            };
            if (ofd.ShowDialog() == true)
            {
                try
                {
                    string json = File.ReadAllText(ofd.FileName);
                    var db = JsonSerializer.Deserialize<SteelDecksDatabase>(json);
                    if (db?.Decks != null && db.Decks.Count > 0)
                    {
                        foreach (var d in db.Decks)
                        {
                            if (!_allDecks.Any(x => x.Id == d.Id))
                            {
                                _allDecks.Add(d);
                            }
                        }
                        SaveDatabase();
                        RefreshList();
                        System.Windows.MessageBox.Show($"Se importaron {db.Decks.Count} perfiles exitosamente.");
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show("Error al importar: " + ex.Message);
                }
            }
        }

        private void OnRenderOptionChanged(object sender, RoutedEventArgs e)
        {
            Render3DPreview();
        }

        private void btnResetView_Click(object sender, RoutedEventArgs e)
        {
            _cameraAngleX = 45;
            _cameraAngleY = 30;
            _cameraDistance = 120;
            UpdateCamera();
        }

        #region Renderizado 3D Interactivo en WPF

        private void Render3DPreview()
        {
            if (_selectedDeck == null) return;

            modelGroup.Children.Clear();
            modelGroup.Children.Add(new AmbientLight(System.Windows.Media.Color.FromRgb(100, 100, 100)));
            modelGroup.Children.Add(new DirectionalLight(System.Windows.Media.Color.FromRgb(220, 220, 220), new Vector3D(-1, 1, -1.5)));
            modelGroup.Children.Add(new DirectionalLight(System.Windows.Media.Color.FromRgb(140, 140, 140), new Vector3D(1, -1, 1)));

            double ribH = _selectedDeck.RibHeightCm > 0 ? _selectedDeck.RibHeightCm : 5.0;
            double pitch = _selectedDeck.PitchCm > 0 ? _selectedDeck.PitchCm : 30.0;
            double topW = _selectedDeck.TopFluteWidthCm > 0 ? _selectedDeck.TopFluteWidthCm : 15.0;
            double botW = _selectedDeck.BottomFluteWidthCm > 0 ? _selectedDeck.BottomFluteWidthCm : 15.0;
            double toppingH = _selectedDeck.DefaultConcreteToppingCm > 0 ? _selectedDeck.DefaultConcreteToppingCm : 5.0;
            double thk = (_selectedDeck.ThicknessMm > 0 ? _selectedDeck.ThicknessMm : 0.75) / 10.0; // cm

            lblInfoPaso.Text = $"Paso / Pitch: {pitch:0.##} cm";
            lblInfoAltura.Text = $"Altura Chapa: {ribH:0.##} cm";
            lblInfoTotalH.Text = $"Espesor Total: {(ribH + toppingH):0.##} cm";

            int numWaves = 3;
            double totalWidth = pitch * numWaves;
            double depthY = 80.0; // cm de longitud en preview 3D

            // 1. Malla 3D de Chapa Corrugada Galvanizada (Steel Deck Sheet)
            var steelMesh = BuildCorrugatedDeckMesh(numWaves, pitch, ribH, topW, botW, thk, depthY);
            var steelMat = new DiffuseMaterial(new SolidColorBrush(System.Windows.Media.Color.FromRgb(190, 205, 220)));
            var steelSpec = new SpecularMaterial(new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255)), 40);
            var steelMatGroup = new MaterialGroup();
            steelMatGroup.Children.Add(steelMat);
            steelMatGroup.Children.Add(steelSpec);

            var steelModel = new GeometryModel3D(steelMesh, steelMatGroup);
            steelModel.BackMaterial = steelMatGroup;
            modelGroup.Children.Add(steelModel);

            // 2. Malla 3D de Capa de Hormigón / Concreto (Topping)
            if (chkShowConcrete.IsChecked == true)
            {
                var concreteMesh = BuildConcreteToppingMesh(numWaves, pitch, ribH, topW, botW, toppingH, depthY);
                var concreteBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 160, 165, 170));
                var concreteMat = new DiffuseMaterial(concreteBrush);
                var concreteModel = new GeometryModel3D(concreteMesh, concreteMat);
                concreteModel.BackMaterial = concreteMat;
                modelGroup.Children.Add(concreteModel);
            }

            // 3. Pernos Conectores de Cortante (Nelson Studs)
            if (chkShowStuds.IsChecked == true)
            {
                double studDiam = (_selectedDeck.StudDiameterIn > 0 ? _selectedDeck.StudDiameterIn * 2.54 : 1.9);
                double studH = _selectedDeck.StudHeightCm > 0 ? _selectedDeck.StudHeightCm : (ribH + toppingH * 0.7);

                var studMat = new DiffuseMaterial(new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 160, 40)));
                var studMatGroup = new MaterialGroup();
                studMatGroup.Children.Add(studMat);
                studMatGroup.Children.Add(new SpecularMaterial(new SolidColorBrush(Colors.White), 30));

                for (int w = 0; w < numWaves; w++)
                {
                    double waveCenterX = (w * pitch) + (pitch / 2.0) - (totalWidth / 2.0);
                    // Stud en el valle del deck
                    double studZ = 0;

                    for (double y = -depthY / 3.0; y <= depthY / 3.0 + 1; y += depthY / 3.0)
                    {
                        var studMesh = BuildStudMesh(new Point3D(waveCenterX, y, studZ), studDiam / 2.0, studH);
                        var studModel = new GeometryModel3D(studMesh, studMatGroup);
                        modelGroup.Children.Add(studModel);
                    }
                }
            }
        }

        private MeshGeometry3D BuildCorrugatedDeckMesh(int numWaves, double pitch, double ribH, double topW, double botW, double thk, double depthY)
        {
            var mesh = new MeshGeometry3D();
            double totalWidth = pitch * numWaves;
            double startX = -totalWidth / 2.0;

            // Generar perfil 2D (X, Z) de la greca
            List<Point3D> profilePoints = new List<Point3D>();

            double slopeW = (pitch - topW - botW) / 2.0;
            if (slopeW < 0) slopeW = 0.01;

            for (int w = 0; w < numWaves; w++)
            {
                double curX = startX + (w * pitch);
                // Valle inicial (mitad)
                profilePoints.Add(new Point3D(curX, 0, 0));
                profilePoints.Add(new Point3D(curX + botW / 2.0, 0, 0));

                // Rampa ascendente
                profilePoints.Add(new Point3D(curX + botW / 2.0 + slopeW, 0, ribH));

                // Cresta superior
                profilePoints.Add(new Point3D(curX + botW / 2.0 + slopeW + topW, 0, ribH));

                // Rampa descendente
                profilePoints.Add(new Point3D(curX + botW / 2.0 + slopeW + topW + slopeW, 0, 0));

                // Valle final (mitad)
                profilePoints.Add(new Point3D(curX + pitch, 0, 0));
            }

            // Extruir a lo largo de Y
            double y0 = -depthY / 2.0;
            double y1 = depthY / 2.0;

            for (int i = 0; i < profilePoints.Count - 1; i++)
            {
                Point3D p0 = profilePoints[i];
                Point3D p1 = profilePoints[i + 1];

                Point3D v0 = new Point3D(p0.X, y0, p0.Z);
                Point3D v1 = new Point3D(p1.X, y0, p1.Z);
                Point3D v2 = new Point3D(p1.X, y1, p1.Z);
                Point3D v3 = new Point3D(p0.X, y1, p0.Z);

                AddQuad(mesh, v0, v1, v2, v3);
            }

            return mesh;
        }

        private MeshGeometry3D BuildConcreteToppingMesh(int numWaves, double pitch, double ribH, double topW, double botW, double toppingH, double depthY)
        {
            var mesh = new MeshGeometry3D();
            double totalWidth = pitch * numWaves;
            double x0 = -totalWidth / 2.0;
            double x1 = totalWidth / 2.0;
            double y0 = -depthY / 2.0;
            double y1 = depthY / 2.0;
            double zTop = ribH + toppingH;

            // Bloque superior plano de compresión
            Point3D t0 = new Point3D(x0, y0, zTop);
            Point3D t1 = new Point3D(x1, y0, zTop);
            Point3D t2 = new Point3D(x1, y1, zTop);
            Point3D t3 = new Point3D(x0, y1, zTop);
            AddQuad(mesh, t0, t1, t2, t3);

            // Caras laterales del concreto
            AddQuad(mesh, new Point3D(x0, y1, ribH), new Point3D(x1, y1, ribH), t2, t3); // Cara Y1
            AddQuad(mesh, t0, t1, new Point3D(x1, y0, ribH), new Point3D(x0, y0, ribH)); // Cara Y0
            AddQuad(mesh, t3, t0, new Point3D(x0, y0, ribH), new Point3D(x0, y1, ribH)); // Cara X0
            AddQuad(mesh, t1, t2, new Point3D(x1, y1, ribH), new Point3D(x1, y0, ribH)); // Cara X1

            return mesh;
        }

        private MeshGeometry3D BuildStudMesh(Point3D baseCenter, double radius, double height)
        {
            var mesh = new MeshGeometry3D();
            int segments = 12;
            double headRadius = radius * 1.6;
            double headH = height * 0.15;
            double shaftH = height - headH;

            // Vástago cilíndrico
            for (int i = 0; i < segments; i++)
            {
                double a0 = (i / (double)segments) * Math.PI * 2;
                double a1 = ((i + 1) / (double)segments) * Math.PI * 2;

                Point3D b0 = new Point3D(baseCenter.X + Math.Cos(a0) * radius, baseCenter.Y + Math.Sin(a0) * radius, baseCenter.Z);
                Point3D b1 = new Point3D(baseCenter.X + Math.Cos(a1) * radius, baseCenter.Y + Math.Sin(a1) * radius, baseCenter.Z);
                Point3D t0 = new Point3D(baseCenter.X + Math.Cos(a0) * radius, baseCenter.Y + Math.Sin(a0) * radius, baseCenter.Z + shaftH);
                Point3D t1 = new Point3D(baseCenter.X + Math.Cos(a1) * radius, baseCenter.Y + Math.Sin(a1) * radius, baseCenter.Z + shaftH);

                AddQuad(mesh, b0, b1, t1, t0);

                // Cabeza del perno (disco)
                Point3D h0 = new Point3D(baseCenter.X + Math.Cos(a0) * headRadius, baseCenter.Y + Math.Sin(a0) * headRadius, baseCenter.Z + shaftH);
                Point3D h1 = new Point3D(baseCenter.X + Math.Cos(a1) * headRadius, baseCenter.Y + Math.Sin(a1) * headRadius, baseCenter.Z + shaftH);
                Point3D ht0 = new Point3D(baseCenter.X + Math.Cos(a0) * headRadius, baseCenter.Y + Math.Sin(a0) * headRadius, baseCenter.Z + height);
                Point3D ht1 = new Point3D(baseCenter.X + Math.Cos(a1) * headRadius, baseCenter.Y + Math.Sin(a1) * headRadius, baseCenter.Z + height);

                AddQuad(mesh, h0, h1, ht1, ht0);
                AddTriangle(mesh, new Point3D(baseCenter.X, baseCenter.Y, baseCenter.Z + height), ht0, ht1);
            }

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

        private void AddTriangle(MeshGeometry3D mesh, Point3D p0, Point3D p1, Point3D p2)
        {
            int idx = mesh.Positions.Count;
            mesh.Positions.Add(p0);
            mesh.Positions.Add(p1);
            mesh.Positions.Add(p2);

            mesh.TriangleIndices.Add(idx);
            mesh.TriangleIndices.Add(idx + 1);
            mesh.TriangleIndices.Add(idx + 2);
        }

        #endregion

        #region Controles de Cámara con Ratón

        private void UpdateCamera()
        {
            double radX = _cameraAngleX * Math.PI / 180.0;
            double radY = _cameraAngleY * Math.PI / 180.0;

            double x = _cameraDistance * Math.Cos(radY) * Math.Sin(radX);
            double y = -_cameraDistance * Math.Cos(radY) * Math.Cos(radX);
            double z = _cameraDistance * Math.Sin(radY);

            camera.Position = new Point3D(x, y, z);
            camera.LookDirection = new Vector3D(-x, -y, -z);
        }

        private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed || e.RightButton == MouseButtonState.Pressed)
            {
                _isOrbiting = true;
                _lastMousePos = e.GetPosition(viewport3D);
                viewport3D.CaptureMouse();
            }
        }

        private void Viewport_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isOrbiting)
            {
                System.Windows.Point currentPos = e.GetPosition(viewport3D);
                double deltaX = currentPos.X - _lastMousePos.X;
                double deltaY = currentPos.Y - _lastMousePos.Y;

                _cameraAngleX += deltaX * 0.6;
                _cameraAngleY += deltaY * 0.6;

                if (_cameraAngleY > 89) _cameraAngleY = 89;
                if (_cameraAngleY < -89) _cameraAngleY = -89;

                _lastMousePos = currentPos;
                UpdateCamera();
            }
        }

        private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isOrbiting)
            {
                _isOrbiting = false;
                viewport3D.ReleaseMouseCapture();
            }
        }

        private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            _cameraDistance -= (e.Delta / 120.0) * 8.0;
            if (_cameraDistance < 20) _cameraDistance = 20;
            if (_cameraDistance > 500) _cameraDistance = 500;
            UpdateCamera();
        }

        #endregion
    }
}
