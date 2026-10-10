#Requires -Version 5.1
# No Pester dependency. Execute production functions against isolated temporary files.
# Profile env vars (USERPROFILE, APPDATA, LOCALAPPDATA) are redirected to a
# sandbox under the test root for the whole run, so a function that ignores its
# fixture path cannot reach real user data.
[CmdletBinding()]
param([string]$ResultPath, [string]$TestRootParent = [IO.Path]::GetTempPath())
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path $TestRootParent ('dwg-relocate-tests-' + [guid]::NewGuid().ToString('N'))
$sandboxUserProfile = Join-Path $testRoot 'profile\userprofile'
$sandboxAppData = Join-Path $testRoot 'profile\appdata'
$sandboxLocalAppData = Join-Path $testRoot 'profile\localappdata'
New-Item -ItemType Directory -Path $sandboxUserProfile, $sandboxAppData, $sandboxLocalAppData -Force | Out-Null
$savedUserProfile = $env:USERPROFILE
$savedAppData = $env:APPDATA
$savedLocalAppData = $env:LOCALAPPDATA
$env:USERPROFILE = $sandboxUserProfile
$env:APPDATA = $sandboxAppData
$env:LOCALAPPDATA = $sandboxLocalAppData
if ($env:USERPROFILE -ne $sandboxUserProfile -or $env:APPDATA -ne $sandboxAppData -or $env:LOCALAPPDATA -ne $sandboxLocalAppData) {
    throw 'Profile env redirection failed'
}
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
$installer = Join-Path $repoRoot 'scripts\install.ps1'
$uninstaller = Join-Path $repoRoot 'scripts\uninstall-all.ps1'
foreach ($script in @($installer, $uninstaller)) {
    $tokens = $null; $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile(
        $script, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count) { throw ($parseErrors | Out-String) }
    # Dot-source only function definitions; the script bodies run installs and
    # prompts. uninstall-all.ps1 loads last so its standalone copies (e.g.
    # Remove-ServerCopy) win - they are identical on purpose.
    foreach ($fn in $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true)) {
        . ([scriptblock]::Create($fn.Extent.Text))
    }
}
# Step4 must take the fixture root explicitly; without it the function falls
# back to the real profile and the tests would delete user data.
if (-not (Get-Command Invoke-Step4-Discovery).Parameters.ContainsKey('Root')) {
    throw 'Invoke-Step4-Discovery does not accept -Root; refusing to run'
}
$results = New-Object System.Collections.Generic.List[object]
$script:handled = @()
$script:skipped = @()
$script:failed  = @()
function Assert($Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Test([string]$Name, [scriptblock]$Body) {
    try { & $Body; $results.Add([pscustomobject]@{name=$Name;passed=$true}); Write-Host "PASS $Name" }
    catch { $results.Add([pscustomobject]@{name=$Name;passed=$false;error=$_.Exception.Message}); Write-Host "FAIL $Name : $_" }
}
function Assert-Throws([scriptblock]$Body, [string]$Pattern) {
    $message = $null
    try { & $Body | Out-Null } catch { $message = $_.Exception.Message }
    Assert ($null -ne $message -and $message -match $Pattern) "Expected error matching '$Pattern', got '$message'"
}
function New-LegacyRootFixture {
    # The pre-rename %LOCALAPPDATA%\Bimwright\Dwg\ tree: config, settings,
    # ToolBaker data, discovery files and server copies under server\<name>.
    $old = Join-Path $sandboxLocalAppData 'Bimwright\Dwg'
    New-Item -ItemType Directory -Path "$old/baked/x", "$old/server/current", "$old/server/0.5.0", "$old/captures" -Force | Out-Null
    Set-Content -LiteralPath "$old/dwgmcp.config.json" '{"custom":true}'
    Set-Content -LiteralPath "$old/settings.json" '{"toast":true}'
    Set-Content -LiteralPath "$old/baked/x/tool.json" '{}'
    Set-Content -LiteralPath "$old/acad-2024.json" '{}'
    Set-Content -LiteralPath "$old/captures/c.png" 'png'
    Set-Content -LiteralPath "$old/server/current/dwg-mcp.exe" 'old-server-current'
    Set-Content -LiteralPath "$old/server/0.5.0/dwg-mcp.exe" 'old-server-050'
    return $old
}
function Clear-ProductRoots {
    # The sandbox profile is shared for the whole run; relocation tests must
    # not inherit each other's roots.
    Remove-Item -LiteralPath (Join-Path $sandboxLocalAppData 'Bimwright') -Recurse -Force -ErrorAction SilentlyContinue
}
function Invoke-Step4 {
    [CmdletBinding(SupportsShouldProcess=$true)]
    param([string]$Root, [switch]$KeepLogs, [switch]$Purge)
    Invoke-Step4-Discovery -Root $Root
}
function Invoke-Wire {
    [CmdletBinding(SupportsShouldProcess=$true)]
    param([string]$Path, [string]$Exe)
    Set-McpConfigEntry -Path $Path -RootKey 'mcpServers' -EntryKind 'standard' -Exe $Exe
}
try {
    Test 'Move-LegacyProductRoot: absent old root is a no-op' {
        Clear-ProductRoots
        $script:installChanges = New-Object System.Collections.Generic.List[object]
        $script:installStage = $null
        Move-LegacyProductRoot -LocalAppData $sandboxLocalAppData
        Assert ($script:installChanges.Count -eq 0) 'recorded a change for nothing'
        Assert (-not (Test-Path -LiteralPath "$sandboxLocalAppData/Bimwright")) 'created the family root for nothing'
        $script:installChanges = $null
    }
    Test 'Move-LegacyProductRoot: relocates the whole old root byte-identically' {
        Clear-ProductRoots
        $old = New-LegacyRootFixture
        $new = Join-Path $sandboxLocalAppData 'Bimwright\dwg-mcp'
        $script:installChanges = New-Object System.Collections.Generic.List[object]
        $script:installStage = $null
        Move-LegacyProductRoot -LocalAppData $sandboxLocalAppData
        Assert (-not (Test-Path -LiteralPath $old)) 'old root still present'
        Assert ((Get-Content -LiteralPath "$new/dwgmcp.config.json" -Raw).Contains('custom')) 'config not relocated'
        Assert ((Get-Content -LiteralPath "$new/settings.json" -Raw).Contains('toast')) 'settings not relocated'
        Assert ((Get-Content -LiteralPath "$new/baked/x/tool.json" -Raw).Trim() -eq '{}') 'baked data not relocated'
        Assert (Test-Path -LiteralPath "$new/acad-2024.json") 'discovery file not relocated'
        Assert ((Get-Content -LiteralPath "$new/server/current/dwg-mcp.exe" -Raw).Trim() -eq 'old-server-current') 'server current not relocated'
        Assert ((Get-Content -LiteralPath "$new/server/0.5.0/dwg-mcp.exe" -Raw).Trim() -eq 'old-server-050') 'versioned server not relocated'
        Assert (@($script:installChanges | Where-Object { $_.Kind -eq 'Relocate' }).Count -eq 1) 'rename not recorded as a Relocate entry'
        $script:installChanges = $null
        Clear-ProductRoots
    }
    Test 'Move-LegacyProductRoot: both roots present throws before any move' {
        Clear-ProductRoots
        $old = New-LegacyRootFixture
        $new = Join-Path $sandboxLocalAppData 'Bimwright\dwg-mcp'
        New-Item -ItemType Directory -Path $new -Force | Out-Null
        $script:installChanges = New-Object System.Collections.Generic.List[object]
        $script:installStage = $null
        Assert-Throws { Move-LegacyProductRoot -LocalAppData $sandboxLocalAppData } 'Move or remove'
        Assert (Test-Path -LiteralPath "$old/server/current") 'old root moved despite the conflict'
        Assert ($script:installChanges.Count -eq 0) 'a partial move was recorded'
        $script:installChanges = $null
        Clear-ProductRoots
    }
    Test 'Move-LegacyProductRoot -WhatIf prints the planned move and writes nothing' {
        Clear-ProductRoots
        $old = New-LegacyRootFixture
        $script:installChanges = New-Object System.Collections.Generic.List[object]
        $script:installStage = $null
        $output = Move-LegacyProductRoot -LocalAppData $sandboxLocalAppData -WhatIf 6>&1 | Out-String
        Assert ($output -match 'preview move') 'planned move not previewed'
        Assert (Test-Path -LiteralPath "$old/server/current/dwg-mcp.exe") 'WhatIf moved files'
        Assert (-not (Test-Path -LiteralPath "$sandboxLocalAppData/Bimwright/dwg-mcp")) 'WhatIf created the new root'
        $script:installChanges = $null
        Clear-ProductRoots
    }
    Test 'Undo-InstallChanges restores a relocation byte-identically' {
        Clear-ProductRoots
        $old = New-LegacyRootFixture
        $new = Join-Path $sandboxLocalAppData 'Bimwright\dwg-mcp'
        $script:installChanges = New-Object System.Collections.Generic.List[object]
        $script:installStage = $null
        Move-LegacyProductRoot -LocalAppData $sandboxLocalAppData
        Undo-InstallChanges
        Assert (-not (Test-Path -LiteralPath $new)) 'undo left the new root behind'
        Assert ((Get-Content -LiteralPath "$old/dwgmcp.config.json" -Raw).Contains('custom')) 'undo lost config data'
        Assert ((Get-Content -LiteralPath "$old/server/current/dwg-mcp.exe" -Raw).Trim() -eq 'old-server-current') 'undo did not restore the server'
        Assert ((Get-Content -LiteralPath "$old/server/0.5.0/dwg-mcp.exe" -Raw).Trim() -eq 'old-server-050') 'undo lost the versioned server'
        Assert ((Get-Content -LiteralPath "$old/acad-2024.json" -Raw).Contains('{}')) 'undo lost discovery data'
        # The Bimwright parent is a shared family root - never removed by undo.
        Assert (Test-Path -LiteralPath "$sandboxLocalAppData/Bimwright") 'family parent removed by undo'
        $script:installChanges = $null
        Clear-ProductRoots
    }
    Test 'Client repoint: old-root and versioned paths repoint; custom launcher kept' {
        Clear-ProductRoots
        $cfg = Join-Path $testRoot 'mcp.json'
        $exe = "$sandboxLocalAppData\Bimwright\dwg-mcp\server\current\dwg-mcp.exe"
        foreach ($old in @('C:\\Users\\Someone\\AppData\\Local\\Bimwright\\Dwg\\server\\current\\dwg-mcp.exe',
                          'C:\\Users\\Someone\\AppData\\Local\\Bimwright\\Dwg\\server\\0.5.0\\dwg-mcp.exe',
                          'C:\\Users\\Someone\\AppData\\Local\\Bimwright\\dwg-mcp\\server\\0.6.0\\dwg-mcp.exe')) {
            Set-Content -LiteralPath $cfg -Value ('{ "mcpServers": { "dwg-mcp": { "command": "' + $old + '", "args": ["--read-only"] } } }')
            $res = Invoke-Wire -Path $cfg -Exe $exe
            Assert ($res -eq 'repointed') "expected repointed for $old, got: $res"
            $j = Get-Content -LiteralPath $cfg -Raw | ConvertFrom-Json
            Assert ($j.mcpServers.'dwg-mcp'.command -eq $exe) "$old not repointed to exactly $exe, got: $($j.mcpServers.'dwg-mcp'.command)"
            Assert ($j.mcpServers.'dwg-mcp'.args[0] -eq '--read-only') 'existing args lost'
        }
        # The new root's own current\ is never repointed.
        Set-Content -LiteralPath $cfg -Value ('{ "mcpServers": { "dwg-mcp": { "command": "' + $exe.Replace('\', '\\') + '" } } }')
        $res = Invoke-Wire -Path $cfg -Exe $exe
        Assert ($res -eq 'already') "expected already for the current path, got: $res"
        # A custom launcher is reported and left alone.
        Set-Content -LiteralPath $cfg -Value '{ "mcpServers": { "dwg-mcp": { "command": "C:\\tools\\my-wrapper.cmd" } } }'
        $res = Invoke-Wire -Path $cfg -Exe $exe
        Assert ($res -eq 'custom') "expected custom, got: $res"
        Assert ((Get-Content -LiteralPath $cfg -Raw).Contains('my-wrapper.cmd')) 'custom launcher clobbered'
    }
    Test 'Uninstall default removes server copies and discovery, keeps personal data' {
        $root = New-LegacyRootFixture
        $script:handled=@(); $script:skipped=@(); $script:failed=@()
        Invoke-Step4 -Root $root
        Assert (-not (Test-Path -LiteralPath "$root/server")) 'server copies not removed'
        Assert (-not (Test-Path -LiteralPath "$root/acad-2024.json")) 'discovery file not removed'
        Assert (Test-Path -LiteralPath "$root/dwgmcp.config.json") 'config removed'
        Assert (Test-Path -LiteralPath "$root/settings.json") 'settings removed'
        Assert (Test-Path -LiteralPath "$root/baked/x/tool.json") 'baked data removed'
        Assert (Test-Path -LiteralPath "$root/captures/c.png") 'captures removed'
        Assert ($script:handled -contains 'step4-discovery') 'step4 not reported'
    }
    Test 'Uninstall -Purge removes product roots but never the Bimwright parent or siblings' {
        $fam = Join-Path $sandboxLocalAppData 'Bimwright'
        New-Item -ItemType Directory -Path "$fam/ipt-mcp", "$fam/nwd-mcp", "$fam/rvt-mcp" -Force | Out-Null
        Set-Content -LiteralPath "$fam/ipt-mcp/keep.txt" 'k'
        Set-Content -LiteralPath "$fam/nwd-mcp/keep.txt" 'k'
        Set-Content -LiteralPath "$fam/rvt-mcp/keep.txt" 'k'
        $old = New-LegacyRootFixture
        $new = Join-Path $fam 'dwg-mcp'
        New-Item -ItemType Directory -Path "$new/server/current" -Force | Out-Null
        Set-Content -LiteralPath "$new/server/current/dwg-mcp.exe" 'exe'
        Set-Content -LiteralPath "$new/dwgmcp.config.json" '{}'
        $script:handled=@(); $script:skipped=@(); $script:failed=@()
        Invoke-Step4 -Root $new -Purge
        Invoke-Step4 -Root $old -Purge
        Assert (-not (Test-Path -LiteralPath $new)) 'new root survived -Purge'
        Assert (-not (Test-Path -LiteralPath $old)) 'legacy root survived -Purge'
        Assert (Test-Path -LiteralPath $fam) 'Bimwright family parent removed'
        foreach ($sib in 'ipt-mcp', 'nwd-mcp', 'rvt-mcp') {
            Assert ((Get-Content -LiteralPath "$fam/$sib/keep.txt" -Raw).Trim() -eq 'k') "$sib sibling touched"
        }
        Clear-ProductRoots
    }
} finally {
    $env:USERPROFILE = $savedUserProfile
    $env:APPDATA = $savedAppData
    $env:LOCALAPPDATA = $savedLocalAppData
    $report = [ordered]@{powershell=$PSVersionTable.PSVersion.ToString();installerSha256=(Get-FileHash $installer -Algorithm SHA256).Hash;uninstallerSha256=(Get-FileHash $uninstaller -Algorithm SHA256).Hash;results=@($results.ToArray())}
    if ($ResultPath) { $report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $ResultPath -Encoding UTF8 }
    # Only remove the uniquely created test directory under the resolved temp root.
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $tempRoot = [IO.Path]::GetFullPath($TestRootParent).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
if (@($results | Where-Object { -not $_.passed }).Count) { throw 'Installer relocation regression tests failed' }
