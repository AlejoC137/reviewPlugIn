@echo off
title Instalador del Plugin Review para Revit 2027
color 0B

if exist "%~dp0error.md" del "%~dp0error.md"

echo.
echo      .-----------------.
echo      ^|                 ^|\
echo      ^|                 ^| \
echo      ^|                 ^|  \
echo      ^|                 +---+
echo      ^|   rrrrrrrr          ^|
echo      ^|   rrrrrrrrrrr       ^|
echo      ^|   rrr     rrr       ^|
echo      ^|   rrr               ^|
echo      ^|   rrr               ^|
echo      ^|   rrr               ^|
echo      ^|   rrr               ^|
echo      ^|   rrr               ^|
echo      ^|                     ^|
echo      '---------------------'
echo.
echo ============================================================
echo         Bienvenido al Instalador de reviewPlugIn
echo                   (Para Revit 2027)
echo ============================================================
echo.
echo  Funcionalidades incluidas en esta version:
echo   [*] Conexion con base de datos Supabase
echo   [*] Exportacion de Metadata (Elementos, Rooms, Schedules)
echo   [*] Exportacion de Planos y Vistas (Sheets/Views)
echo   [*] Exportacion de Materiales del modelo
echo   [*] Guardar JSON a archivo local
echo   [*] Generador Estructural con IA
echo.
echo ============================================================
echo.

:: -----------------------------------------------------------
:: Paso 0: Verificar si Revit esta abierto
:: -----------------------------------------------------------
tasklist /FI "IMAGENAME eq Revit.exe" 2>NUL | find /I /N "Revit.exe">NUL
if "%ERRORLEVEL%"=="0" (
    color 0C
    echo [ADVERTENCIA] Revit esta actualmente abierto.
    echo Si Revit esta abierto, Windows bloqueara los archivos y la instalacion fallara.
    set /p kill_revit="Deseas cerrar Revit automaticamente ahora mismo? (S/N): "
    if /I "%kill_revit%"=="S" (
        taskkill /F /IM Revit.exe
        echo Revit cerrado.
        timeout /t 2 >nul
        color 0B
    ) else (
        echo Por favor, cierra Revit manualmente e intenta de nuevo.
        pause
        exit /b
    )
)

set /p user_choice="Deseas continuar con la instalacion? (S/N): "

if /I "%user_choice%" NEQ "S" (
    echo.
    echo Ok, instalacion cancelada por el usuario. Nos vemos pronto!
    pause
    exit /b
)

echo.
echo Genial. Iniciando instalacion...
echo ------------------------------------------------------------

:: -----------------------------------------------------------
:: Paso 0.5: Compilar el codigo automaticamente (si existe el .csproj)
:: -----------------------------------------------------------
if exist "%~dp0RevitToSupabasePlugin.csproj" (
    echo Compilando el codigo fuente para asegurar que tienes la ultima version...
    dotnet build "%~dp0RevitToSupabasePlugin.csproj" -c Debug
    if errorlevel 1 (
        color 0C
        echo.
        echo [ERROR] La compilacion fallo. Revisa el codigo en Visual Studio.
        echo # Error de Instalacion > "%~dp0error.md"
        echo La compilacion fallo al ejecutar dotnet build. >> "%~dp0error.md"
        pause
        exit /b
    )
    echo Compilacion exitosa.
    echo ------------------------------------------------------------
)

set "ADDIN_FOLDER=%AppData%\Autodesk\Revit\Addins\2027"

:: -----------------------------------------------------------
:: Buscar los archivos compilados en diferentes ubicaciones
:: -----------------------------------------------------------
set "DLL_PATH="
set "ADDIN_PATH="
set "BUILD_DIR="

:: Caso 1: Los archivos estan en la misma carpeta que el .bat (Paquete distribuido a clientes)
if exist "%~dp0RevitToSupabasePlugin.dll" (
    set "DLL_PATH=%~dp0RevitToSupabasePlugin.dll"
    set "ADDIN_PATH=%~dp0RevitToSupabasePlugin.addin"
    set "BUILD_DIR=%~dp0"
    echo [INFO] Modo: Paquete distribuido (archivos en la misma carpeta).
) else if exist "%~dp0bin\Debug\net10.0-windows\RevitToSupabasePlugin.dll" (
    :: Caso 2: Modo Debug (por defecto despues del dotnet build)
    set "DLL_PATH=%~dp0bin\Debug\net10.0-windows\RevitToSupabasePlugin.dll"
    set "ADDIN_PATH=%~dp0RevitToSupabasePlugin.addin"
    set "BUILD_DIR=%~dp0bin\Debug\net10.0-windows\"
    echo [INFO] Modo: Proyecto (Debug).
) else if exist "%~dp0bin\Release\net10.0-windows\RevitToSupabasePlugin.dll" (
    :: Caso 3: Modo Release
    set "DLL_PATH=%~dp0bin\Release\net10.0-windows\RevitToSupabasePlugin.dll"
    set "ADDIN_PATH=%~dp0RevitToSupabasePlugin.addin"
    set "BUILD_DIR=%~dp0bin\Release\net10.0-windows\"
    echo [INFO] Modo: Proyecto (Release).
) else (
    color 0C
    echo.
    echo [ERROR] No se encontraron los archivos compilados del plugin.
    echo.
    echo  Soluciones posibles:
    echo   1. Compila el proyecto en Visual Studio (Build ^> Build Solution).
    echo   2. Asegurate de que el instalador este en la misma carpeta que
    echo      'RevitToSupabasePlugin.dll' (para distribucion sin codigo fuente).
    echo.
    echo # Error de Instalacion > "%~dp0error.md"
    echo No se encontraron los archivos compilados del plugin. >> "%~dp0error.md"
    pause
    exit /b
)

echo.
echo Paso 1: Verificando directorio de destino en Revit 2027...
if not exist "%ADDIN_FOLDER%" (
    echo   - Creando la carpeta de Addins para Revit 2027...
    mkdir "%ADDIN_FOLDER%"
) else (
    echo   - La carpeta de Addins ya existe. Todo en orden.
)

echo Paso 2: Copiando el manifiesto del plugin (.addin)...
copy /Y "%ADDIN_PATH%" "%ADDIN_FOLDER%\" >nul
if errorlevel 1 (
    echo   [AVISO] No se pudo copiar el archivo .addin.
) else (
    echo   - RevitToSupabasePlugin.addin copiado correctamente.
)

echo Paso 3: Copiando la DLL principal del plugin...
copy /Y "%DLL_PATH%" "%ADDIN_FOLDER%\" >nul
if errorlevel 1 (
    color 0C
    echo   [ERROR CRITICO] No se pudo copiar la DLL principal. 
    echo   Probablemente Revit todavia tiene el archivo bloqueado.
    echo # Error de Instalacion > "%~dp0error.md"
    echo No se pudo copiar la DLL principal. Revit podria estar bloqueando el archivo. >> "%~dp0error.md"
    pause
    exit /b
) else (
    echo   - RevitToSupabasePlugin.dll copiado correctamente.
)

echo Paso 4: Copiando dependencias (Newtonsoft.Json, etc.)...
if exist "%BUILD_DIR%Newtonsoft.Json.dll" (
    copy /Y "%BUILD_DIR%Newtonsoft.Json.dll" "%ADDIN_FOLDER%\" >nul
    echo   - Newtonsoft.Json.dll copiado.
) else (
    echo   [AVISO] Newtonsoft.Json.dll no encontrado en la carpeta de compilacion.
)

for %%f in ("%BUILD_DIR%*.dll") do (
    if /I not "%%~nxf"=="RevitToSupabasePlugin.dll" (
        if /I not "%%~nxf"=="RevitAPI.dll" (
            if /I not "%%~nxf"=="RevitAPIUI.dll" (
                copy /Y "%%f" "%ADDIN_FOLDER%\" >nul 2>&1
            )
        )
    )
)

echo Paso 5: Copiando iconos de la interfaz...
set "ICONS_COPIED=0"
for %%f in ("%~dp0icon_*.png") do (
    copy /Y "%%f" "%ADDIN_FOLDER%\" >nul
    set /a ICONS_COPIED+=1
)
:: Compatibilidad con nombre antiguo
if exist "%~dp0icon.png" (
    copy /Y "%~dp0icon.png" "%ADDIN_FOLDER%\" >nul
)
echo   - Iconos copiados (icon_generador.png, icon_masivo.png, etc.).

echo.
color 0A
echo ============================================================
echo  [EXITO] Plugin Review actualizado e instalado correctamente!
echo.
echo  Ubicacion de instalacion:
echo    %ADDIN_FOLDER%
echo.
echo  Ya puedes iniciar Revit 2027 y usar el plugin.
echo ============================================================
echo.
pause
