#Requires -Version 5.1
<#
.SYNOPSIS
    Install or uninstall dwg-mcp (setup ZIP or local plugin build).
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet("2022", "2023", "2024", "2025", "2026", "2027")]
    [string]$Version = "2024",
    [string]$SourceDir,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$bundleName = "Bimwright.Dwg.bundle"
$targetRoot = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\$bundleName"

$isSetupZip = (Test-Path (Join-Path $PSScriptRoot 'manifest.json')) -and (Test-Path (Join-Path $PSScriptRoot 'bundle'))
$setupVersion = 'dev'
if (Test-Path (Join-Path $PSScriptRoot 'manifest.json')) {
    $setupVersion = ((Get-Content -Raw (Join-Path $PSScriptRoot 'manifest.json')) | ConvertFrom-Json).version
}
$serverInstallRoot = Join-Path $env:LOCALAPPDATA "Bimwright\Dwg\server\$setupVersion"

if ($Uninstall) {
    if ($PSCmdlet.ShouldProcess($targetRoot, "Remove AutoCAD bundle")) {
        if (Test-Path $targetRoot) { Remove-Item $targetRoot -Recurse -Force }
        Write-Host "Removed plugin bundle (if present): $targetRoot"
    }
    $serverParent = Join-Path $env:LOCALAPPDATA "Bimwright\Dwg\server"
    if ($PSCmdlet.ShouldProcess($serverParent, "Remove installed servers")) {
        if (Test-Path $serverParent) { Remove-Item $serverParent -Recurse -Force }
        Write-Host "Removed server installs (if present): $serverParent"
    }
    return
}

if ($isSetupZip) {
    $bundleSrc = Join-Path $PSScriptRoot 'bundle'
    if ($PSCmdlet.ShouldProcess($targetRoot, "Install dwg-mcp plugin bundle from setup ZIP")) {
        if (Test-Path $targetRoot) { Remove-Item $targetRoot -Recurse -Force }
        Copy-Item $bundleSrc $targetRoot -Recurse -Force
        Write-Host "Installed plugin bundle: $targetRoot"
    }
    $serverSrc = Join-Path $PSScriptRoot 'server\dwg-mcp.exe'
    if (Test-Path $serverSrc) {
        if ($PSCmdlet.ShouldProcess($serverInstallRoot, "Install dwg-mcp.exe")) {
            New-Item -ItemType Directory -Path $serverInstallRoot -Force | Out-Null
            Copy-Item (Join-Path $PSScriptRoot 'server\*') $serverInstallRoot -Force
            $exe = Join-Path $serverInstallRoot 'dwg-mcp.exe'
            Write-Host "Installed server: $exe"
            Write-Host "Wire your MCP client with command: $exe"
        }
    }
    Write-Host "Restart AutoCAD to load the plugin. Packed years are listed in manifest.json."
    return
}

# Repo / local-build path (single year from bin)
$projectSuffix = @{
    "2022" = "acad22"; "2023" = "acad23"; "2024" = "acad24"
    "2025" = "acad25"; "2026" = "acad26"; "2027" = "acad27"
}[$Version]
$targetFramework = @{
    "2022" = "net48"; "2023" = "net48"; "2024" = "net48"
    "2025" = "net8.0-windows"; "2026" = "net8.0-windows"; "2027" = "net10.0-windows"
}[$Version]

if ([string]::IsNullOrWhiteSpace($SourceDir)) {
    $SourceDir = Join-Path $PSScriptRoot "..\src\plugin-$projectSuffix\bin\Debug\$targetFramework"
}

if (-not (Test-Path $SourceDir)) {
    Write-Error "Source not found: $SourceDir. Build the plugin first or use the GitHub Release ZIP."
    return
}

$contentsDir = Join-Path $targetRoot "Contents\$Version"
if ($PSCmdlet.ShouldProcess($contentsDir, "Deploy AutoCAD $Version plugin bundle")) {
    New-Item -ItemType Directory -Path $targetRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $contentsDir -Force | Out-Null
    $manifest = [xml](Get-Content (Join-Path $PSScriptRoot 'PackageContents.xml') -Raw)
    $appName = "Bimwright.Dwg.Plugin.Acad$($Version.Substring(2, 2))"
    foreach ($component in @($manifest.ApplicationPackage.Components)) {
        if ($component.ComponentEntry.AppName -ne $appName) {
            [void]$manifest.ApplicationPackage.RemoveChild($component)
        }
    }
    $manifest.Save((Join-Path $targetRoot 'PackageContents.xml'))
    Copy-Item (Join-Path $SourceDir '*') $contentsDir -Recurse -Force
    Write-Host "Installed AutoCAD $Version plugin to: $contentsDir"
    Write-Host "Restart AutoCAD to load the plugin."
}
