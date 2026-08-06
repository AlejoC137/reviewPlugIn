using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

        // Referencia visual
        public System.Windows.Shapes.Rectangle UIRect { get; set; }
        public System.Windows.Shapes.Ellipse UIDovela { get; set; }
    }

    public partial class ConvertirMurosWindow : Window
    {
        public BrickType? SelectedBrick { get; private set; }
        public string SelectedPattern { get; private set; } = "Soga";
        public double PegaCm { get; private set; } = 1.5;
        public bool VerPega { get; private set; } = false;
        public bool IsConfirmed { get; private set; } = false;

        public List<BrickLayoutItem> LayoutItems { get; private set; } = new List<BrickLayoutItem>();
        
        private double wallLengthFt;
        private double wallHeightFt;
        private double scale = 20.0; // 1 ft = 20 pixels aprox para preview

        public ConvertirMurosWindow(int wallCount, double wallLenFt, double wallHgtFt)
        {
            InitializeComponent();
            txtSummary.Text = $"Has seleccionado {wallCount} muro(s) para convertir.";
            wallLengthFt = wallLenFt;
            wallHeightFt = wallHgtFt;
            
            txtWallDims.Text = $"Dimensiones base: {(wallLengthFt * 0.3048).ToString("F2")}m x {(wallHeightFt * 0.3048).ToString("F2")}m";
            LoadBricks();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            previewPatternCanvas.MouseLeftButtonDown += Canvas_MouseLeftButtonDown;
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
                if (previewPatternCanvas == null) return;
                previewPatternCanvas.Children.Clear();
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

                // Dimensionar Canvas
                previewPatternCanvas.Width = wallLengthFt * scale;
                previewPatternCanvas.Height = wallHeightFt * scale;

                // Borde del muro (visual)
                System.Windows.Shapes.Rectangle wallBorder = new System.Windows.Shapes.Rectangle
                {
                    Width = wallLengthFt * scale,
                    Height = wallHeightFt * scale,
                    Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200)),
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 4, 4 }
                };
                Canvas.SetLeft(wallBorder, 0);
                Canvas.SetTop(wallBorder, 0);
                previewPatternCanvas.Children.Add(wallBorder);

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

        private void DrawLayout()
        {
            // Remover solo ladrillos (no el borde)
            var toRemove = previewPatternCanvas.Children.OfType<FrameworkElement>().Where(c => c.Tag?.ToString() == "brick").ToList();
            foreach (var c in toRemove) previewPatternCanvas.Children.Remove(c);

            foreach (var item in LayoutItems)
            {
                System.Windows.Media.Color fillColor = item.IsCanchado ? System.Windows.Media.Color.FromArgb(150, 200, 50, 50) : System.Windows.Media.Color.FromArgb(100, 221, 107, 32);
                System.Windows.Media.Color strokeColor = item.IsCanchado ? System.Windows.Media.Color.FromRgb(255, 100, 100) : System.Windows.Media.Color.FromRgb(221, 107, 32);

                System.Windows.Shapes.Rectangle rect = new System.Windows.Shapes.Rectangle
                {
                    Width = item.Length_Ft * scale,
                    Height = item.Height_Ft * scale,
                    Fill = new SolidColorBrush(fillColor),
                    Stroke = new SolidColorBrush(strokeColor),
                    StrokeThickness = 1.5,
                    Tag = "brick"
                };

                // Y invertido para WPF (Top es Y=0, muro sube hacia Y negativo desde abajo)
                double drawY = (wallHeightFt * scale) - (item.PosZ_Ft * scale) - (item.Height_Ft * scale);
                
                Canvas.SetLeft(rect, item.PosX_Ft * scale);
                Canvas.SetTop(rect, drawY);

                item.UIRect = rect;
                previewPatternCanvas.Children.Add(rect);

                if (item.HasDovela)
                {
                    System.Windows.Shapes.Ellipse el = new System.Windows.Shapes.Ellipse
                    {
                        Width = 10,
                        Height = 10,
                        Fill = new SolidColorBrush(System.Windows.Media.Colors.Cyan),
                        Tag = "brick"
                    };
                    Canvas.SetLeft(el, (item.PosX_Ft * scale) + (rect.Width / 2) - 5);
                    Canvas.SetTop(el, drawY + (rect.Height / 2) - 5);
                    item.UIDovela = el;
                    previewPatternCanvas.Children.Add(el);
                }
            }
        }

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            System.Windows.Point pt = e.GetPosition(previewPatternCanvas);
            
            // Buscar si hizo clic en algun ladrillo
            foreach (var item in LayoutItems)
            {
                double x1 = item.PosX_Ft * scale;
                double x2 = x1 + (item.Length_Ft * scale);
                double y2 = (wallHeightFt * scale) - (item.PosZ_Ft * scale);
                double y1 = y2 - (item.Height_Ft * scale);

                if (pt.X >= x1 && pt.X <= x2 && pt.Y >= y1 && pt.Y <= y2)
                {
                    ApplyToolToBrick(item);
                    break;
                }
            }
        }

        private void ApplyToolToBrick(BrickLayoutItem item)
        {
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
                // Cortar mitad izquierda
                double half = item.Length_Ft / 2.0;
                item.Length_Ft = half;
                item.PosX_Ft += half;
                item.CutLeft_Ft = half;
            }
            else if (toolTrimR.IsChecked == true)
            {
                // Cortar mitad derecha
                double half = item.Length_Ft / 2.0;
                item.Length_Ft = half;
                item.CutRight_Ft = half;
            }
            
            DrawLayout();
        }

        private void LoadBricks()
        {
            try
            {
                string assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string dirPath = Path.GetDirectoryName(assemblyPath) ?? string.Empty;
                string jsonPath = Path.Combine(dirPath, "BricksDB.json");

                if (!File.Exists(jsonPath))
                {
                    string projRoot = Path.GetFullPath(Path.Combine(dirPath, "..", ".."));
                    jsonPath = Path.Combine(projRoot, "BricksDB.json");
                }

                if (File.Exists(jsonPath))
                {
                    string jsonContent = File.ReadAllText(jsonPath);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var db = JsonSerializer.Deserialize<BricksDatabase>(jsonContent, options);

                    if (db?.Bricks != null && db.Bricks.Any())
                    {
                        var displayItems = db.Bricks
                            .GroupBy(b => b.Name)
                            .Select(g => g.Last()) 
                            .Select(b => new BrickDisplayItem(b))
                            .ToList();
                        cmbLadrillos.ItemsSource = displayItems;
                        cmbLadrillos.SelectedIndex = 0;
                    }
                }
            }
            catch { }
        }

        private void BtnConvert_Click(object sender, RoutedEventArgs e)
        {
            if (cmbLadrillos.SelectedItem is BrickDisplayItem selectedItem)
            {
                SelectedBrick = selectedItem.Brick;
                SelectedPattern = (cmbPattern.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Soga";
                if (double.TryParse(txtPega.Text, out double p)) PegaCm = p;
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
