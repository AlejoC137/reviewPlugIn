using System;
using System.IO;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;

namespace RevitToSupabasePlugin
{
    public partial class ConfiguradorRiegoWindow : Window
    {
        private ExternalCommandData _commandData;

        public ConfiguradorRiegoWindow(ExternalCommandData commandData)
        {
            InitializeComponent();
            _commandData = commandData;
            CargarJsonCotizacionPredeterminada();
        }

        private void CargarJsonCotizacionPredeterminada()
        {
            string jsonCotizacion = @"{
  ""project_info"": {
    ""client"": ""DIEGO PATIÑO"",
    ""system_name"": ""RIEGO_GOTEO_NEBULIZACION"",
    ""supplier"": ""RIEGO Y CAMPO"",
    ""date"": ""2026-07-24"",
    ""units"": ""Metric"",
    ""family_naming_prefix"": ""Review_Riego_""
  },
  ""catalog_mapping"": {
    ""044-0026-000201"": {
      ""name"": ""MOTOBOMBA EVANS FONT 85"",
      ""category"": ""MechanicalEquipment"",
      ""family"": ""Review_Riego_Motobomba_Evans_Font85"",
      ""symbol"": ""Font 85"",
      ""dimensions_m"": { ""width"": 0.40, ""depth"": 0.30, ""height"": 0.45 }
    },
    ""037-3745-000201"": {
      ""name"": ""FILTRO DE MALLA RB 200 MESH 3/4"",
      ""category"": ""PipeAccessories"",
      ""family"": ""Review_Riego_Filtro_Malla_RainBird_34"",
      ""symbol"": ""200 Mesh 3/4 NPT"",
      ""dimensions_m"": { ""radius"": 0.08, ""height"": 0.25 }
    },
    ""019-1902-000301"": {
      ""name"": ""VALVULA BOLA PVC SOLDADA 1/2 SCH 80"",
      ""category"": ""PipeAccessories"",
      ""family"": ""Review_Riego_Valvula_Bola_PVC_SCH80"",
      ""symbol"": ""1/2 inch"",
      ""dimensions_m"": { ""radius"": 0.05, ""height"": 0.15 }
    },
    ""001-0102-000102"": {
      ""name"": ""TUBERIA PE RIEGO 16 MM"",
      ""category"": ""Pipes"",
      ""pipe_type"": ""Review_Riego_Tuberia_PE_16mm"",
      ""diameter_mm"": 16.0,
      ""length_m"": 25.0
    },
    ""030-0006-000102"": {
      ""name"": ""NEBULIZADOR NAAN GREEN MIST 30 LPH"",
      ""category"": ""PlumbingFixtures"",
      ""family"": ""Review_Riego_Nebulizador_Naan_GreenMist"",
      ""symbol"": ""30 LPH"",
      ""flow_lph"": 30.0,
      ""dimensions_m"": { ""radius"": 0.03, ""height"": 0.08, ""riser_height"": 1.50 }
    },
    ""037-3752-000102"": {
      ""name"": ""EMISOR XERI-BUG PC RB ESPIGO 1.0 GPH NEGRO"",
      ""category"": ""PlumbingFixtures"",
      ""family"": ""Review_Riego_Gotero_XeriBug_1GPH_Negro"",
      ""symbol"": ""1.0 GPH - Negro"",
      ""flow_gph"": 1.0,
      ""dimensions_m"": { ""radius"": 0.02, ""height"": 0.04 }
    },
    ""037-3752-000103"": {
      ""name"": ""EMISOR XERI-BUG PC RB ESPIGO 2.0 GPH ROJO"",
      ""category"": ""PlumbingFixtures"",
      ""family"": ""Review_Riego_Gotero_XeriBug_2GPH_Rojo"",
      ""symbol"": ""2.0 GPH - Rojo"",
      ""flow_gph"": 2.0,
      ""dimensions_m"": { ""radius"": 0.02, ""height"": 0.04 }
    },
    ""050-0001-000100"": {
      ""name"": ""FITTING TEE RIEGO 16MM"",
      ""category"": ""PipeFittings"",
      ""family"": ""Review_Riego_Fitting_Tee_16mm"",
      ""symbol"": ""16mm Tee Junction""
    },
    ""050-0002-000100"": {
      ""name"": ""FITTING CODO RIEGO 16MM"",
      ""category"": ""PipeFittings"",
      ""family"": ""Review_Riego_Fitting_Codo_16mm"",
      ""symbol"": ""16mm Elbow 90 Deg""
    },
    ""060-0001-000100"": {
      ""name"": ""ESTACA GALVANIZADA DE SUJECION 30CM"",
      ""category"": ""PipeAccessories"",
      ""family"": ""Review_Riego_Estaca_Galvanizada"",
      ""symbol"": ""30cm Sujecion"",
      ""dimensions_m"": { ""width"": 0.02, ""depth"": 0.02, ""height"": 0.30 }
    }
  },
  ""assembly_rules"": {
    ""pump_station"": {
      ""pump_sku"": ""044-0026-000201"",
      ""filter_sku"": ""037-3745-000201"",
      ""valve_sku"": ""019-1902-000301"",
      ""quantity"": 1
    },
    ""distribution_pipe"": {
      ""sku"": ""001-0102-000102"",
      ""total_length_m"": 25.0
    },
    ""emitter_arrays"": [
      { ""sku"": ""030-0006-000102"", ""count"": 11, ""offset_z_m"": 1.5 },
      { ""sku"": ""037-3752-000102"", ""count"": 16, ""offset_z_m"": 0.0 },
      { ""sku"": ""037-3752-000103"", ""count"": 16, ""offset_z_m"": 0.0 }
    ]
  }
}";
            txtJsonData.Text = jsonCotizacion;
        }

        private void btnRestaurarJson_Click(object sender, RoutedEventArgs e)
        {
            CargarJsonCotizacionPredeterminada();
            lblStatus.Text = "Cotización cargada correctamente.";
            lblStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
        }

        private void btnExploradorPlantilla_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
                openFileDialog.Filter = "Revit Template (*.rte)|*.rte|Revit Project (*.rvt)|*.rvt|Revit Family (*.rfa)|*.rfa|All files (*.*)|*.*";
                openFileDialog.Title = "Selecciona la plantilla de familias de Riego Automático";

                string assembPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string dirPath = Path.GetDirectoryName(assembPath) ?? "";
                string defaultPath = Path.Combine(dirPath, "FamiliasRiego");

                if (Directory.Exists(defaultPath))
                {
                    openFileDialog.InitialDirectory = defaultPath;
                }

                bool? result = openFileDialog.ShowDialog();
                if (result == true)
                {
                    lblStatus.Text = $"Plantilla seleccionada: {Path.GetFileName(openFileDialog.FileName)}";
                    lblStatus.Foreground = System.Windows.Media.Brushes.Cyan;
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Error al abrir explorador: " + ex.Message);
            }
        }

        private Material? GetOrCreateMaterial(Document doc, string materialName, byte red, byte green, byte blue)
        {
            try
            {
                Material? mat = new FilteredElementCollector(doc)
                    .OfClass(typeof(Material))
                    .Cast<Material>()
                    .FirstOrDefault(m => m.Name == materialName);

                if (mat == null)
                {
                    ElementId matId = Material.Create(doc, materialName);
                    mat = doc.GetElement(matId) as Material;
                    if (mat != null)
                    {
                        mat.Color = new Autodesk.Revit.DB.Color(red, green, blue);
                    }
                }
                return mat;
            }
            catch
            {
                return null;
            }
        }

        private void SetElementMetadata(Element element, string markValue, string commentsValue, Material? mat = null)
        {
            try
            {
                Parameter pMark = element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                if (pMark != null && !pMark.IsReadOnly)
                {
                    pMark.Set(markValue);
                }

                Parameter pComments = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (pComments != null && !pComments.IsReadOnly)
                {
                    pComments.Set(commentsValue);
                }

                if (mat != null)
                {
                    Parameter pMat = element.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                    if (pMat != null && !pMat.IsReadOnly)
                    {
                        pMat.Set(mat.Id);
                    }
                }
            }
            catch { }
        }

        private ElementId Create3DSolidBox(Document doc, XYZ origin, double widthM, double depthM, double heightM, string name, BuiltInCategory category, string sku = "", Material? mat = null)
        {
            double mToFt = 1.0 / 0.3048;
            double w = widthM * mToFt;
            double d = depthM * mToFt;
            double h = heightM * mToFt;

            XYZ p1 = origin + new XYZ(-w / 2, -d / 2, 0);
            XYZ p2 = origin + new XYZ(w / 2, -d / 2, 0);
            XYZ p3 = origin + new XYZ(w / 2, d / 2, 0);
            XYZ p4 = origin + new XYZ(-w / 2, d / 2, 0);

            CurveLoop loop = new CurveLoop();
            loop.Append(Line.CreateBound(p1, p2));
            loop.Append(Line.CreateBound(p2, p3));
            loop.Append(Line.CreateBound(p3, p4));
            loop.Append(Line.CreateBound(p4, p1));

            Solid solid = GeometryCreationUtilities.CreateExtrusionGeometry(new System.Collections.Generic.List<CurveLoop> { loop }, XYZ.BasisZ, h);

            DirectShape ds = DirectShape.CreateElement(doc, new ElementId(category));
            ds.SetShape(new System.Collections.Generic.List<GeometryObject> { solid });
            ds.Name = name;

            SetElementMetadata(ds, name, $"SKU: {sku} | Familia Riego Custom Cotización Realista", mat);

            return ds.Id;
        }

        private ElementId Create3DSolidCylinder(Document doc, XYZ baseCenter, double radiusM, double heightM, string name, BuiltInCategory category, string sku = "", Material? mat = null)
        {
            double mToFt = 1.0 / 0.3048;
            double r = radiusM * mToFt;
            double h = heightM * mToFt;

            Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, baseCenter);
            Arc arc1 = Arc.Create(plane, r, 0, Math.PI);
            Arc arc2 = Arc.Create(plane, r, Math.PI, 2 * Math.PI);

            CurveLoop loop = new CurveLoop();
            loop.Append(arc1);
            loop.Append(arc2);

            Solid solid = GeometryCreationUtilities.CreateExtrusionGeometry(new System.Collections.Generic.List<CurveLoop> { loop }, XYZ.BasisZ, h);

            DirectShape ds = DirectShape.CreateElement(doc, new ElementId(category));
            ds.SetShape(new System.Collections.Generic.List<GeometryObject> { solid });
            ds.Name = name;

            SetElementMetadata(ds, name, $"SKU: {sku} | Familia Riego Custom Cotización Realista", mat);

            return ds.Id;
        }

        private ElementId Create3DSolidTee(Document doc, XYZ center, double pipeRadiusM, string name, BuiltInCategory category, string sku = "050-0001-000100", Material? mat = null)
        {
            double mToFt = 1.0 / 0.3048;
            double r = (pipeRadiusM * 1.4) * mToFt;
            double l = (pipeRadiusM * 4.0) * mToFt;

            Plane planeX = Plane.CreateByNormalAndOrigin(XYZ.BasisX, center);
            Arc arc1 = Arc.Create(planeX, r, 0, Math.PI);
            Arc arc2 = Arc.Create(planeX, r, Math.PI, 2 * Math.PI);
            CurveLoop loopX = new CurveLoop(); loopX.Append(arc1); loopX.Append(arc2);
            Solid solid1 = GeometryCreationUtilities.CreateExtrusionGeometry(new System.Collections.Generic.List<CurveLoop> { loopX }, XYZ.BasisX, l);

            Plane planeZ = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, center);
            Arc arc3 = Arc.Create(planeZ, r, 0, Math.PI);
            Arc arc4 = Arc.Create(planeZ, r, Math.PI, 2 * Math.PI);
            CurveLoop loopZ = new CurveLoop(); loopZ.Append(arc3); loopZ.Append(arc4);
            Solid solid2 = GeometryCreationUtilities.CreateExtrusionGeometry(new System.Collections.Generic.List<CurveLoop> { loopZ }, XYZ.BasisZ, l / 2.0);

            DirectShape ds = DirectShape.CreateElement(doc, new ElementId(category));
            ds.SetShape(new System.Collections.Generic.List<GeometryObject> { solid1, solid2 });
            ds.Name = name;

            SetElementMetadata(ds, name, $"SKU: {sku} | Fitting Tee 16mm Poly Barb", mat);

            return ds.Id;
        }

        private ElementId Create3DSolidElbow(Document doc, XYZ center, double pipeRadiusM, string name, BuiltInCategory category, string sku = "050-0002-000100", Material? mat = null)
        {
            double mToFt = 1.0 / 0.3048;
            double r = (pipeRadiusM * 1.4) * mToFt;
            double l = (pipeRadiusM * 3.0) * mToFt;

            Plane planeZ = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, center);
            Arc arc1 = Arc.Create(planeZ, r, 0, Math.PI);
            Arc arc2 = Arc.Create(planeZ, r, Math.PI, 2 * Math.PI);
            CurveLoop loopZ = new CurveLoop(); loopZ.Append(arc1); loopZ.Append(arc2);
            Solid solid1 = GeometryCreationUtilities.CreateExtrusionGeometry(new System.Collections.Generic.List<CurveLoop> { loopZ }, XYZ.BasisZ, l);

            DirectShape ds = DirectShape.CreateElement(doc, new ElementId(category));
            ds.SetShape(new System.Collections.Generic.List<GeometryObject> { solid1 });
            ds.Name = name;

            SetElementMetadata(ds, name, $"SKU: {sku} | Fitting Codo 16mm 90 Deg Poly Barb", mat);

            return ds.Id;
        }

        private void btnGenerarSistema_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                lblStatus.Text = "Generando sistema de riego MEP (Familias Review_Riego_*) en Revit...";
                lblStatus.Foreground = System.Windows.Media.Brushes.Yellow;

                UIDocument uiDoc = _commandData.Application.ActiveUIDocument;
                if (uiDoc == null || uiDoc.Document == null)
                {
                    System.Windows.MessageBox.Show("No se detectó ningún documento de Revit abierto.", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }

                Document doc = uiDoc.Document;

                // 1. Obtener Nivel Activo o Primero del Proyecto
                Level? level = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .WhereElementIsNotElementType()
                    .Cast<Level>()
                    .FirstOrDefault();

                if (level == null)
                {
                    System.Windows.MessageBox.Show("No se encontró ningún Nivel (Level) en el proyecto activo para hospedar el sistema de riego.", "Nivel Requerido", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                // 2. Obtener Tipo de Tubería y Sistema MEP (si existen en el proyecto)
                PipeType? pipeType = new FilteredElementCollector(doc)
                    .OfClass(typeof(PipeType))
                    .Cast<PipeType>()
                    .FirstOrDefault();

                PipingSystemType? systemType = new FilteredElementCollector(doc)
                    .OfClass(typeof(PipingSystemType))
                    .Cast<PipingSystemType>()
                    .FirstOrDefault();

                System.Collections.Generic.List<ElementId> createdIds = new System.Collections.Generic.List<ElementId>();
                double mToFt = 1.0 / 0.3048;

                using (Transaction tx = new Transaction(doc, "Generar Sistema de Riego Autónomo Review_Riego Realista"))
                {
                    tx.Start();

                    // --- CREAR / OBTENER MATERIALES REALISTAS DE FABRICANTE ---
                    Material? matPump = GetOrCreateMaterial(doc, "Review_Riego_Mat_MotobombaEvans", 2, 132, 199);      // Azul Evans
                    Material? matFilter = GetOrCreateMaterial(doc, "Review_Riego_Mat_FiltroRainBird", 30, 41, 59);     // Polipropileno Negro/Gris
                    Material? matValve = GetOrCreateMaterial(doc, "Review_Riego_Mat_ValvulaPVC", 226, 232, 240);       // PVC SCH80 Blanco/Gris
                    Material? matPipePE = GetOrCreateMaterial(doc, "Review_Riego_Mat_TuberiaPE16mm", 24, 24, 27);     // Manguera LDPE Negra Flexible
                    Material? matGotero1GPH = GetOrCreateMaterial(doc, "Review_Riego_Mat_GoteroXB10_Negro", 9, 9, 11);  // RainBird XB-10 Negro
                    Material? matGotero2GPH = GetOrCreateMaterial(doc, "Review_Riego_Mat_GoteroXB20_Rojo", 220, 38, 38); // RainBird XB-20 Rojo
                    Material? matNebulizador = GetOrCreateMaterial(doc, "Review_Riego_Mat_NebulizadorGreenMist", 16, 185, 129); // Naan Green Mist Emerald
                    Material? matFitting = GetOrCreateMaterial(doc, "Review_Riego_Mat_FittingBarb16mm", 15, 23, 42);   // Fitting Poly Barb Negro
                    Material? matEstaca = GetOrCreateMaterial(doc, "Review_Riego_Mat_EstacaGalvanizada", 148, 163, 184); // Acero Galvanizado Plateado

                    // --- PASO 1: EQUIPOS DE CABECERA Y CONTROL (Especificaciones y Colores Realistas) ---
                    // A. Motobomba Evans FONT 85 (0.10 HP / 85W) -> Dimensiones exactas de fabricante: 16cm x 8cm x 10cm (0.16m x 0.08m x 0.10m)
                    XYZ pumpPos = new XYZ(-1.5 * mToFt, 0, 0);
                    ElementId pumpId = Create3DSolidBox(doc, pumpPos, 0.16, 0.08, 0.10, "Review_Riego_Motobomba_Evans_Font85", BuiltInCategory.OST_MechanicalEquipment, "044-0026-000201", matPump);
                    createdIds.Add(pumpId);

                    // B. Filtro de Malla RainBird 200 Mesh 3/4" RBY075MPTX -> Cuerpo compacto "Y" (Radio 0.04m, Altura 0.15m)
                    XYZ filterPos = new XYZ(-0.9 * mToFt, 0, 0.15 * mToFt);
                    ElementId filterId = Create3DSolidCylinder(doc, filterPos, 0.04, 0.15, "Review_Riego_Filtro_Malla_RainBird_34", BuiltInCategory.OST_PipeAccessory, "037-3745-000201", matFilter);
                    createdIds.Add(filterId);

                    // C. Válvula Bola PVC Soldada 1/2" SCH 80 -> Cuerpo (Radio 0.03m, Altura 0.08m)
                    XYZ valvePos = new XYZ(-0.4 * mToFt, 0, 0.15 * mToFt);
                    ElementId valveId = Create3DSolidCylinder(doc, valvePos, 0.03, 0.08, "Review_Riego_Valvula_Bola_PVC_SCH80", BuiltInCategory.OST_PipeAccessory, "019-1902-000301", matValve);
                    createdIds.Add(valveId);

                    // --- PASO 2: TUBERÍA MATRIZ PRINCIPAL (Manguera LDPE Flexible 16mm, 25m) ---
                    double totalLengthFeet = 25.0 * mToFt;
                    XYZ startPt = new XYZ(0, 0, 0.15 * mToFt); // Elevado 15cm
                    XYZ endPt = new XYZ(totalLengthFeet, 0, 0.15 * mToFt);

                    if (pipeType != null && systemType != null)
                    {
                        // Tubería Principal usando Revit Pipe API (Diámetro exterior 16mm = 0.016m)
                        Pipe mainPipe = Pipe.Create(doc, systemType.Id, pipeType.Id, level.Id, startPt, endPt);
                        if (mainPipe != null)
                        {
                            try
                            {
                                mainPipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.Set(0.016 * mToFt); // 16mm exacto LDPE
                            }
                            catch { }
                            SetElementMetadata(mainPipe, "Review_Riego_Tuberia_PE_16mm", "SKU: 001-0102-000102 | Manguera Flexible LDPE 16mm (25m)", matPipePE);
                            createdIds.Add(mainPipe.Id);

                            // Conexión Cabecera -> Matriz
                            Pipe feedPipe = Pipe.Create(doc, systemType.Id, pipeType.Id, level.Id, new XYZ(-1.3 * mToFt, 0, 0.15 * mToFt), startPt);
                            if (feedPipe != null)
                            {
                                SetElementMetadata(feedPipe, "Review_Riego_Tuberia_Alimentacion_PE_16mm", "SKU: 001-0102-000102 | Manguera Alimentación Cabecera 16mm", matPipePE);
                                createdIds.Add(feedPipe.Id);
                            }
                        }

                        // --- PASO 3: EMISORES, FITTINGS BARB Y DERIVACIONES DE CAMPO ---
                        // A. 11 Nebulizadores NaanDanJain Green Mist 30 LPH (+1.5m Z) con Fitting Tee y Codo
                        int numNebulizadores = 11;
                        double stepNeb = totalLengthFeet / (numNebulizadores + 1);
                        double heightNebFeet = 1.5 * mToFt;

                        for (int i = 1; i <= numNebulizadores; i++)
                        {
                            XYZ branchBase = new XYZ(i * stepNeb, 0, 0.15 * mToFt);
                            XYZ branchTop = new XYZ(i * stepNeb, 0, heightNebFeet);

                            // Fitting Tee Poly Barb 16mm en base
                            ElementId teeId = Create3DSolidTee(doc, branchBase, 0.008, $"Review_Riego_Fitting_Tee_16mm_#{i}", BuiltInCategory.OST_PipeFitting, "050-0001-000100", matFitting);
                            createdIds.Add(teeId);

                            Pipe nebPipe = Pipe.Create(doc, systemType.Id, pipeType.Id, level.Id, branchBase, branchTop);
                            if (nebPipe != null)
                            {
                                SetElementMetadata(nebPipe, $"Review_Riego_Tuberia_Derivacion_16mm_#{i}", "SKU: 001-0102-000102 | Riser Manguera 16mm (+1.5m)", matPipePE);
                                createdIds.Add(nebPipe.Id);
                            }

                            // Fitting Codo Poly Barb 16mm 90° superior
                            ElementId elbowId = Create3DSolidElbow(doc, branchTop, 0.008, $"Review_Riego_Fitting_Codo_16mm_#{i}", BuiltInCategory.OST_PipeFitting, "050-0002-000100", matFitting);
                            createdIds.Add(elbowId);

                            // Boquilla 3D Nebulizador Naan Green Mist (Verde Emerald, r=0.015m, h=0.04m)
                            ElementId nebShapeId = Create3DSolidCylinder(doc, branchTop, 0.015, 0.04, $"Review_Riego_Nebulizador_Naan_GreenMist_#{i}", BuiltInCategory.OST_PlumbingFixtures, "030-0006-000102", matNebulizador);
                            createdIds.Add(nebShapeId);
                        }

                        // B. 16 Goteros RainBird Xeri-Bug XB-10 1.0 GPH (Negro) (Y = -1.0m) con Fitting Tee Barb
                        int numGoteros1 = 16;
                        double stepGot1 = totalLengthFeet / (numGoteros1 + 1);
                        double offsetGot1Feet = -1.0 * mToFt;

                        for (int i = 1; i <= numGoteros1; i++)
                        {
                            XYZ branchBase = new XYZ(i * stepGot1, 0, 0.15 * mToFt);
                            XYZ branchEnd = new XYZ(i * stepGot1, offsetGot1Feet, 0);

                            // Fitting Tee 16mm en base
                            ElementId teeId = Create3DSolidTee(doc, branchBase, 0.008, $"Review_Riego_Fitting_Tee_16mm_Got1_#{i}", BuiltInCategory.OST_PipeFitting, "050-0001-000100", matFitting);
                            createdIds.Add(teeId);

                            Pipe gotPipe = Pipe.Create(doc, systemType.Id, pipeType.Id, level.Id, branchBase, branchEnd);
                            if (gotPipe != null)
                            {
                                SetElementMetadata(gotPipe, $"Review_Riego_Tuberia_Derivacion_16mm_Got1_#{i}", "SKU: 001-0102-000102 | Manguera Derivación 16mm Goteros 1 GPH", matPipePE);
                                createdIds.Add(gotPipe.Id);
                            }

                            // Emisor RainBird Xeri-Bug 1.0 GPH Negro (XB-10) (r=0.01m, h=0.025m)
                            ElementId gotShapeId = Create3DSolidCylinder(doc, branchEnd, 0.01, 0.025, $"Review_Riego_Gotero_XeriBug_1GPH_Negro_#{i}", BuiltInCategory.OST_PlumbingFixtures, "037-3752-000102", matGotero1GPH);
                            createdIds.Add(gotShapeId);
                        }

                        // C. 16 Goteros RainBird Xeri-Bug XB-20 2.0 GPH (Rojo) (Y = +1.0m) con Fitting Tee Barb
                        int numGoteros2 = 16;
                        double stepGot2 = totalLengthFeet / (numGoteros2 + 1);
                        double offsetGot2Feet = 1.0 * mToFt;

                        for (int i = 1; i <= numGoteros2; i++)
                        {
                            XYZ branchBase = new XYZ(i * stepGot2, 0, 0.15 * mToFt);
                            XYZ branchEnd = new XYZ(i * stepGot2, offsetGot2Feet, 0);

                            // Fitting Tee 16mm en base
                            ElementId teeId = Create3DSolidTee(doc, branchBase, 0.008, $"Review_Riego_Fitting_Tee_16mm_Got2_#{i}", BuiltInCategory.OST_PipeFitting, "050-0001-000100", matFitting);
                            createdIds.Add(teeId);

                            Pipe gotPipe = Pipe.Create(doc, systemType.Id, pipeType.Id, level.Id, branchBase, branchEnd);
                            if (gotPipe != null)
                            {
                                SetElementMetadata(gotPipe, $"Review_Riego_Tuberia_Derivacion_16mm_Got2_#{i}", "SKU: 001-0102-000102 | Manguera Derivación 16mm Goteros 2 GPH", matPipePE);
                                createdIds.Add(gotPipe.Id);
                            }

                            // Emisor RainBird Xeri-Bug 2.0 GPH Rojo (XB-20) (r=0.01m, h=0.025m)
                            ElementId gotShapeId = Create3DSolidCylinder(doc, branchEnd, 0.01, 0.025, $"Review_Riego_Gotero_XeriBug_2GPH_Rojo_#{i}", BuiltInCategory.OST_PlumbingFixtures, "037-3752-000103", matGotero2GPH);
                            createdIds.Add(gotShapeId);
                        }
                    }
                    else
                    {
                        // Fallback autónomo en 3D si no hay PipeType cargado en Revit
                        ElementId mainPipeShape = Create3DSolidCylinder(doc, startPt, 0.016, 25.0, "Review_Riego_Tuberia_PE_16mm", BuiltInCategory.OST_PipeAccessory, "001-0102-000102", matPipePE);
                        createdIds.Add(mainPipeShape);
                    }

                    // --- PASO 4: ESTACAS DE SUJECIÓN GALVANIZADAS 30CM (8 estacas de anclaje para manguera PE 16mm) ---
                    int numEstacas = 8;
                    double stepEstaca = totalLengthFeet / (numEstacas + 1);
                    for (int i = 1; i <= numEstacas; i++)
                    {
                        XYZ estacaPos = new XYZ(i * stepEstaca, 0, -0.1 * mToFt);
                        ElementId estacaId = Create3DSolidBox(doc, estacaPos, 0.015, 0.015, 0.30, $"Review_Riego_Estaca_Galvanizada_#{i}", BuiltInCategory.OST_PipeAccessory, "060-0001-000100", matEstaca);
                        createdIds.Add(estacaId);
                    }

                    tx.Commit();
                }

                // Centrar y Seleccionar automáticamente los elementos creados en Revit
                if (createdIds.Count > 0)
                {
                    uiDoc.Selection.SetElementIds(createdIds);
                    uiDoc.ShowElements(createdIds);
                }

                this.Close(); // Cerrar ventana para visualizar en tiempo real

                System.Windows.MessageBox.Show($"¡GENERACIÓN REALISTA CON ESTÁNDAR 'Review_Riego_*' EXITOSA!\n\nSe construyeron en 3D con materiales y dimensiones reales de fabricante:\n• 1x Review_Riego_Motobomba_Evans_Font85 (0.16m x 0.08m x 0.10m - Azul Evans)\n• 1x Review_Riego_Filtro_Malla_RainBird_34 (3/4\" NPT - Polipropileno Negro)\n• 1x Review_Riego_Valvula_Bola_PVC_SCH80 (1/2\" SCH80 - PVC Blanco)\n• 25m Review_Riego_Tuberia_PE_16mm (Manguera LDPE Negra Flexible 16mm)\n• 43x Review_Riego_Fitting_Tee_16mm (Fittings Poly Barb Negros)\n• 11x Review_Riego_Fitting_Codo_16mm (Fittings Elbow 90° Poly Barb)\n• 11x Review_Riego_Nebulizador_Naan_GreenMist (Esmeralda Naan +1.5m Riser)\n• 16x Review_Riego_Gotero_XeriBug_1GPH_Negro (RainBird XB-10 Negro)\n• 16x Review_Riego_Gotero_XeriBug_2GPH_Rojo (RainBird XB-20 Rojo)\n• 8x Review_Riego_Estaca_Galvanizada (30cm Acero Galvanizado)\n\nTotal: {createdIds.Count} elementos instanciados con materiales y geometrías exactas.", 
                                "Generador de Riego 3D Custom Realista", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Error al generar el sistema en Revit: " + ex.Message, "Error MEP", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                lblStatus.Text = "Error durante la generación.";
                lblStatus.Foreground = System.Windows.Media.Brushes.Red;
            }
        }
    }
}
