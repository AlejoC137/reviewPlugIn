@echo off
setlocal
cd /d "%~dp0"
echo ============================================================
echo   PROBANDO CONEXION EN VIVO CON AUTODESK REVIT (MCP)
echo ============================================================
echo.
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$call = '{\"jsonrpc\":\"2.0\",\"id\":100,\"method\":\"tools/call\",\"params\":{\"name\":\"revit_get_active_session\",\"arguments\":{}}}'; " ^
  "$bridge = Join-Path (Split-Path -Parent $PSScriptRoot) 'bridge\revit-mcp-bridge.mjs'; " ^
  "Write-Host 'Enviando revit_get_active_session a Revit...' -ForegroundColor Yellow; " ^
  "$res = $call | node $bridge; " ^
  "Write-Host $res -ForegroundColor Cyan;"
echo.
pause
