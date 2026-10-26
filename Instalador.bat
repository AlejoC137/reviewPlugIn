@echo off
setlocal EnableDelayedExpansion
title Instalador del Plugin Review para Revit 2025 / 2027
color 0B

:: Asegurar que el SDK de .NET sea accesible si esta en el perfil del usuario
if exist "%USERPROFILE%\.dotnet\dotnet.exe" set "PATH=%USERPROFILE%\.dotnet;%PATH%"

if exist "%~dp0error.md" del /f /q "%~dp0error.md" >nul 2>&1

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
echo   [*] Generador Estructural con IA y Acciones Directas IA
echo   [*] Gestor de Categorias de Sistema (Suelos, Muros, DirectShape)
echo   [*] Despiece y Computos de Enchapes, Pisos y Steel Deck
echo   [*] Revisor de Familias, Planos y Estudio de Familias Modulares
echo.
echo ============================================================
echo.

:: -----------------------------------------------------------
:: Paso 0: Verificar si Revit esta abierto
:: -----------------------------------------------------------
tasklist /FI "IMAGENAME eq Revit.exe" 2>NUL | find /I /N "Revit.exe">NUL
if not "%ERRORLEVEL%"=="0" goto :ASK_CONTINUE

color 0C
echo [ADVERTENCIA] Revit esta actualmente abierto.
echo Si Revit esta abierto, Windows bloqueara los archivos y la instalacion fallara.
set "kill_revit=S"
set /p kill_revit="Deseas cerrar Revit automaticamente ahora mismo? (S/N) [Enter = S]: "
if /I "%kill_revit:~0,1%"=="S" (
    taskkill /F /IM Revit.exe
    echo Revit cerrado.
    timeout /t 2 >nul
    color 0B
    goto :ASK_CONTINUE
)
echo Por favor, cierra Revit manualmente e intenta de nuevo.
pause
exit /b

:ASK_CONTINUE
set "user_choice=S"
set /p user_choice="Deseas continuar con la instalacion? (S/N) [Enter = S]: "
if /I not "%user_choice:~0,1%"=="S" (
    echo.
    echo Ok, instalacion cancelada por el usuario. Nos vemos pronto!
    pause
    exit /b
)

echo.
echo ============================================================
echo   Selecciona la version de Revit donde deseas instalar:
echo     [1] AMBAS (Revit 2025 y Revit 2027) [Recomendado]
echo     [2] Solo Revit 2025
echo     [3] Solo Revit 2027
echo ============================================================
set "DEST_CHOICE=1"
set /p DEST_CHOICE="Elige una opcion (1, 2 o 3) [Enter = 1]: "

echo.
echo Genial. Iniciando instalacion...
echo ------------------------------------------------------------

:: Desbloquear archivos descargados de Git/Internet (Zone.Identifier) en la carpeta origen
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -LiteralPath '%~dp0' -Recurse -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue" >nul 2>&1

:: -----------------------------------------------------------
:: Paso 0.5: Compilar automaticamente si existe el SDK de .NET
:: (Si el PC no tiene el SDK o falla, usa los binarios ya
::  incluidos en bin\Release sin detener la instalacion)
:: -----------------------------------------------------------
if not exist "%~dp0RevitToSupabasePlugin.csproj" goto :SKIP_BUILD
where dotnet >nul 2>&1
if errorlevel 1 goto :NO_SDK

if exist "%~dp0obj" rmdir /s /q "%~dp0obj" >nul 2>&1
echo [Compilacion] Verificando compilacion mas reciente...
dotnet build "%~dp0RevitToSupabasePlugin.csproj" -c Release --nologo -v q >nul 2>&1
if errorlevel 1 (
    echo   [INFO] Usando binarios precompilados incluidos en el paquete...
) else (
    echo   [OK] Codigo fuente compilado y actualizado correctamente.
)
echo ------------------------------------------------------------
goto :SKIP_BUILD

:NO_SDK
echo [INFO] Usando binarios precompilados incluidos en el paquete...
echo ------------------------------------------------------------

:SKIP_BUILD
set "ADDIN_PATH=%~dp0RevitToSupabasePlugin.addin"

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

set "DLL_PATH="
set "BUILD_DIR="

if "%TARGET_REVIT%"=="2027" goto :FIND_2027
goto :FIND_2025

:FIND_2027
if exist "%~dp0bin\Release\net10.0-windows\RevitToSupabasePlugin.dll" (
    set "DLL_PATH=%~dp0bin\Release\net10.0-windows\RevitToSupabasePlugin.dll"
    set "BUILD_DIR=%~dp0bin\Release\net10.0-windows\"
    goto :CHECK_DLL
)
if exist "%~dp0bin\Debug\net10.0-windows\RevitToSupabasePlugin.dll" (
    set "DLL_PATH=%~dp0bin\Debug\net10.0-windows\RevitToSupabasePlugin.dll"
    set "BUILD_DIR=%~dp0bin\Debug\net10.0-windows\"
    goto :CHECK_DLL
)
if exist "%~dp0bin\Release\net8.0-windows\RevitToSupabasePlugin.dll" (
    set "DLL_PATH=%~dp0bin\Release\net8.0-windows\RevitToSupabasePlugin.dll"
    set "BUILD_DIR=%~dp0bin\Release\net8.0-windows\"
    goto :CHECK_DLL
)
goto :CHECK_DLL

:FIND_2025
if exist "%~dp0bin\Release\net8.0-windows\RevitToSupabasePlugin.dll" (
    set "DLL_PATH=%~dp0bin\Release\net8.0-windows\RevitToSupabasePlugin.dll"
    set "BUILD_DIR=%~dp0bin\Release\net8.0-windows\"
    goto :CHECK_DLL
)
if exist "%~dp0bin\Debug\net8.0-windows\RevitToSupabasePlugin.dll" (
    set "DLL_PATH=%~dp0bin\Debug\net8.0-windows\RevitToSupabasePlugin.dll"
    set "BUILD_DIR=%~dp0bin\Debug\net8.0-windows\"
    goto :CHECK_DLL
)
if exist "%~dp0bin\Release\net10.0-windows\RevitToSupabasePlugin.dll" (
    set "DLL_PATH=%~dp0bin\Release\net10.0-windows\RevitToSupabasePlugin.dll"
    set "BUILD_DIR=%~dp0bin\Release\net10.0-windows\"
    goto :CHECK_DLL
)

:CHECK_DLL
if not defined DLL_PATH (
    if exist "%~dp0RevitToSupabasePlugin.dll" (
        set "DLL_PATH=%~dp0RevitToSupabasePlugin.dll"
        set "BUILD_DIR=%~dp0"
    )
)

if not defined DLL_PATH (
    color 0C
    echo   [ERROR] No se encontro la DLL compilada para Revit %TARGET_REVIT%.
    echo   Asegurate de que la carpeta bin\Release exista en el proyecto.
    pause
    exit /b 1
)

echo.
echo ------------------------------------------------------------
echo [Revit %TARGET_REVIT%] Instalando archivos del plugin...
echo   Origen: "!DLL_PATH!"
echo   Destino: "!ADDIN_FOLDER!"
echo ------------------------------------------------------------

if not exist "!ADDIN_FOLDER!" (
    echo   - Creando carpeta de Addins para Revit %TARGET_REVIT%...
    mkdir "!ADDIN_FOLDER!"
)

echo   - Copiando manifiesto .addin...
copy /Y "!ADDIN_PATH!" "!ADDIN_FOLDER!\" >nul

echo   - Copiando DLL principal...
copy /Y "!DLL_PATH!" "!ADDIN_FOLDER!\" >nul
if errorlevel 1 (
    color 0C
    echo   [ERROR CRITICO] No se pudo copiar la DLL principal en Revit %TARGET_REVIT%.
    echo   Cierra Revit completamente e intenta de nuevo.
    pause
    exit /b 1
)

:: Actualizar tambien ProgramData si existiera una instalacion previa alli
if exist "%ProgramData%\Autodesk\Revit\Addins\%TARGET_REVIT%\RevitToSupabasePlugin.addin" (
    copy /Y "!ADDIN_PATH!" "%ProgramData%\Autodesk\Revit\Addins\%TARGET_REVIT%\" >nul 2>&1
    copy /Y "!DLL_PATH!" "%ProgramData%\Autodesk\Revit\Addins\%TARGET_REVIT%\" >nul 2>&1
)

echo   - Copiando dependencias...
if exist "!BUILD_DIR!Newtonsoft.Json.dll" (
    copy /Y "!BUILD_DIR!Newtonsoft.Json.dll" "!ADDIN_FOLDER!\" >nul
)
for %%f in ("!BUILD_DIR!*.dll") do (
    if /I not "%%~nxf"=="RevitToSupabasePlugin.dll" (
        if /I not "%%~nxf"=="RevitAPI.dll" (
            if /I not "%%~nxf"=="RevitAPIUI.dll" (
                copy /Y "%%f" "!ADDIN_FOLDER!\" >nul 2>&1
            )
        )
    )
)
if exist "!BUILD_DIR!*.deps.json" (
    copy /Y "!BUILD_DIR!*.deps.json" "!ADDIN_FOLDER!\" >nul 2>&1
)

echo   - Copiando iconos...
for %%f in ("%~dp0icon_*.png") do (
    copy /Y "%%f" "!ADDIN_FOLDER!\" >nul
)
if exist "%~dp0icon.png" (
    copy /Y "%~dp0icon.png" "!ADDIN_FOLDER!\" >nul
)

echo   - Sincronizando bases de datos JSON y recursos...
for %%j in ("%~dp0*.json") do (
    copy /Y "%%j" "!ADDIN_FOLDER!\" >nul
)
if exist "%~dp0BricksPrompt.txt" (
    copy /Y "%~dp0BricksPrompt.txt" "!ADDIN_FOLDER!\" >nul 2>&1
)

echo   - Copiando familias RFA...
if exist "%~dp0Familias" (
    xcopy "%~dp0Familias\*.*" "!ADDIN_FOLDER!\Familias\" /Y /I /E /S >nul 2>&1
)
if exist "%~dp0FamiliasRiego" (
    xcopy "%~dp0FamiliasRiego\*.*" "!ADDIN_FOLDER!\FamiliasRiego\" /Y /I /E /S >nul 2>&1
)
if exist "%~dp0LadrilloProfile" (
    xcopy "%~dp0LadrilloProfile\*.*" "!ADDIN_FOLDER!\LadrilloProfile\" /Y /I /E /S >nul 2>&1
)
for %%r in ("%~dp0*.rfa") do (
    copy /Y "%%r" "!ADDIN_FOLDER!\" >nul 2>&1
)

echo   - Desbloqueando permisos de seguridad de Windows (Unblock-File)...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -LiteralPath '!ADDIN_FOLDER!' -Recurse -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue" >nul 2>&1

echo   - Reiniciando firma CodeSigning para activar ventana Always Load...
reg delete "HKCU\Software\Autodesk\Revit\Autodesk Revit %TARGET_REVIT%\CodeSigning" /f >nul 2>&1

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
echo  Ya puedes abrir Revit, confirmar "Always Load" (Cargar siempre)
echo  y utilizar la pestana ARCA REVIEW actualizada.
echo ============================================================
echo.
pause
