[CmdletBinding()]
param(
    [string]$CosmoteerBin = 'D:\SteamLibrary\steamapps\common\Cosmoteer\Bin',
    [string]$YamlModDir   = 'D:\SteamLibrary\steamapps\workshop\content\799600\3577650065',
    # The LOADED mod folder. The project deliberately lives OUTSIDE the Mods tree so that
    # bin/ and obj/ build artifacts are never scanned by the YAML mod loader
    # (they showed up as ~90 "Unknown libraries" when the project sat inside the mod).
    [string]$ModRoot      = 'C:\Users\Eryk\Saved Games\Cosmoteer\76561199102920901\Mods\ztx.biohazard'
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $here
foreach ($p in @((Join-Path $CosmoteerBin 'Cosmoteer.dll'),(Join-Path $CosmoteerBin 'HalflingCore.dll'),(Join-Path $YamlModDir '0Harmony.dll'))) {
    if (-not (Test-Path $p)) { throw "Required file not found: $p" }
}
if (-not (Test-Path $ModRoot)) { throw "Mod root not found: $ModRoot" }
Write-Host 'Building (dotnet build -c Release)...' -ForegroundColor Yellow
& dotnet build -c Release -p:CosmoteerBin="$CosmoteerBin" -p:YamlModDir="$YamlModDir"
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }
# README and LICENSE ship next to the DLL, as the ramming mod's do. Never locked, so they refresh
# even when the DLL copy below fails because the game is running.
Copy-Item -Force (Join-Path $here 'README.md') (Join-Path $ModRoot 'README.md')
Copy-Item -Force (Join-Path $here 'LICENSE')   (Join-Path $ModRoot 'LICENSE')
$dll = Join-Path $here 'bin\Release\net10.0\ZTX.BioCirculation.dll'
if (-not (Test-Path $dll)) { throw "Output DLL not found: $dll" }
try { Copy-Item -Force $dll (Join-Path $ModRoot 'ZTX.BioCirculation.dll'); Write-Host "Installed DLL to $ModRoot" -ForegroundColor Green }
catch { Write-Warning "DLL copy failed (is Cosmoteer running?). $_" }
