@echo off
title Instalador del Plugin para Revit 2027
color 0B

echo ========================================================
echo       Instalador del Plugin de Revit a Supabase
echo                 (Para Revit 2027)
echo ========================================================
echo.

set "ADDIN_FOLDER=%AppData%\Autodesk\Revit\Addins\2027"

:: Buscar los archivos compilados en diferentes ubicaciones posibles
set "DLL_PATH="
set "ADDIN_PATH="
set "NEWTONSOFT_PATH="

:: Caso 1: Los archivos están en la misma carpeta que el .bat (Paquete distribuido a clientes)
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
    echo [ERROR] No se encontraron los archivos compilados del plugin.
    echo Asegurate de compilar el proyecto en Visual Studio primero (Ctrl + Shift + B).
    echo.
    echo Si este es el instalador final para un usuario, asegurese de que los 
    echo archivos .dll y el .addin esten en la misma carpeta que este archivo .bat.
    echo.
    pause
    exit /b
)

echo Verificando directorio destino...
if not exist "%ADDIN_FOLDER%" (
    echo Creando la carpeta de Addins para Revit 2027...
    mkdir "%ADDIN_FOLDER%"
)

echo Copiando archivos del plugin...
copy /Y "%ADDIN_PATH%" "%ADDIN_FOLDER%\" >nul
copy /Y "%DLL_PATH%" "%ADDIN_FOLDER%\" >nul

if exist "%NEWTONSOFT_PATH%" (
    echo Copiando dependencias (Newtonsoft.Json)...
    copy /Y "%NEWTONSOFT_PATH%" "%ADDIN_FOLDER%\" >nul
)

echo.
color 0A
echo ========================================================
echo [EXITO] Plugin instalado correctamente.
echo Los archivos se han copiado a:
echo %ADDIN_FOLDER%
echo.
echo Ya puedes iniciar Revit 2027 y usar el plugin.
echo ========================================================
echo.
pause
