using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitToSupabasePlugin
{
    public partial class ConvertirSteelDeckWindow : Window
    {
        private List<SteelDeckFloorInfo> _floorInfos;
        private Document _doc;
        private List<SteelDeckType> _availableDecks = new List<SteelDeckType>();
        private List<Autodesk.Revit.DB.Material> _materials = new List<Autodesk.Revit.DB.Material>();

        public bool IsConfirmed { get; private set; } = false;
        public SteelDeckType? SelectedDeck { get; private set; }
        public double SpanAngleDeg { get; private set; } = 0;
        public bool GenerateDeckSheet { get; private set; } = true;
        public bool GenerateConcrete { get; private set; } = true;
        public bool GenerateStuds { get; private set; } = true;
        public double ConcreteToppingCm { get; private set; } = 5.0;
        public double StudHeightCm { get; private set; } = 10.0;

        public Autodesk.Revit.DB.Material? SelectedSteelMaterial { get; private set; }
        public Autodesk.Revit.DB.Material? SelectedConcreteMaterial { get; private set; }

        public List<Solid> DeckSolids { get; private set; } = new List<Solid>();
        public List<Solid> ConcreteSolids { get; private set; } = new List<Solid>();
        public List<XYZ> StudPoints { get; private set; } = new List<XYZ>();

        public double TotalAreaM2 { get; private set; }
        public double TotalConcreteVolM3 { get; private set; }
        public int TotalStudsCount { get; private set; }

        private bool _isUpdating = false;

        public ConvertirSteelDeckWindow(List<SteelDeckFloorInfo> floorInfos, Document doc)
        {
            InitializeComponent();
            _floorInfos = floorInfos;
            _doc = doc;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadMaterials();
            LoadDecksDatabase();
            if (cmbDeckType.Items.Count > 0)
            {
                cmbDeckType.SelectedIndex = 0;
            }
            UpdatePreviewAndCalculations();
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

                cmbConcreteMat.ItemsSource = _materials;
                cmbSteelMat.ItemsSource = _materials;
            }
            catch { }
        }

        private void LoadDecksDatabase()
        {
            try
            {
                string addinDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
                string dbPath = System.IO.Path.Combine(addinDir, "SteelDecksDB.json");
                if (!File.Exists(dbPath))
                {
                    dbPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Autodesk\Revit\Addins\2027\SteelDecksDB.json");
                }
                if (!File.Exists(dbPath))
                {
                    dbPath = @"C:\Users\Alejandro\Documents\GitHub\reviewPlugIn\SteelDecksDB.json";
                }

                if (File.Exists(dbPath))
                {
                    string json = File.ReadAllText(dbPath);
                    var db = JsonSerializer.Deserialize<SteelDecksDatabase>(json);
                    if (db?.Decks != null && db.Decks.Count > 0)
                    {
                        _availableDecks = db.Decks;
                    }
                }

                if (_availableDecks.Count == 0)
                {
                    _availableDecks.Add(new SteelDeckType
                    {
                        Id = "steeldeck_2_cal22",
                        Name = "Steel Deck 2\" - Calibre 22 (0.75mm)",
                        RibHeightCm = 5.08,
                        PitchCm = 30.48,
                        TopFluteWidthCm = 15.24,
                        BottomFluteWidthCm = 15.24,
                        SheetWidthCm = 91.44,
                        ThicknessMm = 0.75,
                        Gauge = "Calibre 22",
                        DefaultConcreteToppingCm = 5.0
                    });
                }

                cmbDeckType.ItemsSource = _availableDecks;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Error cargando base de datos: " + ex.Message);
            }
        }

        private void OnConfigChanged(object sender, EventArgs e)
        {
            if (_isUpdating) return;
            UpdatePreviewAndCalculations();
        }

        private void sldAngle_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdating) return;
            _isUpdating = true;
            txtAngle.Text = $"{sldAngle.Value:0}°";
            _isUpdating = false;
            UpdatePreviewAndCalculations();
        }

        private void txtAngle_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdating) return;
            string clean = txtAngle.Text.Replace("°", "").Trim();
            if (double.TryParse(clean, out double val))
            {
                _isUpdating = true;
                sldAngle.Value = Math.Max(0, Math.Min(360, val));
                _isUpdating = false;
                UpdatePreviewAndCalculations();
            }
        }

        private void btnAngle0_Click(object sender, RoutedEventArgs e)
        {
            sldAngle.Value = 0;
        }

        private void btnAngle90_Click(object sender, RoutedEventArgs e)
        {
            sldAngle.Value = 90;
        }

        private void btnAngle45_Click(object sender, RoutedEventArgs e)
        {
            sldAngle.Value = 45;
        }

        private void btnFitView_Click(object sender, RoutedEventArgs e)
        {
            UpdatePreviewAndCalculations();
        }

        private void previewCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdatePreviewAndCalculations();
        }

        private void UpdatePreviewAndCalculations()
        {
            if (cmbDeckType.SelectedItem is not SteelDeckType deck) return;
            SelectedDeck = deck;

            lblRibHeight.Text = $"Altura: {deck.RibHeightCm:0.##} cm";
            lblPitch.Text = $"Paso: {deck.PitchCm:0.##} cm";
            lblGauge.Text = $"Calibre: {deck.Gauge}";
            lblSheetWidth.Text = $"Ancho útil: {deck.SheetWidthCm:0.##} cm";

            double angleDeg = sldAngle.Value;
            SpanAngleDeg = angleDeg;
            double angleRad = angleDeg * Math.PI / 180.0;

            GenerateDeckSheet = chkGenDeck.IsChecked == true;
            GenerateConcrete = chkGenConcrete.IsChecked == true;
            GenerateStuds = chkGenStuds.IsChecked == true;

            if (double.TryParse(txtToppingCm.Text, out double topCm)) ConcreteToppingCm = topCm;
            else ConcreteToppingCm = deck.DefaultConcreteToppingCm;

            if (double.TryParse(txtStudHeight.Text, out double sH)) StudHeightCm = sH;
            else StudHeightCm = deck.StudHeightCm;

            double studSpacingCm = deck.StudSpacingCm > 0 ? deck.StudSpacingCm : 30.48;
            if (double.TryParse(txtStudSpacing.Text, out double ss) && ss > 0) studSpacingCm = ss;

            SelectedConcreteMaterial = cmbConcreteMat.SelectedItem as Autodesk.Revit.DB.Material;
            SelectedSteelMaterial = cmbSteelMat.SelectedItem as Autodesk.Revit.DB.Material;

            // 1. Calcular Cómputos Métricos
            TotalAreaM2 = _floorInfos.Sum(f => f.AreaM2);
            double pitchM = (deck.PitchCm > 0 ? deck.PitchCm : 30.48) / 100.0;
            double ribHM = (deck.RibHeightCm > 0 ? deck.RibHeightCm : 5.08) / 100.0;
            double topFluteM = (deck.TopFluteWidthCm > 0 ? deck.TopFluteWidthCm : 15.24) / 100.0;
            double botFluteM = (deck.BottomFluteWidthCm > 0 ? deck.BottomFluteWidthCm : 15.24) / 100.0;
            double toppingM = ConcreteToppingCm / 100.0;

            // Volumen de valles por m² de losa (sección trapezoidal promedio)
            double valleyAreaPerPitch = ((botFluteM + (pitchM - topFluteM)) / 2.0) * ribHM;
            double valleyVolPerM2 = valleyAreaPerPitch / pitchM;
            double toppingVolPerM2 = toppingM;
            double totalVolPerM2 = valleyVolPerM2 + toppingVolPerM2;

            double volVallesM3 = TotalAreaM2 * valleyVolPerM2;
            double volToppingM3 = TotalAreaM2 * toppingVolPerM2;
            TotalConcreteVolM3 = TotalAreaM2 * totalVolPerM2;

            double sheetWidthM = (deck.SheetWidthCm > 0 ? deck.SheetWidthCm : 91.44) / 100.0;
            int estimatedPanels = (int)Math.Ceiling(TotalAreaM2 / (sheetWidthM * 4.0)); // Aprox 4m largo

            // Conectores de cortante (1 stud por valle cada studSpacing)
            double studSpacingM = studSpacingCm / 100.0;
            TotalStudsCount = (int)Math.Ceiling(TotalAreaM2 / (pitchM * studSpacingM));

            double totalWeightKg = TotalAreaM2 * (deck.WeightKgM2 > 0 ? deck.WeightKgM2 : 9.0);
            double totalCostUsd = TotalAreaM2 * (deck.CostM2 > 0 ? deck.CostM2 : 20.0);

            // Actualizar KPIs en UI
            lblKpiArea.Text = $"{TotalAreaM2:0.00} m²";
            lblKpiPaneles.Text = $"~{estimatedPanels} Paneles ({deck.Gauge})";

            lblKpiVolumen.Text = $"{TotalConcreteVolM3:0.00} m³";
            lblKpiValles.Text = $"Valles: {volVallesM3:0.00}m³ | Top: {volToppingM3:0.00}m³";

            lblKpiStuds.Text = $"{TotalStudsCount} Pzas";
            lblKpiStudsInfo.Text = $"Ø{deck.StudDiameterIn}\" @ {studSpacingCm:0.#} cm";

            lblKpiCosto.Text = $"${totalCostUsd:0.00} USD";
            lblKpiPeso.Text = $"Peso chapa: {totalWeightKg:0.0} kg";

            // 2. Renderizar Vista 2D en Canvas
            Draw2DCanvas(deck, angleRad, pitchM * 3.28084, studSpacingM * 3.28084);
        }

        private void Draw2DCanvas(SteelDeckType deck, double angleRad, double pitchFt, double studSpacingFt)
        {
            previewCanvas.Children.Clear();
            if (_floorInfos.Count == 0 || previewCanvas.ActualWidth < 10 || previewCanvas.ActualHeight < 10) return;

            // Encontrar BoundingBox global de todos los suelos en pies
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            foreach (var f in _floorInfos)
            {
                foreach (var pt in f.OuterBoundary)
                {
                    if (pt.X < minX) minX = pt.X;
                    if (pt.X > maxX) maxX = pt.X;
                    if (pt.Y < minY) minY = pt.Y;
                    if (pt.Y > maxY) maxY = pt.Y;
                }
            }

            double spanX = maxX - minX;
            double spanY = maxY - minY;
            if (spanX <= 0 || spanY <= 0) return;

            double margin = 30;
            double scaleX = (previewCanvas.ActualWidth - margin * 2) / spanX;
            double scaleY = (previewCanvas.ActualHeight - margin * 2) / spanY;
            double scale = Math.Min(scaleX, scaleY);

            double offsetX = margin + (previewCanvas.ActualWidth - margin * 2 - spanX * scale) / 2.0;
            double offsetY = margin + (previewCanvas.ActualHeight - margin * 2 - spanY * scale) / 2.0;

            System.Windows.Point ToCanvasPt(XYZ p)
            {
                double cx = offsetX + (p.X - minX) * scale;
                double cy = previewCanvas.ActualHeight - (offsetY + (p.Y - minY) * scale);
                return new System.Windows.Point(cx, cy);
            }

            StudPoints.Clear();

            // Dibujar cada suelo
            foreach (var f in _floorInfos)
            {
                if (f.OuterBoundary.Count < 3) continue;

                // 1. Dibujar Polígono del Suelo
                Polygon poly = new Polygon
                {
                    Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(49, 130, 206)),
                    StrokeThickness = 2,
                    Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 49, 130, 206))
                };

                foreach (var pt in f.OuterBoundary)
                {
                    poly.Points.Add(ToCanvasPt(pt));
                }
                previewCanvas.Children.Add(poly);

                // Dibujar huecos interiores
                foreach (var inner in f.InnerBoundaries)
                {
                    Polygon innerPoly = new Polygon
                    {
                        Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(229, 62, 62)),
                        StrokeThickness = 1.5,
                        Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(17, 19, 21))
                    };
                    foreach (var pt in inner) innerPoly.Points.Add(ToCanvasPt(pt));
                    previewCanvas.Children.Add(innerPoly);
                }

                // 2. Generar y Dibujar Líneas de Ondas y Studs
                XYZ dirU = new XYZ(Math.Cos(angleRad), Math.Sin(angleRad), 0); // Transversal
                XYZ dirV = new XYZ(-Math.Sin(angleRad), Math.Cos(angleRad), 0); // Longitudinal

                // Proyectar esquinas al sistema U,V
                double uMin = double.MaxValue, uMax = double.MinValue;
                double vMin = double.MaxValue, vMax = double.MinValue;

                foreach (var pt in f.OuterBoundary)
                {
                    double u = pt.X * dirU.X + pt.Y * dirU.Y;
                    double v = pt.X * dirV.X + pt.Y * dirV.Y;
                    if (u < uMin) uMin = u;
                    if (u > uMax) uMax = u;
                    if (v < vMin) vMin = v;
                    if (v > vMax) vMax = v;
                }

                if (pitchFt <= 0.1) pitchFt = 1.0;
                if (studSpacingFt <= 0.1) studSpacingFt = 1.0;

                for (double curU = uMin; curU <= uMax; curU += pitchFt)
                {
                    XYZ pStart = dirU * curU + dirV * vMin + XYZ.BasisZ * f.BaseElevationFt;
                    XYZ pEnd = dirU * curU + dirV * vMax + XYZ.BasisZ * f.BaseElevationFt;

                    System.Windows.Point cpStart = ToCanvasPt(pStart);
                    System.Windows.Point cpEnd = ToCanvasPt(pEnd);

                    // Línea de onda (cresta/valle) en Canvas 2D
                    System.Windows.Shapes.Line waveLine = new System.Windows.Shapes.Line
                    {
                        X1 = cpStart.X,
                        Y1 = cpStart.Y,
                        X2 = cpEnd.X,
                        Y2 = cpEnd.Y,
                        Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(120, 99, 179, 237)),
                        StrokeThickness = 1,
                        StrokeDashArray = new DoubleCollection { 4, 3 }
                    };
                    previewCanvas.Children.Add(waveLine);

                    // Pernos Nelson Studs a lo largo de la onda
                    if (GenerateStuds)
                    {
                        for (double curV = vMin + studSpacingFt / 2.0; curV <= vMax; curV += studSpacingFt)
                        {
                            XYZ studPt = dirU * curU + dirV * curV + XYZ.BasisZ * f.BaseElevationFt;
                            if (IsPointInsidePolygon(studPt, f.OuterBoundary))
                            {
                                StudPoints.Add(studPt);

                                System.Windows.Point cStud = ToCanvasPt(studPt);
                                System.Windows.Shapes.Ellipse studDot = new System.Windows.Shapes.Ellipse
                                {
                                    Width = 5,
                                    Height = 5,
                                    Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(236, 201, 75)),
                                    Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(183, 121, 31)),
                                    StrokeThickness = 1
                                };
                                Canvas.SetLeft(studDot, cStud.X - 2.5);
                                Canvas.SetTop(studDot, cStud.Y - 2.5);
                                previewCanvas.Children.Add(studDot);
                            }
                        }
                    }
                }
            }
        }

        private bool IsPointInsidePolygon(XYZ pt, List<XYZ> polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                if (((polygon[i].Y > pt.Y) != (polygon[j].Y > pt.Y)) &&
                    (pt.X < (polygon[j].X - polygon[i].X) * (pt.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X))
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private void btnGenerate_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedDeck == null) return;

            // Generar sólidos geométricos en memoria para Revit
            GenerateSolidsForRevit();

            IsConfirmed = true;
            DialogResult = true;
            Close();
        }

        private void GenerateSolidsForRevit()
        {
            DeckSolids.Clear();
            ConcreteSolids.Clear();
            if (SelectedDeck == null) return;

            double ribHFt = (SelectedDeck.RibHeightCm / 100.0) * 3.28084;
            double pitchFt = (SelectedDeck.PitchCm / 100.0) * 3.28084;
            double topWFt = (SelectedDeck.TopFluteWidthCm / 100.0) * 3.28084;
            double botWFt = (SelectedDeck.BottomFluteWidthCm / 100.0) * 3.28084;
            double toppingHFt = (ConcreteToppingCm / 100.0) * 3.28084;
            double thkFt = ((SelectedDeck.ThicknessMm > 0 ? SelectedDeck.ThicknessMm : 0.75) / 1000.0) * 3.28084;

            foreach (var f in _floorInfos)
            {
                if (f.OuterBoundary.Count < 3) continue;

                // 1. Crear Sólido de Capa de Compresión Superior
                if (GenerateConcrete && toppingHFt > 0.001)
                {
                    try
                    {
                        CurveLoop toppingLoop = new CurveLoop();
                        for (int i = 0; i < f.OuterBoundary.Count; i++)
                        {
                            XYZ p0 = f.OuterBoundary[i];
                            XYZ p1 = f.OuterBoundary[(i + 1) % f.OuterBoundary.Count];
                            toppingLoop.Append(Autodesk.Revit.DB.Line.CreateBound(
                                new XYZ(p0.X, p0.Y, f.BaseElevationFt + ribHFt),
                                new XYZ(p1.X, p1.Y, f.BaseElevationFt + ribHFt)));
                        }

                        List<CurveLoop> loops = new List<CurveLoop> { toppingLoop };
                        foreach (var inner in f.InnerBoundaries)
                        {
                            if (inner.Count >= 3)
                            {
                                CurveLoop inLoop = new CurveLoop();
                                for (int i = 0; i < inner.Count; i++)
                                {
                                    XYZ ip0 = inner[i];
                                    XYZ ip1 = inner[(i + 1) % inner.Count];
                                    inLoop.Append(Autodesk.Revit.DB.Line.CreateBound(
                                        new XYZ(ip0.X, ip0.Y, f.BaseElevationFt + ribHFt),
                                        new XYZ(ip1.X, ip1.Y, f.BaseElevationFt + ribHFt)));
                                }
                                loops.Add(inLoop);
                            }
                        }

                        Solid toppingSolid = GeometryCreationUtilities.CreateExtrusionGeometry(loops, XYZ.BasisZ, toppingHFt);
                        if (toppingSolid != null && toppingSolid.Volume > 0)
                        {
                            ConcreteSolids.Add(toppingSolid);
                        }
                    }
                    catch { }
                }

                // 2. Crear Sólidos de Lámina Grecada y Relleno de Valles
                if (GenerateDeckSheet || GenerateConcrete)
                {
                    try
                    {
                        // Crear sólido base de la zona grecada
                        CurveLoop deckBaseLoop = new CurveLoop();
                        for (int i = 0; i < f.OuterBoundary.Count; i++)
                        {
                            XYZ p0 = f.OuterBoundary[i];
                            XYZ p1 = f.OuterBoundary[(i + 1) % f.OuterBoundary.Count];
                            deckBaseLoop.Append(Autodesk.Revit.DB.Line.CreateBound(
                                new XYZ(p0.X, p0.Y, f.BaseElevationFt),
                                new XYZ(p1.X, p1.Y, f.BaseElevationFt)));
                        }

                        List<CurveLoop> deckLoops = new List<CurveLoop> { deckBaseLoop };
                        foreach (var inPts in f.InnerBoundaries)
                        {
                            if (inPts.Count >= 3)
                            {
                                CurveLoop inL = new CurveLoop();
                                for (int i = 0; i < inPts.Count; i++)
                                {
                                    XYZ ip0 = inPts[i];
                                    XYZ ip1 = inPts[(i + 1) % inPts.Count];
                                    inL.Append(Autodesk.Revit.DB.Line.CreateBound(
                                        new XYZ(ip0.X, ip0.Y, f.BaseElevationFt),
                                        new XYZ(ip1.X, ip1.Y, f.BaseElevationFt)));
                                }
                                deckLoops.Add(inL);
                            }
                        }

                        if (GenerateDeckSheet)
                        {
                            Solid deckSheetSolid = GeometryCreationUtilities.CreateExtrusionGeometry(deckLoops, XYZ.BasisZ, thkFt > 0 ? thkFt : 0.01);
                            if (deckSheetSolid != null && deckSheetSolid.Volume > 0)
                            {
                                DeckSolids.Add(deckSheetSolid);
                            }
                        }

                        if (GenerateConcrete)
                        {
                            // Relleno de valles
                            Solid valleyFillSolid = GeometryCreationUtilities.CreateExtrusionGeometry(deckLoops, XYZ.BasisZ, ribHFt);
                            if (valleyFillSolid != null && valleyFillSolid.Volume > 0)
                            {
                                ConcreteSolids.Add(valleyFillSolid);
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            DialogResult = false;
            Close();
        }
    }
}
