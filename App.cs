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

            // Suscribir al evento Idling para restaurar los 3 botones si pyRevit recarga la interfaz
            application.Idling += Application_Idling;

            return Result.Succeeded;
        }

        private void Application_Idling(object? sender, IdlingEventArgs e)
        {
            if (_uiApp != null)
            {
                EnsureRibbonButtonsExist(_uiApp);
            }
        }

        private static void InitializeRibbon(UIControlledApplication application)
        {
            string tabName = "Review";
            try
            {
                application.CreateRibbonTab(tabName);
            }
            catch { }

            EnsureRibbonButtonsExist(application);
        }

        private static void EnsureRibbonButtonsExist(UIControlledApplication application)
        {
            string tabName = "Review";
            string panelName = "Generador IA";

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

            void AddOrUpdateBtn(string name, string text, string className, string iconName)
            {
                PushButton? btn = null;
                if (!existingButtons.ContainsKey(name))
                {
                    PushButtonData bData = new PushButtonData(name, text, thisAssemblyPath, "RevitToSupabasePlugin." + className);
                    btn = panel.AddItem(bData) as PushButton;
                }
                else
                {
                    btn = existingButtons[name];
                }

                if (btn != null && dirPath != null)
                {
                    string iconPath = Path.Combine(dirPath, iconName);
                    if (File.Exists(iconPath))
                    {
                        btn.LargeImage = new BitmapImage(new Uri(iconPath, UriKind.Absolute));
                    }
                }
            }

            AddOrUpdateBtn("cmdGen", "Generador\nEstructural", "CommandGenerador", "icon_generador.png");
            AddOrUpdateBtn("cmdMCP", "Conexión\nSupabase", "Command", "icon_masivo.png");
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            if (_uiApp != null)
            {
                _uiApp.Idling -= Application_Idling;
            }
            return Result.Succeeded;
        }
    }
}
