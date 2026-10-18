@echo off
title Instalador del Plugin Review para Revit 2025 / 2027
color 0B

:: Asegurar que el SDK de .NET sea accesible si esta en el perfil del usuario
if exist "%USERPROFILE%\.dotnet\dotnet.exe" set "PATH=%USERPROFILE%\.dotnet;%PATH%"

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
echo               (Compatible con Revit 2025 y 2027)
echo ============================================================
echo.
echo  Funcionalidades incluidas en esta version:
echo   [*] Conexion con base de datos Supabase
echo   [*] Exportacion de Metadata (Elementos, Rooms, Schedules)
echo   [*] Exportacion de Planos y Vistas (Sheets/Views)
echo   [*] Exportacion de Materiales del modelo
echo   [*] Guardar JSON a archivo local
echo   [*] Generador Estructural con IA
echo   [*] Gestor de Categorias de Sistema (Suelos, Muros, DirectShape)
echo   [*] Despiece y Cómputos de Enchapes, Pisos y Steel Deck
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

set "user_choice=S"
set /p user_choice="Deseas continuar con la instalacion? (S/N) [Enter = S]: "

if /I not "%user_choice:~0,1%"=="S" (
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

:: -----------------------------------------------------------
:: Buscar los archivos compilados en diferentes ubicaciones
:: -----------------------------------------------------------
set "DLL_PATH="
set "ADDIN_PATH="
set "BUILD_DIR="

if exist "%~dp0RevitToSupabasePlugin.dll" (
    set "DLL_PATH=%~dp0RevitToSupabasePlugin.dll"
    set "ADDIN_PATH=%~dp0RevitToSupabasePlugin.addin"
    set "BUILD_DIR=%~dp0"
    echo [INFO] Modo: Paquete distribuido - archivos en la misma carpeta
    goto :FOUND_BUILD
)
if exist "%~dp0bin\Debug\net8.0-windows\RevitToSupabasePlugin.dll" (
    set "DLL_PATH=%~dp0bin\Debug\net8.0-windows\RevitToSupabasePlugin.dll"
    set "ADDIN_PATH=%~dp0RevitToSupabasePlugin.addin"
    set "BUILD_DIR=%~dp0bin\Debug\net8.0-windows\"
    echo [INFO] Modo: Proyecto - Debug net8.0-windows
    goto :FOUND_BUILD
)
if exist "%~dp0bin\Release\net8.0-windows\RevitToSupabasePlugin.dll" (
    set "DLL_PATH=%~dp0bin\Release\net8.0-windows\RevitToSupabasePlugin.dll"
    set "ADDIN_PATH=%~dp0RevitToSupabasePlugin.addin"
    set "BUILD_DIR=%~dp0bin\Release\net8.0-windows\"
    echo [INFO] Modo: Proyecto - Release net8.0-windows
    goto :FOUND_BUILD
)
if exist "%~dp0bin\Debug\net10.0-windows\RevitToSupabasePlugin.dll" (
    set "DLL_PATH=%~dp0bin\Debug\net10.0-windows\RevitToSupabasePlugin.dll"
    set "ADDIN_PATH=%~dp0RevitToSupabasePlugin.addin"
    set "BUILD_DIR=%~dp0bin\Debug\net10.0-windows\"
    echo [INFO] Modo: Proyecto - Debug net10.0-windows
    goto :FOUND_BUILD
)
if exist "%~dp0bin\Release\net10.0-windows\RevitToSupabasePlugin.dll" (
    set "DLL_PATH=%~dp0bin\Release\net10.0-windows\RevitToSupabasePlugin.dll"
    set "ADDIN_PATH=%~dp0RevitToSupabasePlugin.addin"
    set "BUILD_DIR=%~dp0bin\Release\net10.0-windows\"
    echo [INFO] Modo: Proyecto - Release net10.0-windows
    goto :FOUND_BUILD
)

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

:FOUND_BUILD
echo.
echo ============================================================
echo   Selecciona la version de Revit donde deseas instalar:
echo     [1] AMBAS (Revit 2025 y Revit 2027) [Recomendado]
echo     [2] Solo Revit 2025
echo     [3] Solo Revit 2027
echo ============================================================
set "DEST_CHOICE=1"
set /p DEST_CHOICE="Elige una opcion (1, 2 o 3) [Enter = 1]: "

if "%DEST_CHOICE:~0,1%"=="2" (
    call :DO_INSTALL 2025
    goto :SUCCESS_END
)
if "%DEST_CHOICE:~0,1%"=="3" (
    call :DO_INSTALL 2027
    goto :SUCCESS_END
)

call :DO_INSTALL 2025
call :DO_INSTALL 2027
goto :SUCCESS_END

:DO_INSTALL
set "TARGET_REVIT=%~1"
set "ADDIN_FOLDER=%AppData%\Autodesk\Revit\Addins\%TARGET_REVIT%"

echo.
echo ------------------------------------------------------------
echo [Revit %TARGET_REVIT%] Iniciando copia de archivos...
echo ------------------------------------------------------------

if not exist "%ADDIN_FOLDER%" (
    echo   - Creando la carpeta de Addins para Revit %TARGET_REVIT%...
    mkdir "%ADDIN_FOLDER%"
) else (
    echo   - Carpeta de Addins detectada: %ADDIN_FOLDER%
)

echo   - Copiando manifiesto .addin...
copy /Y "%ADDIN_PATH%" "%ADDIN_FOLDER%\" >nul

echo   - Copiando DLL principal...
copy /Y "%DLL_PATH%" "%ADDIN_FOLDER%\" >nul
if errorlevel 1 (
    color 0C
    echo   [ERROR CRITICO] No se pudo copiar la DLL principal en Revit %TARGET_REVIT%.
    echo   Revit podria estar en ejecucion y bloqueando el archivo.
    echo # Error de Instalacion > "%~dp0error.md"
    echo No se pudo copiar la DLL principal a %ADDIN_FOLDER%. >> "%~dp0error.md"
    pause
    exit /b 1
)

echo   - Copiando dependencias...
if exist "%BUILD_DIR%Newtonsoft.Json.dll" (
    copy /Y "%BUILD_DIR%Newtonsoft.Json.dll" "%ADDIN_FOLDER%\" >nul
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

echo   - Copiando iconos...
for %%f in ("%~dp0icon_*.png") do (
    copy /Y "%%f" "%ADDIN_FOLDER%\" >nul
)
if exist "%~dp0icon.png" (
    copy /Y "%~dp0icon.png" "%ADDIN_FOLDER%\" >nul
)

echo   - Sincronizando bases de datos JSON...
for %%j in ("%~dp0*.json") do (
    copy /Y "%%j" "%ADDIN_FOLDER%\" >nul
)

echo   - Copiando familias RFA...
if exist "%~dp0Familias" (
    xcopy "%~dp0Familias\*.*" "%ADDIN_FOLDER%\Familias\" /Y /I /E /S >nul 2>&1
)
for %%r in ("%~dp0*.rfa") do (
    copy /Y "%%r" "%ADDIN_FOLDER%\" >nul 2>&1
)

echo   [OK] Plugin instalado correctamente para Revit %TARGET_REVIT%!
exit /b 0

:SUCCESS_END
echo.
color 0A
echo ============================================================
echo  [EXITO] Proceso completado exitosamente!
echo.
if "%DEST_CHOICE:~0,1%"=="2" echo   [*] Revit 2025 - %AppData%\Autodesk\Revit\Addins\2025
if "%DEST_CHOICE:~0,1%"=="3" echo   [*] Revit 2027 - %AppData%\Autodesk\Revit\Addins\2027
if not "%DEST_CHOICE:~0,1%"=="2" if not "%DEST_CHOICE:~0,1%"=="3" (
    echo   [*] Revit 2025 - %AppData%\Autodesk\Revit\Addins\2025
    echo   [*] Revit 2027 - %AppData%\Autodesk\Revit\Addins\2027
)
echo.
echo  Ya puedes abrir Revit y utilizar la pestana ARCA REVIEW.
echo ============================================================
echo.
pause
