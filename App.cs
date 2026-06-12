using System;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace RevitToSupabasePlugin
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            // Nombre de la pestaña personalizada en Revit
            string tabName = "Review";
            string panelName = "Supabase MCP";

            try
            {
                // Crear nueva pestaña
                application.CreateRibbonTab(tabName);
            }
            catch { }

            // Crear el panel dentro de la pestaña
            RibbonPanel panel = application.CreateRibbonPanel(tabName, "AI Studio");
            string thisAssemblyPath = Assembly.GetExecutingAssembly().Location;
            string? dirPath = System.IO.Path.GetDirectoryName(thisAssemblyPath);

            // Método helper para crear botones
            void AddButton(string name, string text, string className, string iconName)
            {
                PushButtonData bData = new PushButtonData(name, text, thisAssemblyPath, "RevitToSupabasePlugin." + className);
                PushButton? btn = panel.AddItem(bData) as PushButton;
                if (btn != null && dirPath != null)
                {
                    string iconPath = System.IO.Path.Combine(dirPath, iconName);
                    if (System.IO.File.Exists(iconPath))
                    {
                        btn.LargeImage = new BitmapImage(new Uri(iconPath, UriKind.Absolute));
                    }
                }
            }

            AddButton("cmdGen", "Generador\nEstructural", "CommandGenerador", "icon_generador.png");
            AddButton("cmdQA", "QA/QC\nInspector", "CommandQAQC", "icon_qaqc.png");
            AddButton("cmdMasivo", "Gestor\nMasivo", "CommandMasivo", "icon_masivo.png");
            AddButton("cmdDoc", "Auto\nDocumentación", "CommandDoc", "icon_doc.png");
            AddButton("cmdCostos", "Asistente\nCostos", "CommandCostos", "icon_costos.png");
            AddButton("cmdCopilot", "Revit\nCopilot", "CommandCopilot", "icon_copilot.png");

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
