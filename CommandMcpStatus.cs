using System;
using System.Diagnostics;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitTaskDialog = Autodesk.Revit.UI.TaskDialog;
using RevitTaskDialogCommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons;
using RevitTaskDialogResult = Autodesk.Revit.UI.TaskDialogResult;
using RevitTaskDialogCommandLinkId = Autodesk.Revit.UI.TaskDialogCommandLinkId;

namespace RevitToSupabasePlugin
{
    [Transaction(TransactionMode.Manual)]
    public class CommandMcpStatus : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument?.Document;
            string projectName = doc?.ProjectInformation?.Name ?? "Sin Proyecto Activo";
            string userEmail = AuthenticationService.UserEmail.Length > 0 ? AuthenticationService.UserEmail : "Sesión por defecto (AnonKey)";
            bool isMcpRunning = App.McpServerInstance?.IsRunning ?? false;
            int port = App.McpServerInstance?.Port ?? 8080;

            string dialogText = $"📡 SERVIDOR MCP LOCAL (REVIT):\n" +
                                $"• Estado: {(isMcpRunning ? "🟢 EN LÍNEA (Escuchando)" : "🔴 DETENIDO")}\n" +
                                $"• Endpoint: http://localhost:{port}/mcp/\n\n" +
                                $"👤 AUTENTICACIÓN SUPABASE:\n" +
                                $"• Usuario: {userEmail}\n" +
                                $"• Proyecto: {projectName}\n\n" +
                                $"🤖 CONFIGURACIÓN BI-MCP:\n" +
                                $"  \"mcpServers\": {{\n" +
                                $"    \"review_cloud\": {{ \"url\": \"https://arca-review.vercel.app/mcp\" }},\n" +
                                $"    \"review_local\": {{ \"url\": \"http://localhost:{port}/mcp\" }}\n" +
                                $"  }}\n" +
                                $"}}\n\n" +
                                $"¿Deseas iniciar sesión con tu usuario de Supabase (user_profiles) para sincronizar con tu cuenta?";

            var mainDialog = new RevitTaskDialog("Review Status - Servidor Bi-MCP")
            {
                MainInstruction = "Servidor Bi-MCP Revit Activo",
                MainContent = dialogText,
                CommonButtons = RevitTaskDialogCommonButtons.Yes | RevitTaskDialogCommonButtons.Close,
                DefaultButton = RevitTaskDialogResult.Close
            };

            mainDialog.AddCommandLink(RevitTaskDialogCommandLinkId.CommandLink1, "🔐 Iniciar Sesión con Cuenta Review (user_profiles)");
            mainDialog.AddCommandLink(RevitTaskDialogCommandLinkId.CommandLink2, "☁️ Sincronizar Modelo a Supabase Ahora");

            var res = mainDialog.Show();

            if (res == RevitTaskDialogResult.CommandLink1)
            {
                System.Threading.Tasks.Task.Run(async () =>
                {
                    await AuthenticationService.LoginWithBrowserAsync();
                });
            }
            else if (res == RevitTaskDialogResult.CommandLink2 && doc != null)
            {
                var package = MetadataExporterService.CollectMetadata(
                    doc, true, true, true, true, true, true, true, false, true, false, null, null
                );
                string msgOut = "";
                bool ok = MetadataExporterService.SendToSupabaseDirect(package, out msgOut);
                RevitTaskDialog.Show("Sincronización Supabase", msgOut);
            }

            return Result.Succeeded;
        }
    }
}
