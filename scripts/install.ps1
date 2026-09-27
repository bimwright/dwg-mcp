#Requires -Version 5.1
<#
.SYNOPSIS
  Install, update or uninstall the dwg-mcp AutoCAD plugin bundle and MCP server.

.DESCRIPTION
  In a client setup ZIP, this script installs:
    - the plugin bundle (bundle/) into
      %APPDATA%\Autodesk\ApplicationPlugins\Bimwright.Dwg.bundle for every
      installed AutoCAD 2022-2027 that the package ships (a year counts when
      its acad.exe exists and the package has a Contents\<year> payload)
    - the self-contained MCP server from server/ at the fixed path
      %LOCALAPPDATA%\Bimwright\Dwg\server\current\dwg-mcp.exe
    - a default %LOCALAPPDATA%\Bimwright\Dwg\dwgmcp.config.json with
      toolsets=["all"] (skipped when the file already sets toolsets)

  It then wires every detected MCP client (-Client <names> limits this,
  -Client none skips it). Config edits are minimal text edits (JSONC comments
  survive) with a <config>.bak backup first; custom launchers and legacy
  bimwright-dwg* entries are reported but never replaced. Updating keeps the
  server path, so wired clients only need a restart.

  Every replacement is recorded and rolled back on error. Other bundles
  carrying dwg-mcp's ProductCode (Bimwright-era copies) are removed; a
  machine-wide copy under %ProgramData% blocks the install. The installed
  bundle is verified against the package and the server is started once
  with --help.

  From the repo (no bundle/ beside this script) it deploys
  src\plugin-acad<YY>\bin\<Config>\<tfm> for the selected -Years/-Version.

.PARAMETER SourceDir
  Setup root or plugin source directory. Defaults to the current setup root
  when bundle/ or server/ exists beside this script.

.PARAMETER Uninstall
  Remove the Bimwright.Dwg plugin bundle (and stale bundles with the same
  ProductCode). The server and user data stay; use uninstall-all.ps1 for a
  full removal.

.PARAMETER Client
  MCP clients to wire after installing. Omitted: every detected client
  (same as 'auto'/'all'). A comma list of client names (claude, codex,
  cursor, vscode, ...) wires only those; 'none' leaves client configs
  untouched. Every edit is previewed under -WhatIf and backed up to
  <config>.bak first. With -Uninstall, removes the dwg-mcp entry from the
  named clients; without -Client, uninstall leaves client configs alone.

.PARAMETER PruneOldServers
  Remove legacy version-named server copies (for example 1.0.0\) beside
  current\ after a successful install. Repoint clients that still use them
  first.

.EXAMPLE
  pwsh .\install.ps1 -WhatIf
  pwsh .\install.ps1                        # bundle, server, every detected MCP client
  pwsh .\install.ps1 -Client cursor,claude  # wire specific clients only
  pwsh .\install.ps1 -Client none           # leave client configs untouched
  pwsh .\install.ps1 -Years 2024            # repo build: deploy acad24 bin output
  pwsh .\install.ps1 -Uninstall -Client cursor
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$SourceDir,
    [switch]$Uninstall,
    [ValidateRange(2022, 2027)][int[]]$Years,
    [ValidateSet("2022", "2023", "2024", "2025", "2026", "2027")]
    [string]$Version,   # legacy single-year alias for -Years
    [ValidateSet('Debug', 'Release')][string]$Config = 'Debug',
    [ValidateSet('none', 'auto', 'all',
        'claude', 'claude-desktop', 'codex', 'grok',
        'cursor', 'cline', 'gemini', 'antigravity', 'devin', 'lmstudio',
        'kiro', 'qwen', 'windsurf', 'opencode', 'kilo', 'vscode', 'kun',
        'zed', 'cherry-studio')]
    [string[]]$Client = @(),
    [string]$ServerInstallRoot,
    [switch]$PruneOldServers
)

$ErrorActionPreference = 'Stop'

$bundleName = 'Bimwright.Dwg.bundle'
$bundleTargetRoot = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\$bundleName"
$machineBundleTarget = Join-Path $env:ProgramData "Autodesk\ApplicationPlugins\$bundleName"
# Product identity: every Bimwright.Dwg bundle carries this ProductCode.
$DwgMcpProductCode = '{8A3F7B2D-1C4E-4D5F-9A6B-7E8C9D0F1A2B}'

# AutoCAD release series <-> calendar year (same map PackageContents.xml uses).
$YearSeries = @{
    2022 = 'R24.1'; 2023 = 'R24.2'; 2024 = 'R24.3'
    2025 = 'R25.0'; 2026 = 'R25.1'; 2027 = 'R26.0'
}
$YearTfm = @{
    2022 = 'net48'; 2023 = 'net48'; 2024 = 'net48'
    2025 = 'net8.0-windows'; 2026 = 'net8.0-windows'; 2027 = 'net10.0-windows'
}

$hasSetupLayout = $false
if (-not $SourceDir) {
    $hasSetupLayout = (Test-Path (Join-Path $PSScriptRoot 'bundle')) -or (Test-Path (Join-Path $PSScriptRoot 'server'))
    if ($hasSetupLayout) {
        $SourceDir = $PSScriptRoot
    }
}
if ($SourceDir -and (Test-Path $SourceDir)) {
    $SourceDir = (Resolve-Path $SourceDir).Path
}
if ($SourceDir) {
    $hasSetupLayout = (Test-Path (Join-Path $SourceDir 'bundle')) -or (Test-Path (Join-Path $SourceDir 'server'))
}

$bundleSourceDir = if ($SourceDir -and (Test-Path (Join-Path $SourceDir 'bundle'))) {
    Join-Path $SourceDir 'bundle'
} else {
    $null
}
$serverSourceDir = if ($SourceDir -and (Test-Path (Join-Path $SourceDir 'server'))) {
    Join-Path $SourceDir 'server'
} else {
    $null
}

$manifestPath = if ($SourceDir) { Join-Path $SourceDir 'manifest.json' } else { $null }
$manifest = $null
$setupVersion = 'dev'
if ($manifestPath -and (Test-Path $manifestPath)) {
    try {
        $manifest = Get-Content -Raw -Path $manifestPath | ConvertFrom-Json
        if ($manifest.version) { $setupVersion = [string]$manifest.version }
    } catch {
        throw ("[setup] could not parse manifest.json: {0}" -f $_.Exception.Message)
    }
}
if ($setupVersion -notmatch '^v?\d+\.\d+\.\d+(?:[-+][A-Za-z0-9.-]+)?$' -and $setupVersion -ne 'dev') {
    throw '[setup] Invalid version in manifest.json.'
}

# Fixed path for every version: MCP clients are configured once and an update
# only replaces the files behind it.
if (-not $ServerInstallRoot) {
    $ServerInstallRoot = Join-Path $env:LOCALAPPDATA 'Bimwright\Dwg\server\current'
}

# A year counts only when acad.exe exists: registry keys can survive uninstalls.
function Get-InstalledAutoCadYears([string]$RegistryRoot = 'HKLM:\SOFTWARE\Autodesk\AutoCAD', [string]$ProgramFilesRoot = $env:ProgramFiles) {
    $detected = @()
    foreach ($year in 2022..2027) {
        $candidates = @()
        $seriesKey = Join-Path $RegistryRoot $script:YearSeries[$year]
        if (Test-Path -LiteralPath $seriesKey) {
            foreach ($sub in Get-ChildItem -LiteralPath $seriesKey -ErrorAction SilentlyContinue) {
                $location = (Get-ItemProperty -LiteralPath $sub.PSPath -ErrorAction SilentlyContinue).AcadLocation
                if ($location) { $candidates += (Join-Path $location 'acad.exe') }
            }
        }
        if ($ProgramFilesRoot) { $candidates += (Join-Path $ProgramFilesRoot "Autodesk\AutoCAD $year\acad.exe") }
        if (@($candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }).Count) { $detected += $year }
    }
    return $detected
}

# --- bundle manifest (PackageContents.xml) -------------------------------

# Unreadable or foreign manifests belong to someone else: return $null, never throw.
function Read-BundleManifest([string]$BundleDir) {
    $xmlPath = Join-Path $BundleDir 'PackageContents.xml'
    if (-not (Test-Path -LiteralPath $xmlPath -PathType Leaf)) { return $null }
    try { [xml]$xml = [IO.File]::ReadAllText($xmlPath) } catch { return $null }
    $code = $null
    try { $code = [string]$xml.ApplicationPackage.ProductCode } catch { return $null }
    $modules = @()
    try {
        foreach ($entry in @($xml.ApplicationPackage.Components.ComponentEntry)) {
            $mod = [string]$entry.ModuleName
            if (-not $mod) { continue }
            if ($mod -match '(\d{4})[\\/][^\\/]+\.dll') { $modules += [int]$Matches[1] }
        }
    } catch { return $null }
    return [pscustomobject]@{
        Path = $xmlPath
        ProductCode = if ($code) { $code.Trim().Trim('{', '}').ToLowerInvariant() } else { $null }
        Years = $modules
    }
}

# ProductCode-shaped check: our GUID regardless of brace/case formatting.
function Test-DwgMcpBundle([string]$BundleDir) {
    $m = Read-BundleManifest $BundleDir
    return ($null -ne $m -and $m.ProductCode -eq $DwgMcpProductCode.Trim('{}').ToLowerInvariant())
}

# Other bundles carrying our ProductCode (Bimwright-era copies, stray
# duplicates). Two bundles with one ProductCode are undefined for AutoCAD.
function Get-DuplicateBundleItems([string]$KeepBundle) {
    $roots = @((Split-Path -Parent $bundleTargetRoot))
    $found = @()
    $keep = [IO.Path]::GetFullPath($KeepBundle).TrimEnd('\')
    foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root -PathType Container)) { continue }
        foreach ($dir in Get-ChildItem -LiteralPath $root -Directory -Filter '*.bundle' -ErrorAction SilentlyContinue) {
            if ([IO.Path]::GetFullPath($dir.FullName).TrimEnd('\') -eq $keep) { continue }
            if (Test-DwgMcpBundle $dir.FullName) { $found += $dir.FullName }
        }
    }
    return $found
}

# A machine-wide bundle with the same ProductCode would shadow or be shadowed
# by the per-user one, and a per-user installer cannot remove it.
function Find-MachineWideDuplicates {
    $found = @()
    $machineRoot = Split-Path -Parent $machineBundleTarget
    if (Test-Path -LiteralPath $machineRoot -PathType Container) {
        foreach ($dir in Get-ChildItem -LiteralPath $machineRoot -Directory -Filter '*.bundle' -ErrorAction SilentlyContinue) {
            if (Test-DwgMcpBundle $dir.FullName) { $found += $dir.FullName }
        }
    }
    return $found
}

# Write a PackageContents.xml covering $coverYears to $OutPath, from a
# template (the package's own manifest when present, else the repo's
# scripts\PackageContents.xml). XmlWriter to file keeps the utf-8
# declaration honest - StringWriter would mislabel it utf-16.
function Save-BundleManifest([string]$OutPath, [int[]]$CoverYears, [string]$TemplatePath) {
    [xml]$xml = Get-Content -LiteralPath $TemplatePath -Raw
    $toRemove = @()
    foreach ($comp in @($xml.ApplicationPackage.Components)) {
        $mod = [string]$comp.ComponentEntry.ModuleName
        $year = 0
        if ($mod -match '(\d{4})[\\/][^\\/]+\.dll') { $year = [int]$Matches[1] }
        if ($CoverYears -notcontains $year) { $toRemove += $comp }
    }
    foreach ($comp in $toRemove) { [void]$xml.ApplicationPackage.RemoveChild($comp) }
    $settings = New-Object Xml.XmlWriterSettings
    $settings.Encoding = New-Object Text.UTF8Encoding($false)
    $writer = [Xml.XmlWriter]::Create($OutPath, $settings)
    try { $xml.Save($writer) } finally { $writer.Dispose() }
}

# --- rollback transaction -------------------------------------------------

# Every backup is beside its target. Never delete a caller-supplied directory
# unless it is a recorded target of this transaction or our unique staging area.
function Remove-InstallPath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    $allowed = @($script:installStage)
    foreach ($change in $script:installChanges) { $allowed += $change.Path; $allowed += $change.Backup }
    if ($full -notin $allowed -or $full.TrimEnd('\') -eq [IO.Path]::GetPathRoot($full).TrimEnd('\')) {
        throw "Refusing cleanup outside this install transaction: $full"
    }
    if (Test-Path -LiteralPath $full) { Remove-Item -LiteralPath $full -Recurse -Force }
}

function Set-InstallPath([string]$Source, [string]$Destination) {
    $full = [IO.Path]::GetFullPath($Destination)
    if ($full.TrimEnd('\') -eq [IO.Path]::GetPathRoot($full).TrimEnd('\')) { throw 'Cannot install into a drive root.' }
    $parent = Split-Path -Parent $full
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    $backup = $null
    if (Test-Path -LiteralPath $full) {
        $backup = $full + '.dwgmcp-rollback-' + [guid]::NewGuid().ToString('N')
        Move-Item -LiteralPath $full -Destination $backup
    }
    $script:installChanges.Add([pscustomobject]@{Path=$full;Backup=$backup})
    Move-Item -LiteralPath $Source -Destination $full
}

function Move-ToRollback([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    $backup = $full + '.dwgmcp-rollback-' + [guid]::NewGuid().ToString('N')
    Move-Item -LiteralPath $full -Destination $backup
    $script:installChanges.Add([pscustomobject]@{Path=$full;Backup=$backup})
}

function Undo-InstallChanges {
    $failures = @()
    for ($i = $script:installChanges.Count - 1; $i -ge 0; $i--) {
        $change = $script:installChanges[$i]
        try {
            Remove-InstallPath $change.Path
            if ($change.Backup) { Move-Item -LiteralPath $change.Backup -Destination $change.Path }
        } catch { $failures += "$($change.Path): $_ (backup: $($change.Backup))" }
    }
    if ($failures.Count) { throw ("Rollback incomplete; retain backup files and restore manually:`n" + ($failures -join "`n")) }
}

function Assert-AutoCadClosed {
    if (@(Get-Process -Name acad -ErrorAction SilentlyContinue).Count) {
        throw 'AutoCAD running. Close every AutoCAD window before installing or uninstalling plugins; no files have been replaced.'
    }
}

# --- package validation ---------------------------------------------------

function Assert-SetupManifest([string]$Root, $Manifest) {
    if (-not $Manifest) { return } # Repo/dev layout has no manifest.
    if (-not $Manifest.files) { throw 'Setup manifest has no file checksums.' }
    $prefix = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    foreach ($file in $Manifest.files) {
        $path = [IO.Path]::GetFullPath((Join-Path $Root $file.path))
        if (-not $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Invalid manifest path: $($file.path)" }
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Setup file missing: $($file.path)" }
        # Get-FileHash's provider reads honor inherited WhatIf in Windows
        # PowerShell 5.1. Hash directly so previews still validate actual bytes.
        $stream = [IO.File]::OpenRead($path)
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
        finally { $sha.Dispose(); $stream.Dispose() }
        if ($hash -ne $file.sha256) {
            throw "Setup checksum failed: $($file.path). Download and extract the setup ZIP again."
        }
    }
}

function Get-StreamSha256($Stream) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($Stream)).Replace('-', '') } finally { $sha.Dispose() }
}

# The setup bundle must carry the ProductCode and a Contents\<year>\ payload
# with the plugin DLL for every requested year.
function Assert-BundlePackage([string]$BundleDir, [int[]]$InstallYears) {
    if (-not $BundleDir -or -not (Test-Path -LiteralPath $BundleDir -PathType Container)) {
        throw 'Setup bundle directory is missing (expected bundle\ beside install.ps1).'
    }
    if (-not (Test-DwgMcpBundle $BundleDir)) {
        throw "Setup bundle at $BundleDir has no PackageContents.xml with dwg-mcp's ProductCode."
    }
    foreach ($year in $InstallYears) {
        $dll = Join-Path $BundleDir ("Contents\{0}\Bimwright.Dwg.Plugin.Acad{1}.dll" -f $year, "{0:D2}" -f ($year - 2000))
        if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) {
            throw "Missing plugin payload for AutoCAD ${year}: $dll"
        }
    }
}

# A repo build deploys src\plugin-acad<YY>\bin\<Config>\<tfm>; it must contain
# the plugin DLL.
function Assert-RepoPayload([string]$RepoRoot, [int]$Year, [string]$Configuration) {
    $tfm = $script:YearTfm[$Year]
    $dir = Join-Path $RepoRoot ("src\plugin-acad{0}\bin\{1}\{2}" -f ("{0:D2}" -f ($Year - 2000)), $Configuration, $tfm)
    $dll = Join-Path $dir ("Bimwright.Dwg.Plugin.Acad{0}.dll" -f ("{0:D2}" -f ($Year - 2000)))
    if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) {
        throw "Build output missing for AutoCAD ${Year}: $dll. Run: dotnet build src\plugin-acad$("{0:D2}" -f ($Year - 2000)) -c $Configuration"
    }
    return $dir
}

# The installed bundle must carry the ProductCode, and every requested year
# must land its DLL; package installs also hash-verify against the payload.
function Assert-InstalledPlugin([int[]]$InstallYears, [string]$BundleDir) {
    $prefix = 'Add-in verification failed:'
    if (-not (Test-DwgMcpBundle $BundleDir)) {
        throw "$prefix expected PackageContents.xml with dwg-mcp's ProductCode under $BundleDir"
    }
    foreach ($year in $InstallYears) {
        $dll = Join-Path $BundleDir ("Contents\{0}\Bimwright.Dwg.Plugin.Acad{1}.dll" -f $year, "{0:D2}" -f ($year - 2000))
        if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) { throw "$prefix missing file $dll" }
        if ($script:bundleSourceDir -and (Test-Path -LiteralPath (Join-Path $script:bundleSourceDir "Contents\$year"))) {
            $expectedStream = [IO.File]::OpenRead((Join-Path $script:bundleSourceDir "Contents\$year\Bimwright.Dwg.Plugin.Acad$("{0:D2}" -f ($year - 2000)).dll"))
            try { $expected = Get-StreamSha256 $expectedStream } finally { $expectedStream.Dispose() }
            $actualStream = [IO.File]::OpenRead($dll)
            try { $actual = Get-StreamSha256 $actualStream } finally { $actualStream.Dispose() }
            if ($expected -ne $actual) { throw "$prefix $dll differs from the package" }
        }
    }
}

# --- server ----------------------------------------------------------------

function Find-ServerSourceExe {
    param([string]$ServerDir)
    if (-not $ServerDir) { return $null }
    $preferred = Join-Path $ServerDir 'dwg-mcp.exe'
    if (Test-Path $preferred) { return $preferred }
    $fallback = Join-Path $ServerDir 'Bimwright.Dwg.Server.exe'
    if (Test-Path $fallback) { return $fallback }
    return $null
}

function Install-DwgMcpServer {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [string]$ServerDir,
        [string]$InstallRoot
    )
    $sourceExe = Find-ServerSourceExe -ServerDir $ServerDir
    if (-not $sourceExe) { return $null }

    $plannedExe = Join-Path $InstallRoot 'dwg-mcp.exe'
    if ($PSCmdlet.ShouldProcess($InstallRoot, 'Install self-contained dwg-mcp server')) {
        Set-InstallPath -Source $ServerDir -Destination $InstallRoot
        Write-Host ("[server] installed -> {0}" -f $plannedExe)
    } else {
        Write-Host ("[server] preview install -> {0}" -f $plannedExe)
    }
    return $plannedExe
}

# --help returns before any side effect in src/server/Program.cs, so this only
# proves the installed executable can start (antivirus, policy, corruption).
function Test-ServerExecutable([string]$Path, [int]$TimeoutSeconds = 30, [string]$Arguments = '--help') {
    $hint = 'Antivirus or policy may have blocked it. Previous installation restored.'
    $psi = New-Object Diagnostics.ProcessStartInfo $Path
    $psi.Arguments = $Arguments
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    try { $process = [Diagnostics.Process]::Start($psi) }
    catch { throw ("Server executable could not start ({0}). {1}" -f $_.Exception.Message, $hint) }
    try {
        $null = $process.StandardOutput.ReadToEndAsync()
        $null = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            try { $process.Kill() } catch { }
            throw ("Server executable could not start (no exit after {0} s). {1}" -f $TimeoutSeconds, $hint)
        }
        if ($process.ExitCode -ne 0) { throw ("Server executable could not start (exit code {0}). {1}" -f $process.ExitCode, $hint) }
    } finally { $process.Dispose() }
}

function Get-OtherServerVersions([string]$InstallRoot) {
    $parent = Split-Path -Parent $InstallRoot
    if (-not $parent -or -not (Test-Path -LiteralPath $parent)) { return @() }
    $current = Split-Path -Leaf $InstallRoot
    return @(Get-ChildItem -LiteralPath $parent -Directory | Where-Object {
        $_.Name -ne $current -and $_.Name -match '^v?\d+\.\d+\.\d+([-+][A-Za-z0-9.-]+)?$'
    })
}

function Test-ServerCopy([string]$Dir) {
    return (Test-Path -LiteralPath (Join-Path $Dir 'dwg-mcp.exe')) -or (Test-Path -LiteralPath (Join-Path $Dir 'Bimwright.Dwg.Server.exe'))
}

# A running server's exe cannot be deleted. Probe it first so a copy that an
# MCP client still runs is kept whole instead of half-deleted.
function Remove-ServerCopy([string]$Dir) {
    foreach ($name in 'dwg-mcp.exe', 'Bimwright.Dwg.Server.exe') {
        $exe = Join-Path $Dir $name
        if (Test-Path -LiteralPath $exe) {
            try { Remove-Item -LiteralPath $exe -Force -ErrorAction Stop } catch { return $false }
        }
    }
    try { Remove-Item -LiteralPath $Dir -Recurse -Force -ErrorAction Stop; return $true }
    catch { Write-Warning ("Could not fully remove {0}: {1}" -f $Dir, $_.Exception.Message); return $false }
}

# Only names this installer creates: <name>.dwgmcp-rollback-<32 hex>.
function Get-LeftoverServerCopies([string]$ServerParent) {
    if (-not (Test-Path -LiteralPath $ServerParent -PathType Container)) { return @() }
    return @(Get-ChildItem -LiteralPath $ServerParent -Directory | Where-Object { $_.Name -match '^.+\.dwgmcp-rollback-[0-9a-f]{32}$' })
}

# Legacy version directories (from installers before server\current) are kept
# by default because clients may still point at them; -PruneOldServers opts
# into removal. Only version-shaped directories are touched (current\, dev\ and
# arbitrary names are preserved). Each move is recorded in the transaction:
# rollback restores them, and the post-install sweep deletes the backups with
# the exe-first rule. A locked directory is skipped, not fatal.
function Remove-StaleServerVersions {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param([string]$InstallRoot)
    foreach ($dir in Get-OtherServerVersions -InstallRoot $InstallRoot) {
        if ($PSCmdlet.ShouldProcess($dir.FullName, 'Remove stale server version')) {
            $backup = "$($dir.FullName).dwgmcp-rollback-$([guid]::NewGuid().ToString('N'))"
            try {
                Move-Item -LiteralPath $dir.FullName -Destination $backup
                $script:installChanges.Add([pscustomobject]@{Path=$dir.FullName;Backup=$backup})
                Write-Host ("[server] removed stale version -> {0}" -f $dir.FullName)
            } catch {
                Write-Warning ("[server] could not remove stale version {0}: {1}" -f $dir.FullName, $_.Exception.Message)
            }
        } else {
            Write-Host ("[server] preview remove stale version -> {0}" -f $dir.FullName)
        }
    }
}

# ==== MCP client wiring (minimal JSONC-safe text edits, scripted) =========
# -Client wires the installed server into MCP clients after a successful
# install. File configs get a minimal text edit - never a JSON round-trip,
# so JSONC comments and key order survive - with a <path>.bak backup and a
# parse-check that restores the backup on failure. CLI clients go through
# their own `mcp add`. Nothing is touched unless -Client names it.
# The config entry key is always the literal 'dwg-mcp'.

# Per-client spec. Kind: cli (own `mcp` command), file (edit config),
# deeplink (no safe file path - hand the user a cherrystudio:// URL).
# RootKey/EntryKind describe the entry shape; DetectPaths/Paths use
# %USERPROFILE%/%APPDATA%/%LOCALAPPDATA% so tests can sandbox them.
function Get-McpClientSpecs {
    $up = $env:USERPROFILE; $la = $env:LOCALAPPDATA; $ra = $env:APPDATA
    @(
        [pscustomobject]@{ Name='claude';    Kind='cli'; Cli='claude'; DetectPaths=@((Join-Path $up '.claude.json')); Paths=@((Join-Path $up '.claude.json')); RootKey='mcpServers'; EntryKind='standard' }
        [pscustomobject]@{ Name='codex';     Kind='cli'; Cli='codex';  DetectPaths=@((Join-Path $up '.codex')) }
        [pscustomobject]@{ Name='grok';      Kind='cli'; Cli='grok';   DetectPaths=@((Join-Path $up '.grok')) }
        # claude-desktop: MSIX keeps its virtualized Roaming under the package
        # LocalCache - the package family name varies by install channel, so
        # glob any name. Classic/native installers use %APPDATA%\Claude; the
        # AnthropicClaude installer still reads that same Roaming path.
        [pscustomobject]@{ Name='claude-desktop'; Kind='file'; EntryKind='standard'; RootKey='mcpServers'; ProcName='claude'; ProcPathLike='*WindowsApps*'; Paths=@((Join-Path $la 'Packages\*\LocalCache\Roaming\Claude\claude_desktop_config.json'), (Join-Path $ra 'Claude\claude_desktop_config.json')); DetectPaths=@((Join-Path $la 'Packages\*\LocalCache\Roaming\Claude'), (Join-Path $ra 'Claude'), (Join-Path $la 'AnthropicClaude')) }
        [pscustomobject]@{ Name='cursor';    Kind='file'; EntryKind='standard'; RootKey='mcpServers'; Paths=@((Join-Path $up '.cursor\mcp.json'));            DetectPaths=@((Join-Path $up '.cursor')) }
        # Cline and Roo-style VS Code extensions keep their MCP settings under
        # globalStorage\<publisher>.<ext>\settings - first existing wins.
        [pscustomobject]@{ Name='cline';     Kind='file'; EntryKind='standard'; RootKey='mcpServers'; Paths=@((Join-Path $ra 'Code\User\globalStorage\saoudrizwan.claude-dev\settings\cline_mcp_settings.json'), (Join-Path $ra 'Code\User\globalStorage\rooveterinaryinc.roo-cline\settings\mcp_settings.json'), (Join-Path $ra 'Code\User\globalStorage\rooveterinaryinc.roo-cline\settings\cline_mcp_settings.json')); DetectPaths=@((Join-Path $ra 'Code\User\globalStorage\saoudrizwan.claude-dev'), (Join-Path $ra 'Code\User\globalStorage\rooveterinaryinc.roo-cline')) }
        [pscustomobject]@{ Name='gemini';    Kind='file'; EntryKind='standard'; RootKey='mcpServers'; Paths=@((Join-Path $up '.gemini\settings.json'));           DetectPaths=@((Join-Path $up '.gemini\settings.json')) }
        [pscustomobject]@{ Name='antigravity'; Kind='file'; EntryKind='standard'; RootKey='mcpServers'; Paths=@((Join-Path $up '.gemini\antigravity\mcp_config.json'), (Join-Path $up '.gemini\config\mcp_config.json')); DetectPaths=@((Join-Path $up '.gemini\antigravity'), (Join-Path $up '.gemini\config')) }
        [pscustomobject]@{ Name='devin';     Kind='file'; EntryKind='standard'; RootKey='mcpServers'; Paths=@((Join-Path $ra 'Devin\mcp_config.json'));              DetectPaths=@((Join-Path $ra 'Devin')) }
        [pscustomobject]@{ Name='lmstudio';  Kind='file'; EntryKind='standard'; RootKey='mcpServers'; Paths=@((Join-Path $up '.lmstudio\mcp.json'));                 DetectPaths=@((Join-Path $up '.lmstudio')) }
        [pscustomobject]@{ Name='kiro';      Kind='file'; EntryKind='standard'; RootKey='mcpServers'; Paths=@((Join-Path $up '.kiro\settings\mcp.json'));            DetectPaths=@((Join-Path $up '.kiro')) }
        [pscustomobject]@{ Name='qwen';      Kind='file'; EntryKind='standard'; RootKey='mcpServers'; Paths=@((Join-Path $up '.qwen\settings.json'));                DetectPaths=@((Join-Path $up '.qwen')) }
        [pscustomobject]@{ Name='windsurf';  Kind='file'; EntryKind='standard'; RootKey='mcpServers'; Paths=@((Join-Path $up '.codeium\windsurf\mcp_config.json'));  DetectPaths=@((Join-Path $up '.codeium\windsurf')) }
        [pscustomobject]@{ Name='opencode';  Kind='file'; EntryKind='opencode'; RootKey='mcp';        Paths=@((Join-Path $up '.config\opencode\opencode.json'));     DetectPaths=@((Join-Path $up '.config\opencode')) }
        [pscustomobject]@{ Name='kilo';      Kind='file'; EntryKind='opencode'; RootKey='mcp';        Paths=@((Join-Path $up '.config\kilo\kilo.jsonc'));            DetectPaths=@((Join-Path $up '.config\kilo')) }
        [pscustomobject]@{ Name='vscode';    Kind='file'; EntryKind='vscode';   RootKey='servers';    Paths=@((Join-Path $ra 'Code\User\mcp.json'));                 DetectPaths=@((Join-Path $ra 'Code\User')) }
        [pscustomobject]@{ Name='kun';       Kind='file'; EntryKind='kun';      RootKey='servers';    Paths=@((Join-Path $up '.kun\mcp.json'));                      DetectPaths=@((Join-Path $up '.kun')) }
        [pscustomobject]@{ Name='zed';       Kind='file'; EntryKind='zed';      RootKey='context_servers'; Paths=@((Join-Path $ra 'Zed\settings.json'));             DetectPaths=@((Join-Path $ra 'Zed')) }
        [pscustomobject]@{ Name='cherry-studio'; Kind='deeplink'; DetectPaths=@((Join-Path $ra 'Cherry Studio')) }
    )
}

# The dwg-mcp entry literal per client shape (path escaped for JSON).
function Get-McpEntryText([string]$EntryKind, [string]$Exe) {
    $e = $Exe.Replace('\', '\\')
    switch ($EntryKind) {
        'opencode' { return '{ "type": "local", "command": ["' + $e + '"], "enabled": true }' }
        'vscode'   { return '{ "type": "stdio", "command": "' + $e + '", "args": [] }' }
        'kun'      { return '{ "command": "' + $e + '", "args": [], "env": {}, "url": null }' }
        'zed'      { return '{ "command": { "path": "' + $e + '", "args": [] } }' }
        default    { return '{ "command": "' + $e + '", "args": [] }' }
    }
}

# Next index in $Text that is not whitespace or a // / /* */ comment.
function Skip-JsonSpace([string]$Text, [int]$i) {
    while ($i -lt $Text.Length) {
        $c = $Text[$i]
        if ($c -eq ' ' -or $c -eq "`t" -or $c -eq "`r" -or $c -eq "`n") { $i++; continue }
        if ($c -eq '/' -and $i + 1 -lt $Text.Length -and $Text[$i + 1] -eq '/') {
            while ($i -lt $Text.Length -and $Text[$i] -ne "`n") { $i++ }
            continue
        }
        if ($c -eq '/' -and $i + 1 -lt $Text.Length -and $Text[$i + 1] -eq '*') {
            $i += 2
            while ($i + 1 -lt $Text.Length -and -not ($Text[$i] -eq '*' -and $Text[$i + 1] -eq '/')) { $i++ }
            $i += 2
            continue
        }
        break
    }
    return $i
}

# Index of the char that ends the JSON value starting at $i (string,
# object/array via bracket balance, or scalar until , } ] ). Strings and
# comments are skipped, so braces inside them cannot unbalance the count.
function Get-JsonValueEnd([string]$Text, [int]$i) {
    $i = Skip-JsonSpace $Text $i
    if ($i -ge $Text.Length) { return $i }
    if ($Text[$i] -eq '"') {
        $i++
        while ($i -lt $Text.Length) {
            if ($Text[$i] -eq '\') { $i += 2; continue }
            if ($Text[$i] -eq '"') { return $i + 1 }
            $i++
        }
        return $i
    }
    if ($Text[$i] -eq '{' -or $Text[$i] -eq '[') {
        $open = $Text[$i]; $close = $(if ($open -eq '{') { '}' } else { ']' })
        $depth = 0
        while ($i -lt $Text.Length) {
            $c = $Text[$i]
            if ($c -eq '"') { $i = Get-JsonValueEnd $Text $i; continue }
            if ($c -eq '/' -and $i + 1 -lt $Text.Length -and ($Text[$i + 1] -eq '/' -or $Text[$i + 1] -eq '*')) {
                $i = Skip-JsonSpace $Text $i; continue
            }
            if ($c -eq $open) { $depth++ }
            elseif ($c -eq $close) { $depth--; if ($depth -eq 0) { return $i + 1 } }
            $i++
        }
        return $i
    }
    while ($i -lt $Text.Length -and $Text[$i] -notin @(',', '}', ']')) { $i++ }
    return $i
}

# Span of `"name": <value>` inside the object whose '{' is at $ObjOpen.
# Returns MemberStart..MemberEnd (value end, comma excluded) or $null.
function Get-JsonMemberSpan([string]$Text, [int]$ObjOpen, [string]$Name) {
    $i = Skip-JsonSpace $Text ($ObjOpen + 1)
    while ($i -lt $Text.Length -and $Text[$i] -ne '}') {
        if ($Text[$i] -eq '"') {
            $strStart = $i
            $i = Get-JsonValueEnd $Text $i
            $member = $Text.Substring($strStart + 1, $i - $strStart - 2)
            $afterColon = Skip-JsonSpace $Text $i
            if ($member -eq $Name -and $afterColon -lt $Text.Length -and $Text[$afterColon] -eq ':') {
                $valueStart = Skip-JsonSpace $Text ($afterColon + 1)
                return [pscustomobject]@{ MemberStart = $strStart; ValueStart = $valueStart; MemberEnd = (Get-JsonValueEnd $Text $valueStart) }
            }
            if ($afterColon -lt $Text.Length -and $Text[$afterColon] -eq ':') {
                $i = Get-JsonValueEnd $Text (Skip-JsonSpace $Text ($afterColon + 1))
            }
            $i = Skip-JsonSpace $Text $i
            continue
        }
        if ($Text[$i] -eq ',') { $i = Skip-JsonSpace $Text ($i + 1); continue }
        $i++
    }
    return $null
}

# All member names of the JSON object whose '{' is at $ObjOpen.
function Get-JsonObjectMemberNames([string]$Text, [int]$ObjOpen) {
    $names = New-Object System.Collections.Generic.List[string]
    $i = Skip-JsonSpace $Text ($ObjOpen + 1)
    while ($i -lt $Text.Length -and $Text[$i] -ne '}') {
        if ($Text[$i] -eq '"') {
            $s = $i; $i = Get-JsonValueEnd $Text $i
            $names.Add($Text.Substring($s + 1, $i - $s - 2))
            $afterColon = Skip-JsonSpace $Text $i
            if ($afterColon -lt $Text.Length -and $Text[$afterColon] -eq ':') {
                $i = Get-JsonValueEnd $Text (Skip-JsonSpace $Text ($afterColon + 1))
            }
            $i = Skip-JsonSpace $Text $i
            continue
        }
        $i++
    }
    return $names
}

# Skip-JsonSpace backwards: whitespace and /* */ blocks; // line comments are
# left for the parse-check to catch (rare between a comma and a member).
function Skip-JsonSpaceBack([string]$Text, [int]$i) {
    while ($i -ge 0) {
        $c = $Text[$i]
        if ($c -eq ' ' -or $c -eq "`t" -or $c -eq "`r" -or $c -eq "`n") { $i--; continue }
        if ($i -ge 1 -and $Text[$i] -eq '/' -and $Text[$i - 1] -eq '*') {
            $i -= 2
            while ($i -ge 0 -and -not ($Text[$i] -eq '/' -and $i + 1 -lt $Text.Length -and $Text[$i + 1] -eq '*')) { $i-- }
            $i--
            continue
        }
        break
    }
    return $i
}

# Strips // and /* */ comments so ConvertFrom-Json can verify JSONC files.
function ConvertFrom-JsoncText([string]$Text) {
    $out = New-Object Text.StringBuilder
    $i = 0
    while ($i -lt $Text.Length) {
        $c = $Text[$i]
        if ($c -eq '"') {
            $end = Get-JsonValueEnd $Text $i
            [void]$out.Append($Text.Substring($i, $end - $i)); $i = $end; continue
        }
        if ($c -eq '/' -and $i + 1 -lt $Text.Length -and ($Text[$i + 1] -eq '/' -or $Text[$i + 1] -eq '*')) {
            $i = Skip-JsonSpace $Text $i; continue
        }
        [void]$out.Append($c); $i++
    }
    $json = $out.ToString()
    try { return ($json | ConvertFrom-Json) }
    catch {
        # Keys that differ only in case (Claude Code's per-project Windows
        # paths) are valid JSON that ConvertFrom-Json rejects; parse
        # case-sensitively before calling the file broken.
        if ($PSVersionTable.PSVersion.Major -ge 6) { return ($json | ConvertFrom-Json -AsHashtable) }
        Add-Type -AssemblyName System.Web.Extensions
        $serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
        $serializer.MaxJsonLength = [int]::MaxValue
        return $serializer.DeserializeObject($json)
    }
}

# Back up, write, and parse-verify a config edit; a failed parse restores
# the .bak so a bad edit can never leave a broken client config behind.
function Save-McpConfigText([string]$Path, [string]$NewText, [string]$OldText) {
    $bak = "$Path.bak"
    Copy-Item -LiteralPath $Path -Destination $bak -Force
    try { $null = ConvertFrom-JsoncText $NewText }
    catch {
        Copy-Item -LiteralPath $bak -Destination $Path -Force
        throw "Edit produced invalid JSON for $Path - restored backup. ($($_.Exception.Message))"
    }
    [IO.File]::WriteAllText($Path, $NewText)
}

# True when the config's top-level <RootKey> object has a dwg-mcp member.
# Text scan only, so a file ConvertFrom-Json rejects is still inspected.
function Test-McpConfigHasEntry([string]$Path, [string]$RootKey) {
    $text = [IO.File]::ReadAllText($Path)
    $rootOpen = Skip-JsonSpace $text 0
    if ($rootOpen -ge $text.Length -or $text[$rootOpen] -ne '{') { return $false }
    $keySpan = Get-JsonMemberSpan $text $rootOpen $RootKey
    if ($null -eq $keySpan -or $text[$keySpan.ValueStart] -ne '{') { return $false }
    return $null -ne (Get-JsonMemberSpan $text $keySpan.ValueStart 'dwg-mcp')
}

# Minimal-edit add/remove of the dwg-mcp entry in a file config.
# Returns a status word: created|added|repointed|already|custom|removed|absent|previewed.
function Set-McpConfigEntry {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [string]$Path, [string]$RootKey, [string]$EntryKind,
        [string]$Exe, [switch]$Remove
    )
    $full = [IO.Path]::GetFullPath($Path)
    $escapedExe = if ($Exe) { $Exe.Replace('\', '\\') } else { $null }
    $entryText = if ($Exe) { Get-McpEntryText $EntryKind $Exe } else { $null }
    # Spans the whole JSON string value (quote to quote), so a repoint replaces
    # the full path rather than splicing the new one after the old prefix.
    $legacyRx = '(?i)(?<=")[^"]*?[Dd]wg(?:\\{2}|\\)+server(?:\\{2}|\\)+(?!current(?:\\{2}|\\)+)[^"\\]+(?:\\{2}|\\)+dwg-mcp\.exe(?=")'

    if (-not (Test-Path -LiteralPath $full)) {
        if ($Remove) { return 'absent' }
        $body = '{ "' + $RootKey + '": { "dwg-mcp": ' + $entryText + ' } }'
        if ($PSCmdlet.ShouldProcess($full, 'Wire dwg-mcp into client config')) {
            New-Item -ItemType Directory -Path (Split-Path -Parent $full) -Force | Out-Null
            [IO.File]::WriteAllText($full, $body)
            return 'created'
        }
        return 'previewed'
    }

    $text = [IO.File]::ReadAllText($full)
    if ($text -notmatch '\S') {
        # Empty file (windsurf ships one) - write the full skeleton.
        if ($Remove) { return 'absent' }
        $body = '{ "' + $RootKey + '": { "dwg-mcp": ' + $entryText + ' } }'
        if (-not $PSCmdlet.ShouldProcess($full, 'Wire dwg-mcp into client config')) { return 'previewed' }
        Save-McpConfigText $full $body $text
        return 'created'
    }
    $rootOpen = Skip-JsonSpace $text 0
    if ($rootOpen -ge $text.Length -or $text[$rootOpen] -ne '{') {
        Write-Warning "[client] $full is not a JSON object; left unchanged."
        return 'skipped'
    }
    # An unparseable existing file is reported and left alone - never
    # clobbered, never edited blind.
    try { $null = ConvertFrom-JsoncText $text } catch {
        Write-Warning "[client] $full is not valid JSON; left unchanged."
        return 'skipped'
    }
    $keySpan = Get-JsonMemberSpan $text $rootOpen $RootKey
    # Legacy pre-rename entries are reported, never removed silently.
    if ($null -ne $keySpan -and $text[$keySpan.ValueStart] -eq '{') {
        $legacy = @(Get-JsonObjectMemberNames $text $keySpan.ValueStart | Where-Object { $_ -like 'bimwright-dwg*' })
        if ($legacy.Count) {
            Write-Warning "[client] $full has legacy entries ($($legacy -join ', ')) - left in place; remove them in the client if unwanted."
        }
    }

    if ($Remove) {
        if ($null -eq $keySpan -or $text[$keySpan.ValueStart] -ne '{') { return 'absent' }
        $entrySpan = Get-JsonMemberSpan $text $keySpan.ValueStart 'dwg-mcp'
        if ($null -eq $entrySpan) { return 'absent' }
        # Cut the member plus one adjacent comma (trailing preferred).
        $after = Skip-JsonSpace $text $entrySpan.MemberEnd
        $from = $entrySpan.MemberStart; $to = $entrySpan.MemberEnd
        if ($after -lt $text.Length -and $text[$after] -eq ',') {
            $to = $after + 1
        } else {
            $before = Skip-JsonSpaceBack $text ($entrySpan.MemberStart - 1)
            if ($before -ge 0 -and $text[$before] -eq ',') { $from = $before }
        }
        if (-not $PSCmdlet.ShouldProcess($full, 'Remove dwg-mcp from client config')) { return 'previewed' }
        Save-McpConfigText $full ($text.Substring(0, $from) + $text.Substring($to)) $text
        return 'removed'
    }

    if ($null -eq $keySpan -or $text[$keySpan.ValueStart] -ne '{') {
        # Insert "<RootKey>": { "dwg-mcp": <entry> } as the first root member.
        $inner = '"' + $RootKey + '": { "dwg-mcp": ' + $entryText + ' }'
        $next = Skip-JsonSpace $text ($rootOpen + 1)
        if ($next -lt $text.Length -and $text[$next] -ne '}') { $inner += ',' }
        $newText = $text.Insert($rootOpen + 1, "`n    " + $inner + "`n")
        if (-not $PSCmdlet.ShouldProcess($full, 'Wire dwg-mcp into client config')) { return 'previewed' }
        Save-McpConfigText $full $newText $text
        return 'added'
    }

    $entrySpan = Get-JsonMemberSpan $text $keySpan.ValueStart 'dwg-mcp'
    if ($null -eq $entrySpan) {
        $next = Skip-JsonSpace $text ($keySpan.ValueStart + 1)
        $inner = '"dwg-mcp": ' + $entryText
        if ($next -lt $text.Length -and $text[$next] -ne '}') { $inner += ',' }
        $newText = $text.Insert($keySpan.ValueStart + 1, "`n        " + $inner + "`n    ")
        if (-not $PSCmdlet.ShouldProcess($full, 'Wire dwg-mcp into client config')) { return 'previewed' }
        Save-McpConfigText $full $newText $text
        return 'added'
    }

    # Entry exists: identical -> already; legacy versioned path -> repoint;
    # anything else is a custom launcher and is reported, never replaced.
    $memberText = $text.Substring($entrySpan.MemberStart, $entrySpan.MemberEnd - $entrySpan.MemberStart)
    $normalized = $memberText -replace '\\\\', '\'
    if ($normalized.Contains($Exe)) { return 'already' }
    if ($memberText -match $legacyRx) {
        # MatchEvaluator returns the escaped path literally - no regex
        # metachar reinterpretation of the replacement string.
        $newMember = [regex]::Replace($memberText, $legacyRx, { param($m) $escapedExe })
        $newText = $text.Substring(0, $entrySpan.MemberStart) + $newMember + $text.Substring($entrySpan.MemberEnd)
        if (-not $PSCmdlet.ShouldProcess($full, 'Repoint dwg-mcp to the current server')) { return 'previewed' }
        Save-McpConfigText $full $newText $text
        return 'repointed'
    }
    return 'custom'
}

# First existing candidate wins. When none exists: a globbed package dir that
# already contains the app's cache (MSIX) beats a stray literal dir - an MSIX
# app never reads the real %APPDATA%; else a literal path under a detected
# install dir; else the first literal path (create-if-absent).
function Resolve-McpConfigPath($spec) {
    foreach ($p in $spec.Paths) {
        $hit = @(Resolve-Path $p -ErrorAction SilentlyContinue)
        if ($hit.Count) { return $hit[0].Path }
    }
    if ($spec.Paths[0] -match '\*') {
        # Rebuild inside any package dir that already holds the app's cache -
        # works regardless of the package family name (enterprise repackage).
        $tail = $spec.Paths[0].Substring($spec.Paths[0].IndexOf('*\') + 2)
        $innerDir = Split-Path -Parent $tail
        $baseDir = $spec.Paths[0].Substring(0, $spec.Paths[0].IndexOf('*\'))
        if (Test-Path -LiteralPath $baseDir) {
            $pkg = @(Get-ChildItem -LiteralPath $baseDir -Directory -ErrorAction SilentlyContinue |
                Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName $innerDir) } |
                Select-Object -First 1)
            if ($pkg.Count) { return Join-Path $pkg[0].FullName $tail }
        }
    }
    foreach ($p in $spec.Paths) {
        if ($p -match '\*') { continue }
        foreach ($d in @($spec.DetectPaths)) {
            if ($d -and $d -notmatch '\*' -and $p.StartsWith($d, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $d)) { return $p }
        }
    }
    return @($spec.Paths | Where-Object { $_ -notmatch '\*' } | Select-Object -First 1)
}

# codex `mcp add` replaces the whole entry - pull args forward from the
# existing [mcp_servers.dwg-mcp] table so a repoint doesn't drop them.
function Read-CodexEntryArgs([string]$TomlPath) {
    if (-not (Test-Path -LiteralPath $TomlPath)) { return @() }
    $text = [IO.File]::ReadAllText($TomlPath)
    $m = [regex]::Match($text, '(?ms)^\[mcp_servers\.dwg-mcp\]\s*$(.*?)(?=^\[|\z)')
    if (-not $m.Success) { return @() }
    $a = [regex]::Match($m.Groups[1].Value, 'args\s*=\s*\[(.*?)\]')
    if (-not $a.Success) { return @() }
    return @([regex]::Matches($a.Groups[1].Value, '"((?:[^"\\]|\\.)*)"') | ForEach-Object { $_.Groups[1].Value -replace '\\(.)', '$1' })
}

# Fresh installs default the server to the full tool surface: seed
# dwgmcp.config.json with toolsets=["all"]. An existing toolsets key is the
# user's explicit choice and is kept; --toolsets/--read-only args and
# BIMWRIGHT_* env vars still outrank the file. The write rides the install
# transaction, so a failed install restores the previous config.
function Set-DefaultToolsetsConfig {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param([string]$ConfigPath)
    $full = [IO.Path]::GetFullPath($ConfigPath)
    $existed = Test-Path -LiteralPath $full
    $root = $null
    if ($existed) {
        try { $root = Get-Content -LiteralPath $full -Raw | ConvertFrom-Json }
        catch {
            Write-Warning ("[config] {0} is not valid JSON; left unchanged." -f $full)
            return 'skipped'
        }
        if ($null -ne $root -and $root.PSObject.Properties.Match('toolsets').Count) { return 'kept' }
    }
    if (-not $PSCmdlet.ShouldProcess($full, 'Seed default toolsets=all in dwgmcp.config.json')) {
        Write-Host ("[config] preview seed toolsets=all -> {0}" -f $full)
        return 'previewed'
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $full) -Force | Out-Null
    if ($existed) { Move-ToRollback $full }
    else { $script:installChanges.Add([pscustomobject]@{Path=$full;Backup=$null}) }
    if ($null -eq $root) { $root = [pscustomobject]@{} }
    $root | Add-Member -NotePropertyName toolsets -NotePropertyValue @('all') -Force
    [IO.File]::WriteAllText($full, ($root | ConvertTo-Json -Depth 8))
    return $(if ($existed) { 'merged' } else { 'seeded' })
}

# A client counts as installed when its CLI is on PATH, its config file
# exists, or its config directory does.
function Test-McpClientDetected($spec) {
    if ($spec.Cli -and (Get-Command $spec.Cli -ErrorAction SilentlyContinue)) { return $true }
    foreach ($p in @($spec.Paths) + @($spec.DetectPaths)) {
        if ($p -and (Test-Path $p)) { return $true }
    }
    return $false
}

# Wire (Mode=Add) or unwire (Mode=Remove) the requested clients.
# $Clients entries: names, 'auto'/'all' (every detected client), 'none'.
# Per-client failures are collected, never thrown - a bad client edit must
# not roll back an otherwise-good install.
function Invoke-McpClientWiring {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param([string[]]$Clients, [string]$Exe, [ValidateSet('Add', 'Remove')][string]$Mode)
    $report = New-Object System.Collections.Generic.List[string]
    $requested = @($Clients | Where-Object { $_ -and $_ -ne 'none' })
    if ($requested.Count -eq 0) { return $report }

    $specs = Get-McpClientSpecs
    if ($requested | Where-Object { $_ -in @('auto', 'all', 'Auto') }) {
        $requested = @($specs | Where-Object { Test-McpClientDetected $_ } | ForEach-Object { $_.Name })
        if ($requested.Count -eq 0) { $report.Add('auto: no MCP clients detected'); return $report }
    }

    foreach ($name in $requested) {
        $spec = $specs | Where-Object { $_.Name -eq $name } | Select-Object -First 1
        if (-not $spec) { $report.Add("${name}: unknown client"); continue }
        try {
            switch ($spec.Kind) {
                'cli' {
                    $cli = Get-Command $spec.Cli -ErrorAction SilentlyContinue
                    if ($spec.Name -eq 'claude' -and $spec.Paths -and (Test-Path $spec.Paths[0]) -and
                        (-not $cli -or ($Mode -eq 'Add' -and (Test-McpConfigHasEntry $spec.Paths[0] $spec.RootKey)))) {
                        # ~/.claude.json carries the user-scope mcpServers key -
                        # same shape as file clients. Used without the CLI on
                        # PATH, and for an existing entry: `claude mcp add`
                        # refuses to replace one, so it could never be repointed.
                        $st = Set-McpConfigEntry -Path (Resolve-McpConfigPath $spec) -RootKey $spec.RootKey -EntryKind $spec.EntryKind -Exe $Exe -Remove:($Mode -eq 'Remove')
                        $report.Add("claude: $st (~/.claude.json)")
                        continue
                    }
                    if (-not $cli) { $report.Add("$($spec.Name): $($spec.Cli) CLI not on PATH"); continue }
                    if ($Mode -eq 'Add') {
                        if (-not $PSCmdlet.ShouldProcess($spec.Name, 'wire dwg-mcp via client CLI')) { $report.Add("$($spec.Name): previewed"); continue }
                        switch ($spec.Name) {
                            'claude' {
                                $null = & claude mcp remove dwg-mcp -s local 2>$null  # drop any project-local shadow
                                $out = & claude mcp add -s user dwg-mcp -- $Exe 2>&1
                                $verify = & claude mcp get dwg-mcp 2>&1
                            }
                            'codex' {
                                $toml = Join-Path ($(if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' })) 'config.toml'
                                $oldArgs = Read-CodexEntryArgs $toml
                                $out = & codex mcp add dwg-mcp -- $Exe @oldArgs 2>&1
                                $verify = & codex mcp get dwg-mcp --json 2>&1
                            }
                            'grok' {
                                $out = & grok mcp add dwg-mcp $Exe --transport stdio 2>&1
                                $verify = & grok mcp list 2>&1
                            }
                        }
                        # A refused add leaves the old entry in place; `get`
                        # still finds dwg-mcp, so it must not read as wired.
                        if (($out | Out-String) -match 'already exists') {
                            $report.Add("$($spec.Name): existing dwg-mcp entry left unchanged - inspect it with $($spec.Cli) mcp list")
                            continue
                        }
                        $verified = ($verify | Out-String) -match 'dwg-mcp'
                        $report.Add("$($spec.Name): " + $(if ($verified) { 'wired' } else { 'wired (verify inconclusive - check the client)' }))
                        if ($spec.Name -eq 'grok' -and ($verify | Out-String) -match '\(project\)') {
                            $report.Add('grok: a project-scope entry may shadow the user one - check grok mcp list from the repo')
                        }
                    } else {
                        if (-not $PSCmdlet.ShouldProcess($spec.Name, 'remove dwg-mcp via client CLI')) { $report.Add("$($spec.Name): previewed"); continue }
                        switch ($spec.Name) {
                            'claude' { $null = & claude mcp remove dwg-mcp -s user 2>&1 }
                            'codex'  { $null = & codex mcp remove dwg-mcp 2>&1 }
                            'grok'   { $null = & grok mcp remove dwg-mcp 2>&1 }
                        }
                        $report.Add("$($spec.Name): removed")
                    }
                }
                'file' {
                    if ($Mode -eq 'Add' -and -not (Test-McpClientDetected $spec)) {
                        $report.Add("$($spec.Name): not detected - skipping (checked: $($spec.DetectPaths -join '; '))")
                        continue
                    }
                    $path = Resolve-McpConfigPath $spec
                    if (-not $path) { $report.Add("$($spec.Name): no config file found - wire it manually per the README"); continue }
                    $status = Set-McpConfigEntry -Path $path -RootKey $spec.RootKey -EntryKind $spec.EntryKind -Exe $Exe -Remove:($Mode -eq 'Remove')
                    $report.Add("$($spec.Name): $status -> $path")
                    # Some apps (Claude Desktop) persist their config from
                    # memory on quit - an edit made while the app runs can be
                    # overwritten. Surface it instead of silently losing the entry.
                    if ($spec.ProcName -and $status -in @('added', 'created', 'repointed', 'removed')) {
                        $running = @(Get-Process -Name $spec.ProcName -ErrorAction SilentlyContinue |
                            Where-Object { -not $spec.ProcPathLike -or $_.Path -like $spec.ProcPathLike })
                        if ($running.Count) {
                            $report.Add("$($spec.Name): app is running - fully quit it and verify the entry survives before trusting it")
                        }
                    }
                }
                'deeplink' {
                    if ($Mode -eq 'Remove') { $report.Add('cherry-studio: remove dwg-mcp in Settings -> MCP Servers'); continue }
                    $json = '{"mcpServers":{"dwg-mcp":{"command":"' + $Exe.Replace('\', '\\') + '","args":[]}}}'
                    $url = 'cherrystudio://mcp/install?servers=' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json))
                    $report.Add('cherry-studio: open this URL to install: ' + $url)
                }
            }
        } catch {
            $report.Add("$($spec.Name): failed - $($_.Exception.Message)")
        }
    }
    return $report
}

function Get-InstallGuidePath([string]$ScriptRoot) {
    if ($ScriptRoot) {
        foreach ($candidate in @((Join-Path $ScriptRoot 'README.md'), (Join-Path (Split-Path -Parent $ScriptRoot) 'README.md'))) {
            if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
        }
    }
    return 'https://github.com/bimwright/dwg-mcp/blob/master/README.md'
}

# --- main ------------------------------------------------------------------

if (-not $Years -or $Years.Count -eq 0) {
    if ($Version) {
        $Years = @([int]$Version)
    } elseif ($Uninstall) {
        # Removal must not depend on AutoCAD still being installed.
        $Years = 2022..2027
    } elseif ($hasSetupLayout) {
        # Setup ZIP: install every packed year that has an AutoCAD present.
        $packed = @()
        if ($bundleSourceDir) {
            $packed = @(Get-ChildItem -LiteralPath (Join-Path $bundleSourceDir 'Contents') -Directory -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -match '^\d{4}$' } | ForEach-Object { [int]$_.Name })
        }
        $installed = @(Get-InstalledAutoCadYears)
        $Years = @($packed | Where-Object { $installed -contains $_ })
        if ($Years.Count -eq 0) {
            Write-Warning "No packed AutoCAD year is installed on this machine (packed: $($packed -join ', '); detected: $(if ($installed.Count) { $installed -join ', ' } else { 'none' }))."
        } else {
            Write-Host ("Detected AutoCAD years to install: {0}" -f ($Years -join ', '))
        }
    } else {
        $Years = @(Get-InstalledAutoCadYears)
        if ($Years.Count -eq 0) {
            Write-Warning "No installed AutoCAD 2022-2027 found (no acad.exe). Use -Years to force an explicit list."
            return
        }
        Write-Host ("Detected AutoCAD years: {0}" -f ($Years -join ', '))
    }
}

$installGuide = Get-InstallGuidePath $PSScriptRoot
# No -Client: an install wires every detected client; an uninstall leaves
# client configs alone (the server stays, so their entries still work).
$clientRequested = @($Client).Count -gt 0
if (-not $clientRequested -and -not $Uninstall) { $Client = @('auto') }

$handled = @()
$skipped = @()
$previewed = @()
$removedDuplicates = @()
$verified = @()
$inUse = @()
$legacyServers = @()
$serverCommand = $null
$serverCheck = 'not run'
$configDefault = $null
$wiredClients = @()
# A caller-supplied -ServerInstallRoot's parent is not ours to prune or sweep.
$ownServerRoot = -not $PSBoundParameters.ContainsKey('ServerInstallRoot')
$Years = @($Years | Sort-Object -Unique)
$script:installChanges = New-Object System.Collections.Generic.List[object]
$script:installStage = $null

try {
    # Validate every selected payload before replacing any installed file.
    if (-not $WhatIfPreference) { Assert-AutoCadClosed }
    if (-not $Uninstall) {
        Assert-SetupManifest -Root $SourceDir -Manifest $manifest
        if ($bundleSourceDir) {
            Assert-BundlePackage -BundleDir $bundleSourceDir -InstallYears $Years
        } else {
            $repoRoot = Split-Path -Parent $PSScriptRoot
            foreach ($year in $Years) { $null = Assert-RepoPayload -RepoRoot $repoRoot -Year $year -Configuration $Config }
        }
        if ($serverSourceDir -and -not (Find-ServerSourceExe $serverSourceDir)) { throw 'Setup server executable is missing.' }
    }
    # A machine-wide bundle with the same ProductCode would shadow or be
    # shadowed by the per-user one, and a per-user installer cannot remove it.
    $machineDupes = @(Find-MachineWideDuplicates)
    if ($machineDupes.Count) {
        $message = "A machine-wide dwg-mcp bundle exists at $($machineDupes -join ', ') (same ProductCode). Remove it with administrator rights"
        if ($Uninstall) { Write-Warning "$message; it was left in place." }
        else { throw "$message, then run the installer again. Nothing was changed." }
    }
    if (-not $Uninstall) {
        if (-not $WhatIfPreference) {
            $script:installStage = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('dwgmcp-install-' + [guid]::NewGuid().ToString('N'))))
            New-Item -ItemType Directory -Path $script:installStage | Out-Null
            foreach ($year in $Years) {
                $stageYear = Join-Path $script:installStage "Contents\$year"
                New-Item -ItemType Directory -Path $stageYear -Force | Out-Null
                if ($bundleSourceDir) {
                    Copy-Item -LiteralPath (Join-Path $bundleSourceDir "Contents\$year") -Destination $stageYear -Recurse -Force
                } else {
                    $binDir = Assert-RepoPayload -RepoRoot (Split-Path -Parent $PSScriptRoot) -Year $year -Configuration $Config
                    # Ship every file except the AutoCAD interop DLLs the host
                    # already provides.
                    Get-ChildItem -LiteralPath $binDir -File | Where-Object {
                        $_.Name -notmatch '^(accoremgd|acdbmgd|acmgd)\.dll$'
                    } | Copy-Item -Destination $stageYear -Force
                    if (Test-Path -LiteralPath (Join-Path $binDir 'Fonts')) {
                        Copy-Item -LiteralPath (Join-Path $binDir 'Fonts') -Destination (Join-Path $stageYear 'Fonts') -Recurse -Force
                    }
                }
            }
            if ($serverSourceDir) {
                Copy-Item -LiteralPath $serverSourceDir -Destination (Join-Path $script:installStage 'server') -Recurse
                $serverSourceDir = Join-Path $script:installStage 'server'
            }
            # Recheck after staging; a user may have launched AutoCAD meanwhile.
            Assert-AutoCadClosed
        }
    }

    $duplicates = @(Get-DuplicateBundleItems -KeepBundle $bundleTargetRoot)
    if ($Uninstall) {
        $targets = @($bundleTargetRoot) + $duplicates | Where-Object { Test-Path -LiteralPath $_ }
        foreach ($item in $targets) {
            if ($PSCmdlet.ShouldProcess($item, 'Remove dwg-mcp plugin bundle')) { Remove-Item -LiteralPath $item -Recurse -Force }
        }
        foreach ($item in $duplicates) { $removedDuplicates += (Split-Path -Leaf $item) }
        if ($targets.Count) {
            if ($WhatIfPreference) {
                Write-Host ("[bundle] preview uninstall from {0}" -f (Split-Path -Parent $bundleTargetRoot))
                $previewed += 'bundle'
            } else {
                Write-Host ("[bundle] uninstalled from {0}" -f (Split-Path -Parent $bundleTargetRoot))
                $handled += 'bundle'
            }
        } else {
            Write-Host ("[bundle] nothing to remove at {0}" -f $bundleTargetRoot)
            $skipped += 'bundle'
        }
    } else {
        foreach ($item in $duplicates) {
            if ($PSCmdlet.ShouldProcess($item, 'Remove duplicate dwg-mcp bundle (same ProductCode)')) { Move-ToRollback $item }
            $removedDuplicates += (Split-Path -Leaf $item)
        }

        if ($PSCmdlet.ShouldProcess($bundleTargetRoot, 'Install plugin bundle with rollback')) {
            foreach ($year in $Years) {
                Set-InstallPath -Source (Join-Path $script:installStage "Contents\$year") -Destination (Join-Path $bundleTargetRoot "Contents\$year")
            }
            # Regenerate PackageContents.xml covering every year dir that will
            # exist after this install (union of already-present and new).
            $coverYears = @($Years)
            foreach ($dir in @(Get-ChildItem -LiteralPath (Join-Path $bundleTargetRoot 'Contents') -Directory -ErrorAction SilentlyContinue)) {
                if ($dir.Name -match '^\d{4}$' -and $coverYears -notcontains [int]$dir.Name) { $coverYears += [int]$dir.Name }
            }
            $template = if ($bundleSourceDir) { Join-Path $bundleSourceDir 'PackageContents.xml' } else { Join-Path $PSScriptRoot 'PackageContents.xml' }
            $manifestDir = Join-Path $script:installStage 'manifest-out'
            New-Item -ItemType Directory -Path $manifestDir -Force | Out-Null
            Save-BundleManifest -OutPath (Join-Path $manifestDir 'PackageContents.xml') -CoverYears $coverYears -TemplatePath $template
            Set-InstallPath -Source (Join-Path $manifestDir 'PackageContents.xml') -Destination (Join-Path $bundleTargetRoot 'PackageContents.xml')
        }

        if ($WhatIfPreference) {
            Write-Host ("[bundle] preview install -> {0} (years: {1})" -f $bundleTargetRoot, ($Years -join ', '))
            $previewed += 'bundle'
        } else {
            Write-Host ("[bundle] installed -> {0} (years: {1})" -f $bundleTargetRoot, ($Years -join ', '))
            $handled += 'bundle'
        }

        $serverCommand = Install-DwgMcpServer -ServerDir $serverSourceDir -InstallRoot $ServerInstallRoot
        if ($serverCommand -and $ownServerRoot) {
            if ($PruneOldServers) {
                Remove-StaleServerVersions -InstallRoot $ServerInstallRoot
            } else {
                $legacyServers = @(Get-OtherServerVersions -InstallRoot $ServerInstallRoot)
            }
        }
        if ($serverCommand -and -not $WhatIfPreference) {
            # A browser-downloaded ZIP extracted by Explorer marks every file as
            # coming from the Internet; Copy-Item/Move-Item keep that mark.
            Get-ChildItem -LiteralPath $ServerInstallRoot -Recurse -File | Unblock-File
            Test-ServerExecutable -Path $serverCommand
            $serverCheck = 'OK'
        } elseif ($serverCommand) {
            $serverCheck = 'skipped (WhatIf)'
        }
        $configDefault = Set-DefaultToolsetsConfig -ConfigPath (Join-Path $env:LOCALAPPDATA 'Bimwright\Dwg\dwgmcp.config.json')
        if (@($Client | Where-Object { $_ -ne 'none' }).Count) {
            if ($serverCommand) {
                $wiredClients = @(Invoke-McpClientWiring -Clients $Client -Exe $serverCommand -Mode Add)
            } elseif ($clientRequested) {
                $wiredClients = @('-Client requested but this package has no server to point at')
            }
        } elseif (-not $WhatIfPreference) {
            $detected = @(Get-McpClientSpecs | Where-Object { Test-McpClientDetected $_ } | ForEach-Object { $_.Name })
            if ($detected.Count) {
                $wiredClients = @("detected: $($detected -join ', ') - wire with -Client <names> or -Client auto")
            }
        }
        if (-not $WhatIfPreference) {
            Assert-InstalledPlugin -InstallYears $Years -BundleDir $bundleTargetRoot
            $verified += ($Years | ForEach-Object { "Acad$_" })
        }
    }
    if ($Uninstall -and @($Client | Where-Object { $_ -ne 'none' }).Count) {
        $wiredClients = @(Invoke-McpClientWiring -Clients $Client -Mode Remove)
    }
} catch {
    $installFailure = $_
    Undo-InstallChanges
    throw $installFailure
} finally {
    if ($script:installStage) {
        try { Remove-InstallPath $script:installStage } catch { Write-Warning "Staging cleanup failed: $_" }
    }
}
# Keep backups until every bundle and server change has succeeded.
foreach ($change in $script:installChanges) {
    if (-not $change.Backup -or -not (Test-Path -LiteralPath $change.Backup)) { continue }
    if (Test-ServerCopy $change.Backup) {
        if (-not (Remove-ServerCopy $change.Backup)) { $inUse += $change.Backup }
    } else {
        try { Remove-InstallPath $change.Backup } catch { Write-Warning "Backup retained at $($change.Backup): $_" }
    }
}
# Previous runs may have left copies that a client was still running.
if (-not $Uninstall -and -not $WhatIfPreference -and $serverCommand -and $ownServerRoot) {
    foreach ($dir in Get-LeftoverServerCopies (Split-Path -Parent ([IO.Path]::GetFullPath($ServerInstallRoot)))) {
        if ($inUse -contains $dir.FullName) { continue }
        if (-not (Remove-ServerCopy $dir.FullName)) { $inUse += $dir.FullName }
    }
}
$script:installChanges = $null

Write-Host ""
Write-Host "=== install.ps1 summary ==="
Write-Host ("Mode    : {0}" -f $(if ($Uninstall) { 'Uninstall' } else { 'Install' }))
if (-not $Uninstall) { Write-Host ("Version : {0}" -f $setupVersion) }
Write-Host ("Source  : {0}" -f $(if ($SourceDir) { $SourceDir } else { 'repo bin\' + $Config }))
Write-Host ("Years   : {0}" -f ($Years -join ', '))
Write-Host ("Handled : {0}" -f $(if ($handled.Count) { $handled -join ', ' } else { 'none' }))
if ($previewed.Count) { Write-Host ("Previewed: {0}" -f ($previewed -join ', ')) }
if ($skipped.Count) { Write-Host ("Skipped : {0}" -f ($skipped -join ', ')) }
if ($removedDuplicates.Count) { Write-Host ("Removed : {0}{1}" -f ($removedDuplicates -join ', '), $(if ($WhatIfPreference) { ' (preview)' } else { '' })) }
if ($verified.Count) { Write-Host ("Verified: {0} match the package" -f ($verified -join ', ')) }
if (-not $Uninstall) {
    if ($serverCommand) { Write-Host ("Server  : {0} (check: {1})" -f $serverCommand, $serverCheck) }
    else { Write-Host 'Server  : not in this package' }
    switch ($configDefault) {
        'seeded'    { Write-Host ("Config  : seeded toolsets=all -> {0}\Bimwright\Dwg\dwgmcp.config.json" -f $env:LOCALAPPDATA) }
        'merged'    { Write-Host ("Config  : added toolsets=all to {0}\Bimwright\Dwg\dwgmcp.config.json" -f $env:LOCALAPPDATA) }
        'kept'      { Write-Host 'Config  : kept existing toolsets setting' }
        'skipped'   { Write-Host 'Config  : unreadable config - unchanged' }
        'previewed' { Write-Host ("Config  : preview seed toolsets=all -> {0}\Bimwright\Dwg\dwgmcp.config.json" -f $env:LOCALAPPDATA) }
    }
}
foreach ($w in $wiredClients) { Write-Host ("Client  : {0}" -f $w) }
if ($Uninstall -and -not @($Client | Where-Object { $_ -ne 'none' }).Count) {
    Write-Host 'Client  : entries left in place - re-run with -Client <names> to unwire'
}
if ($inUse.Count) { Write-Host ("In use  : {0} - restart MCP clients; removed at next install" -f ($inUse -join ', ')) }
if ($legacyServers.Count) { Write-Host ("Legacy  : {0} - repoint clients to the Server path, then run install.ps1 -PruneOldServers" -f (($legacyServers | ForEach-Object { $_.Name }) -join ', ')) }
if (-not $Uninstall) { Write-Host ("Next    : restart wired MCP clients to load dwg-mcp; manual wiring: {0}" -f $installGuide) }
