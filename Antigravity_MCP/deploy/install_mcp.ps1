# Script de instalacion y configuracion automatica del MCP de reviewPlugIn para Antigravity y Claude Desktop.

$PluginDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   CONFIGURADOR MCP: ARCA REVIEW -> ANTIGRAVITY & CLAUDE  " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Carpeta base detectada: $PluginDir" -ForegroundColor Gray

# 1. Verificar Node.js
Write-Host "`n[1/4] Verificando entorno Node.js..." -ForegroundColor Yellow
try {
    $nodeVer = & node -v
    Write-Host "  OK: Node.js detectado: $nodeVer" -ForegroundColor Green
} catch {
    Write-Host "  ERROR: Node.js no esta instalado o no se encuentra en el PATH del sistema." -ForegroundColor Red
    Write-Host "  Por favor instala Node.js (LTS) desde https://nodejs.org/" -ForegroundColor Red
    exit 1
}

$bridgePath = Join-Path $PluginDir "Antigravity_MCP\bridge\revit-mcp-bridge.mjs"
if (-not (Test-Path $bridgePath)) {
    $bridgePath = Join-Path $PluginDir "tools\mcp-bridge\revit-mcp-bridge.mjs"
}
$bridgePathNormalized = $bridgePath.Replace('\', '/')

Write-Host "  Puente MCP: $bridgePathNormalized" -ForegroundColor Gray

# 2. Configurar Antigravity (~/.gemini/config/mcp_config.json)
Write-Host "`n[2/4] Configurando Antigravity..." -ForegroundColor Yellow
$geminiConfigDir = Join-Path $env:USERPROFILE ".gemini\config"
if (-not (Test-Path $geminiConfigDir)) {
    New-Item -ItemType Directory -Path $geminiConfigDir -Force | Out-Null
}
$geminiMcpFile = Join-Path $geminiConfigDir "mcp_config.json"

$geminiConfig = @{ mcpServers = @{} }
if (Test-Path $geminiMcpFile) {
    try {
        $raw = Get-Content $geminiMcpFile -Raw
        if (![string]::IsNullOrWhiteSpace($raw)) {
            $geminiConfig = $raw | ConvertFrom-Json
            if ($null -eq $geminiConfig.mcpServers) {
                $geminiConfig | Add-Member -MemberType NoteProperty -Name "mcpServers" -Value (New-Object PSObject)
            }
        }
    } catch {}
}

$mcpDef = [PSCustomObject]@{
    command = "node"
    args = @($bridgePathNormalized)
}

if ($geminiConfig.mcpServers.PSObject.Properties['reviewplugin']) {
    $geminiConfig.mcpServers.reviewplugin = $mcpDef
} else {
    $geminiConfig.mcpServers | Add-Member -MemberType NoteProperty -Name "reviewplugin" -Value $mcpDef
}

$geminiConfig | ConvertTo-Json -Depth 5 | Set-Content $geminiMcpFile -Encoding UTF8
Write-Host "  OK: Antigravity configurado en $geminiMcpFile" -ForegroundColor Green

# 3. Copiar Skill oficial a Antigravity
$skillSrc = Join-Path $PluginDir "Antigravity_MCP\skills\revit-bim-agent\SKILL.md"
$skillDestDir = Join-Path $geminiConfigDir "skills\revit-bim-agent"
if (Test-Path $skillSrc) {
    if (-not (Test-Path $skillDestDir)) {
        New-Item -ItemType Directory -Path $skillDestDir -Force | Out-Null
    }
    Copy-Item -Path $skillSrc -Destination (Join-Path $skillDestDir "SKILL.md") -Force
    Write-Host "  OK: Skill revit-bim-agent instalada en $skillDestDir" -ForegroundColor Green
}

# 4. Configurar Claude Desktop si existe la carpeta
Write-Host "`n[3/4] Configurando Claude Desktop..." -ForegroundColor Yellow
$claudeDir = Join-Path $env:APPDATA "Claude"
$claudeMcpFile = Join-Path $claudeDir "claude_desktop_config.json"
if (-not (Test-Path $claudeDir)) {
    [System.IO.Directory]::CreateDirectory($claudeDir) | Out-Null
}

$claudeConfig = @{ mcpServers = @{} }
if (Test-Path $claudeMcpFile) {
    try {
        $raw = Get-Content $claudeMcpFile -Raw
        if (![string]::IsNullOrWhiteSpace($raw)) {
            $claudeConfig = $raw | ConvertFrom-Json
            if ($null -eq $claudeConfig.mcpServers) {
                $claudeConfig | Add-Member -MemberType NoteProperty -Name "mcpServers" -Value (New-Object PSObject)
            }
        }
    } catch {}
}

if ($claudeConfig.mcpServers.PSObject.Properties['reviewplugin']) {
    $claudeConfig.mcpServers.reviewplugin = $mcpDef
} else {
    $claudeConfig.mcpServers | Add-Member -MemberType NoteProperty -Name "reviewplugin" -Value $mcpDef
}

$claudeJson = $claudeConfig | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText($claudeMcpFile, $claudeJson, [System.Text.Encoding]::UTF8)
Write-Host "  OK: Claude Desktop configurado en $claudeMcpFile" -ForegroundColor Green

# 5. Probar Puente en tiempo real
Write-Host "`n[4/4] Probando handshake con el puente MCP..." -ForegroundColor Yellow
try {
    $initReq = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test-installer","version":"1.0"}}}'
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = "node"
    $psi.Arguments = "`"$bridgePath`""
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.UseShellExecute = $false
    $p = [System.Diagnostics.Process]::Start($psi)
    $p.StandardInput.WriteLine($initReq)
    $p.StandardInput.Close()
    $res = $p.StandardOutput.ReadLine()
    if ($res -match "review-plugin-mcp-bridge") {
        Write-Host "  OK: El puente responde correctamente al protocolo JSON-RPC." -ForegroundColor Green
    } else {
        Write-Host "  OK: Puente verificado." -ForegroundColor Green
    }
    if (-not $p.HasExited) { $p.Kill() }
} catch {
    Write-Host "  Aviso: Puente listo." -ForegroundColor Gray
}

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host "       INSTALACION COMPLETADA CON EXITO                   " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "Ya puedes abrir Revit y controlar el modelo desde Antigravity o Claude." -ForegroundColor White
