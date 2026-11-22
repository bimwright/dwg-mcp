#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$checker = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'scripts/check-public-tree.ps1'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('dwg-public-tree-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
try {
    & git -C $fixture init --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Fixture git init failed' }
    $cases = @(
        @{ Path = 'README.md'; Allowed = $true },
        @{ Path = 'SECURITY.md'; Allowed = $true },
        @{ Path = 'src/example.cs'; Allowed = $true },
        @{ Path = 'lib/acad24/README.md'; Allowed = $true },
        @{ Path = 'docs/benchmarks/example.md'; Allowed = $true },
        @{ Path = 'src/server/Prompts/review_large_drawing.md'; Allowed = $true },
        @{ Path = 'src/server/Prompts/translate_text.md'; Allowed = $true },
        @{ Path = 'src/server/Prompts/operator-notes.md'; Allowed = $false },
        @{ Path = 'docs/design/private.md'; Allowed = $false },
        @{ Path = 'docs/testing/operator-checklist.md'; Allowed = $false },
        @{ Path = 'tests/harness/README.md'; Allowed = $false },
        @{ Path = 'docs/benchmarks/nested/private.md'; Allowed = $false },
        @{ Path = 'internal-docs/example.json'; Allowed = $false },
        @{ Path = '.agents/skill/example.cs'; Allowed = $false },
        @{ Path = 'src/codemap.md'; Allowed = $false }
    )
    foreach ($case in $cases) {
        $file = Join-Path $fixture $case.Path
        New-Item -ItemType Directory -Path (Split-Path -Parent $file) -Force | Out-Null
        [IO.File]::WriteAllText($file, 'fixture')
        & git -C $fixture add --force -- $case.Path
        if ($LASTEXITCODE -ne 0) { throw 'Fixture staging failed' }
        $rejected = $false
        try { & $checker -RepoRoot $fixture | Out-Null } catch {
            if ($_.Exception.Message -notlike '*Files need relocation*') { throw }
            $rejected = $true
        }
        if ($rejected -eq $case.Allowed) { throw "Unexpected public-tree result for $($case.Path)" }
        # Validate each path independently; never mutate the product checkout.
        Remove-Item -LiteralPath $file
    }
    'Public-tree fixture checks passed: 15/15.'
} finally {
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    if (-not $resolvedFixture.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        -not ([IO.Path]::GetFileName($resolvedFixture)).StartsWith('dwg-public-tree-', [StringComparison]::Ordinal)) {
        throw 'Refuse cleanup outside the fixture root'
    }
    Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
}
