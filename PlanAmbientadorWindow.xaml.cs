using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using RevitColor = Autodesk.Revit.DB.Color;
using RevitGrid = Autodesk.Revit.DB.Grid;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace RevitToSupabasePlugin
{
    public partial class PlanAmbientadorWindow : Window
    {
        private readonly UIDocument _uidoc;
        private readonly Document _doc;
        private ViewPlan? _currentPlanView;

        public PlanAmbientadorWindow(UIDocument uidoc)
        {
            InitializeComponent();
            _uidoc = uidoc;
            _doc = uidoc.Document;

            LoadCurrentViewInfo();
        }

        private void LoadCurrentViewInfo()
        {
            try
            {
                Autodesk.Revit.DB.View activeView = _doc.ActiveView;
                if (activeView is ViewPlan planView)
                {
                    _currentPlanView = planView;
                    txtViewName.Text = planView.Name;
                    txtViewScale.Text = $"1:{planView.Scale}";
                    txtViewLevel.Text = planView.GenLevel != null ? planView.GenLevel.Name : "Sin Nivel Asociado";
                    txtStatus.Text = $"Vista de planta cargada: '{planView.Name}'. Listo para aplicar estándar ARCA / Plan:B.";
                }
                else
                {
                    _currentPlanView = null;
                    txtViewName.Text = $"{activeView.Name} (No es vista de planta)";
                    txtViewScale.Text = $"1:{activeView.Scale}";
                    txtViewLevel.Text = "N/A";
                    txtStatus.Text = "⚠️ Advertencia: La vista activa no es una vista de planta (FloorPlan / CeilingPlan). Ábrela para mejores resultados.";
                }
            }
            catch (Exception ex)
            {
                txtStatus.Text = "Error al leer vista activa: " + ex.Message;
            }
        }

        private void BtnRefreshView_Click(object sender, RoutedEventArgs e)
        {
            LoadCurrentViewInfo();
        }

        private void BtnCerrar_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        // =========================================================================
        // ACCIONES PRINCIPALES
        // =========================================================================

        private void BtnAmbientarTodo_Click(object sender, RoutedEventArgs e)
        {
            ExecuteAmbientacion(estilo: true, rooms: true, cotas: true);
        }

        private void BtnSoloEstilo_Click(object sender, RoutedEventArgs e)
        {
            ExecuteAmbientacion(estilo: true, rooms: false, cotas: false);
        }

        private void BtnSoloRooms_Click(object sender, RoutedEventArgs e)
        {
            ExecuteAmbientacion(estilo: false, rooms: true, cotas: false);
        }

        private void BtnSoloCotas_Click(object sender, RoutedEventArgs e)
        {
            ExecuteAmbientacion(estilo: false, rooms: false, cotas: true);
        }

        private void ExecuteAmbientacion(bool estilo, bool rooms, bool cotas)
        {
            if (_currentPlanView == null)
            {
                TaskDialog.Show("Vista No Válida", "Por favor abre y activa una vista de planta (Floor Plan) en Revit antes de ambientar.");
                return;
            }

            List<string> report = new List<string>();

            try
            {
                using (Transaction t = new Transaction(_doc, "Plan Ambientador - Estándar ARCA"))
                {
                    t.Start();

                    // 1. APLICAR ESTILOS GRÁFICOS Y SOMBRAS (ARCA / Plan:B)
                    if (estilo)
                    {
                        ApplyGraphicOverrides(_doc, _currentPlanView, report);
                        ApplyShadowsAndLighting(_doc, _currentPlanView, report);
                        ApplyHalftoneFurniture(_doc, _currentPlanView, report);
                        ApplyFloorStyleOverrides(_doc, _currentPlanView, report);
                        ApplyWaterFeatures(_doc, _currentPlanView, report);
                    }

                    // 2. APLICAR RECINTOS (ROOMS) & NORMALIZACIÓN MAYÚSCULAS
                    if (rooms)
                    {
                        ProcessRoomsAndTags(_doc, _currentPlanView, report);
                    }

                    // 3. APLICAR COTAS AUTOMÁTICAS
                    if (cotas)
                    {
                        ProcessDimensions(_doc, _currentPlanView, report);
                    }

                    t.Commit();
                }

                _uidoc.RefreshActiveView();

                string resumen = string.Join("\n• ", report);
                txtStatus.Text = $"✅ ¡Ambientación ARCA aplicada con éxito! ({report.Count} acciones ejecutadas).";
                TaskDialog.Show("Plan Ambientador - Estándar ARCA", 
                    "Se ha completado la ambientación de la planta activa:\n\n• " + resumen);
            }
            catch (Exception ex)
            {
                txtStatus.Text = "❌ Error durante la ambientación: " + ex.Message;
                TaskDialog.Show("Error", "Ocurrió un error al procesar la vista:\n" + ex.Message);
            }
        }

        // =========================================================================
        // MÓDULO 1: ESTILOS GRÁFICOS, MUROS Y SOMBRAS (ARCA / PLAN:B)
        // =========================================================================

        private void ApplyGraphicOverrides(Document doc, ViewPlan view, List<string> report)
        {
            FillPatternElement? solidPattern = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(fp => 
                    fp.Name.Equals("<Solid fill>", StringComparison.OrdinalIgnoreCase) || 
                    fp.Name.Equals("<Relleno sólido>", StringComparison.OrdinalIgnoreCase) ||
                    fp.Name.Contains("Solid", StringComparison.OrdinalIgnoreCase) ||
                    fp.Name.Contains("Sólido", StringComparison.OrdinalIgnoreCase) ||
                    fp.Name.Contains("Relleno", StringComparison.OrdinalIgnoreCase));

            if (solidPattern == null)
            {
                report.Add("No se encontró patrón de relleno sólido en el proyecto.");
                return;
            }

            // Determinar color seleccionado: por defecto Negro Puro (Preset ARCA / Plan:B)
            RevitColor wallColor = new RevitColor(0, 0, 0); // Negro Tinta Puro
            int selectedIdx = cmbWallColor.SelectedIndex;
            switch (selectedIdx)
            {
                case 0: // Negro Tinta Puro (Estándar ARCA)
                    wallColor = new RevitColor(0, 0, 0);
                    break;
                case 1: // Gris Grafito Minimalista (#2A2A2E)
                    wallColor = new RevitColor(42, 42, 46);
                    break;
                case 2: // Gris Cálido Arquitectónico (#3E3B3A)
                    wallColor = new RevitColor(62, 59, 58);
                    break;
                case 3: // Gris Claro Técnico (#555555)
                    wallColor = new RevitColor(85, 85, 85);
                    break;
            }

            // Override para Muros
            if (chkMurosSolidos.IsChecked == true)
            {
                OverrideGraphicSettings ogsMuros = new OverrideGraphicSettings();
                ogsMuros.SetCutForegroundPatternId(solidPattern.Id);
                ogsMuros.SetCutForegroundPatternColor(wallColor);
                ogsMuros.SetCutForegroundPatternVisible(true);

                ElementId catWalls = new ElementId(BuiltInCategory.OST_Walls);
                view.SetCategoryOverrides(catWalls, ogsMuros);
                report.Add($"Muros configurados con relleno sólido de corte en negro arquitectónico ({wallColor.Red},{wallColor.Green},{wallColor.Blue}).");
            }

            // Override para Columnas y Estructura (Máxima jerarquía)
            if (chkColumnasSolidas.IsChecked == true)
            {
                OverrideGraphicSettings ogsCols = new OverrideGraphicSettings();
                ogsCols.SetCutForegroundPatternId(solidPattern.Id);
                ogsCols.SetCutForegroundPatternColor(wallColor);
                ogsCols.SetCutForegroundPatternVisible(true);

                view.SetCategoryOverrides(new ElementId(BuiltInCategory.OST_Columns), ogsCols);
                view.SetCategoryOverrides(new ElementId(BuiltInCategory.OST_StructuralColumns), ogsCols);
                report.Add("Columnas arquitectónicas y estructurales configuradas con corte negro de alto contraste.");
            }

            // Limpieza de categorías parásitas / CAD
            if (chkCleanCAD.IsChecked == true)
            {
                BuiltInCategory[] parasiteCats = new[]
                {
                    BuiltInCategory.OST_CLines, // Planos de referencia
                    BuiltInCategory.OST_ImportObjectStyles // CADs importados
                };

                foreach (BuiltInCategory bCat in parasiteCats)
                {
                    try
                    {
                        ElementId catId = new ElementId(bCat);
                        if (view.CanCategoryBeHidden(catId))
                        {
                            view.SetCategoryHidden(catId, true);
                        }
                    }
                    catch { }
                }
                report.Add("Planos de referencia y elementos parásitos ocultados en la vista.");
            }
        }

        private void ApplyShadowsAndLighting(Document doc, ViewPlan view, List<string> report)
        {
            // Sombras Arrojadas y Calibración Solar (Estilo revista / concurso)
            if (chkCastShadows.IsChecked == true)
            {
                Parameter pShadows = view.get_Parameter(BuiltInParameter.GRAPHIC_DISPLAY_OPTIONS_SHADOWS);
                if (pShadows != null && !pShadows.IsReadOnly)
                {
                    pShadows.Set(1);
                }

                try { view.SunlightIntensity = 85; } catch { }
                try { view.ShadowIntensity = 18; } catch { }

                report.Add("Sombras proyectadas configuradas al 18% con iluminación solar al 85%.");
            }

            // Anti-Aliasing de líneas (Smooth Edges)
            if (chkAntiAliasing.IsChecked == true)
            {
                try
                {
                    ViewDisplayModel vdm = view.GetViewDisplayModel();
                    vdm.SmoothEdges = true;
                    view.SetViewDisplayModel(vdm);
                    report.Add("Anti-aliasing de líneas activado (Smooth Edges).");
                }
                catch { }
            }
        }

        private void ApplyHalftoneFurniture(Document doc, ViewPlan view, List<string> report)
        {
            BuiltInCategory[] halftoneCats = new[]
            {
                BuiltInCategory.OST_Furniture,
                BuiltInCategory.OST_FurnitureSystems,
                BuiltInCategory.OST_Casework,
                BuiltInCategory.OST_PlumbingFixtures,
                BuiltInCategory.OST_SpecialityEquipment,
                BuiltInCategory.OST_Planting
            };

            OverrideGraphicSettings ogsHalftone = new OverrideGraphicSettings();
            ogsHalftone.SetHalftone(true);

            int count = 0;
            foreach (BuiltInCategory bCat in halftoneCats)
            {
                try
                {
                    ElementId catId = new ElementId(bCat);
                    view.SetCategoryOverrides(catId, ogsHalftone);
                    count++;
                }
                catch { }
            }

            if (count > 0)
            {
                report.Add($"Mobiliario, sanitarios y vegetación atenuados en Halftone ({count} categorías).");
            }
        }

        private void ApplyFloorStyleOverrides(Document doc, ViewPlan view, List<string> report)
        {
            if (chkSoftFloors.IsChecked == true)
            {
                // Suavizar tramas de superficie de suelos a gris tenue arquitectónico #B5B5B5
                OverrideGraphicSettings ogsFloor = new OverrideGraphicSettings();
                RevitColor floorGray = new RevitColor(185, 185, 185);
                ogsFloor.SetSurfaceForegroundPatternColor(floorGray);
                ogsFloor.SetSurfaceForegroundPatternVisible(true);

                try
                {
                    view.SetCategoryOverrides(new ElementId(BuiltInCategory.OST_Floors), ogsFloor);
                    report.Add("Tramas de pisos atenuadas a gris arquitectónico sutil (#B5B5B5).");
                }
                catch { }
            }
        }

        private void ApplyWaterFeatures(Document doc, ViewPlan view, List<string> report)
        {
            // Detección de elementos de piscina / jacuzzi para aplicar el azul cian pastel translúcido de ARCA
            try
            {
                var floorElements = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(Floor))
                    .Cast<Floor>()
                    .Where(f => {
                        string name = f.Name.ToLowerInvariant();
                        ElementType? type = doc.GetElement(f.GetTypeId()) as ElementType;
                        string typeName = type != null ? type.Name.ToLowerInvariant() : "";
                        return name.Contains("piscina") || name.Contains("pool") || name.Contains("jacuzzi") || name.Contains("agua") ||
                               typeName.Contains("piscina") || typeName.Contains("pool") || typeName.Contains("jacuzzi") || typeName.Contains("agua");
                    })
                    .ToList();

                if (floorElements.Count > 0)
                {
                    FillPatternElement? solidPattern = new FilteredElementCollector(doc)
                        .OfClass(typeof(FillPatternElement))
                        .Cast<FillPatternElement>()
                        .FirstOrDefault(fp => fp.Name.Contains("Solid") || fp.Name.Contains("Sólido") || fp.Name.Contains("Relleno"));

                    OverrideGraphicSettings ogsWater = new OverrideGraphicSettings();
                    ogsWater.SetSurfaceForegroundPatternColor(new RevitColor(197, 232, 250)); // Azul cian pastel #C5E8FA
                    if (solidPattern != null)
                    {
                        ogsWater.SetSurfaceForegroundPatternId(solidPattern.Id);
                        ogsWater.SetSurfaceForegroundPatternVisible(true);
                    }
                    ogsWater.SetSurfaceTransparency(25); // 25% transparencia para ver mosaicos/escalones

                    foreach (var floor in floorElements)
                    {
                        view.SetElementOverrides(floor.Id, ogsWater);
                    }

                    report.Add($"Zonas de agua (Piscina / Jacuzzi) ambientadas en Azul Pastel Translúcido ({floorElements.Count} elementos).");
                }
            }
            catch { }
        }

        // =========================================================================
        // MÓDULO 2: RECINTOS (ROOMS) & TAGS EN MAYÚSCULAS
        // =========================================================================

        private void ProcessRoomsAndTags(Document doc, ViewPlan view, List<string> report)
        {
            Level level = view.GenLevel;
            if (level == null)
            {
                report.Add("No se pueden procesar Rooms: La vista no tiene un nivel asociado.");
                return;
            }

            // 1. Detección / Creación de Rooms en recintos cerrados
            int createdRooms = 0;
            if (chkAutoCreateRooms.IsChecked == true)
            {
                try
                {
                    ICollection<ElementId> newRoomIds = doc.Create.NewRooms2(level);
                    createdRooms = newRoomIds.Count;
                    if (createdRooms > 0)
                    {
                        report.Add($"Se crearon automáticamente {createdRooms} Rooms en espacios cerrados.");
                    }
                }
                catch
                {
                    // Si no había espacios vacíos o ya estaban creados, continuar silenciosamente
                }
            }

            // 2. Normalización de nombres a MAYÚSCULAS (Estándar ARCA / PLAN:B)
            int renamedRooms = 0;
            List<Room> roomsInLevel = new FilteredElementCollector(doc)
                .OfClass(typeof(SpatialElement))
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WherePasses(new ElementLevelFilter(level.Id))
                .Cast<Room>()
                .Where(r => r.Area > 0.01)
                .ToList();

            if (chkUppercaseRooms.IsChecked == true)
            {
                foreach (Room r in roomsInLevel)
                {
                    string currentName = r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                    if (!string.IsNullOrEmpty(currentName))
                    {
                        string upper = currentName.ToUpperInvariant();
                        if (upper != currentName)
                        {
                            r.get_Parameter(BuiltInParameter.ROOM_NAME)?.Set(upper);
                            renamedRooms++;
                        }
                    }
                }
                if (renamedRooms > 0)
                {
                    report.Add($"Nombres de {renamedRooms} recintos normalizados a MAYÚSCULAS.");
                }
            }

            // 3. Colocación de Room Tags si faltan
            int taggedCount = 0;
            if (chkRoomTags.IsChecked == true)
            {
                foreach (Room r in roomsInLevel)
                {
                    LocationPoint? locPoint = r.Location as LocationPoint;
                    if (locPoint != null)
                    {
                        UV uv = new UV(locPoint.Point.X, locPoint.Point.Y);
                        try
                        {
                            RoomTag existingTag = new FilteredElementCollector(doc, view.Id)
                                .OfClass(typeof(SpatialElementTag))
                                .OfCategory(BuiltInCategory.OST_RoomTags)
                                .Cast<RoomTag>()
                                .FirstOrDefault(t => t.Room != null && t.Room.Id == r.Id)!;

                            if (existingTag == null)
                            {
                                doc.Create.NewRoomTag(new LinkElementId(r.Id), uv, view.Id);
                                taggedCount++;
                            }
                        }
                        catch { }
                    }
                }
                if (taggedCount > 0)
                {
                    report.Add($"Se colocaron {taggedCount} Room Tags centrados en recintos.");
                }
            }
        }

        // =========================================================================
        // MÓDULO 3: ACOTADO JERARQUIZADO
        // =========================================================================

        private void ProcessDimensions(Document doc, ViewPlan view, List<string> report)
        {
            // Acotado entre Ejes Estructurales (Grids)
            if (chkCotaEjes.IsChecked == true)
            {
                List<RevitGrid> gridsInView = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(RevitGrid))
                    .Cast<RevitGrid>()
                    .ToList();

                if (gridsInView.Count >= 2)
                {
                    var groupedGrids = GroupParallelGrids(gridsInView);
                    int dimCount = 0;

                    foreach (var group in groupedGrids)
                    {
                        if (group.Count >= 2)
                        {
                            ReferenceArray refArray = new ReferenceArray();
                            foreach (RevitGrid g in group)
                            {
                                refArray.Append(new Reference(g));
                            }

                            Curve? firstCurve = group[0].Curve;
                            if (firstCurve is Line line)
                            {
                                XYZ dir = line.Direction.Normalize();
                                XYZ normal = new XYZ(-dir.Y, dir.X, 0); // Vector normal 2D

                                XYZ p1 = line.GetEndPoint(0) + normal * 12.0; // Desfase exterior
                                XYZ p2 = p1 + dir * 10.0;
                                Line dimLine = Line.CreateBound(p1, p2);

                                try
                                {
                                    doc.Create.NewDimension(view, dimLine, refArray);
                                    dimCount++;
                                }
                                catch { }
                            }
                        }
                    }

                    if (dimCount > 0)
                    {
                        report.Add($"Se generaron {dimCount} cadenas de cotas automáticas entre ejes estructurales.");
                    }
                }
            }
        }

        private List<List<RevitGrid>> GroupParallelGrids(List<RevitGrid> grids)
        {
            List<List<RevitGrid>> result = new List<List<RevitGrid>>();

            foreach (RevitGrid g in grids)
            {
                if (!(g.Curve is Line line)) continue;
                XYZ dir = line.Direction.Normalize();

                bool added = false;
                foreach (var group in result)
                {
                    if (group.Count > 0 && group[0].Curve is Line groupLine)
                    {
                        XYZ groupDir = groupLine.Direction.Normalize();
                        if (Math.Abs(dir.CrossProduct(groupDir).Z) < 0.05)
                        {
                            group.Add(g);
                            added = true;
                            break;
                        }
                    }
                }

                if (!added)
                {
                    result.Add(new List<RevitGrid> { g });
                }
            }

            return result;
        }
    }
}
