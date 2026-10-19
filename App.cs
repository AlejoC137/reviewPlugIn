using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace RevitToSupabasePlugin
{
    public class App : IExternalApplication
    {
        private static UIControlledApplication? _uiApp;
        public static RevitMcpServer? McpServerInstance { get; private set; }

        public Result OnStartup(UIControlledApplication application)
        {
            _uiApp = application;

            // Registrar Panel Acoplable (Dockable Pane) de Review
            try
            {
                var controlPanelPage = new ReviewControlPanelPage();
                application.RegisterDockablePane(ReviewControlPanelPage.PaneId, "📕 Review - Centro de Control BIM", controlPanelPage);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error registrando Dockable Pane: {ex.Message}");
            }

            InitializeRibbon(application);

            // Servidor MCP Local 8080 desmontado: La comunicación con IA se canaliza vía Review Cloud (HTTPS)
            // y a través del comando 'Acciones Directas IA' con validación segura de JSON.

            return Result.Succeeded;
        }

        /*
        private void Application_Idling(object? sender, IdlingEventArgs e)
        {
            if (_uiApp != null)
            {
                EnsureRibbonButtonsExist(_uiApp);
            }
        }
        */

        private static void InitializeRibbon(UIControlledApplication application)
        {
            string tabName = "📕 Review";
            try
            {
                application.CreateRibbonTab(tabName);
            }
            catch { }

            EnsureRibbonButtonsExist(application);
        }

        private static void EnsureRibbonButtonsExist(UIControlledApplication application)
        {
            string tabName = "📕 Review";
            string thisAssemblyPath = Assembly.GetExecutingAssembly().Location;
            string? dirPath = Path.GetDirectoryName(thisAssemblyPath);

            BitmapImage? GetIcon(string baseName, bool large = false)
            {
                int targetPixel = large ? 32 : 16;
                string fileWithSuffix = large ? $"{baseName}_32.png" : $"{baseName}.png";
                string fileFallback = large ? $"{baseName}.png" : $"{baseName}_32.png";

                var candidateDirs = new List<string>();
                if (!string.IsNullOrEmpty(dirPath)) candidateDirs.Add(dirPath);

                string appData2027 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Autodesk\Revit\Addins\2027");
                string appData2025 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Autodesk\Revit\Addins\2025");
                candidateDirs.Add(appData2027);
                candidateDirs.Add(appData2025);
                candidateDirs.Add(@"G:\Mi unidad\git\reviewPlugIn");

                foreach (var dir in candidateDirs.Distinct())
                {
                    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;

                    string path = Path.Combine(dir, fileWithSuffix);
                    if (!File.Exists(path)) path = Path.Combine(dir, fileFallback);

                    if (File.Exists(path))
                    {
                        try
                        {
                            byte[] bytes = File.ReadAllBytes(path);
                            using var ms = new MemoryStream(bytes);
                            var bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.StreamSource = ms;
                            bmp.DecodePixelWidth = targetPixel;
                            bmp.DecodePixelHeight = targetPixel;
                            bmp.EndInit();
                            bmp.Freeze();
                            return bmp;
                        }
                        catch { }
                    }
                }
                return null;
            }

            PushButtonData CreateBtnData(string name, string text, string className, string iconBaseName)
            {
                PushButtonData bData = new PushButtonData(name, text, thisAssemblyPath, "RevitToSupabasePlugin." + className);
                if (!string.IsNullOrEmpty(iconBaseName))
                {
                    BitmapImage? icon16 = GetIcon(iconBaseName, false);
                    if (icon16 != null) bData.Image = icon16;

                    BitmapImage? icon32 = GetIcon(iconBaseName, true);
                    if (icon32 != null) bData.LargeImage = icon32;
                }
                return bData;
            }

            RibbonPanel GetOrCreatePanel(string name)
            {
                try
                {
                    List<RibbonPanel> existingPanels = application.GetRibbonPanels(tabName);
                    foreach (RibbonPanel p in existingPanels)
                    {
                        if (p.Name == name) return p;
                    }
                }
                catch { }

                try
                {
                    return application.CreateRibbonPanel(tabName, name);
                }
                catch
                {
                    return null!;
                }
            }

            // --- Panel 0: 🎛️ Centro de Control ---
            RibbonPanel panelControl = GetOrCreatePanel("🎛️ Centro de Control");
            if (panelControl != null && panelControl.GetItems().Count == 0)
            {
                PushButtonData btnControl = CreateBtnData("cmdToggleControlPanel", "Panel de\nControl", "CommandToggleControlPanel", "icon_control_panel");
                PushButtonData btnSyncSheets = CreateBtnData("cmdSyncSheetsQueue", "Gestor de\nCambios", "CommandSyncSheets", "icon_sync_sheets");
                btnSyncSheets.ToolTip = "Gestor de Cambios: Revisa, aprueba e implementa en Revit los cambios solicitados desde la App de Review (Láminas, parámetros, etc.).";
                panelControl.AddItem(btnControl);
                panelControl.AddItem(btnSyncSheets);

                // Slide-Out: Documentación y mapa
                panelControl.AddSlideOut();
                PushButtonData btnManual = CreateBtnData("cmdManual", "📘 Manual & Mapa", "CommandManualApp", "icon_manual");
                btnManual.ToolTip = "📘 Manual Técnico & Mapa de la App: Consulta la documentación técnica, estado de arquitectura y utilidades del plugin.";
                panelControl.AddItem(btnManual);
            }

            // --- Panel 1: 🤖 Acciones IA ---
            RibbonPanel panelAccionesIA = GetOrCreatePanel("🤖 Acciones IA");
            if (panelAccionesIA != null && panelAccionesIA.GetItems().Count == 0)
            {
                PushButtonData btnGenFamilias = CreateBtnData("cmdGenFamilias", "Generador\nFamilias IA", "CommandGenerador", "icon_gen_ia");
                btnGenFamilias.ToolTip = "Generador de Familias IA: Módulo especializado para creación y parametrización de familias paramétricas con IA multi-modal.";

                PushButtonData btnAccionesDirectas = CreateBtnData("cmdAccionesDirectasIA", "Acciones\nDirectas IA", "CommandAccionesDirectasIA", "icon_copilot");
                btnAccionesDirectas.ToolTip = "Acciones Directas IA: Pega instrucciones y JSON generados por Gemini para auditar y aplicar modificaciones directas en Revit.";

                panelAccionesIA.AddItem(btnGenFamilias);
                panelAccionesIA.AddItem(btnAccionesDirectas);
            }

            // --- Panel 2: 🏷️ Categorías & Plantillas ---
            RibbonPanel panelCategorias = GetOrCreatePanel("🏷️ Categorías & Plantillas");
            if (panelCategorias != null && panelCategorias.GetItems().Count == 0)
            {
                PushButtonData btnGestorCat = CreateBtnData("cmdGestorCat", "Gestor de\nCategorías", "CommandGestorCategorias", "icon_gestor_cat");
                PushButtonData btnConfigPlantillas = CreateBtnData("cmdConfigPlantillas", "Configurar\nPlantillas", "CommandConfiguradorPlantilla", "icon_config_plantilla");
                btnConfigPlantillas.ToolTip = "Configurador IA de Plantillas y Metadatos de Proyecto.";
                panelCategorias.AddItem(btnGestorCat);
                panelCategorias.AddItem(btnConfigPlantillas);
            }

            // --- Panel 3: 🎨 Materiales & Acabados ---
            RibbonPanel panelMateriales = GetOrCreatePanel("🎨 Materiales");
            if (panelMateriales != null && panelMateriales.GetItems().Count == 0)
            {
                PushButtonData btnSync = CreateBtnData("cmdSyncMaterials", "Sincronizar\nMateriales", "CommandSyncMaterials", "icon_sync_materials");
                btnSync.ToolTip = "Sincroniza y normaliza los nombres y parámetros de materiales con la base de datos de Review en Supabase.";
                panelMateriales.AddItem(btnSync);

                PushButtonData btnMapeo = CreateBtnData("cmdMapeoMateriales", "Mapeo Matriz", "CommandMapeoMateriales", "icon_qaqc");
                PushButtonData btnAuditoria = CreateBtnData("cmdAuditoriaMateriales", "Auditoría", "CommandAuditoriaMateriales", "icon_export_metadata");
                panelMateriales.AddStackedItems(btnMapeo, btnAuditoria);
            }

            // --- Panel 4: 💧 Sistema de Riego ---
            RibbonPanel panelRiego = GetOrCreatePanel("💧 Sistema de Riego");
            if (panelRiego != null && panelRiego.GetItems().Count == 0)
            {
                PushButtonData btnRiego = CreateBtnData("cmdCargarRiego", "Explorador\nde Riego", "CommandCargarPlantillaRiego", "icon_riego_plantilla");
                btnRiego.ToolTip = "Explorador y gestor de componentes paramétricos de fontanería y redes de riego técnico.";
                panelRiego.AddItem(btnRiego);
            }

            // --- Panel 5: 🧱 Albañilería & Pegas ---
            RibbonPanel panelAlba = GetOrCreatePanel("🧱 Albañilería");
            if (panelAlba != null && panelAlba.GetItems().Count == 0)
            {
                PushButtonData btnGenLadrillos = CreateBtnData("cmdGenLadrillos", "Manejador\nde Pegas", "CommandGenerarFamiliasLadrillos", "icon_gen_ladrillos");
                btnGenLadrillos.ToolTip = "Manejador de Familias de Ladrillo y Mortero de Pega: Catálogo de piezas de mampostería, bloques de ladrillo y familias paramétricas de mortero de pega (PegaH / PegaV).";

                PushButtonData btnConvMuros = CreateBtnData("cmdConvMuros", "Modelador\nde Muros", "CommandConvertirMurosALadrillos", "icon_conv_muros");
                btnConvMuros.ToolTip = "Modelador de Muros a Ladrillos y Generador Técnico de Pegas de Mortero.";

                panelAlba.AddItem(btnGenLadrillos);
                panelAlba.AddItem(btnConvMuros);
            }

            // --- Panel 6: 🪵 Enchapes & Pisos ---
            RibbonPanel panelPisos = GetOrCreatePanel("🪵 Enchapes & Pisos");
            if (panelPisos != null && panelPisos.GetItems().Count == 0)
            {
                PushButtonData btnGenPisos = CreateBtnData("cmdGenPisos", "Piezas y\nBaldosas", "CommandGenerarFamiliasPisos", "icon_gen_pisos");
                PushButtonData btnConvPisos = CreateBtnData("cmdConvPisos", "Modelador\nde Pisos", "CommandConvertirPisosABaldosas", "icon_conv_pisos");
                PushButtonData btnEstudioModular = CreateBtnData("cmdEstudioModular", "Estudio .RFA\n& In-Situ", "CommandEstudioFamiliasModulares", "icon_gen_ia");
                btnEstudioModular.ToolTip = "Estudio de Familias Cargables Modulares (0,0) (Tejas, Zócalos, Baldosas, Pegas, Listones) y Convertidor de Familias In-Situ a Cargables (.rfa).";
                panelPisos.AddItem(btnGenPisos);
                panelPisos.AddItem(btnConvPisos);
                panelPisos.AddItem(btnEstudioModular);
            }

            // --- Panel 7: 🔩 Steel Decks ---
            RibbonPanel panelSteelDeck = GetOrCreatePanel("🔩 Steel Decks");
            if (panelSteelDeck != null && panelSteelDeck.GetItems().Count == 0)
            {
                PushButtonData btnGenSteelDeck = CreateBtnData("cmdGenSteelDeck", "Perfiles\nSteel Deck", "CommandGenerarFamiliasSteelDeck", "icon_gen_steel");
                PushButtonData btnConvSteelDeck = CreateBtnData("cmdConvSteelDeck", "Modelador\nSteel Deck", "CommandConvertirSteelDeck", "icon_conv_steel");
                panelSteelDeck.AddItem(btnGenSteelDeck);
                panelSteelDeck.AddItem(btnConvSteelDeck);
            }

            // --- Panel 8: 📊 Tablas & Cómputos (UNIFICADO) ---
            RibbonPanel panelTablas = GetOrCreatePanel("📊 Tablas & Cómputos");
            if (panelTablas != null && panelTablas.GetItems().Count == 0)
            {
                PushButtonData btnGestorTablas = CreateBtnData("cmdSchedules", "Gestor Tablas\n& Ensambles", "CommandSchedulesIA", "icon_tablas_ia");
                btnGestorTablas.ToolTip = "Gestor Central de Tablas & Ensambles BIM: Administra, duplica, renombra, elimina y vincula tablas a láminas.";
                panelTablas.AddItem(btnGestorTablas);

                PushButtonData btnCrearTablasAlba = CreateBtnData("cmdCrearTablasAlbanileria", "Cómputos Muros", "CommandCrearTablasAlbanileria", "icon_tablas_ladrillos");
                PushButtonData btnCrearTablasPisos = CreateBtnData("cmdCrearTablasPisos", "Cómputos Pisos", "CommandCrearTablasPisos", "icon_tablas_pisos");
                panelTablas.AddStackedItems(btnCrearTablasAlba, btnCrearTablasPisos);

                PushButtonData btnCrearTablasSteel = CreateBtnData("cmdCrearTablasSteelDecks", "Cómputos Steel", "CommandCrearTablasSteelDecks", "icon_tablas_steel");
                PushButtonData btnPlumbing = CreateBtnData("cmdPlumbingSheet", "Plumbing Sheet", "CommandPlumbingSheet", "icon_plumbing_sheet");
                btnPlumbing.ToolTip = "Genera tablas completas de cómputo hidráulico y fontanería (Tuberías, aparatos sanitarios, uniones, accesorios y equipos).";
                panelTablas.AddStackedItems(btnCrearTablasSteel, btnPlumbing);
            }

            // --- Panel 9: 📐 Asistente de Anotación & Gráficos ---
            RibbonPanel panelAnotacion = GetOrCreatePanel("📐 Asistente de Anotación");
            if (panelAnotacion != null && panelAnotacion.GetItems().Count == 0)
            {
                PushButtonData btnHub = CreateBtnData("cmdAsistenteAnotacion", "Asistente\nAnotación", "CommandAsistenteAnotacion", "icon_doc");
                panelAnotacion.AddItem(btnHub);

                PushButtonData btnAmbientador = CreateBtnData("cmdAmbientador", "Ambientador", "CommandPlanAmbientador", "icon_plan_ambientador");
                PushButtonData btnDetalles = CreateBtnData("cmdGestorDetalles", "Gestor Detalles", "CommandGestorDetalles", "icon_detalles");
                panelAnotacion.AddStackedItems(btnAmbientador, btnDetalles);

                PushButtonData btnFamilyRevisor = CreateBtnData("cmdFamilyRevisor", "Revisor Familias", "CommandFamilyRevisor", "icon_gestor_cat");
                PushButtonData btnSheetRevisor = CreateBtnData("cmdSheetRevisor", "Revisor Sheets", "CommandSheetRevisor", "icon_masivo");
                panelAnotacion.AddStackedItems(btnFamilyRevisor, btnSheetRevisor);
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            if (_uiApp != null)
            {
                // _uiApp.Idling -= Application_Idling;
            }
            return Result.Succeeded;
        }
    }
}
