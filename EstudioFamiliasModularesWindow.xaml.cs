using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace RevitToSupabasePlugin
{
    public partial class EstudioFamiliasModularesWindow : Window
    {
        private readonly UIDocument? _uidoc;
        private readonly Document? _doc;

        private List<Autodesk.Revit.DB.Material> _materials = new List<Autodesk.Revit.DB.Material>();
        private List<InPlaceFamilyItem> _inPlaceItems = new List<InPlaceFamilyItem>();

        private System.Windows.Point _lastMousePos;
        private double _cameraAngleX = -45;
        private double _cameraAngleY = 55;
        private double _cameraDistance = 120;
        private bool _isDragging = false;
        private bool _isUpdatingSubtypes = false;

        public EstudioFamiliasModularesWindow(UIDocument? uidoc)
        {
            InitializeComponent();
            _uidoc = uidoc;
            _doc = uidoc?.Document;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadMaterials();
            PopulateSubtypesForCategory(0);
            RefreshLoadedModularFamilies();
            RefreshInPlaceList();
            UpdateCamera();
            RenderCurrentPreview();
        }

        private void LoadMaterials()
        {
            if (_doc == null) return;
            try
            {
                _materials = new FilteredElementCollector(_doc)
                    .OfClass(typeof(Autodesk.Revit.DB.Material))
                    .Cast<Autodesk.Revit.DB.Material>()
                    .OrderBy(m => m.Name)
                    .ToList();

                cmbSpecMaterial.ItemsSource = _materials;
                cmbInPlaceMaterial.ItemsSource = _materials;
                cmbRoofTileMaterial.ItemsSource = _materials;
                cmbSkirtingMaterial.ItemsSource = _materials;

                if (_materials.Count > 0)
                {
                    cmbSpecMaterial.SelectedIndex = 0;
                    cmbInPlaceMaterial.SelectedIndex = 0;
                    cmbRoofTileMaterial.SelectedIndex = 0;
                    cmbSkirtingMaterial.SelectedIndex = 0;
                }
            }
            catch { }
        }

        private void RefreshLoadedModularFamilies()
        {
            if (_doc == null) return;
            try
            {
                var allSymbols = new FilteredElementCollector(_doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(s => s.Family != null && !s.Family.IsInPlace)
                    .OrderBy(s => s.Family.Name)
                    .ThenBy(s => s.Name)
                    .Select(s => new TileTypeItem(s))
                    .ToList();

                var roofCandidates = allSymbols.Where(i =>
                    i.FullName.IndexOf("Teja", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    i.FullName.IndexOf("Tile", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    i.FullName.IndexOf("Cubierta", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    i.FullName.IndexOf("Caballete", StringComparison.OrdinalIgnoreCase) >= 0).ToList();

                cmbRoofTileSymbol.ItemsSource = roofCandidates.Count > 0 ? roofCandidates : allSymbols;
                if (cmbRoofTileSymbol.Items.Count > 0) cmbRoofTileSymbol.SelectedIndex = 0;

                var skirtingCandidates = allSymbols.Where(i =>
                    i.FullName.IndexOf("Zocalo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    i.FullName.IndexOf("Zócalo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    i.FullName.IndexOf("Guardaescoba", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    i.FullName.IndexOf("Pega", StringComparison.OrdinalIgnoreCase) >= 0).ToList();

                cmbSkirtingSymbol.ItemsSource = skirtingCandidates.Count > 0 ? skirtingCandidates : allSymbols;
                if (cmbSkirtingSymbol.Items.Count > 0) cmbSkirtingSymbol.SelectedIndex = 0;
            }
            catch { }
        }

        private void CmbModularCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbModularCategory == null || cmbModularSubtype == null) return;
            PopulateSubtypesForCategory(cmbModularCategory.SelectedIndex);
        }

        private void PopulateSubtypesForCategory(int catIdx)
        {
            _isUpdatingSubtypes = true;
            cmbModularSubtype.Items.Clear();

            switch (catIdx)
            {
                case 0: // Tejas para Cubiertas
                    cmbModularSubtype.Items.Add("Barro / Colonial Curva");
                    cmbModularSubtype.Items.Add("Plana / Shingle");
                    cmbModularSubtype.Items.Add("Termoacústica Trapezoidal");
                    SetDefaultSpecFields("Teja_Barro_Modular", "Colonial 45x22 cm", "45.0", "22.0", "1.8", "6.5", "3.0", "7.0", "2.0");
                    break;

                case 1: // Zócalos Perimetrales
                    cmbModularSubtype.Items.Add("Recto Biselado");
                    cmbModularSubtype.Items.Add("Media Caña");
                    cmbModularSubtype.Items.Add("Escalonado Arquitectónico");
                    SetDefaultSpecFields("Zocalo_Perimetral_Modular", "Zócalo h=10cm e=1.5cm", "100.0", "1.5", "1.5", "10.0", "0.0", "0.0", "3.0");
                    break;

                case 2: // Baldosas / Enchapes
                    cmbModularSubtype.Items.Add("Rectificada (Contrato Tiles Base)");
                    cmbModularSubtype.Items.Add("Porcelanato Gran Formato");
                    cmbModularSubtype.Items.Add("Enchape Muro Cerámico");
                    SetDefaultSpecFields("Baldosa_Modular_Review", "Porcelanato 60x60 cm", "60.0", "60.0", "1.0", "1.0", "0.0", "0.0", "1.5");
                    break;

                case 3: // Juntas / Pegas
                    cmbModularSubtype.Items.Add("Junta Plana (Contrato Base Tiles Pega)");
                    cmbModularSubtype.Items.Add("Junta Perimetral elástica 5mm");
                    SetDefaultSpecFields("Junta_Pega_Modular", "Pega 3mm x 10mm", "60.0", "0.3", "1.0", "1.0", "0.0", "0.0", "0.0");
                    break;

                case 4: // Listones / Cielo Raso / Deck
                    cmbModularSubtype.Items.Add("Listón Lineal Madera / WPC");
                    cmbModularSubtype.Items.Add("Baffle Acústico Cielo Raso");
                    SetDefaultSpecFields("Liston_Deck_Modular", "Listón 240x14 cm", "240.0", "14.0", "2.2", "2.2", "0.5", "0.0", "2.0");
                    break;

                case 5: // Caballetes / Cumbreras
                    cmbModularSubtype.Items.Add("Caballete Curvo Colonial");
                    cmbModularSubtype.Items.Add("Caballete Angular Dos Aguas");
                    SetDefaultSpecFields("Caballete_Cumbrera_Modular", "Cumbrera 45x24 cm", "45.0", "24.0", "1.8", "9.0", "0.0", "5.0", "2.0");
                    break;
            }

            if (cmbModularSubtype.Items.Count > 0)
                cmbModularSubtype.SelectedIndex = 0;

            _isUpdatingSubtypes = false;
            RenderCurrentPreview();
        }

        private void SetDefaultSpecFields(string famName, string typeName, string len, string wid, string thk, string wave, string ovU, string ovV, string bev)
        {
            if (txtNewFamilyName != null) txtNewFamilyName.Text = famName;
            if (txtNewTypeName != null) txtNewTypeName.Text = typeName;
            if (txtSpecLengthCm != null) txtSpecLengthCm.Text = len;
            if (txtSpecWidthCm != null) txtSpecWidthCm.Text = wid;
            if (txtSpecThicknessCm != null) txtSpecThicknessCm.Text = thk;
            if (txtSpecWaveCm != null) txtSpecWaveCm.Text = wave;
            if (txtSpecOverlapUCm != null) txtSpecOverlapUCm.Text = ovU;
            if (txtSpecOverlapVCm != null) txtSpecOverlapVCm.Text = ovV;
            if (txtSpecBevelMm != null) txtSpecBevelMm.Text = bev;
        }

        private void CmbModularSubtype_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingSubtypes) return;
            RenderCurrentPreview();
        }

        private void SpecInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingSubtypes) return;
            RenderCurrentPreview();
        }

        private void TabMain_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is System.Windows.Controls.TabControl)
            {
                RenderCurrentPreview();
            }
        }

        private ModularFamilySpec ReadCurrentModularSpec()
        {
            int catIdx = cmbModularCategory?.SelectedIndex ?? 0;
            ModularElementCategory cat = catIdx switch
            {
                0 => ModularElementCategory.TejaCubierta,
                1 => ModularElementCategory.ZocaloPerimetral,
                2 => ModularElementCategory.BaldosaEnchape,
                3 => ModularElementCategory.JuntaPega,
                4 => ModularElementCategory.CieloRasoListon,
                5 => ModularElementCategory.CaballeteCumbrera,
                _ => ModularElementCategory.TejaCubierta
            };

            var mat = cmbSpecMaterial?.SelectedItem as Autodesk.Revit.DB.Material;

            return new ModularFamilySpec
            {
                FamilyName = txtNewFamilyName?.Text?.Trim() ?? "Familia_Modular",
                TypeName = txtNewTypeName?.Text?.Trim() ?? "Estándar",
                Category = cat,
                Subtype = cmbModularSubtype?.SelectedItem?.ToString() ?? "Estándar",
                LengthCm = ParseDouble(txtSpecLengthCm?.Text, 45.0),
                WidthCm = ParseDouble(txtSpecWidthCm?.Text, 22.0),
                ThicknessCm = ParseDouble(txtSpecThicknessCm?.Text, 1.8),
                WaveHeightCm = ParseDouble(txtSpecWaveCm?.Text, 6.5),
                OverlapUCm = ParseDouble(txtSpecOverlapUCm?.Text, 3.0),
                OverlapVCm = ParseDouble(txtSpecOverlapVCm?.Text, 7.0),
                BevelMm = ParseDouble(txtSpecBevelMm?.Text, 2.0),
                MaterialId = mat != null ? mat.Id : ElementId.InvalidElementId,
                MaterialName = mat?.Name ?? ""
            };
        }

        private void BtnGenerateModularRfa_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) return;
            try
            {
                ModularFamilySpec spec = ReadCurrentModularSpec();
                FamilySymbol? createdSymbol = ModularFamilyStudioService.CreateAndLoadModularFamily(_doc, spec, out string savedPath);

                RefreshLoadedModularFamilies();

                if (createdSymbol != null)
                {
                    lblStatus.Text = $"Familia cargable creada y activada en (0,0,0): { savedPath }";
                    System.Windows.MessageBox.Show(
                        $"Familia cargable (.rfa) creada exitosamente con origen en (0,0,0) y cargada en el proyecto:\n\n" +
                        $"• Familia: {createdSymbol.Family.Name}\n" +
                        $"• Tipo: {createdSymbol.Name}\n" +
                        $"• Archivo .RFA: {savedPath}\n\n" +
                        $"Ya está disponible en el Motor de Instanciamiento (Pestaña 3) y en el Modelador de Pisos.",
                        "Familia Modular Creada",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error al crear la familia cargable (.rfa):\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // =========================================================================
        // PESTAÑA 2: IN-SITU A CARGABLE (.RFA)
        // =========================================================================

        private void BtnRefreshInPlace_Click(object sender, RoutedEventArgs e)
        {
            RefreshInPlaceList();
        }

        private void RefreshInPlaceList()
        {
            if (_doc == null) return;
            try
            {
                _inPlaceItems = ModularFamilyStudioService.GetInPlaceFamilies(_doc);
                lstInPlaceFamilies.ItemsSource = _inPlaceItems;

                if (_inPlaceItems.Count > 0)
                {
                    lstInPlaceFamilies.SelectedIndex = 0;
                    lblStatus.Text = $"Se detectaron {_inPlaceItems.Count} familia(s) In-Situ en el modelo listas para convertir a .RFA cargables.";
                }
                else
                {
                    lblStatus.Text = "No se encontraron familias In-Situ (IsInPlace) en el documento actual.";
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Error escaneando In-Situ: " + ex.Message;
            }
        }

        private void LstInPlaceFamilies_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstInPlaceFamilies.SelectedItem is InPlaceFamilyItem item)
            {
                txtInPlaceNewFamilyName.Text = item.FamilyName + "_Cargable";
                if (item.MaterialId != ElementId.InvalidElementId)
                {
                    var foundMat = _materials.FirstOrDefault(m => m.Id == item.MaterialId);
                    if (foundMat != null) cmbInPlaceMaterial.SelectedItem = foundMat;
                }
                RenderCurrentPreview();
            }
        }

        private void BtnSelectInPlaceInView_Click(object sender, RoutedEventArgs e)
        {
            if (_uidoc == null) return;
            if (lstInPlaceFamilies.SelectedItem is InPlaceFamilyItem item)
            {
                try
                {
                    _uidoc.Selection.SetElementIds(new List<ElementId> { item.InstanceId });
                    _uidoc.ShowElements(item.InstanceId);
                }
                catch { }
            }
        }

        private void BtnConvertInPlace_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) return;
            if (lstInPlaceFamilies.SelectedItem is not InPlaceFamilyItem item)
            {
                System.Windows.MessageBox.Show("Selecciona primero una Familia In-Situ de la lista.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                string newFamName = txtInPlaceNewFamilyName.Text.Trim();
                var overrideMat = cmbInPlaceMaterial.SelectedItem as Autodesk.Revit.DB.Material;
                bool replaceInModel = chkReplaceInPlaceInstance.IsChecked == true;

                FamilySymbol? sym = ModularFamilyStudioService.ConvertInPlaceToLoadableFamily(
                    _doc,
                    item,
                    newFamName,
                    "Tipo Modular (0,0)",
                    overrideMat != null ? overrideMat.Id : ElementId.InvalidElementId,
                    replaceInModel,
                    out string savedRfaPath,
                    out FamilyInstance? replacementInst);

                RefreshInPlaceList();
                RefreshLoadedModularFamilies();

                if (sym != null)
                {
                    string replaceMsg = replacementInst != null
                        ? $"• Instancia In-Situ reemplazada en su coordenada (0,0) por ID {replacementInst.Id.Value}."
                        : "• Familia cargada en el proyecto (el elemento In-Situ original se conservó).";

                    System.Windows.MessageBox.Show(
                        $"Conversión de In-Situ a Familia Cargable (.rfa) completada:\n\n" +
                        $"• Nueva Familia: {sym.Family.Name}\n" +
                        $"• Dimensiones en (0,0,0): {item.LengthCm:F1} × {item.WidthCm:F1} × {item.HeightCm:F1} cm\n" +
                        $"• Archivo .RFA: {savedRfaPath}\n" +
                        $"{replaceMsg}",
                        "Conversión In-Situ Exitosa",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error convirtiendo familia In-Situ a cargable:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // =========================================================================
        // PESTAÑA 3: MOTOR DE INSTANCIAMIENTO EN PROYECTO DESDE (0,0)
        // =========================================================================

        private void BtnPlaceRoofTiles_Click(object sender, RoutedEventArgs e)
        {
            if (_uidoc == null || _doc == null) return;
            if (cmbRoofTileSymbol.SelectedItem is not TileTypeItem tileItem || tileItem.Symbol == null)
            {
                System.Windows.MessageBox.Show("Selecciona o genera primero una Familia Cargable de Teja (.rfa).", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            double ovU = ParseDouble(txtRoofOverlapUCm.Text, 3.0);
            double ovV = ParseDouble(txtRoofOverlapVCm.Text, 7.0);
            var mat = cmbRoofTileMaterial.SelectedItem as Autodesk.Revit.DB.Material;

            this.Hide();
            try
            {
                Reference pickedRef = _uidoc.Selection.PickObject(
                    ObjectType.Element,
                    "Selecciona la Cubierta (Roof) o Losa inclinada donde instanciar las tejas desde (0,0)");

                Element? host = _doc.GetElement(pickedRef);
                if (host != null)
                {
                    int count = ModularFamilyStudioService.InstantiateRoofTilesOnHost(
                        _doc,
                        host,
                        tileItem.Symbol,
                        ovU,
                        ovV,
                        mat != null ? mat.Id : ElementId.InvalidElementId);

                    System.Windows.MessageBox.Show(
                        $"Se instanciaron {count} piezas de '{tileItem.FullName}' sobre la cubierta desde el origen (0,0) de cada faldón.",
                        "Modulación de Cubierta Completada",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error instanciando tejas en cubierta:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.ShowDialog();
            }
        }

        private void BtnPlaceSkirtingOnFloor_Click(object sender, RoutedEventArgs e)
        {
            if (_uidoc == null || _doc == null) return;
            if (cmbSkirtingSymbol.SelectedItem is not TileTypeItem skirtItem || skirtItem.Symbol == null)
            {
                System.Windows.MessageBox.Show("Selecciona o genera primero una Familia Cargable de Zócalo (.rfa).", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var mat = cmbSkirtingMaterial.SelectedItem as Autodesk.Revit.DB.Material;

            this.Hide();
            try
            {
                Reference pickedRef = _uidoc.Selection.PickObject(
                    ObjectType.Element,
                    new FloorSelectionFilter(),
                    "Selecciona el Piso (Floor) para instanciar zócalos perimetrales desde (0,0)");

                if (_doc.GetElement(pickedRef) is Floor floor)
                {
                    int count = ModularFamilyStudioService.InstantiateSkirtingOnFloor(
                        _doc,
                        floor,
                        skirtItem.Symbol,
                        mat != null ? mat.Id : ElementId.InvalidElementId);

                    System.Windows.MessageBox.Show(
                        $"Se instanciaron {count} tramos de zócalo '{skirtItem.FullName}' en el perímetro del piso.",
                        "Zócalos Instanciados",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error instanciando zócalos:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.ShowDialog();
            }
        }

        private void BtnOpenFloorModeler_Click(object sender, RoutedEventArgs e)
        {
            if (_uidoc == null) return;
            var managerWin = new ManagerFamiliasPisoWindow(_uidoc);
            managerWin.ShowDialog();
            RefreshLoadedModularFamilies();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        // =========================================================================
        // VISOR 3D INTERACTIVO EN ORIGEN CARTESIANO (0,0,0)
        // =========================================================================

        private void RenderCurrentPreview()
        {
            if (modelGroup == null) return;

            modelGroup.Children.Clear();
            modelGroup.Children.Add(new AmbientLight(System.Windows.Media.Color.FromRgb(115, 115, 115)));
            modelGroup.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-1.2, 1.0, -1.8)));
            modelGroup.Children.Add(new DirectionalLight(System.Windows.Media.Color.FromRgb(90, 105, 120), new Vector3D(1.0, -1.2, 0.8)));

            // Si estamos en la Pestaña 2 (In-Situ) y hay un elemento seleccionado, renderizar su malla real
            if (tabMain != null && tabMain.SelectedIndex == 1 && lstInPlaceFamilies?.SelectedItem is InPlaceFamilyItem inPlaceItem)
            {
                lblPreviewTitle.Text = $"VISTA 3D IN-SITU NORMALIZADA A (0,0,0): {inPlaceItem.FamilyName}";
                lblPreviewMetrics.Text = $"Largo X: {inPlaceItem.LengthCm:F1} cm | Ancho Y: {inPlaceItem.WidthCm:F1} cm | Alto Z: {inPlaceItem.HeightCm:F1} cm | Vol: {inPlaceItem.VolumeM3:F3} m³";

                double maxDim = Math.Max(20, Math.Max(inPlaceItem.LengthCm, Math.Max(inPlaceItem.WidthCm, inPlaceItem.HeightCm)));
                AddCartesianAxesAndGrid(maxDim * 0.75);

                MeshGeometry3D inPlaceMesh = ModularFamilyStudioService.BuildWpfMeshFromInPlaceItem(inPlaceItem);
                var mat = new DiffuseMaterial(new SolidColorBrush(System.Windows.Media.Color.FromRgb(205, 133, 84)));
                var geomModel = new GeometryModel3D(inPlaceMesh, mat) { BackMaterial = mat };
                // Trasladar para que su esquina mínima se vea en (0,0,0)
                geomModel.Transform = new TranslateTransform3D(inPlaceItem.LengthCm * 0.5, inPlaceItem.WidthCm * 0.5, inPlaceItem.HeightCm * 0.5);
                modelGroup.Children.Add(geomModel);
                return;
            }

            // Renderizar el arquetipo paramétrico de la Pestaña 1 desde (0,0,0)
            ModularFamilySpec spec = ReadCurrentModularSpec();
            double len = Math.Max(1.0, spec.LengthCm);
            double wid = Math.Max(0.3, spec.WidthCm);
            double thk = Math.Max(0.3, spec.ThicknessCm);
            double wave = Math.Max(0.0, spec.WaveHeightCm);

            lblPreviewTitle.Text = $"VISTA PREVIA 3D EN (0,0,0) — {spec.FamilyName} ({spec.Subtype})";
            double stepU = Math.Max(0.1, wid - spec.OverlapUCm);
            double stepV = Math.Max(0.1, len - spec.OverlapVCm);
            lblPreviewMetrics.Text = $"U (Length): {len:F1} cm | V (Width): {wid:F1} cm | Espesor: {thk:F1} cm | Paso útil: {stepU:F1} × {stepV:F1} cm";

            double axisSpan = Math.Max(30.0, Math.Max(len, wid) * 1.25);
            AddCartesianAxesAndGrid(axisSpan);

            if (spec.Category == ModularElementCategory.TejaCubierta && !spec.Subtype.Contains("Plana"))
            {
                modelGroup.Children.Add(BuildWaveTilePreviewModel(len, wid, thk, Math.Max(thk * 1.5, wave)));
            }
            else if (spec.Category == ModularElementCategory.ZocaloPerimetral)
            {
                double h = Math.Max(4.0, wave);
                modelGroup.Children.Add(BuildBoxAtOrigin(len, wid, h, System.Windows.Media.Color.FromRgb(218, 212, 202)));
            }
            else if (spec.Category == ModularElementCategory.CaballeteCumbrera)
            {
                double h = Math.Max(5.0, wave);
                modelGroup.Children.Add(BuildRidgePreviewModel(len, wid, thk, h));
            }
            else
            {
                System.Windows.Media.Color col = spec.Category == ModularElementCategory.JuntaPega
                    ? System.Windows.Media.Color.FromRgb(130, 145, 160)
                    : System.Windows.Media.Color.FromRgb(224, 228, 232);
                modelGroup.Children.Add(BuildBoxAtOrigin(len, wid, thk, col));
            }
        }

        private void AddCartesianAxesAndGrid(double span)
        {
            // Marcador esférico/cubo en el Origen exacto (0,0,0)
            GeometryModel3D originMarker = BuildBoxCentered(2.2, 2.2, 2.2, System.Windows.Media.Color.FromRgb(255, 193, 7));
            modelGroup.Children.Add(originMarker);

            // Eje X / U (Rojo) desde (0,0,0) hacia +X
            GeometryModel3D axisX = BuildBoxAtOrigin(span, 0.5, 0.5, System.Windows.Media.Color.FromRgb(229, 57, 53));
            // Eje Y / V (Verde) desde (0,0,0) hacia +Y
            GeometryModel3D axisY = BuildBoxAtOrigin(0.5, span, 0.5, System.Windows.Media.Color.FromRgb(67, 160, 71));
            // Eje Z (Azul) desde (0,0,0) hacia +Z
            GeometryModel3D axisZ = BuildBoxAtOrigin(0.5, 0.5, span * 0.6, System.Windows.Media.Color.FromRgb(30, 136, 229));

            modelGroup.Children.Add(axisX);
            modelGroup.Children.Add(axisY);
            modelGroup.Children.Add(axisZ);

            // Plano base en el primer cuadrante (X >= 0, Y >= 0)
            GeometryModel3D basePlane = BuildBoxAtOrigin(span, span, 0.1, System.Windows.Media.Color.FromArgb(40, 255, 255, 255));
            basePlane.Transform = new TranslateTransform3D(0, 0, -0.15);
            modelGroup.Children.Add(basePlane);
        }

        private GeometryModel3D BuildWaveTilePreviewModel(double lenX, double widY, double thkZ, double waveZ)
        {
            Model3DGroup group = new Model3DGroup();
            int strips = 14;
            double dy = widY / strips;
            System.Windows.Media.Color clayColor = System.Windows.Media.Color.FromRgb(196, 102, 62);

            MeshGeometry3D mesh = new MeshGeometry3D();
            for (int i = 0; i < strips; i++)
            {
                double t0 = (double)i / strips;
                double t1 = (double)(i + 1) / strips;
                double y0 = i * dy;
                double y1 = (i + 1) * dy;
                double z0 = (waveZ * 0.5) * (1.0 - Math.Cos(2.0 * Math.PI * t0));
                double z1 = (waveZ * 0.5) * (1.0 - Math.Cos(2.0 * Math.PI * t1));

                AppendExtrudedSegmentToMesh(mesh, lenX, y0, y1, z0, z1, thkZ);
            }

            var mat = new DiffuseMaterial(new SolidColorBrush(clayColor));
            return new GeometryModel3D(mesh, mat) { BackMaterial = mat };
        }

        private GeometryModel3D BuildRidgePreviewModel(double lenX, double widY, double thkZ, double heightZ)
        {
            MeshGeometry3D mesh = new MeshGeometry3D();
            double halfY = widY * 0.5;
            AppendExtrudedSegmentToMesh(mesh, lenX, 0, halfY, 0, heightZ, thkZ);
            AppendExtrudedSegmentToMesh(mesh, lenX, halfY, widY, heightZ, 0, thkZ);
            var mat = new DiffuseMaterial(new SolidColorBrush(System.Windows.Media.Color.FromRgb(185, 92, 54)));
            return new GeometryModel3D(mesh, mat) { BackMaterial = mat };
        }

        private void AppendExtrudedSegmentToMesh(MeshGeometry3D mesh, double lenX, double y0, double y1, double z0, double z1, double thkZ)
        {
            int b = mesh.Positions.Count;
            mesh.Positions.Add(new Point3D(0, y0, z0));
            mesh.Positions.Add(new Point3D(lenX, y0, z0));
            mesh.Positions.Add(new Point3D(lenX, y1, z1));
            mesh.Positions.Add(new Point3D(0, y1, z1));
            mesh.Positions.Add(new Point3D(0, y0, z0 + thkZ));
            mesh.Positions.Add(new Point3D(lenX, y0, z0 + thkZ));
            mesh.Positions.Add(new Point3D(lenX, y1, z1 + thkZ));
            mesh.Positions.Add(new Point3D(0, y1, z1 + thkZ));

            int[] idx = {
                0,1,2, 0,2,3,
                4,6,5, 4,7,6,
                0,5,1, 0,4,5,
                3,2,6, 3,6,7,
                0,3,7, 0,7,4,
                1,5,6, 1,6,2
            };
            foreach (int i in idx) mesh.TriangleIndices.Add(b + i);
        }

        private GeometryModel3D BuildBoxAtOrigin(double lenX, double widY, double thkZ, System.Windows.Media.Color color)
        {
            MeshGeometry3D mesh = new MeshGeometry3D();
            AppendExtrudedSegmentToMesh(mesh, lenX, 0, widY, 0, 0, thkZ);
            var mat = new DiffuseMaterial(new SolidColorBrush(color));
            return new GeometryModel3D(mesh, mat) { BackMaterial = mat };
        }

        private GeometryModel3D BuildBoxCentered(double lx, double wy, double hz, System.Windows.Media.Color color)
        {
            GeometryModel3D m = BuildBoxAtOrigin(lx, wy, hz, color);
            m.Transform = new TranslateTransform3D(-lx * 0.5, -wy * 0.5, -hz * 0.5);
            return m;
        }

        private void UpdateCamera()
        {
            if (camera3D == null) return;
            double radX = _cameraAngleX * Math.PI / 180.0;
            double radY = _cameraAngleY * Math.PI / 180.0;

            double cx = 20.0, cy = 15.0, cz = 5.0;
            double x = cx + _cameraDistance * Math.Sin(radY) * Math.Cos(radX);
            double y = cy + _cameraDistance * Math.Sin(radY) * Math.Sin(radX);
            double z = cz + _cameraDistance * Math.Cos(radY);

            camera3D.Position = new Point3D(x, y, z);
            Vector3D look = new Vector3D(cx - x, cy - y, cz - z);
            look.Normalize();
            camera3D.LookDirection = look;
            camera3D.UpDirection = new Vector3D(0, 0, 1);
        }

        private void BtnResetCamera_Click(object sender, RoutedEventArgs e)
        {
            _cameraAngleX = -45;
            _cameraAngleY = 55;
            _cameraDistance = 115;
            UpdateCamera();
        }

        private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _lastMousePos = e.GetPosition(this);
            Mouse.Capture((IInputElement)sender);
        }

        private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            Mouse.Capture(null);
        }

        private void Viewport_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isDragging) return;
            System.Windows.Point cur = e.GetPosition(this);
            _cameraAngleX -= (cur.X - _lastMousePos.X) * 0.5;
            _cameraAngleY = Math.Max(10, Math.Min(85, _cameraAngleY - (cur.Y - _lastMousePos.Y) * 0.5));
            _lastMousePos = cur;
            UpdateCamera();
        }

        private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            _cameraDistance = Math.Max(20, Math.Min(500, _cameraDistance - e.Delta * 0.08));
            UpdateCamera();
        }

        private static double ParseDouble(string? text, double fallback)
        {
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            if (double.TryParse(text.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                return v;
            return fallback;
        }
    }
}
