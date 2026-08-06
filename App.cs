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
            string panelName = "🤖 Generador IA";

            RibbonPanel? panel = null;
            try
            {
                List<RibbonPanel> existingPanels = application.GetRibbonPanels(tabName);
                foreach (RibbonPanel p in existingPanels)
                {
                    if (p.Name == panelName)
                    {
                        panel = p;
                        break;
                    }
                }
            }
            catch { }

            if (panel == null)
            {
                try
                {
                    panel = application.CreateRibbonPanel(tabName, panelName);
                }
                catch
                {
                    return;
                }
            }

            string thisAssemblyPath = Assembly.GetExecutingAssembly().Location;
            string? dirPath = Path.GetDirectoryName(thisAssemblyPath);

            // Obtener ítems existentes en el panel
            IList<RibbonItem> existingItems = panel.GetItems();
            Dictionary<string, PushButton> existingButtons = new Dictionary<string, PushButton>();
            foreach (RibbonItem item in existingItems)
            {
                if (item is PushButton btn)
                {
                    existingButtons[btn.Name] = btn;
                }
            }

            void AddOrUpdateBtn(string name, string text, string className)
            {
                PushButton? btn = null;
                if (!existingButtons.ContainsKey(name))
                {
                    PushButtonData bData = new PushButtonData(name, text, thisAssemblyPath, "RevitToSupabasePlugin." + className);
                    btn = panel.AddItem(bData) as PushButton;
                }
            }

            AddOrUpdateBtn("cmdGen", "🏗️ Generador\nFamilias IA", "CommandGenerador");
            AddOrUpdateBtn("cmdMCP", "📊 Exportar\nMetadata", "Command");
            AddOrUpdateBtn("cmdIA", "⚙️ Configurador\nPlantillas", "CommandConfiguradorPlantilla");
            AddOrUpdateBtn("cmdSchedules", "📝 Asistente\nTablas IA", "CommandSchedulesIA");
            AddOrUpdateBtn("cmdSyncMaterials", "🔄 Sincronizar\nMateriales", "CommandSyncMaterials");

            // --- Panel y Botones para Sistema de Riego ---
            string panelRiegoName = "💧 Sistema de Riego";

            RibbonPanel? panelRiego = null;
            try
            {
                List<RibbonPanel> existingPanels = application.GetRibbonPanels(tabName);
                foreach (RibbonPanel p in existingPanels)
                {
                    if (p.Name == panelRiegoName)
                    {
                        panelRiego = p;
                        break;
                    }
                }
            }
            catch { }

            if (panelRiego == null)
            {
                try
                {
                    panelRiego = application.CreateRibbonPanel(tabName, panelRiegoName);
                }
                catch { }
            }

            if (panelRiego != null)
            {
                IList<RibbonItem> existingRiegoItems = panelRiego.GetItems();
                Dictionary<string, PushButton> existingRiegoButtons = new Dictionary<string, PushButton>();
                foreach (RibbonItem item in existingRiegoItems)
                {
                    if (item is PushButton btn)
                    {
                        existingRiegoButtons[btn.Name] = btn;
                    }
                }

                void AddOrUpdateRiegoBtn(string name, string text, string className)
                {
                    if (!existingRiegoButtons.ContainsKey(name))
                    {
                        PushButtonData bData = new PushButtonData(name, text, thisAssemblyPath, "RevitToSupabasePlugin." + className);
                        panelRiego.AddItem(bData);
                    }
                }

                AddOrUpdateRiegoBtn("cmdCargarRiego", "📂 Explorador\nPlantilla Riego", "CommandCargarPlantillaRiego");
            }

            // --- Panel y Botones para Albañilería ---
            string panelAlbaName = "🧱 Albañilería";
            RibbonPanel? panelAlba = null;
            try
            {
                List<RibbonPanel> existingPanels = application.GetRibbonPanels(tabName);
                foreach (RibbonPanel p in existingPanels)
                {
                    if (p.Name == panelAlbaName)
                    {
                        panelAlba = p;
                        break;
                    }
                }
            }
            catch { }

            if (panelAlba == null)
            {
                try
                {
                    panelAlba = application.CreateRibbonPanel(tabName, panelAlbaName);
                }
                catch { }
            }

            if (panelAlba != null)
            {
                IList<RibbonItem> existingAlbaItems = panelAlba.GetItems();
                Dictionary<string, PushButton> existingAlbaButtons = new Dictionary<string, PushButton>();
                foreach (RibbonItem item in existingAlbaItems)
                {
                    if (item is PushButton btn)
                    {
                        existingAlbaButtons[btn.Name] = btn;
                    }
                }

                void AddOrUpdateAlbaBtn(string name, string text, string className)
                {
                    if (!existingAlbaButtons.ContainsKey(name))
                    {
                        PushButtonData bData = new PushButtonData(name, text, thisAssemblyPath, "RevitToSupabasePlugin." + className);
                        panelAlba.AddItem(bData);
                    }
                }

                AddOrUpdateAlbaBtn("cmdGenLadrillos", "🏭 Generar Familias\nde Ladrillos", "CommandGenerarFamiliasLadrillos");
                AddOrUpdateAlbaBtn("cmdConvMuros", "🧱 Convertir Muros\na Ladrillos", "CommandConvertirMurosALadrillos");
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
