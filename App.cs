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
            RibbonPanel panel = application.CreateRibbonPanel(tabName, panelName);

            // Obtener la ruta del ensamblado (DLL) actual
            string thisAssemblyPath = Assembly.GetExecutingAssembly().Location;

            // Crear la información del botón
            PushButtonData buttonData = new PushButtonData(
                "cmdReviewMCP",
                "Conectar\nLocal Server",
                thisAssemblyPath,
                "RevitToSupabasePlugin.Command");

            // Añadir el botón al panel
            PushButton pushButton = panel.AddItem(buttonData) as PushButton;

            // Tooltip explicativo
            pushButton.ToolTip = "Obtiene datos de la app React/Supabase vía Local Server";
            
            try
            {
                // Intentar cargar la imagen icon.png desde la misma carpeta que el dll
                string iconPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(thisAssemblyPath), "icon.png");
                if (System.IO.File.Exists(iconPath))
                {
                    Uri iconUri = new Uri(iconPath, UriKind.Absolute);
                    BitmapImage bitmapImage = new BitmapImage(iconUri);
                    pushButton.LargeImage = bitmapImage; // Icono grande (32x32)
                }
            }
            catch { }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
