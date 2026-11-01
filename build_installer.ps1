# ==============================================================================
# Script de Creacion del Instalador .EXE Autonomo para ARCA REVIEW
# ==============================================================================
param(
    [string]$TargetRevitFramework = "net10.0-windows" # o net8.0-windows
)

$ErrorActionPreference = "Stop"
$rootDir = $PSScriptRoot
Set-Location $rootDir

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "   ARCA REVIEW - GENERADOR DEL INSTALADOR EJECUTABLE (.EXE)" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan

# 1. Compilar el plugin principal
Write-Host "`n[1/5] Compilando RevitToSupabasePlugin en Release ($TargetRevitFramework)..." -ForegroundColor Yellow
dotnet build "$rootDir\RevitToSupabasePlugin.csproj" -c Release -f $TargetRevitFramework
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Fallo la compilacion del plugin." -ForegroundColor Red
    exit 1
}

# 2. Preparar carpeta temporal de staging
$stagingDir = Join-Path $rootDir "tools\Installer\staging"
$resourcesDir = Join-Path $rootDir "tools\Installer\Resources"
$payloadZip = Join-Path $resourcesDir "payload.zip"

Write-Host "`n[2/5] Preparando los archivos a incluir en el instalador..." -ForegroundColor Yellow
if (Test-Path $stagingDir) {
    Remove-Item $stagingDir -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null
if (-not (Test-Path $resourcesDir)) {
    New-Item -ItemType Directory -Path $resourcesDir -Force | Out-Null
}

# Copiar Binarios compilados
$binDir = Join-Path $rootDir "bin\Release\$TargetRevitFramework"
Copy-Item (Join-Path $binDir "RevitToSupabasePlugin.dll") -Destination $stagingDir -Force
Copy-Item (Join-Path $binDir "Newtonsoft.Json.dll") -Destination $stagingDir -Force
if (Test-Path (Join-Path $binDir "RevitToSupabasePlugin.deps.json")) {
    Copy-Item (Join-Path $binDir "RevitToSupabasePlugin.deps.json") -Destination $stagingDir -Force
}

# Copiar Addin Manifest
Copy-Item (Join-Path $rootDir "RevitToSupabasePlugin.addin") -Destination $stagingDir -Force

# Copiar Iconos
Get-ChildItem -Path $rootDir -Filter "icon_*.png" | ForEach-Object {
    Copy-Item $_.FullName -Destination $stagingDir -Force
}

# Copiar Familias RFA de raiz
Get-ChildItem -Path $rootDir -Filter "*.rfa" | ForEach-Object {
    Copy-Item $_.FullName -Destination $stagingDir -Force
}

# Copiar Bases de Datos JSON y TXT
Get-ChildItem -Path $rootDir -Filter "*DB.json" | ForEach-Object {
    Copy-Item $_.FullName -Destination $stagingDir -Force
}
if (Test-Path (Join-Path $rootDir "BricksPrompt.txt")) {
    Copy-Item (Join-Path $rootDir "BricksPrompt.txt") -Destination $stagingDir -Force
}

# Copiar Carpetas de Familias
$foldersToInclude = @("Familias", "FamiliasRiego", "LadrilloProfile")
foreach ($folder in $foldersToInclude) {
    $src = Join-Path $rootDir $folder
    if (Test-Path $src) {
        Copy-Item $src -Destination (Join-Path $stagingDir $folder) -Recurse -Force
    }
}

Write-Host "   Archivos recolectados en staging:" -ForegroundColor Green
(Get-ChildItem $stagingDir).Count.ToString() + " elementos base listos." | Write-Host -ForegroundColor DarkGray

# 3. Empaquetar payload.zip
Write-Host "`n[3/5] Comprimiendo payload.zip para incrustar en el ejecutable..." -ForegroundColor Yellow
if (Test-Path $payloadZip) {
    Remove-Item $payloadZip -Force
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stagingDir, $payloadZip)
Write-Host "   Payload generado exitosamente ($([Math]::Round((Get-Item $payloadZip).Length / 1MB, 2)) MB)." -ForegroundColor Green

# 4. Compilar el Instalador .EXE Self-Contained (Single-File)
Write-Host "`n[4/5] Compilando el ejecutable autonomo Instalador_ARCA_REVIEW.exe..." -ForegroundColor Yellow
$distDir = Join-Path $rootDir "dist"
if (Test-Path $distDir) {
    Remove-Item $distDir -Recurse -Force
}
New-Item -ItemType Directory -Path $distDir -Force | Out-Null

$installerProj = Join-Path $rootDir "tools\Installer\ArcaReviewInstaller.csproj"

# Publicar como archivo unico auto-contenido (no requiere .NET instalado en la maquina del cliente)
dotnet publish $installerProj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $distDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Fallo la compilacion del instalador." -ForegroundColor Red
    exit 1
}

# Renombrar al nombre comercial
$rawExe = Join-Path $distDir "ArcaReviewInstaller.exe"
$finalExe = Join-Path $distDir "Instalador_ARCA_REVIEW.exe"
if (Test-Path $rawExe) {
    if (Test-Path $finalExe) { Remove-Item $finalExe -Force }
    Rename-Item -Path $rawExe -NewName "Instalador_ARCA_REVIEW.exe"
}

# Limpiar archivos .pdb del dist para dejar solo el .exe limpio
Get-ChildItem -Path $distDir -Filter "*.pdb" | Remove-Item -Force

# Limpiar staging
Remove-Item $stagingDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "`n[5/5] ¡LISTO!" -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "   INSTALADOR CREADO CON EXITO:" -ForegroundColor Cyan
Write-Host "   $finalExe" -ForegroundColor Yellow
Write-Host "   Tamano: $([Math]::Round((Get-Item $finalExe).Length / 1MB, 2)) MB" -ForegroundColor Yellow
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "Puedes enviar este archivo .exe a cualquier usuario para que lo instale con 1 clic." -ForegroundColor White
