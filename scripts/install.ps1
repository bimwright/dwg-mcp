#Requires -Version 5.1
<#
.SYNOPSIS
    Install or uninstall the Bimwright.Dwg plugin bundle for AutoCAD 2022-2027.
.PARAMETER Version
    AutoCAD year to deploy. Defaults to 2024.
.PARAMETER SourceDir
    Path to built plugin binaries. Defaults to the selected version's Debug build output.
.PARAMETER Uninstall
    Remove the installed bundle.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet("2022", "2023", "2024", "2025", "2026", "2027")]
    [string]$Version = "2024",
    [string]$SourceDir,
    [switch]$Uninstall
)

$bundleName = "Bimwright.Dwg.bundle"
$targetRoot = "$env:APPDATA\Autodesk\ApplicationPlugins\$bundleName"
$projectSuffix = @{
    "2022" = "acad22"
    "2023" = "acad23"
    "2024" = "acad24"
    "2025" = "acad25"
    "2026" = "acad26"
    "2027" = "acad27"
}[$Version]
$targetFramework = @{
    "2022" = "net48"
    "2023" = "net48"
    "2024" = "net48"
    "2025" = "net8.0-windows"
    "2026" = "net8.0-windows"
    "2027" = "net10.0-windows"
}[$Version]

if ([string]::IsNullOrWhiteSpace($SourceDir)) {
    $SourceDir = "$PSScriptRoot\..\src\plugin-$projectSuffix\bin\Debug\$targetFramework"
}

if ($Uninstall) {
    if (Test-Path $targetRoot) {
        if ($PSCmdlet.ShouldProcess($targetRoot, "Remove bundle")) {
            Remove-Item $targetRoot -Recurse -Force
            Write-Host "Uninstalled: $targetRoot"
        }
    } else {
        Write-Host "Not installed."
    }
    return
}

if (-not (Test-Path $SourceDir)) {
    Write-Error "Source not found: $SourceDir. Build the plugin first."
    return
}

$contentsDir = "$targetRoot\Contents\$Version"
if ($PSCmdlet.ShouldProcess($contentsDir, "Deploy AutoCAD $Version plugin bundle")) {
    New-Item -ItemType Directory -Path $targetRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $contentsDir -Force | Out-Null
    $manifest = [xml](Get-Content "$PSScriptRoot\PackageContents.xml" -Raw)
    $appName = "Bimwright.Dwg.Plugin.Acad$($Version.Substring(2, 2))"
    foreach ($component in @($manifest.ApplicationPackage.Components)) {
        if ($component.ComponentEntry.AppName -ne $appName) {
            [void]$manifest.ApplicationPackage.RemoveChild($component)
        }
    }
    $manifest.Save("$targetRoot\PackageContents.xml")
    Copy-Item "$SourceDir\*" $contentsDir -Recurse -Force
    Write-Host "Installed AutoCAD $Version plugin to: $contentsDir"
    Write-Host "Restart AutoCAD to load the plugin."
}
