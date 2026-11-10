using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitToSupabasePlugin
{
    public partial class GestorInSituCargableWindow : Window
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

        public GestorInSituCargableWindow(UIDocument? uidoc)
        {
            InitializeComponent();
            _uidoc = uidoc;
            _doc = uidoc?.Document;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadMaterials();
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

                cmbInPlaceMaterial.ItemsSource = _materials;
                if (_materials.Count > 0)
                {
                    cmbInPlaceMaterial.SelectedIndex = 0;
                }
            }
            catch { }
        }

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
                    RenderCurrentPreview();
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

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void RenderCurrentPreview()
        {
            if (modelGroup == null) return;

            modelGroup.Children.Clear();
            modelGroup.Children.Add(new AmbientLight(System.Windows.Media.Color.FromRgb(115, 115, 115)));
            modelGroup.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-1.2, 1.0, -1.8)));
            modelGroup.Children.Add(new DirectionalLight(System.Windows.Media.Color.FromRgb(90, 105, 120), new Vector3D(1.0, -1.2, 0.8)));

            if (lstInPlaceFamilies?.SelectedItem is InPlaceFamilyItem inPlaceItem)
            {
                lblPreviewTitle.Text = $"VISTA 3D IN-SITU NORMALIZADA A (0,0,0): {inPlaceItem.FamilyName}";
                lblPreviewMetrics.Text = $"Largo X: {inPlaceItem.LengthCm:F1} cm | Ancho Y: {inPlaceItem.WidthCm:F1} cm | Alto Z: {inPlaceItem.HeightCm:F1} cm | Vol: {inPlaceItem.VolumeM3:F3} m³";

                double maxDim = Math.Max(20, Math.Max(inPlaceItem.LengthCm, Math.Max(inPlaceItem.WidthCm, inPlaceItem.HeightCm)));
                AddCartesianAxesAndGrid(maxDim * 0.75);

                MeshGeometry3D inPlaceMesh = ModularFamilyStudioService.BuildWpfMeshFromInPlaceItem(inPlaceItem);
                var mat = new DiffuseMaterial(new SolidColorBrush(System.Windows.Media.Color.FromRgb(205, 133, 84)));
                var geomModel = new GeometryModel3D(inPlaceMesh, mat) { BackMaterial = mat };
                geomModel.Transform = new TranslateTransform3D(inPlaceItem.LengthCm * 0.5, inPlaceItem.WidthCm * 0.5, inPlaceItem.HeightCm * 0.5);
                modelGroup.Children.Add(geomModel);
            }
            else
            {
                lblPreviewTitle.Text = "VISTA 3D IN-SITU NORMALIZADA EN (0,0,0)";
                lblPreviewMetrics.Text = "Sin elemento In-Situ seleccionado.";
                AddCartesianAxesAndGrid(40.0);
            }
        }

        private void AddCartesianAxesAndGrid(double span)
        {
            GeometryModel3D originMarker = BuildBoxCentered(2.2, 2.2, 2.2, System.Windows.Media.Color.FromRgb(255, 193, 7));
            modelGroup.Children.Add(originMarker);

            GeometryModel3D axisX = BuildBoxAtOrigin(span, 0.5, 0.5, System.Windows.Media.Color.FromRgb(229, 57, 53));
            GeometryModel3D axisY = BuildBoxAtOrigin(0.5, span, 0.5, System.Windows.Media.Color.FromRgb(67, 160, 71));
            GeometryModel3D axisZ = BuildBoxAtOrigin(0.5, 0.5, span * 0.6, System.Windows.Media.Color.FromRgb(30, 136, 229));

            modelGroup.Children.Add(axisX);
            modelGroup.Children.Add(axisY);
            modelGroup.Children.Add(axisZ);

            GeometryModel3D basePlane = BuildBoxAtOrigin(span, span, 0.1, System.Windows.Media.Color.FromArgb(40, 255, 255, 255));
            basePlane.Transform = new TranslateTransform3D(0, 0, -0.15);
            modelGroup.Children.Add(basePlane);
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
    }
}
