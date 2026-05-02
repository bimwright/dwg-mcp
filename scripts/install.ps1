#Requires -Version 5.1
<#
.SYNOPSIS
    Install or uninstall the Bimwright.Dwg plugin bundle for AutoCAD 2024.
.PARAMETER SourceDir
    Path to built plugin binaries. Defaults to the Debug build output.
.PARAMETER Uninstall
    Remove the installed bundle.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$SourceDir = "$PSScriptRoot\..\src\plugin-acad24\bin\Debug\net48",
    [switch]$Uninstall
)

$bundleName = "Bimwright.Dwg.bundle"
$targetRoot = "$env:APPDATA\Autodesk\ApplicationPlugins\$bundleName"

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

$contentsDir = "$targetRoot\Contents"
if ($PSCmdlet.ShouldProcess($contentsDir, "Deploy plugin bundle")) {
    New-Item -ItemType Directory -Path $contentsDir -Force | Out-Null
    Copy-Item "$PSScriptRoot\PackageContents.xml" "$targetRoot\PackageContents.xml" -Force
    Copy-Item "$SourceDir\*" $contentsDir -Recurse -Force
    Write-Host "Installed to: $targetRoot"
    Write-Host "Restart AutoCAD to load the plugin."
}
