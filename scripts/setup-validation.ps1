#Requires -Version 5.1
# Shared, read-only Setup validation. A checksum is byte evidence, not publisher authentication.
function Assert-SetupPlainPath([string]$Path) {
    for ($at = [IO.Path]::GetFullPath($Path); $at; $at = [IO.Path]::GetDirectoryName($at)) {
        if ((Test-Path -LiteralPath $at) -and (([IO.File]::GetAttributes($at) -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
            throw "Setup refuses linked paths: $at"
        }
    }
}

function Get-SetupSha256([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose(); $stream.Dispose() }
}

function Get-SetupFiles([string]$Root) {
    Assert-SetupPlainPath $Root
    $pending = New-Object 'System.Collections.Generic.Queue[string]'
    $pending.Enqueue([IO.Path]::GetFullPath($Root))
    while ($pending.Count) {
        foreach ($path in [IO.Directory]::GetFileSystemEntries($pending.Dequeue())) {
            Assert-SetupPlainPath $path
            if ([IO.Directory]::Exists($path)) { $pending.Enqueue($path) } else { $path }
        }
    }
}

function Assert-SetupRelativePath([string]$Path) {
    if (-not $Path -or [IO.Path]::IsPathRooted($Path) -or $Path.Contains(':') -or $Path.Contains('\') -or
        @($Path.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count) {
        throw "Invalid manifest path: $Path"
    }
}

function Assert-SetupManifest([string]$Root, $Manifest) {
    if (-not $Manifest) { throw 'Setup manifest.json is required for a packaged layout.' }
    if ($Manifest.name -ne 'DwgMcp.Setup' -or $Manifest.platform -ne 'win-x64' -or
        $Manifest.version -notmatch '^v?\d+\.\d+\.\d+(?:[-+][A-Za-z0-9.-]+)?$') { throw 'Invalid Setup identity/version/platform.' }
    if ($Manifest.server.command -ne 'server/dwg-mcp.exe' -or $Manifest.server.selfContained -isnot [bool] -or
        $Manifest.server.requiresDotnet -isnot [bool] -or $Manifest.server.selfContained -ne $true -or $Manifest.server.requiresDotnet -ne $false) {
        throw 'Setup server contract is invalid.'
    }
    if (-not $Manifest.files) { throw 'Setup manifest has no file checksums.' }
    $prefix = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $listed = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $Manifest.files) {
        $relative = [string]$file.path
        if ($file.path -isnot [string] -or ($file.bytes -isnot [int] -and $file.bytes -isnot [long])) { throw 'Invalid manifest file entry types.' }
        Assert-SetupRelativePath $relative
        if ($relative -eq 'manifest.json' -or -not $listed.Add($relative)) { throw "Duplicate/self-referencing manifest path: $relative" }
        $path = [IO.Path]::GetFullPath((Join-Path $Root $relative))
        if (-not $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Invalid manifest path: $relative" }
        Assert-SetupPlainPath $path
        if (-not [IO.File]::Exists($path)) { throw "Setup file missing: $relative" }
        if ($file.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or $null -eq $file.bytes -or
            (Get-Item -LiteralPath $path).Length -ne $file.bytes -or (Get-SetupSha256 $path) -ne $file.sha256) {
            throw "Setup checksum/length failed: $relative. Download and extract the setup ZIP again."
        }
    }
    foreach ($path in Get-SetupFiles $Root) {
        $relative = $path.Substring($prefix.Length).Replace('\', '/')
        if ($relative -ne 'manifest.json' -and -not $listed.Contains($relative)) { throw "Unlisted Setup file: $relative" }
    }
    foreach ($required in @('install.ps1', 'plugin-payload.ps1', 'setup-validation.ps1', 'server/dwg-mcp.exe', 'bundle/PackageContents.xml')) {
        if (-not $listed.Contains($required)) { throw "Setup manifest omits required file: $required" }
    }
    $years = @($Manifest.packedAutocadYears)
    if (-not $years.Count -or @($years | Where-Object { $_ -notin (2022..2027) }).Count -or
        @($years | Sort-Object -Unique).Count -ne $years.Count) { throw 'Invalid packed AutoCAD years.' }
    $actual = @(Get-ChildItem -LiteralPath (Join-Path $Root 'bundle/Contents') -Directory | ForEach-Object {
        if ($_.Name -notmatch '^202[2-7]$') { throw "Unexpected AutoCAD payload directory: $($_.Name)" }; [int]$_.Name
    })
    if (@(Compare-Object @($years | Sort-Object) @($actual | Sort-Object)).Count -or
        @(Compare-Object @($years | Sort-Object) @($Manifest.supportedAutocadYears | Sort-Object)).Count) { throw 'Setup manifest years differ from included payloads.' }
    [xml]$xml = [IO.File]::ReadAllText((Join-Path $Root 'bundle/PackageContents.xml'))
    if ([string]$xml.ApplicationPackage.ProductCode -ne '{8A3F7B2D-1C4E-4D5F-9A6B-7E8C9D0F1A2B}') { throw 'Foreign Setup bundle ProductCode.' }
    $xmlYears = @($xml.ApplicationPackage.Components | ForEach-Object {
        $module = [string]$_.ComponentEntry.ModuleName
        if ($module -notmatch '^\./Contents/(202[2-7])/Bimwright\.Dwg\.Plugin\.Acad(\d{2})\.dll$' -or
            [int]$Matches[2] -ne ([int]$Matches[1] - 2000)) { throw "Invalid Setup bundle module: $module" }
        [int]$Matches[1]
    })
    if ($xmlYears.Count -ne $years.Count -or @(Compare-Object @($years | Sort-Object) @($xmlYears | Sort-Object)).Count) { throw 'Bundle XML advertises different AutoCAD years.' }
}

function Assert-SetupArchive([string]$ZipPath, [string]$ReleaseTag, [string]$Root) {
    if ($ReleaseTag -notmatch '^v\d+\.\d+\.\d+(?:[-+][A-Za-z0-9.-]+)?$' -or
        [IO.Path]::GetFileName($ZipPath) -cne "DwgMcp.Setup-$ReleaseTag-win-x64.zip") { throw 'Setup ZIP must match the exact release tag.' }
    Assert-SetupPlainPath $ZipPath
    Assert-SetupPlainPath ($ZipPath + '.sha256')
    $sidecar = [IO.File]::ReadAllText($ZipPath + '.sha256').Trim()
    if ($sidecar -notmatch '\A([a-fA-F0-9]{64})[ \t]+\*?([^\r\n]+)\z' -or
        $Matches[2] -cne [IO.Path]::GetFileName($ZipPath) -or (Get-SetupSha256 $ZipPath) -ne $Matches[1]) { throw 'Setup ZIP checksum/sidecar failed.' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $entries = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        $manifestEntry = $null
        foreach ($entry in $zip.Entries) {
            # Windows PowerShell 5.1 Compress-Archive uses backslash entries.
            # Normalize before traversal/duplicate checks, not after extraction.
            $name = $entry.FullName.Replace('\', '/').TrimEnd('/')
            Assert-SetupRelativePath $name
            if (($entry.ExternalAttributes -band 0x400) -ne 0 -or (($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000) { throw 'Setup ZIP contains a linked entry.' }
            if ($entry.FullName.EndsWith('/') -or $entry.FullName.EndsWith('\')) { continue }
            if (-not $entries.Add($name)) { throw "Duplicate ZIP entry: $name" }
            if ($name -ceq 'manifest.json') { $manifestEntry = $entry }
        }
        if (-not $manifestEntry) { throw 'Setup ZIP has no manifest.json.' }
        $stream = $manifestEntry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $manifestHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
        finally { $sha.Dispose(); $stream.Dispose() }
        if ((Get-SetupSha256 (Join-Path $Root 'manifest.json')) -ne $manifestHash) { throw 'Extracted manifest differs from the pinned Setup ZIP.' }
        $manifest = [IO.File]::ReadAllText((Join-Path $Root 'manifest.json')) | ConvertFrom-Json
        if ($manifest.version.TrimStart('v') -cne $ReleaseTag.Substring(1)) { throw 'Setup manifest version differs from the release tag.' }
        if ($entries.Count -ne @($manifest.files).Count + 1) { throw 'ZIP inventory differs from its manifest.' }
        foreach ($file in $manifest.files) { if (-not $entries.Contains([string]$file.path)) { throw "ZIP file missing: $($file.path)" } }
        Assert-SetupManifest $Root $manifest
    } finally { $zip.Dispose() }
}
