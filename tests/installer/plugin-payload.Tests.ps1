#Requires -Version 5.1
# Exercise the SDK's real dependency-copy targets without requiring AutoCAD.
[CmdletBinding()]
param([string]$ResultPath, [string]$TestRootParent = [IO.Path]::GetTempPath())
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../../scripts/setup-validation.ps1')
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$testRoot = Join-Path $TestRootParent ('dwg-plugin-payload-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$results = New-Object System.Collections.Generic.List[object]
function Assert($Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Test([string]$Name, [scriptblock]$Body) {
    try { & $Body; $results.Add([pscustomobject]@{name=$Name;passed=$true}); Write-Host "PASS $Name" }
    catch { $results.Add([pscustomobject]@{name=$Name;passed=$false;error=$_.Exception.Message}); Write-Host "FAIL $Name : $_" }
}
function Assert-Throws([scriptblock]$Body, [string]$Pattern) {
    $message = $null
    try { & $Body | Out-Null } catch { $message = $_.Exception.Message }
    Assert ($null -ne $message -and $message -match $Pattern) "Expected '$Pattern', got '$message'"
}
# Load definitions only: never run the installer's main body or touch user profiles.
foreach ($file in @('scripts/plugin-payload.ps1', 'scripts/install.ps1')) {
    $path = Join-Path $repoRoot $file
    if (-not (Test-Path -LiteralPath $path)) { continue }
    $tokens = $null; $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count) { throw ($parseErrors | Out-String) }
    foreach ($fn in $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true)) {
        . ([scriptblock]::Create($fn.Extent.Text))
    }
}
$script:YearTfm = @{2024='net48'; 2027='net10.0-windows'}
$script:DwgMcpProductCode = '{8A3F7B2D-1C4E-4D5F-9A6B-7E8C9D0F1A2B}'
$script:bundleSourceDir = $null
$runtimeDlls = @('Microsoft.CodeAnalysis.dll', 'Microsoft.CodeAnalysis.CSharp.dll',
    'Microsoft.CodeAnalysis.Scripting.dll', 'Microsoft.CodeAnalysis.CSharp.Scripting.dll', 'Newtonsoft.Json.dll')
function New-PayloadFixture([string]$Path) {
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    foreach ($dll in @($runtimeDlls) + @('Bimwright.Dwg.Plugin.Acad27.dll', 'System.Collections.Immutable.dll')) {
        Set-Content -LiteralPath (Join-Path $Path $dll) -Value 'presence-check fixture'
    }
    $deps = @{runtimeTarget=@{name='.NETCoreApp,Version=v10.0'}; targets=@{
        '.NETCoreApp,Version=v10.0'=@{'Transitive/1.0'=@{runtime=@{'lib/net8.0/System.Collections.Immutable.dll'=@{}}}}
    }}
    $deps | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Path 'Bimwright.Dwg.Plugin.Acad27.deps.json')
}
try {
    foreach ($year in 2025..2027) {
        Test "AutoCAD ${year}: real SDK copy includes scripting dependencies" {
            $suffix = $year - 2000
            $project = Join-Path $repoRoot "src/plugin-acad$suffix/Bimwright.Dwg.Plugin.Acad$suffix.csproj"
            $caseDir = Join-Path $testRoot "$year"
            $payload = Join-Path $caseDir 'payload'
            New-Item -ItemType Directory -Path $payload -Force | Out-Null
            $restore = & dotnet restore $project --verbosity quiet 2>&1 | Out-String
            Assert ($LASTEXITCODE -eq 0) "Restore failed: $restore"
            $output = & dotnet msbuild $project -nologo -verbosity:quiet `
                '-target:ResolveLockFileCopyLocalFiles;_CopyFilesMarkedCopyLocal' `
                '-p:Configuration=Release' "-p:OutDir=$payload\" `
                "-p:ProjectAssetsCacheFile=$caseDir/assets.cache" `
                '-getProperty:CopyLocalLockFileAssemblies,EnableDynamicLoading' 2>&1 | Out-String
            Assert ($LASTEXITCODE -eq 0) "Dependency copy failed: $output"
            $missing = @($runtimeDlls | Where-Object { -not (Test-Path -LiteralPath (Join-Path $payload $_) -PathType Leaf) })
            Assert ($missing.Count -eq 0) "Missing runtime DLLs: $($missing -join ', ')"
            Assert (($output | ConvertFrom-Json).Properties.CopyLocalLockFileAssemblies -eq 'true') 'SDK disabled dependency copying'
        }
    }
    $fixtureRepo = Join-Path $testRoot 'repo'
    $repoPayload = Join-Path $fixtureRepo 'src/plugin-acad27/bin/Release/net10.0-windows'
    New-PayloadFixture $repoPayload
    Test 'Repo install accepts a complete payload' {
        Assert ((Assert-RepoPayload $fixtureRepo 2027 Release) -eq $repoPayload) 'Unexpected payload directory'
    }
    Test 'Repo install rejects the reported missing scripting DLL' {
        $path = Join-Path $repoPayload 'Microsoft.CodeAnalysis.CSharp.Scripting.dll'
        $contents = Get-Content -LiteralPath $path -Raw
        Remove-Item -LiteralPath $path
        try { Assert-Throws { Assert-RepoPayload $fixtureRepo 2027 Release } 'Microsoft.CodeAnalysis.CSharp.Scripting.dll' }
        finally { Set-Content -LiteralPath $path -Value $contents }
    }
    Test 'Modern payload requires its dependency manifest' {
        $path = Join-Path $repoPayload 'Bimwright.Dwg.Plugin.Acad27.deps.json'
        $contents = Get-Content -LiteralPath $path -Raw
        Remove-Item -LiteralPath $path
        try { Assert-Throws { Assert-RepoPayload $fixtureRepo 2027 Release } 'deps.json' }
        finally { Set-Content -LiteralPath $path -Value $contents }
    }
    $bundle = Join-Path $testRoot 'bundle'
    $bundlePayload = Join-Path $bundle 'Contents/2027'
    New-PayloadFixture $bundlePayload
    Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts/PackageContents.xml') -Destination $bundle
    Test 'Setup install accepts a complete payload' { Assert-BundlePackage $bundle @(2027) }
    Test 'Setup install rejects a missing declared transitive runtime DLL' {
        $path = Join-Path $bundlePayload 'System.Collections.Immutable.dll'
        $contents = Get-Content -LiteralPath $path -Raw
        Remove-Item -LiteralPath $path
        try { Assert-Throws { Assert-BundlePackage $bundle @(2027) } 'System.Collections.Immutable.dll' }
        finally { Set-Content -LiteralPath $path -Value $contents }
    }
    Test 'Installed verification rejects missing compiler dependencies' {
        $path = Join-Path $bundlePayload 'Microsoft.CodeAnalysis.dll'
        $contents = Get-Content -LiteralPath $path -Raw
        Remove-Item -LiteralPath $path
        try { Assert-Throws { Assert-InstalledPlugin @(2027) $bundle } 'Microsoft.CodeAnalysis.dll' }
        finally { Set-Content -LiteralPath $path -Value $contents }
    }
    $legacyPayload = Join-Path $testRoot 'net48'
    New-Item -ItemType Directory -Path $legacyPayload -Force | Out-Null
    $legacyDlls = @($runtimeDlls) + @('Bimwright.Dwg.Plugin.Acad24.dll', 'System.Buffers.dll',
        'System.Collections.Immutable.dll', 'System.Memory.dll', 'System.Numerics.Vectors.dll',
        'System.Reflection.Metadata.dll', 'System.Runtime.CompilerServices.Unsafe.dll',
        'System.Text.Encoding.CodePages.dll', 'System.Threading.Tasks.Extensions.dll')
    foreach ($dll in $legacyDlls) { Set-Content -LiteralPath (Join-Path $legacyPayload $dll) -Value 'presence-check fixture' }
    Test 'net48 payload works without a dependency manifest' { Assert-PluginPayload $legacyPayload 2024 }
    Test 'net48 payload requires Roslyn transitive dependencies' {
        Remove-Item -LiteralPath (Join-Path $legacyPayload 'System.Collections.Immutable.dll')
        Assert-Throws { Assert-PluginPayload $legacyPayload 2024 } 'System.Collections.Immutable.dll'
    }
}
finally {
    if ($ResultPath) { $results.ToArray() | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ResultPath -Encoding UTF8 }
    # Retain the exact dependency payload for inspection.
    Write-Host "Fixtures: $testRoot"
}
if (@($results | Where-Object { -not $_.passed }).Count) { exit 1 }
