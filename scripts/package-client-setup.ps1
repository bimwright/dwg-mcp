#Requires -Version 5.1
<#
.SYNOPSIS
  Build the end-user dwg-mcp setup ZIP (self-contained server + AutoCAD plugins that compile here).
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Config = 'Release',
    [string]$RepoRoot,
    [string]$Version,
    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) { $RepoRoot = Split-Path -Parent $PSScriptRoot }
$RepoRoot = (Resolve-Path $RepoRoot).Path
if (-not $OutputDir) { $OutputDir = Join-Path $RepoRoot 'build\client-setup' }

if (-not $Version) {
    $serverJsonPath = Join-Path $RepoRoot 'server.json'
    $Version = ((Get-Content -Raw -Path $serverJsonPath) | ConvertFrom-Json).version
}
if (-not $Version) { throw 'Pass -Version or set server.json version.' }

$displayVersion = if ($Version.StartsWith('v')) { $Version } else { "v$Version" }
$stageRoot = Join-Path $OutputDir 'stage'
$serverStage = Join-Path $stageRoot 'server'
$bundleStage = Join-Path $stageRoot 'bundle'
$contentsStage = Join-Path $bundleStage 'Contents'

if (Test-Path $stageRoot) { Remove-Item $stageRoot -Recurse -Force }
New-Item -ItemType Directory -Path $serverStage, $contentsStage -Force | Out-Null

Write-Host "=== dwg-mcp package-client-setup ($displayVersion) ==="

$serverProject = Join-Path $RepoRoot 'src\server\Bimwright.Dwg.Server.csproj'
& dotnet publish $serverProject -c $Config -r win-x64 --self-contained true /p:PublishSingleFile=true -o $serverStage
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $LASTEXITCODE" }

$published = Join-Path $serverStage 'Bimwright.Dwg.Server.exe'
$friendly = Join-Path $serverStage 'dwg-mcp.exe'
if (-not (Test-Path $published)) { throw "Missing $published" }
Move-Item $published $friendly -Force

$years = @(
    @{ Year = 2022; Suffix = '22'; Tfm = 'net48' }
    @{ Year = 2023; Suffix = '23'; Tfm = 'net48' }
    @{ Year = 2024; Suffix = '24'; Tfm = 'net48' }
    @{ Year = 2025; Suffix = '25'; Tfm = 'net8.0-windows' }
    @{ Year = 2026; Suffix = '26'; Tfm = 'net8.0-windows' }
    @{ Year = 2027; Suffix = '27'; Tfm = 'net10.0-windows' }
)

$packedYears = @()
foreach ($y in $years) {
    $csproj = Join-Path $RepoRoot ("src\plugin-acad{0}\Bimwright.Dwg.Plugin.Acad{0}.csproj" -f $y.Suffix)
    Write-Host "[plugin] AutoCAD $($y.Year)"
    & dotnet build $csproj -c $Config --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Skipping AutoCAD $($y.Year) (SDK/refs missing or build failed)."
        continue
    }
    $outDir = Join-Path $RepoRoot ("src\plugin-acad{0}\bin\{1}\{2}" -f $y.Suffix, $Config, $y.Tfm)
    $dll = Join-Path $outDir ("Bimwright.Dwg.Plugin.Acad{0}.dll" -f $y.Suffix)
    if (-not (Test-Path $dll)) { throw "Built but missing $dll" }
    $dest = Join-Path $contentsStage "$($y.Year)"
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    Get-ChildItem $outDir -File | Where-Object {
        $_.Name -notmatch '^(accoremgd|acdbmgd|acmgd)\.dll$'
    } | Copy-Item -Destination $dest -Force
    if (Test-Path (Join-Path $outDir 'Fonts')) {
        Copy-Item (Join-Path $outDir 'Fonts') (Join-Path $dest 'Fonts') -Recurse -Force
    }
    $packedYears += $y.Year
}

if ($packedYears.Count -eq 0) { throw 'No AutoCAD plugin years compiled. Cannot ship an empty ZIP.' }

$templatePath = Join-Path $RepoRoot 'scripts\PackageContents.xml'
[xml]$manifestXml = Get-Content -Raw $templatePath
$manifestXml.ApplicationPackage.AppVersion = $Version
$toRemove = @()
foreach ($comp in @($manifestXml.ApplicationPackage.Components)) {
    $mod = [string]$comp.ComponentEntry.ModuleName
    $keep = $false
    foreach ($yr in $packedYears) {
        if ($mod -match "/$yr/") { $keep = $true; break }
    }
    if (-not $keep) { $toRemove += $comp }
}
foreach ($comp in $toRemove) { [void]$manifestXml.ApplicationPackage.RemoveChild($comp) }
$manifestXml.Save((Join-Path $bundleStage 'PackageContents.xml'))

Copy-Item (Join-Path $RepoRoot 'scripts\install.ps1') (Join-Path $stageRoot 'install.ps1') -Force
Copy-Item (Join-Path $RepoRoot 'scripts\uninstall.ps1') (Join-Path $stageRoot 'uninstall.ps1') -Force

function Get-Rel([string]$Root, [string]$Path) {
    return $Path.Substring($Root.Length).TrimStart('\', '/') -replace '\\', '/'
}
function Get-Sha256Lower([string]$Path) {
    return ((Get-FileHash -Algorithm SHA256 -Path $Path).Hash).ToLowerInvariant()
}

$commit = ''
try { $commit = (& git -C $RepoRoot rev-parse HEAD).Trim() } catch { }

$files = @()
foreach ($f in @(Get-ChildItem $stageRoot -File -Recurse | Sort-Object FullName)) {
    $files += [ordered]@{ path = Get-Rel $stageRoot $f.FullName; sha256 = Get-Sha256Lower $f.FullName; bytes = $f.Length }
}

$manifest = [ordered]@{
    name = 'DwgMcp.Setup'
    version = $Version
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    commit = $commit
    platform = 'win-x64'
    packedAutocadYears = @($packedYears)
    supportedAutocadYears = @(2022, 2023, 2024, 2025, 2026, 2027)
    server = [ordered]@{ command = 'server/dwg-mcp.exe'; selfContained = $true; requiresDotnet = $false }
    files = $files
}
$manifest | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $stageRoot 'manifest.json') -Encoding UTF8

$setupZip = Join-Path $OutputDir ("DwgMcp.Setup-{0}-win-x64.zip" -f $displayVersion)
if (Test-Path $setupZip) { Remove-Item $setupZip -Force }
Compress-Archive -Path (Join-Path $stageRoot '*') -DestinationPath $setupZip -Force
Write-Host "Output : $setupZip"
Write-Host "Years  : $($packedYears -join ', ')"
