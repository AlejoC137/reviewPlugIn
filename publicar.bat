@echo off
setlocal
title Publicar cambios a Git (Compilar Release + Commit + Push)
color 0A

echo ============================================================
echo         PUBLICADOR AUTOMATICO - ARCA REVIEW PLUGIN
echo ============================================================
echo.
echo 1. Compilando en modo Release (net8.0 y net10.0)...
call "%~dp0build.cmd"
if %ERRORLEVEL% neq 0 (
    color 0C
    echo [ERROR] La compilacion fallo. Por favor corrige los errores antes de publicar.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo 2. Preparando archivos para Git...
git add .

set /p MSG="Ingresa la descripcion de los cambios (o presiona ENTER para 'Actualizacion'): "
if "%MSG%"=="" set "MSG=Actualizacion de plugin y binarios"

echo.
echo 3. Creando commit...
git commit -m "%MSG%"

echo.
echo 4. Enviando a GitHub (git push)...
git push

if %ERRORLEVEL% equ 0 (
    echo.
    echo ============================================================
    echo  [EXITO] Cambios y DLLs actualizadas publicados en GitHub!
    echo ============================================================
) else (
    color 0C
    echo.
    echo [ALERTA] Ocurrio un problema al hacer git push. Verifica tu conexion o credenciales.
)

echo.
pause
