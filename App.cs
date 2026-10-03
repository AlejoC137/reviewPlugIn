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

        public Result OnStartup(UIControlledApplication application)
        {
            _uiApp = application;
            InitializeRibbon(application);

            // Deshabilitado: el evento Idling estaba rompiendo la interfaz de Revit (ocultando el Project Browser)
            // application.Idling += Application_Idling;

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
                if (!string.IsNullOrEmpty(dirPath))
                {
                    string fileName = large ? $"{baseName}_32.png" : $"{baseName}.png";
                    string iconPath = Path.Combine(dirPath, fileName);
                    if (!File.Exists(iconPath))
                    {
                        iconPath = Path.Combine(dirPath, $"{baseName}.png");
                    }

                    if (File.Exists(iconPath))
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(iconPath);
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                        bmp.EndInit();
                        bmp.Freeze();
                        return bmp;
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
                    if (icon16 != null)
                    {
                        bData.Image = icon16;
                    }

                    BitmapImage? icon32 = GetIcon(iconBaseName, true);
                    if (icon32 != null)
                    {
                        bData.LargeImage = icon32;
                    }
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

            // --- Panel 1: 🤖 Generador IA ---
            RibbonPanel panelIA = GetOrCreatePanel("🤖 Generador IA");
            if (panelIA != null && panelIA.GetItems().Count == 0)
            {
                PushButtonData btnGen = CreateBtnData("cmdGen", "Generador Familias IA", "CommandGenerador", "icon_gen_ia");
                PushButtonData btnMCP = CreateBtnData("cmdMCP", "Exportar Metadata", "Command", "icon_export_metadata");
                PushButtonData btnIA = CreateBtnData("cmdIA", "Configurador Plantillas", "CommandConfiguradorPlantilla", "icon_config_plantilla");
                panelIA.AddStackedItems(btnGen, btnMCP, btnIA);

                PushButtonData btnSchedules = CreateBtnData("cmdSchedules", "Asistente Tablas IA", "CommandSchedulesIA", "icon_tablas_ia");
                PushButtonData btnSync = CreateBtnData("cmdSyncMaterials", "Sincronizar Materiales", "CommandSyncMaterials", "icon_sync_materials");
                panelIA.AddStackedItems(btnSchedules, btnSync);
            }

            // --- Panel 2: 💧 Sistema de Riego ---
            RibbonPanel panelRiego = GetOrCreatePanel("💧 Sistema de Riego");
            if (panelRiego != null && panelRiego.GetItems().Count == 0)
            {
                PushButtonData btnRiego = CreateBtnData("cmdCargarRiego", "Explorador Plantilla Riego", "CommandCargarPlantillaRiego", "icon_riego_plantilla");
                PushButtonData btnPlumbing = CreateBtnData("cmdPlumbingSheet", "Plumbing Sheet", "CommandPlumbingSheet", "icon_plumbing_sheet");
                panelRiego.AddStackedItems(btnRiego, btnPlumbing);
            }

            // --- Panel 3: 🧱 Albañilería ---
            RibbonPanel panelAlba = GetOrCreatePanel("🧱 Albañilería");
            if (panelAlba != null && panelAlba.GetItems().Count == 0)
            {
                PushButtonData btnGenLadrillos = CreateBtnData("cmdGenLadrillos", "Generar Familias de Ladrillos", "CommandGenerarFamiliasLadrillos", "icon_gen_ladrillos");
                PushButtonData btnConvMuros = CreateBtnData("cmdConvMuros", "Convertir Muros a Ladrillos", "CommandConvertirMurosALadrillos", "icon_conv_muros");
                PushButtonData btnCrearTablas = CreateBtnData("cmdCrearTablas", "Crear Tablas de Cómputos", "CommandCrearTablasAlbanileria", "icon_tablas_ladrillos");
                panelAlba.AddStackedItems(btnGenLadrillos, btnConvMuros, btnCrearTablas);
            }

            // --- Panel 4: 🪵 Enchapes & Pisos ---
            RibbonPanel panelPisos = GetOrCreatePanel("🪵 Enchapes & Pisos");
            if (panelPisos != null && panelPisos.GetItems().Count == 0)
            {
                PushButtonData btnGenPisos = CreateBtnData("cmdGenPisos", "Gestor de Piezas y Baldosas", "CommandGenerarFamiliasPisos", "icon_gen_pisos");
                PushButtonData btnConvPisos = CreateBtnData("cmdConvPisos", "Instanciador de Enchapes & Pisos", "CommandConvertirPisosABaldosas", "icon_conv_pisos");
                PushButtonData btnCrearTablasPisos = CreateBtnData("cmdCrearTablasPisos", "Cómputos de Enchapes & Zócalos", "CommandCrearTablasPisos", "icon_tablas_pisos");
                panelPisos.AddStackedItems(btnGenPisos, btnConvPisos, btnCrearTablasPisos);

                PushButtonData btnGestorCat = CreateBtnData("cmdGestorCat", "Gestor de\nCategorías", "CommandGestorCategorias", "icon_gestor_cat");
                panelPisos.AddItem(btnGestorCat);
            }

            // --- Panel 5: 🔩 Steel Decks ---
            RibbonPanel panelSteelDeck = GetOrCreatePanel("🔩 Steel Decks");
            if (panelSteelDeck != null && panelSteelDeck.GetItems().Count == 0)
            {
                PushButtonData btnGenSteelDeck = CreateBtnData("cmdGenSteelDeck", "Gestor de Perfiles Steel Deck", "CommandGenerarFamiliasSteelDeck", "icon_gen_steel");
                PushButtonData btnConvSteelDeck = CreateBtnData("cmdConvSteelDeck", "Modelador de Steel Decks", "CommandConvertirSteelDeck", "icon_conv_steel");
                PushButtonData btnCrearTablasSteelDecks = CreateBtnData("cmdCrearTablasSteelDecks", "Cómputos de Steel Decks", "CommandCrearTablasSteelDecks", "icon_tablas_steel");
                panelSteelDeck.AddStackedItems(btnGenSteelDeck, btnConvSteelDeck, btnCrearTablasSteelDecks);
            }

            // --- Panel 6: 🎨 Plan Ambientador ---
            RibbonPanel panelAmbientador = GetOrCreatePanel("🎨 Plan Ambientador");
            if (panelAmbientador != null && panelAmbientador.GetItems().Count == 0)
            {
                PushButtonData btnAmbientador = CreateBtnData("cmdAmbientador", "Plan\nAmbientador", "CommandPlanAmbientador", "icon_plan_ambientador");
                panelAmbientador.AddItem(btnAmbientador);
            }

            // --- Panel 7: 📐 Gestión de Detalles ---
            RibbonPanel panelDetalles = GetOrCreatePanel("📐 Detalles Constructivos");
            if (panelDetalles != null && panelDetalles.GetItems().Count == 0)
            {
                PushButtonData btnDetalles = CreateBtnData("cmdGestorDetalles", "Gestor de\nDetalles", "CommandGestorDetalles", "icon_detalles");
                panelDetalles.AddItem(btnDetalles);
            }

            // --- Panel 8: ℹ️ Ayuda & Docs ---
            RibbonPanel panelHelp = GetOrCreatePanel("ℹ️ Ayuda & Docs");
            if (panelHelp != null && panelHelp.GetItems().Count == 0)
            {
                PushButtonData btnManual = CreateBtnData("cmdManual", "📘 Manual & Mapa\nde la App", "CommandManualApp", "icon_manual");
                panelHelp.AddItem(btnManual);
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
