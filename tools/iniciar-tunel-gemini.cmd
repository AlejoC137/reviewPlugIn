@echo off
echo ========================================================
echo   INICIANDO TUNEL HTTPS MCP PARA GOOGLE GEMINI (REVIT)
echo ========================================================
echo.
echo Recuerda tener Autodesk Revit abierto con reviewPlugIn.
echo Copia la URL HTTPS que termine en trycloudflare.com
echo y agregale /mcp/ para pegarla en Gemini.
echo.
"%~dp0cloudflared\cloudflared.exe" tunnel --http-host-header "localhost:8080" --url http://localhost:8080
pause
