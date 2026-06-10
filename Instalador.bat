@echo off
title Instalador del Plugin para Revit 2027
color 0B

echo.
echo      .----------------.
echo      ^|                ^|\
echo      ^|                ^| \
echo      ^|                ^|  \
echo      ^|                +---+
echo      ^|   rrrrrrrr         ^|
echo      ^|   rrrrrrrrrrr      ^|
echo      ^|   rrr     rrr      ^|
echo      ^|   rrr              ^|
echo      ^|   rrr              ^|
echo      ^|   rrr              ^|
echo      ^|   rrr              ^|
echo      ^|   rrr              ^|
echo      ^|                    ^|
echo      '--------------------'
echo.
echo ========================================================
echo       Bienvenido al Instalador de reviewPlugIn
echo                 (Para Revit 2027)
echo ========================================================
echo.
echo Hola! Este asistente instalara el plugin "Review" en tu
echo entorno de Autodesk Revit 2027.
echo.
set /p user_choice="Deseas continuar con la instalacion? (S/N): "

if /I "%user_choice%" NEQ "S" (
    echo.
    echo Ok, instalacion cancelada por el usuario. Nos vemos pronto!
    pause
    exit /b
)

echo.
echo Genial. Iniciando instalacion...
echo --------------------------------------------------------

set "ADDIN_FOLDER=%AppData%\Autodesk\Revit\Addins\2027"

:: Buscar los archivos compilados en diferentes ubicaciones posibles
set "DLL_PATH="
set "ADDIN_PATH="
set "NEWTONSOFT_PATH="

:: Caso 1: Los archivos estan en la misma carpeta que el .bat (Paquete distribuido a clientes)
if exist "%~dp0RevitToSupabasePlugin.dll" (
    set "DLL_PATH=%~dp0RevitToSupabasePlugin.dll"
    set "ADDIN_PATH=%~dp0RevitToSupabasePlugin.addin"
    set "NEWTONSOFT_PATH=%~dp0Newtonsoft.Json.dll"
) else if exist "%~dp0bin\Release\net8.0-windows\RevitToSupabasePlugin.dll" (
    :: Caso 2: El .bat se ejecuta desde el proyecto (Modo Release)
    set "DLL_PATH=%~dp0bin\Release\net8.0-windows\RevitToSupabasePlugin.dll"
    set "ADDIN_PATH=%~dp0RevitToSupabasePlugin.addin"
    set "NEWTONSOFT_PATH=%~dp0bin\Release\net8.0-windows\Newtonsoft.Json.dll"
) else if exist "%~dp0bin\Debug\net8.0-windows\RevitToSupabasePlugin.dll" (
    :: Caso 3: El .bat se ejecuta desde el proyecto (Modo Debug)
    set "DLL_PATH=%~dp0bin\Debug\net8.0-windows\RevitToSupabasePlugin.dll"
    set "ADDIN_PATH=%~dp0RevitToSupabasePlugin.addin"
    set "NEWTONSOFT_PATH=%~dp0bin\Debug\net8.0-windows\Newtonsoft.Json.dll"
) else (
    color 0C
    echo [ERROR] Hmm... no logro encontrar los archivos del plugin.
    echo Asegurate de que el instalador este en la misma carpeta que 'RevitToSupabasePlugin.dll'.
    echo.
    pause
    exit /b
)

echo Paso 1: Verificando directorio de destino en Revit 2027...
if not exist "%ADDIN_FOLDER%" (
    echo   - Creando la carpeta de Addins para Revit 2027...
    mkdir "%ADDIN_FOLDER%"
) else (
    echo   - La carpeta de Addins ya existe. Todo en orden.
)

echo Paso 2: Copiando los archivos principales del plugin...
copy /Y "%ADDIN_PATH%" "%ADDIN_FOLDER%\" >nul
copy /Y "%DLL_PATH%" "%ADDIN_FOLDER%\" >nul

if exist "%NEWTONSOFT_PATH%" (
    echo Paso 3: Copiando dependencias adicionales...
    copy /Y "%NEWTONSOFT_PATH%" "%ADDIN_FOLDER%\" >nul
)

echo.
color 0A
echo ========================================================
echo [EXITO] Plugin de Review instalado correctamente.
echo.
echo Todo listo! Tu conexion con Supabase/React te espera.
echo Ya puedes iniciar Revit 2027.
echo ========================================================
echo.
pause
