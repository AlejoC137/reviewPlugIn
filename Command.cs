using System;
using System.IO;
using System.Net.Http;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using Newtonsoft.Json;

namespace RevitToSupabasePlugin
{
    // =========================================================================
    // 1. COMANDO: CONEXIÓN CON MCP LOCAL / SUPABASE
    // =========================================================================
    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null) return Result.Failed;

            try
            {
                // Prioridad: Mostrar el Panel de Control centralizado y enfocar la pestaña de Exportar
                try
                {
                    DockablePane pane = uiapp.GetDockablePane(ReviewControlPanelPage.PaneId);
                    if (pane != null)
                    {
                        if (!pane.IsShown())
                        {
                            pane.Show();
                        }
                        ReviewControlPanelPage.Instance?.SelectExportTab();
                        return Result.Succeeded;
                    }
                }
                catch { }

                // Fallback modal si el dockable pane no está registrado
                ExportMetadataExternalEventHandler handler = new ExportMetadataExternalEventHandler();
                ExternalEvent extEvent = ExternalEvent.Create(handler);

                ExportadorMetadataWindow window = new ExportadorMetadataWindow(uidoc.Document, extEvent, handler);
                System.Windows.Interop.WindowInteropHelper helper = new System.Windows.Interop.WindowInteropHelper(window);
                helper.Owner = uiapp.MainWindowHandle;
                window.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Excepción", ex.ToString());
                return Result.Failed;
            }
        }
    }

    public class SupabaseDataModel
    {
        [JsonProperty("id")]
        public string? Id { get; set; }

        [JsonProperty("name")]
        public string? Name { get; set; }

        [JsonProperty("status")]
        public string? Status { get; set; }
    }

    // =========================================================================
    // 2. COMANDO: GENERADOR ESTRUCTURAL / IA
    // =========================================================================
    [Transaction(TransactionMode.Manual)]
    public class CommandGenerador : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null)
            {
                TaskDialog.Show("Error", "No hay ningún documento activo.");
                return Result.Failed;
            }

            try
            {
                GeneradorIAWindow window = new GeneradorIAWindow(uidoc.Document);
                System.Windows.Interop.WindowInteropHelper helper = new System.Windows.Interop.WindowInteropHelper(window);
                helper.Owner = uiapp.MainWindowHandle;
                window.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Error", "No se pudo iniciar el generador: " + ex.ToString());
                return Result.Failed;
            }
        }
    }


    // =========================================================================
    // 3. COMANDO: CONFIGURADOR IA DE PLANTILLAS
    // =========================================================================
    [Transaction(TransactionMode.Manual)]
    public class CommandConfiguradorPlantilla : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null)
            {
                TaskDialog.Show("Error", "No hay ningún documento activo.");
                return Result.Failed;
            }

            try
            {
                ConfiguradorIAPlantillaWindow window = new ConfiguradorIAPlantillaWindow(uidoc.Document);
                System.Windows.Interop.WindowInteropHelper helper = new System.Windows.Interop.WindowInteropHelper(window);
                helper.Owner = uiapp.MainWindowHandle;
                window.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Error", "No se pudo iniciar el Configurador IA: " + ex.ToString());
                return Result.Failed;
            }
        }
    }

    // =========================================================================
    // 4. COMANDO: ASISTENTE TABLAS IA
    // =========================================================================
    [Transaction(TransactionMode.Manual)]
    public class CommandSchedulesIA : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null)
            {
                TaskDialog.Show("Error", "No hay ningún documento activo.");
                return Result.Failed;
            }

            try
            {
                SchedulesIAWindow window = new SchedulesIAWindow(uidoc.Document, uidoc);
                System.Windows.Interop.WindowInteropHelper helper = new System.Windows.Interop.WindowInteropHelper(window);
                helper.Owner = uiapp.MainWindowHandle;
                window.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Error", "No se pudo iniciar el Asistente de Tablas IA: " + ex.ToString());
                return Result.Failed;
            }
        }
    }

    // =========================================================================
    // MANEJADOR DE EVENTO EXTERNO (IExternalEventHandler) - PLANTILLA
    // =========================================================================
    public class ExportMetadataExternalEventHandler : IExternalEventHandler
    {
        public string UserEmail { get; set; } = "";
        public string UserPassword { get; set; } = "";
        public bool IncludeProjectInfo { get; set; } = true;
        public bool IncludeSiteLocation { get; set; } = true;
        public bool IncludeLevels { get; set; } = true;
        public bool IncludeCategorySummary { get; set; } = true;
        public bool IncludeElementDetails { get; set; } = true;
        public bool IncludeRooms { get; set; } = true;
        public bool IncludeSchedules { get; set; } = true;
        public bool IncludeSheetsAndViews { get; set; } = true;
        public bool IncludeMaterials { get; set; } = true;
        public bool IncludeWeb3dModel { get; set; } = true;

        private ExportadorMetadataWindow? _mainWindow;

        public void SetWindowReference(ExportadorMetadataWindow window)
        {
            _mainWindow = window;
        }

        public void Execute(UIApplication uiapp)
        {
            ProgressWindow? progressWin = null;
            try
            {
                UIDocument uidoc = uiapp.ActiveUIDocument;
                if (uidoc == null) return;
                Document doc = uidoc.Document;

                // Crear y mostrar la ventana de progreso
                progressWin = new ProgressWindow();
                progressWin.Show();

                // Recopilar metadata del modelo según los checkboxes activados
                ProjectMetadataPackage package = MetadataExporterService.CollectMetadata(
                    doc,
                    IncludeElementDetails,
                    IncludeProjectInfo,
                    IncludeSiteLocation,
                    IncludeLevels,
                    IncludeCategorySummary,
                    IncludeRooms,
                    IncludeSchedules,
                    IncludeSheetsAndViews,
                    IncludeMaterials,
                    IncludeWeb3dModel,
                    (current, total, msg) => progressWin.UpdateProgress(current, total, msg),
                    () => progressWin.IsInterrupted
                );

                package.UserEmail = !string.IsNullOrEmpty(UserEmail) ? UserEmail : AuthenticationService.UserEmail;

                // Comprobar si el proceso fue cancelado por el usuario
                if (progressWin.IsInterrupted)
                {
                    progressWin.Close();
                    TaskDialog.Show("Proceso Cancelado", "El proceso de exportación fue interrumpido por el usuario.");
                    if (_mainWindow != null)
                    {
                        _mainWindow.Show();
                        _mainWindow.UpdateStatus("Exportación cancelada por el usuario.");
                    }
                    return;
                }

                progressWin.Close();

                // Mostrar notificación de éxito
                int elemCount = package.ElementsById != null ? package.ElementsById.Count : 0;
                string jsonSerialized = JsonConvert.SerializeObject(package, Formatting.Indented);

                if (_mainWindow != null)
                {
                    _mainWindow.Show();
                    _mainWindow.UpdateStatus($"Metadata generada exitosamente ({elemCount} elementos procesados). Lista para exportar.");
                    _mainWindow.UpdatePreviewText(jsonSerialized);
                }
            }
            catch (Exception ex)
            {
                if (progressWin != null) progressWin.Close();
                TaskDialog.Show("Excepción en Exportación", ex.Message + "\n" + ex.StackTrace);
                if (_mainWindow != null)
                {
                    _mainWindow.Show();
                    _mainWindow.UpdateStatus("Excepción producida durante la exportación.");
                }
            }
        }

        public string GetName()
        {
            return "ExportMetadataEventHandler";
        }
    }
}
