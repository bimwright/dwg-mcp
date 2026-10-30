#Requires -Version 5.1
# Production functions + main transaction in a redirected profile. CAD and the
# server startup probe are doubles; no installed host/client is modified.
[CmdletBinding()]
param([string]$ResultPath, [string]$TestRootParent = [IO.Path]::GetTempPath(), [string]$NamePattern = '.*')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$installer = Join-Path $repo 'scripts/install.ps1'
$testRoot = [IO.Path]::GetFullPath((Join-Path $TestRootParent ('dwg-upgrade-' + [guid]::NewGuid().ToString('N'))))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$savedProfile = @{USERPROFILE=$env:USERPROFILE;APPDATA=$env:APPDATA;LOCALAPPDATA=$env:LOCALAPPDATA}
. (Join-Path $repo 'scripts/setup-validation.ps1')
. (Join-Path $repo 'scripts/plugin-payload.ps1')
$tokens=$null; $errors=$null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($installer,[ref]$tokens,[ref]$errors)
if ($errors.Count) { throw ($errors | Out-String) }
foreach ($fn in $ast.FindAll({param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst]}, $true)) { . ([scriptblock]::Create($fn.Extent.Text)) }
$text = [IO.File]::ReadAllText($installer)
$main = [scriptblock]::Create($text.Substring($text.IndexOf('# --- main ')))
$realSet = (Get-Command Set-InstallPath).ScriptBlock
$realSeed = (Get-Command Set-DefaultToolsetsConfig).ScriptBlock
$realSave = (Get-Command Save-McpConfigText).ScriptBlock
$realVerify = (Get-Command Assert-InstalledPlugin).ScriptBlock
$realComplete = (Get-Command Complete-LegacyMigration).ScriptBlock
$results = New-Object System.Collections.Generic.List[object]
function Assert($Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Throws([scriptblock]$Body, [string]$Pattern) {
    $message=$null
    try { & $Body | Out-Null } catch { $message=$_.Exception.Message }
    Assert ($message -and $message -match $Pattern) "Expected '$Pattern'; got '$message'"
    return $message
}
function Test([string]$Name, [scriptblock]$Body) {
    if ($Name -notmatch $NamePattern) { return }
    try { & $Body; $results.Add([pscustomobject]@{name=$Name;passed=$true}); Write-Host "PASS $Name" }
    catch { $results.Add([pscustomobject]@{name=$Name;passed=$false;error=$_.Exception.Message;stack=$_.ScriptStackTrace}); Write-Host "FAIL $Name : $_" }
    finally { if ($script:held) { $script:held.Dispose(); $script:held=$null } }
}
function Put([string]$Path, [string]$Text) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    [IO.File]::WriteAllText($Path,$Text,(New-Object Text.UTF8Encoding($false)))
}
function Tree([string]$Path) {
    if (-not [IO.Directory]::Exists($Path)) { return '' }
    $prefix=$Path.TrimEnd('\')+'\'
    return (@(Get-SetupFiles $Path | Sort-Object | ForEach-Object { $_.Substring($prefix.Length)+'|'+(Get-SetupSha256 $_) }) -join "`n")
}
function Write-FixtureManifest {
    $files=@(Get-SetupFiles $script:SourceDir | Where-Object { [IO.Path]::GetFileName($_) -ne 'manifest.json' } | Sort-Object | ForEach-Object {
        [ordered]@{path=$_.Substring($script:SourceDir.Length+1).Replace('\','/');sha256=(Get-SetupSha256 $_);bytes=(Get-Item -LiteralPath $_).Length}
    })
    $script:manifest=[pscustomobject]@{name='DwgMcp.Setup';version='99.0.0';platform='win-x64';packedAutocadYears=@(2024);supportedAutocadYears=@(2024);
        server=[pscustomobject]@{command='server/dwg-mcp.exe';selfContained=$true;requiresDotnet=$false};files=$files}
    Put (Join-Path $script:SourceDir 'manifest.json') ($script:manifest | ConvertTo-Json -Depth 20)
}
function New-Fixture([bool]$Legacy=$false) {
    $script:caseRoot=Join-Path $testRoot ([guid]::NewGuid().ToString('N'))
    $env:USERPROFILE=Join-Path $script:caseRoot 'profile/user'
    $env:APPDATA=Join-Path $script:caseRoot 'profile/roaming'
    $env:LOCALAPPDATA=Join-Path $script:caseRoot 'profile/local'
    $script:SourceDir=Join-Path $script:caseRoot 'package'
    $script:bundleSourceDir=Join-Path $script:SourceDir 'bundle'
    $script:serverSourceDir=Join-Path $script:SourceDir 'server'
    $script:bundleTargetRoot=Join-Path $env:APPDATA 'Autodesk/ApplicationPlugins/Bimwright.Dwg.bundle'
    $script:machineBundleTarget=Join-Path $script:caseRoot 'machine/Bimwright.Dwg.bundle'
    $script:oldRoot=Join-Path $env:LOCALAPPDATA 'Bimwright/Dwg'
    $script:newRoot=Join-Path $env:LOCALAPPDATA 'Bimwright/dwg-mcp'
    $script:ServerInstallRoot=Join-Path $script:newRoot 'server/current'
    $script:cursor=Join-Path $env:USERPROFILE '.cursor/mcp.json'
    $script:cline=Join-Path $env:APPDATA 'Code/User/cline.json'
    $script:FailAt=''; $script:replacements=0; $script:configWrites=0; $script:probes=0; $script:held=$null
    $script:Years=@(2024); $script:Client=@('none'); $script:Uninstall=$false; $script:Version=$null
    $script:Config='Debug'; $script:hasSetupLayout=$true; $script:setupVersion='99.0.0'
    $script:SetupZip=$null; $script:ReleaseTag=$null; $script:PruneOldServers=$false
    $script:installChanges=$null; $script:installStage=$null; $script:legacyRootRelocated=$false
    $script:DwgMcpProductCode='{8A3F7B2D-1C4E-4D5F-9A6B-7E8C9D0F1A2B}'
    $script:YearTfm=@{2024='net48'}
    foreach ($scriptName in @('install.ps1','plugin-payload.ps1','setup-validation.ps1')) {
        Put (Join-Path $script:SourceDir $scriptName) ([IO.File]::ReadAllText((Join-Path $repo "scripts/$scriptName")))
    }
    New-Item -ItemType Directory -Path $script:bundleSourceDir -Force | Out-Null
    Save-BundleManifest -OutPath (Join-Path $script:bundleSourceDir 'PackageContents.xml') -CoverYears @(2024) -TemplatePath (Join-Path $repo 'scripts/PackageContents.xml')
    foreach ($dll in @('Bimwright.Dwg.Plugin.Acad24.dll','Microsoft.CodeAnalysis.dll','Microsoft.CodeAnalysis.CSharp.dll',
        'Microsoft.CodeAnalysis.Scripting.dll','Microsoft.CodeAnalysis.CSharp.Scripting.dll','Newtonsoft.Json.dll',
        'System.Buffers.dll','System.Collections.Immutable.dll','System.Memory.dll','System.Numerics.Vectors.dll',
        'System.Reflection.Metadata.dll','System.Runtime.CompilerServices.Unsafe.dll','System.Text.Encoding.CodePages.dll','System.Threading.Tasks.Extensions.dll')) {
        Put (Join-Path $script:bundleSourceDir "Contents/2024/$dll") ('new-fixture-'+$dll)
    }
    Put (Join-Path $script:serverSourceDir 'dwg-mcp.exe') 'new-server-fixture-not-executable'
    Put (Join-Path $script:serverSourceDir 'dependency.dll') 'new-server-dependency'
    New-Item -ItemType Directory -Path $script:bundleTargetRoot -Force | Out-Null
    Save-BundleManifest -OutPath (Join-Path $script:bundleTargetRoot 'PackageContents.xml') -CoverYears @(2024,2027) -TemplatePath (Join-Path $repo 'scripts/PackageContents.xml')
    Put (Join-Path $script:bundleTargetRoot 'Contents/2024/Bimwright.Dwg.Plugin.Acad24.dll') 'old-plugin24'
    Put (Join-Path $script:bundleTargetRoot 'Contents/2027/Bimwright.Dwg.Plugin.Acad27.dll') 'preserve-unselected27'
    $data=if($Legacy){$script:oldRoot}else{$script:newRoot}
    Put (Join-Path $data 'server/current/dwg-mcp.exe') 'old-server'
    Put (Join-Path $data 'dwgmcp.config.json') '{"custom":{"keep":42}}'
    Put (Join-Path $data 'settings.json') '{"autoStartTransport":false,"toastIdleSeconds":30}'
    Put (Join-Path $data 'baked/bake.db') 'personal-toolbaker-data'
    Put (Join-Path $data 'captures/personal.png') 'personal-capture'
    $legacyCommand=(Join-Path $script:oldRoot 'server/0.5.0/dwg-mcp.exe').Replace('\','\\')
    Put $script:cursor ('{ /*keep comment*/ "mcpServers": { "dwg-mcp": {"command":"'+$legacyCommand+'","args":["--read-only"],"env":{"KEEP":"value"}},"other":{"command":"keep"}} }')
    Put $script:cline '{"mcpServers":{"other":{"command":"keep"}}}'
    Put ($script:cursor+'.bak') 'pre-existing-backup-preserve'
    Write-FixtureManifest
}
# Tests use only these scoped clients; no real client CLI can be discovered.
function Get-McpClientSpecs {
    @([pscustomobject]@{Name='cursor';Kind='file';Paths=@($script:cursor);DetectPaths=@((Split-Path $script:cursor));RootKey='mcpServers';EntryKind='standard'},
      [pscustomobject]@{Name='cline';Kind='file';Paths=@($script:cline);DetectPaths=@((Split-Path $script:cline));RootKey='mcpServers';EntryKind='standard'},
      [pscustomobject]@{Name='grok';Kind='cli';Cli='unavailable-fixture-cli'})
}
function Assert-AutoCadClosed { }
function Set-InstallPath([string]$Source,[string]$Destination) {
    & $realSet -Source $Source -Destination $Destination
    $script:replacements++
    if ($script:FailAt -eq "replace-$($script:replacements)") { throw 'injected replacement failure' }
    if ($script:FailAt -eq 'corrupt-plugin' -and $Destination -match 'Contents[\\/]2024$') { Put (Join-Path $Destination 'Microsoft.CodeAnalysis.dll') 'tampered' }
    if ($script:FailAt -eq 'corrupt-server' -and $Destination -eq $script:ServerInstallRoot) { Put (Join-Path $Destination 'dependency.dll') 'tampered' }
}
function Set-DefaultToolsetsConfig { [CmdletBinding(SupportsShouldProcess=$true)]param([string]$ConfigPath)
    $result=& $realSeed -ConfigPath $ConfigPath
    if ($script:FailAt -eq 'config-seed') { throw 'injected config seed failure' }; return $result
}
function Save-McpConfigText([string]$Path,[string]$NewText,[string]$OldText) {
    & $realSave $Path $NewText $OldText
    $script:configWrites++
    if ($script:FailAt -eq "client-$($script:configWrites)") { throw 'injected client failure' }
}
function Test-ServerExecutable([string]$Path) {
    $script:probes++
    Assert ([IO.File]::Exists($Path)) 'Missing staged fixture server'
    if ($script:FailAt -eq 'server-check') { throw 'injected server startup failure' }
}
function Assert-InstalledPlugin([int[]]$InstallYears,[string]$BundleDir) {
    & $realVerify -InstallYears $InstallYears -BundleDir $BundleDir
    if ($script:FailAt -eq 'lock-client') { $script:held=New-Object IO.FileStream($script:cursor,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None); throw 'injected after client edit' }
    if ($script:FailAt -eq 'corrupt-backup') {
        $backup=($script:installChanges | Where-Object Path -eq $script:ServerInstallRoot).Backup
        Put (Join-Path $backup 'dwg-mcp.exe') 'corrupted recovery fixture'; throw 'injected recovery corruption'
    }
    if ($script:FailAt -eq 'recreated-legacy') { Put (Join-Path $script:oldRoot 'concurrent.txt') 'concurrent-data'; throw 'injected recreated old root' }
    if ($script:FailAt -eq 'final-verify') { throw 'injected final verification failure' }
}
function Complete-LegacyMigration {
    & $realComplete
    if ($script:FailAt -eq 'marker-commit') { throw 'injected after marker' }
}
function Invoke-FixtureInstall { [CmdletBinding(SupportsShouldProcess=$true)]param() . $main }
function Make-Zip {
    $script:zipPath=Join-Path $script:caseRoot 'DwgMcp.Setup-v99.0.0-win-x64.zip'
    Compress-Archive -Path (Join-Path $script:SourceDir '*') -DestinationPath $script:zipPath
    Put ($script:zipPath+'.sha256') ((Get-SetupSha256 $script:zipPath)+'  '+[IO.Path]::GetFileName($script:zipPath)+"`n")
}
try {
    Test 'Complete manifest validates exact inventory and included years' { New-Fixture; Assert-SetupManifest $script:SourceDir $script:manifest }
    Test 'Payload tampering fails before any target change' { New-Fixture; $before=Tree (Join-Path $script:caseRoot 'profile'); Put (Join-Path $script:serverSourceDir 'dependency.dll') 'bad'; $null=Throws {Invoke-FixtureInstall} 'checksum'; Assert ((Tree (Join-Path $script:caseRoot 'profile')) -ceq $before) 'Targets changed' }
    Test 'Unlisted and missing manifest files are refused' {
        New-Fixture; Put (Join-Path $script:SourceDir 'extra.dll') 'extra'; $null=Throws {Assert-SetupManifest $script:SourceDir $script:manifest} 'Unlisted'
        Remove-Item -LiteralPath (Join-Path $script:SourceDir 'extra.dll'); Remove-Item -LiteralPath (Join-Path $script:serverSourceDir 'dependency.dll')
        $null=Throws {Assert-SetupManifest $script:SourceDir $script:manifest} 'missing'
    }
    Test 'Duplicate and traversal manifest paths are refused' {
        New-Fixture; $script:manifest.files+=@($script:manifest.files[0]); $null=Throws {Assert-SetupManifest $script:SourceDir $script:manifest} 'Duplicate'
        $script:manifest.files[0].path='../outside'; $null=Throws {Assert-SetupManifest $script:SourceDir $script:manifest} 'Invalid manifest path'
    }
    Test 'JSON year claims and bundle XML must match included payloads' {
        New-Fixture; $script:manifest.supportedAutocadYears=@(2024,2027); $null=Throws {Assert-SetupManifest $script:SourceDir $script:manifest} 'years'
        Write-FixtureManifest; [xml]$xml=[IO.File]::ReadAllText((Join-Path $script:bundleSourceDir 'PackageContents.xml'))
        $xml.ApplicationPackage.Components.ComponentEntry.ModuleName='./Contents/2027/Bimwright.Dwg.Plugin.Acad27.dll'
        $xml.Save((Join-Path $script:bundleSourceDir 'PackageContents.xml')); Write-FixtureManifest
        $null=Throws {Assert-SetupManifest $script:SourceDir $script:manifest} 'XML advertises'
    }
    Test 'Pinned ZIP verifies checksum and binds extracted manifest' {
        New-Fixture; Make-Zip; Assert-SetupArchive $script:zipPath 'v99.0.0' $script:SourceDir
        $null=Throws {Assert-SetupArchive $script:zipPath 'v99.0.1' $script:SourceDir} 'exact release tag'
        Put (Join-Path $script:serverSourceDir 'dependency.dll') 'changed'; Write-FixtureManifest
        $null=Throws {Assert-SetupArchive $script:zipPath 'v99.0.0' $script:SourceDir} 'Extracted manifest differs'
    }
    Test 'Bad ZIP checksum and unsafe ZIP entries are refused' {
        New-Fixture; Make-Zip; Put ($script:zipPath+'.sha256') ((('0'*64))+'  '+[IO.Path]::GetFileName($script:zipPath))
        $null=Throws {Assert-SetupArchive $script:zipPath 'v99.0.0' $script:SourceDir} 'checksum'
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive=[IO.Compression.ZipFile]::Open($script:zipPath,[IO.Compression.ZipArchiveMode]::Update)
        try { $null=$archive.CreateEntry('../outside.txt') } finally {$archive.Dispose()}
        Put ($script:zipPath+'.sha256') ((Get-SetupSha256 $script:zipPath)+'  '+[IO.Path]::GetFileName($script:zipPath))
        $null=Throws {Assert-SetupArchive $script:zipPath 'v99.0.0' $script:SourceDir} 'Invalid manifest path'
    }
    Test 'ZIP duplicate paths are refused after separator normalization' {
        New-Fixture; Make-Zip
        $archive=[IO.Compression.ZipFile]::Open($script:zipPath,[IO.Compression.ZipArchiveMode]::Update)
        try { $null=$archive.CreateEntry('server/dwg-mcp.exe'.Replace('/', '\')) ; $null=$archive.CreateEntry('SERVER/dwg-mcp.exe') }
        finally {$archive.Dispose()}
        Put ($script:zipPath+'.sha256') ((Get-SetupSha256 $script:zipPath)+'  '+[IO.Path]::GetFileName($script:zipPath))
        $null=Throws {Assert-SetupArchive $script:zipPath 'v99.0.0' $script:SourceDir} 'Duplicate ZIP entry'
    }
    Test 'Pinned ZIP WhatIf validates the package and changes no files' {
        New-Fixture $true; Make-Zip; $script:SetupZip=$script:zipPath; $script:ReleaseTag='v99.0.0'
        $before=Tree $script:caseRoot; Invoke-FixtureInstall -WhatIf
        Assert ((Tree $script:caseRoot) -ceq $before) 'Pinned ZIP preview wrote files'
    }
    Test 'Linked package paths are refused before any target change' {
        New-Fixture; $protected=Join-Path $script:caseRoot 'protected/keep.txt'; Put $protected 'keep'
        $null=New-Item -ItemType Junction -Path (Join-Path $script:serverSourceDir 'linked') -Target (Split-Path $protected)
        $before=Tree (Join-Path $script:caseRoot 'profile')
        $null=Throws {Invoke-FixtureInstall} 'refuses linked paths'
        Assert ((Tree (Join-Path $script:caseRoot 'profile')) -ceq $before) 'Linked package changed targets'
        Assert ([IO.File]::ReadAllText($protected) -ceq 'keep') 'Linked data changed'
    }
    foreach ($phase in @('replace-1','replace-2','replace-3','server-check','config-seed','client-1','client-2','final-verify','corrupt-plugin','corrupt-server','marker-commit')) {
        Test "Caught failure $phase restores all files and legacy root" {
            New-Fixture $true; $script:Client=@('cursor','cline'); $script:FailAt=$phase
            $before=Tree (Join-Path $script:caseRoot 'profile')
            $null=Throws {Invoke-FixtureInstall} 'injected|verification failed'
            Assert ((Tree (Join-Path $script:caseRoot 'profile')) -ceq $before) 'Rollback did not restore exact file bytes/inventory'
            Assert (-not [IO.Directory]::Exists($script:newRoot)) 'Relocated root was not restored'
        }
    }
    Test 'Success preserves data, pre-existing backups and unselected AutoCAD payload' {
        New-Fixture $true; $script:Client=@('cursor','cline'); Invoke-FixtureInstall
        Assert (([IO.File]::ReadAllText((Join-Path $script:newRoot 'baked/bake.db'))) -eq 'personal-toolbaker-data') 'Lost ToolBaker data'
        Assert (([IO.File]::ReadAllText((Join-Path $script:newRoot 'captures/personal.png'))) -eq 'personal-capture') 'Lost capture'
        Assert (([IO.File]::ReadAllText(($script:cursor+'.bak'))) -eq 'pre-existing-backup-preserve') 'Overwrote old .bak'
        $cfg=ConvertFrom-JsoncText ([IO.File]::ReadAllText($script:cursor))
        Assert ($cfg.mcpServers.'dwg-mcp'.command -eq (Join-Path $script:ServerInstallRoot 'dwg-mcp.exe')) 'Wrong repoint'
        Assert ($cfg.mcpServers.'dwg-mcp'.args[0] -eq '--read-only' -and $cfg.mcpServers.'dwg-mcp'.env.KEEP -eq 'value') 'Lost args/env'
        Assert ([IO.File]::ReadAllText($script:cursor).Contains('keep comment')) 'Lost JSONC comment'
        Assert (Test-LegacyMigrationMarker $script:oldRoot $script:newRoot) 'Missing committed migration marker'
        Assert ((Read-BundleManifest $script:bundleTargetRoot).Years -contains 2027) 'Lost unselected installed year in XML'
        Assert (([IO.File]::ReadAllText((Join-Path $script:bundleTargetRoot 'Contents/2027/Bimwright.Dwg.Plugin.Acad27.dll'))) -eq 'preserve-unselected27') 'Touched unselected payload'
    }
    Test 'Comma and space lists bind the same years/clients as arrays' {
        foreach ($years in @('2024,2027', '2024 2027', @(2024, 2027), @('2024', '2027'))) {
            $got=@(Resolve-YearsArgument $years)
            Assert (($got -join '|') -eq '2024|2027' -and $got[0] -is [int]) "Years '$years' -> '$($got -join '|')'"
        }
        Assert ((@(Resolve-ClientArgument 'cursor,cline') -join '|') -eq 'cursor|cline') 'Client comma list'
        Assert (@(Resolve-YearsArgument $null).Count -eq 0 -and @(Resolve-ClientArgument @()).Count -eq 0) 'Empty lists'
        $null=Throws {Resolve-YearsArgument '2024,2028'} "2022-2027; got '2028'"
        $null=Throws {Resolve-YearsArgument '20242027'} "got '20242027'"
        $null=Throws {Resolve-ClientArgument 'cursor,nope'} 'Unknown -Client value\(s\): nope'
    }
    Test 'powershell -File splits -Years and -Client lists before binding fails' {
        # Windows PowerShell 5.1 turns redirected native stderr into a terminating error under Stop.
        $ErrorActionPreference='Continue'
        New-Fixture; $shell=(Get-Process -Id $PID).Path; $script=Join-Path $script:SourceDir 'install.ps1'
        $years=& $shell -NoProfile -ExecutionPolicy Bypass -File $script -Years '2024,2028' -Client none -WhatIf 2>&1 | Out-String
        Assert ($years -match "2022-2027; got '2028'" -and $years -notmatch '20242028') "Years not split: $years"
        $clients=& $shell -NoProfile -ExecutionPolicy Bypass -File $script -Years 2024 -Client 'cursor,nope' -WhatIf 2>&1 | Out-String
        Assert ($clients -match 'Unknown -Client value\(s\): nope' -and $clients -notmatch 'does not belong to the set') "Client not split: $clients"
    }
    Test 'WhatIf previews exact years/clients and changes no files' {
        New-Fixture $true; $script:Client=@('cursor'); $before=Tree $script:caseRoot
        $preview=Invoke-FixtureInstall -WhatIf 6>&1 | Out-String
        Assert ($preview -match 'years=2024; clients=cursor') 'Missing scope preview'
        Assert ((Tree $script:caseRoot) -ceq $before) 'WhatIf wrote files'
    }
    Test 'Unmarked dual roots and foreign markers preserve both roots' {
        New-Fixture $true; Put (Join-Path $script:newRoot 'settings.json') 'independent current data'
        Put (Join-Path $script:newRoot '.migrated-from-rvtmcp-v2') 'foreign'
        $before=Tree (Join-Path $script:caseRoot 'profile'); $null=Throws {Invoke-FixtureInstall} 'Both .* exist'
        Assert ((Tree (Join-Path $script:caseRoot 'profile')) -ceq $before) 'Dual-root data changed'
    }
    Test 'Valid DWG marker keeps both roots separate without merging data' {
        New-Fixture $true; $script:installChanges=New-Object System.Collections.Generic.List[object]
        Move-LegacyProductRoot -LocalAppData $env:LOCALAPPDATA
        Complete-LegacyMigration
        Put (Join-Path $script:oldRoot 'legacy-recovery.txt') 'keep separately'
        $before=Tree (Join-Path $script:caseRoot 'profile'); Move-LegacyProductRoot -LocalAppData $env:LOCALAPPDATA
        Assert ((Tree (Join-Path $script:caseRoot 'profile')) -ceq $before) 'Marker route merged/changed roots'
        $marker=Join-Path $script:newRoot '.migrated-from-dwg-v1.json'; $m=[IO.File]::ReadAllText($marker) | ConvertFrom-Json; $m.legacy_root='wrong'; Put $marker ($m | ConvertTo-Json)
        $null=Throws {Move-LegacyProductRoot -LocalAppData $env:LOCALAPPDATA} 'Both .* exist'
    }
    Test 'Marked dual roots upgrade preserves independent data and committed marker' {
        New-Fixture $true; $script:installChanges=New-Object System.Collections.Generic.List[object]
        Move-LegacyProductRoot -LocalAppData $env:LOCALAPPDATA; Complete-LegacyMigration
        Put (Join-Path $script:oldRoot 'settings.json') 'independent-legacy-settings'
        Put (Join-Path $script:oldRoot 'baked/bake.db') 'independent-legacy-tools'
        $legacyBefore=Tree $script:oldRoot
        $settingsBefore=Get-SetupSha256 (Join-Path $script:newRoot 'settings.json')
        $markerBefore=Get-SetupSha256 (Join-Path $script:newRoot '.migrated-from-dwg-v1.json')
        Invoke-FixtureInstall
        Assert ((Tree $script:oldRoot) -ceq $legacyBefore) 'Merged or changed independent legacy data'
        Assert ((Get-SetupSha256 (Join-Path $script:newRoot 'settings.json')) -eq $settingsBefore) 'Changed current settings'
        Assert ((Get-SetupSha256 (Join-Path $script:newRoot '.migrated-from-dwg-v1.json')) -eq $markerBefore) 'Replaced committed marker'
        Assert ([IO.File]::ReadAllText((Join-Path $script:newRoot 'baked/bake.db')) -eq 'personal-toolbaker-data') 'Lost current ToolBaker data'
    }
    Test 'New client config is removed on caught failure without losing an existing backup' {
        New-Fixture $true; Remove-Item -LiteralPath $script:cursor
        $script:Client=@('cursor'); $script:FailAt='final-verify'
        $before=Tree (Join-Path $script:caseRoot 'profile')
        $null=Throws {Invoke-FixtureInstall} 'injected'
        Assert ((Tree (Join-Path $script:caseRoot 'profile')) -ceq $before) 'New client config/rollback changed original inventory'
    }
    Test 'Recreated legacy root is retained separately during rollback' {
        New-Fixture $true; $script:FailAt='recreated-legacy'
        $null=Throws {Invoke-FixtureInstall} 'Rollback incomplete'
        Assert ([IO.Directory]::Exists($script:newRoot)) 'Lost relocated recovery root'
        Assert (([IO.File]::ReadAllText((Join-Path $script:oldRoot 'concurrent.txt'))) -eq 'concurrent-data') 'Clobbered recreated root'
        Assert (-not [IO.Directory]::Exists((Join-Path $script:oldRoot 'dwg-mcp'))) 'Nested/merged roots on undo'
    }
    Test 'Locked client rollback reports and retains the verified recovery copy' {
        New-Fixture; $script:Client=@('cursor'); $script:FailAt='lock-client'; $before=Get-SetupSha256 $script:cursor
        $message=Throws {Invoke-FixtureInstall} 'Rollback incomplete'
        $script:held.Dispose(); $script:held=$null
        $change=$script:installChanges | Where-Object { $_.Kind -eq 'File' -and $_.Path -eq $script:cursor }
        Assert ([IO.File]::Exists($change.Backup)) 'Recovery backup deleted'
        Assert ((Get-SetupSha256 $change.Backup) -eq $before) 'Recovery copy differs from original'
        Assert ($message.Contains($change.Backup)) 'Recovery path not reported'
        Assert (([IO.File]::ReadAllText((Join-Path $script:ServerInstallRoot 'dwg-mcp.exe'))) -eq 'old-server') 'Server not restored'
    }
    Test 'Bad recovery snapshot is refused before target deletion and root relocation is held' {
        New-Fixture $true; $script:FailAt='corrupt-backup'
        $message=Throws {Invoke-FixtureInstall} 'Rollback incomplete'
        Assert ($message.Contains($script:newRoot)) 'Actual retained root not reported'
        Assert (([IO.File]::ReadAllText((Join-Path $script:ServerInstallRoot 'dwg-mcp.exe'))) -eq 'new-server-fixture-not-executable') 'Deleted target despite bad backup'
        Assert ([IO.Directory]::Exists($script:newRoot) -and -not [IO.Directory]::Exists($script:oldRoot)) 'Moved a root containing unresolved recovery files'
    }
    Test 'Foreign install roots and unsupported CLI recovery stop before replacement' {
        New-Fixture; Remove-Item -LiteralPath (Join-Path $script:ServerInstallRoot 'dwg-mcp.exe'); Put (Join-Path $script:ServerInstallRoot 'personal.txt') 'keep'
        $before=Tree (Join-Path $script:caseRoot 'profile'); $null=Throws {Invoke-FixtureInstall} 'no recognized'; Assert ((Tree (Join-Path $script:caseRoot 'profile')) -ceq $before) 'Touched foreign server root'
        New-Fixture; $script:Client=@('grok'); $before=Tree (Join-Path $script:caseRoot 'profile'); $null=Throws {Invoke-FixtureInstall} 'no verified config recovery path'; Assert ((Tree (Join-Path $script:caseRoot 'profile')) -ceq $before) 'Unsupported CLI mutated install'
    }
    Test 'Codex command-only repoint and rollback preserve TOML args/env/comments/other tables' {
        New-Fixture; $path=Join-Path $script:caseRoot 'codex/config.toml'
        $old=(Join-Path $script:oldRoot 'server/0.5.0/dwg-mcp.exe').Replace('\','\\')
        $toml="# comment`r`n[mcp_servers.dwg-mcp]`r`ncommand = `"$old`" # keep`r`nargs = [`"--read-only`"]`r`n[mcp_servers.dwg-mcp.env]`r`nKEEP = `"value`"`r`n[other]`r`nx = 1`r`n"
        Put $path $toml; $script:installChanges=New-Object System.Collections.Generic.List[object]
        $status=Set-CodexConfigCommand -Path $path -Exe (Join-Path $script:ServerInstallRoot 'dwg-mcp.exe')
        Assert ($status -eq 'repointed') 'Codex entry not repointed'
        $updated=[IO.File]::ReadAllText($path)
        Assert ($updated.Contains('args = ["--read-only"]') -and $updated.Contains('KEEP = "value"') -and $updated.Contains('# keep') -and $updated.Contains('[other]')) 'Dropped TOML data'
        Undo-InstallChanges; Assert ([IO.File]::ReadAllText($path) -ceq $toml) 'TOML rollback not byte-identical'
    }
    Test 'Codex unknown TOML entry shapes remain untouched' {
        New-Fixture; $path=Join-Path $script:caseRoot 'codex/config.toml'
        $toml='[mcp_servers."dwg-mcp"]' + "`ncommand = 'custom.exe'`nargs = ['keep']`n"
        Put $path $toml; $script:installChanges=New-Object System.Collections.Generic.List[object]
        Assert ((Set-CodexConfigCommand -Path $path -Exe 'replacement.exe') -eq 'custom') 'Unknown entry could fall through to CLI replacement'
        Assert ([IO.File]::ReadAllText($path) -ceq $toml) 'Changed unknown TOML entry'
        Assert ($script:installChanges.Count -eq 0) 'Unknown entry was journaled/mutated'
    }
    Test 'Native CLI failure restores a snapshotted client config' {
        New-Fixture; $before=Get-SetupSha256 $script:cursor
        $script:installChanges=New-Object System.Collections.Generic.List[object]; Register-InstallFile $script:cursor
        $cliScript=Join-Path $script:caseRoot 'fixture-cli.ps1'
        Put $cliScript 'param([string]$ConfigPath) [IO.File]::WriteAllText($ConfigPath,"partially-written"); exit 17'
        $shell=(Get-Process -Id $PID).Path
        $null=Throws {Invoke-ClientCli $shell @('-NoProfile','-File',$cliScript,$script:cursor)} 'exit 17'
        Undo-InstallChanges
        Assert ((Get-SetupSha256 $script:cursor) -eq $before) 'CLI failure lost original client config'
    }
} finally {
    if ($script:held) { $script:held.Dispose() }
    $env:USERPROFILE=$savedProfile.USERPROFILE; $env:APPDATA=$savedProfile.APPDATA; $env:LOCALAPPDATA=$savedProfile.LOCALAPPDATA
    if ($ResultPath) {
        New-Item -ItemType Directory -Path (Split-Path -Parent ([IO.Path]::GetFullPath($ResultPath))) -Force | Out-Null
        [ordered]@{powershell=$PSVersionTable.PSVersion.ToString();fixtures=$testRoot;installerSha256=(Get-SetupSha256 $installer);results=@($results.ToArray())} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $ResultPath -Encoding UTF8
    }
    Write-Host "Fixtures retained: $testRoot"
}
if (@($results | Where-Object {-not $_.passed}).Count) { exit 1 }
