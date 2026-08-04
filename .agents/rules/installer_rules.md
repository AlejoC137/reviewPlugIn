---
description: Workflow and instructions for compiling, installing, and deploying the reviewPlugIn Revit addin.
---

# Flujo de Compilación e Instalación (Revit Plugin)

1. **Uso de Instalador.bat:**
   Para compilar, instalar o probar los cambios del plugin, **SIEMPRE** debes usar el script `Instalador.bat` ubicado en la raíz del proyecto. No dependas únicamente de `dotnet build` desde la terminal.
   - El script se encarga de verificar si Revit está abierto, preguntar para cerrarlo y evitar el error de "Infracción al compartir" (Sharing Violation).
   - El script compila el código (si encuentra el `.csproj`) y luego copia los archivos a la carpeta correcta de Revit (`%AppData%\Autodesk\Revit\Addins\2027`).

2. **Actualizar el Instalador.bat (MUY IMPORTANTE):**
   Si durante el desarrollo agregas:
   - Nuevas dependencias (archivos `.dll`).
   - Nuevas imágenes o íconos (archivos `.png`).
   - Cualquier otro archivo necesario en tiempo de ejecución.
   
   **DEBES** actualizar el código de `Instalador.bat` para asegurarte de que esos nuevos archivos se copien a la carpeta destino (`%ADDIN_FOLDER%`). De lo contrario, el plugin crasheará o le faltarán piezas al instalarlo en la computadora del usuario o la tuya.

3. **README_USO:**
   El proyecto cuenta con un `README_USO.md` que también explica el proceso de instalación manual o cómo armar un paquete de distribución para el cliente final (Deploy). Revísalo si tienes dudas sobre la arquitectura.
