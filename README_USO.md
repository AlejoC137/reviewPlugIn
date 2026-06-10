# Guía de Uso del Plugin en Revit 2027

Este documento detalla los pasos que debes seguir para compilar, instalar y usar el plugin dentro de Revit 2027.

## 1. Prerrequisitos

*   **Revit 2027** instalado en tu computadora.
*   **Visual Studio 2022** con soporte para desarrollo de escritorio con .NET.
*   **Local Server en ejecución:** Antes de ejecutar el comando en Revit, asegúrate de que tu servidor local (Node/React/Python) esté encendido y respondiendo en la URL configurada (`http://localhost:3000/api/mcp-data`).

## 2. Compilación e Instalación

1.  Abre el archivo `RevitToSupabasePlugin.csproj` en Visual Studio 2022.
2.  En el menú superior, ve a **Compilar** -> **Compilar solución** (o presiona `Ctrl + Shift + B`).
3.  **¡Listo!** Gracias al evento Post-Build que configuramos previamente, los archivos compilados (`.dll` y `.addin`) se han copiado automáticamente a la carpeta correcta de complementos de Revit (`%AppData%\Autodesk\Revit\Addins\2025`).

## 3. Ejecutar el Plugin en Revit

1.  Abre **Revit 2027**.
    *   *Nota:* La primera vez que abras Revit después de compilar, puede aparecer una advertencia de seguridad indicando que se ha cargado un nuevo complemento ("Unverified Publisher"). Selecciona **Cargar siempre** (Always Load).
2.  Abre un modelo existente o crea uno nuevo.
3.  Dirígete a la pestaña superior llamada **Complementos** (o **Add-ins** si lo tienes en inglés).
4.  Busca el panel llamado **Herramientas externas** (External Tools) y despliega el menú haciendo clic.
5.  Haz clic en el botón que dice **Conectar con MCP Local**.

## 4. Resultados Esperados

Una vez hagas clic en el botón, el plugin hará una pausa de unos milisegundos mientras consulta a tu Local Server:

*   **Conexión Exitosa:** Verás un cuadro de diálogo emergente (TaskDialog) mostrando la lista de elementos provenientes de tu base de datos de Supabase. Verás la información concatenada como: `ID: 1 - Nombre: Muro - Estado: Pendiente`.
*   **Error de Conexión:** Si el servidor local está apagado, la URL es incorrecta, o hay un problema de red, verás una alerta de Revit indicando: *"Error de Conexión: No se pudo obtener respuesta del Local Server. Verifica que tu app Node/React esté corriendo."*
*   **Sin Datos:** Si la conexión es exitosa pero Supabase no devolvió ningún registro, aparecerá un aviso indicando: *"La consulta al Local Server no devolvió datos."*
