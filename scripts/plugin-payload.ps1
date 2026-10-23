#Requires -Version 5.1
# Shared by repo installation, Setup packaging and installed-file verification.
function Assert-PluginPayload([string]$PayloadDir, [int]$Year) {
    $pluginName = 'Bimwright.Dwg.Plugin.Acad{0:D2}' -f ($Year - 2000)
    $required = @("$pluginName.dll", 'Microsoft.CodeAnalysis.dll', 'Microsoft.CodeAnalysis.CSharp.dll',
        'Microsoft.CodeAnalysis.Scripting.dll', 'Microsoft.CodeAnalysis.CSharp.Scripting.dll', 'Newtonsoft.Json.dll')
    if ($Year -le 2024) {
        # Roslyn's net48 transitive dependencies; these builds have no deps.json.
        $required += @('System.Buffers.dll', 'System.Collections.Immutable.dll', 'System.Memory.dll',
            'System.Numerics.Vectors.dll', 'System.Reflection.Metadata.dll', 'System.Runtime.CompilerServices.Unsafe.dll',
            'System.Text.Encoding.CodePages.dll', 'System.Threading.Tasks.Extensions.dll')
    }
    $depsPath = Join-Path $PayloadDir "$pluginName.deps.json"
    if ($Year -ge 2025 -or (Test-Path -LiteralPath $depsPath -PathType Leaf)) {
        if (-not (Test-Path -LiteralPath $depsPath -PathType Leaf)) { throw "AutoCAD ${Year} payload is missing $pluginName.deps.json. Rebuild before packaging or installing." }
        $deps = Get-Content -LiteralPath $depsPath -Raw | ConvertFrom-Json
        $targetName = [string]$deps.runtimeTarget.name
        $target = $deps.targets.PSObject.Properties[$targetName]
        if (-not $target) { throw "AutoCAD ${Year} dependency manifest has no runtime target '$targetName'." }
        foreach ($library in $target.Value.PSObject.Properties) {
            $runtime = $library.Value.PSObject.Properties['runtime']
            if (-not $runtime) { continue }
            foreach ($asset in $runtime.Value.PSObject.Properties) {
                $required += [IO.Path]::GetFileName($asset.Name)
            }
        }
    }
    $missing = @($required | Sort-Object -Unique | Where-Object {
        -not (Test-Path -LiteralPath (Join-Path $PayloadDir $_) -PathType Leaf)
    })
    if ($missing.Count) {
        throw "AutoCAD ${Year} payload is incomplete: $($missing -join ', '). Rebuild the plugin and include its runtime dependencies before packaging or installing."
    }
}
