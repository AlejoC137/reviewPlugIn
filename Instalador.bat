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

:: -----------------------------------------------------------
:: Paso 0.5: Compilar el codigo automaticamente si hay SDK disponible
:: (Si el otro PC no tiene el SDK instalado, usa los binarios ya
::  compilados y sincronizados en Google Drive sin abortar)
:: -----------------------------------------------------------
if exist "%~dp0RevitToSupabasePlugin.csproj" (
    where dotnet >nul 2>&1
    if not errorlevel 1 (
        if exist "%~dp0obj" rmdir /s /q "%~dp0obj" >nul 2>&1
        if not "%DEST_CHOICE:~0,1%"=="3" (
            echo [Compilacion] Actualizando binario para Revit 2025 ^(net8.0-windows^)...
            dotnet build "%~dp0RevitToSupabasePlugin.csproj" -c Release -f net8.0-windows --nologo -v q
            if errorlevel 1 (
                echo   [AVISO] No se pudo compilar net8.0-windows en este equipo. Se usara el binario precompilado de Google Drive.
            ) else (
                echo   [OK] Binario net8.0-windows compilado exitosamente.
            )
        )
        if not "%DEST_CHOICE:~0,1%"=="2" (
            echo [Compilacion] Actualizando binario para Revit 2027 ^(net10.0-windows^)...
            dotnet build "%~dp0RevitToSupabasePlugin.csproj" -c Release -f net10.0-windows --nologo -v q
            if errorlevel 1 (
                echo   [AVISO] No se pudo compilar net10.0-windows en este equipo. Se usara el binario precompilado de Google Drive.
            ) else (
                echo   [OK] Binario net10.0-windows compilado exitosamente.
            )
        )
        echo ------------------------------------------------------------
    ) else (
        echo [INFO] SDK de .NET no detectado en este equipo.
        echo        Usando binarios precompilados sincronizados en Google Drive...
        echo ------------------------------------------------------------
    )
)

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

:: Resolver la DLL compilada segun la version de Revit objetivo
set "DLL_PATH="
set "BUILD_DIR="

if "%TARGET_REVIT%"=="2027" (
    if exist "%~dp0bin\Release\net10.0-windows\RevitToSupabasePlugin.dll" (
        set "DLL_PATH=%~dp0bin\Release\net10.0-windows\RevitToSupabasePlugin.dll"
        set "BUILD_DIR=%~dp0bin\Release\net10.0-windows\"
    ) else if exist "%~dp0bin\Debug\net10.0-windows\RevitToSupabasePlugin.dll" (
        set "DLL_PATH=%~dp0bin\Debug\net10.0-windows\RevitToSupabasePlugin.dll"
        set "BUILD_DIR=%~dp0bin\Debug\net10.0-windows\"
    ) else if exist "%~dp0bin\Release\net8.0-windows\RevitToSupabasePlugin.dll" (
        set "DLL_PATH=%~dp0bin\Release\net8.0-windows\RevitToSupabasePlugin.dll"
        set "BUILD_DIR=%~dp0bin\Release\net8.0-windows\"
    )
) else (
    if exist "%~dp0bin\Release\net8.0-windows\RevitToSupabasePlugin.dll" (
        set "DLL_PATH=%~dp0bin\Release\net8.0-windows\RevitToSupabasePlugin.dll"
        set "BUILD_DIR=%~dp0bin\Release\net8.0-windows\"
    ) else if exist "%~dp0bin\Debug\net8.0-windows\RevitToSupabasePlugin.dll" (
        set "DLL_PATH=%~dp0bin\Debug\net8.0-windows\RevitToSupabasePlugin.dll"
        set "BUILD_DIR=%~dp0bin\Debug\net8.0-windows\"
    ) else if exist "%~dp0bin\Release\net10.0-windows\RevitToSupabasePlugin.dll" (
        set "DLL_PATH=%~dp0bin\Release\net10.0-windows\RevitToSupabasePlugin.dll"
        set "BUILD_DIR=%~dp0bin\Release\net10.0-windows\"
    )
)

if not defined DLL_PATH (
    if exist "%~dp0RevitToSupabasePlugin.dll" (
        set "DLL_PATH=%~dp0RevitToSupabasePlugin.dll"
        set "BUILD_DIR=%~dp0"
    )
)

if not defined DLL_PATH (
    color 0C
    echo   [ERROR] No se encontro la DLL compilada para Revit %TARGET_REVIT%.
    exit /b 1
)

echo.
echo ------------------------------------------------------------
echo [Revit %TARGET_REVIT%] Iniciando copia de archivos...
echo   Origen: %DLL_PATH%
for %%I in ("%DLL_PATH%") do echo   Fecha/Tamano DLL: %%~tI ^- %%~zI bytes
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

:: Si existe copia previa en ProgramData, actualizarla tambien para evitar conflictos
if exist "%ProgramData%\Autodesk\Revit\Addins\%TARGET_REVIT%\RevitToSupabasePlugin.addin" (
    copy /Y "%ADDIN_PATH%" "%ProgramData%\Autodesk\Revit\Addins\%TARGET_REVIT%\" >nul 2>&1
    copy /Y "%DLL_PATH%" "%ProgramData%\Autodesk\Revit\Addins\%TARGET_REVIT%\" >nul 2>&1
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
if exist "%BUILD_DIR%*.deps.json" (
    copy /Y "%BUILD_DIR%*.deps.json" "%ADDIN_FOLDER%\" >nul 2>&1
)

echo   - Copiando iconos...
for %%f in ("%~dp0icon_*.png") do (
    copy /Y "%%f" "%ADDIN_FOLDER%\" >nul
)
if exist "%~dp0icon.png" (
    copy /Y "%~dp0icon.png" "%ADDIN_FOLDER%\" >nul
)

echo   - Sincronizando bases de datos JSON y recursos...
for %%j in ("%~dp0*.json") do (
    copy /Y "%%j" "%ADDIN_FOLDER%\" >nul
)
if exist "%~dp0BricksPrompt.txt" (
    copy /Y "%~dp0BricksPrompt.txt" "%ADDIN_FOLDER%\" >nul 2>&1
)

echo   - Copiando familias RFA...
if exist "%~dp0Familias" (
    xcopy "%~dp0Familias\*.*" "%ADDIN_FOLDER%\Familias\" /Y /I /E /S >nul 2>&1
)
if exist "%~dp0FamiliasRiego" (
    xcopy "%~dp0FamiliasRiego\*.*" "%ADDIN_FOLDER%\FamiliasRiego\" /Y /I /E /S >nul 2>&1
)
if exist "%~dp0LadrilloProfile" (
    xcopy "%~dp0LadrilloProfile\*.*" "%ADDIN_FOLDER%\LadrilloProfile\" /Y /I /E /S >nul 2>&1
)
for %%r in ("%~dp0*.rfa") do (
    copy /Y "%%r" "%ADDIN_FOLDER%\" >nul 2>&1
)

:: Reiniciar firma de carga en el registro de Revit para forzar la ventana "Always Load" al abrir Revit
reg delete "HKCU\Software\Autodesk\Revit\Autodesk Revit %TARGET_REVIT%\CodeSigning" /v "a1b2c3d4-e5f6-4a5b-8c9d-e0f1a2b3c4d5" /f >nul 2>&1
reg delete "HKCU\Software\Autodesk\Revit\Autodesk Revit %TARGET_REVIT%\CodeSigning" /v "A1B2C3D4-E5F6-4A5B-8C9D-E0F1A2b3c4d5" /f >nul 2>&1

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
echo  Ya puedes abrir Revit, confirmar "Always Load" y utilizar
echo  la pestana ARCA REVIEW con todos los cambios actualizados.
echo ============================================================
echo.
pause
