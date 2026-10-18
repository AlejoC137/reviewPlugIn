# Guía de Uso del Plugin en Revit 2025 / 2027

Este documento detalla los pasos que debes seguir para compilar, instalar y usar el plugin dentro de **Revit 2025** y **Revit 2027**.

## 1. Prerrequisitos

*   **Autodesk Revit 2025** y/o **Revit 2027** instalado en tu computadora.
*   **Visual Studio 2022** o **SDK de .NET 8.0 / 10.0**.
*   **Local Server en ejecución (Opcional si usas MCP):** Asegúrate de que tus servicios locales o puente estén encendidos.

## 2. Compilación e Instalación Automática

1.  Abre el archivo `RevitToSupabasePlugin.csproj` en Visual Studio (o ejecuta `Instalador.bat`).
2.  Al compilar la solución (`Ctrl + Shift + B` o con `dotnet build`), el evento **Post-Build** copia automáticamente todos los archivos (`.dll`, `.addin`, íconos, bases de datos JSON y familias) a las dos carpetas de complementos:
    *   `%AppData%\Autodesk\Revit\Addins\2025`
    *   `%AppData%\Autodesk\Revit\Addins\2027`

## 3. Instalación para Usuarios Finales (Deploy)

Si deseas compartir este plugin con otra persona (o instalarlo tú mismo sin tener que abrir Visual Studio):
1.  Distribuye la carpeta con los archivos o el proyecto.
2.  Ejecuta **`Instalador.bat`**.
3.  El instalador te permitirá elegir:
    *   **[1] Instalar en AMBAS (Revit 2025 y Revit 2027)** *(Recomendado)*
    *   **[2] Solo Revit 2025**
    *   **[3] Solo Revit 2027**

## 4. Ejecutar el Plugin en Revit

1.  Abre **Revit 2025** o **Revit 2027**.
    *   *Nota:* La primera vez que abras Revit después de instalar, puede aparecer una advertencia de seguridad indicando que se ha cargado un nuevo complemento. Selecciona **Cargar siempre** (Always Load).
2.  Abre un modelo existente o crea uno nuevo.
3.  Dirígete a la pestaña superior llamada **Complementos** (o **Add-ins** si lo tienes en inglés).
4.  Busca el panel llamado **Herramientas externas** (External Tools) y despliega el menú haciendo clic.
5.  Haz clic en el botón que dice **Conectar con MCP Local**.

## 4. Resultados Esperados

Una vez hagas clic en el botón, el plugin hará una pausa de unos milisegundos mientras consulta a tu Local Server:

*   **Conexión Exitosa:** Verás un cuadro de diálogo emergente (TaskDialog) mostrando la lista de elementos provenientes de tu base de datos de Supabase. Verás la información concatenada como: `ID: 1 - Nombre: Muro - Estado: Pendiente`.
*   **Error de Conexión:** Si el servidor local está apagado, la URL es incorrecta, o hay un problema de red, verás una alerta de Revit indicando: *"Error de Conexión: No se pudo obtener respuesta del Local Server. Verifica que tu app Node/React esté corriendo."*
*   **Sin Datos:** Si la conexión es exitosa pero Supabase no devolvió ningún registro, aparecerá un aviso indicando: *"La consulta al Local Server no devolvió datos."*
