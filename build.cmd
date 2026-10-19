@echo off
if exist "%USERPROFILE%\.dotnet\dotnet.exe" (
    "%USERPROFILE%\.dotnet\dotnet.exe" build "%~dp0RevitToSupabasePlugin.csproj" -c Release %*
) else (
    dotnet build "%~dp0RevitToSupabasePlugin.csproj" -c Release %*
)
