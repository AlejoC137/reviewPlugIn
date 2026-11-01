@echo off
title Generador del Instalador ARCA REVIEW
echo ========================================================
echo Iniciando compilacion y empaquetado del instalador...
echo ========================================================
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build_installer.ps1"
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Ocurrio un error durante la generacion. Revisa los mensajes arriba.
    pause
    exit /b %ERRORLEVEL%
)
echo.
echo Proceso finalizado con exito. El archivo .exe esta listo en la carpeta 'dist'.
pause
