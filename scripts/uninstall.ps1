#Requires -Version 5.1
<#
.SYNOPSIS
    Uninstall the Bimwright.Dwg plugin bundle.
#>
[CmdletBinding(SupportsShouldProcess)]
param()

& "$PSScriptRoot\install.ps1" -Uninstall @PSBoundParameters
