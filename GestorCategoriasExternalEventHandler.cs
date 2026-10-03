using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitToSupabasePlugin
{
    /// <summary>
    /// Manejador de eventos externos para ejecutar transacciones y operaciones
    /// de Revit API de forma segura desde la ventana flotante GestorCategoriasWindow.
    /// </summary>
    public class GestorCategoriasExternalEventHandler : IExternalEventHandler
    {
        private Action<UIApplication>? _action;
        private ExternalEvent? _externalEvent;

        public void SetExternalEvent(ExternalEvent externalEvent)
        {
            _externalEvent = externalEvent;
        }

        public void Run(Action<UIApplication> action)
        {
            _action = action;
            _externalEvent?.Raise();
        }

        public void Execute(UIApplication app)
        {
            try
            {
                _action?.Invoke(app);
            }
            catch (Exception ex)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Error en Contexto Revit", $"Ocurrió un error al ejecutar la operación en Revit:\n{ex.Message}");
            }
            finally
            {
                _action = null;
            }
        }

        public string GetName() => "ARCA_GestorCategoriasExternalEventHandler";
    }
}
