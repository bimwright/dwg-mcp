#Requires -Version 7.0
[CmdletBinding()]
param([string]$RepoRoot = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
# Keep these product paths aligned with the family release profile. Developer
# checklists and test-harness notes belong in internal-docs, not this public tree.
$publicDocs = @(
    'README.md', 'README.vi.md', 'README.ja.md', 'README.zh-CN.md',
    'CHANGELOG.md', 'CONTRIBUTING.md', 'CODE_OF_CONDUCT.md', 'SECURITY.md',
    'ARCHITECTURE.md', 'AGENTS.md', 'CLAUDE.md', 'THIRD_PARTY_NOTICES.md',
    'lib/acad24/README.md', 'src/shared/assets/fonts/README.md',
    'src/server/Prompts/review_large_drawing.md', 'src/server/Prompts/translate_text.md'
)
$privatePath = '(^|/)(\.(artifacts[^/]*|worktrees|claude|cursor|kilo|agents|openclaude)|internal-docs|analysis|artifacts|runs|spikes)(/|$)|^docs/(superpowers|design|reviews|research-archive|archive)/|(^|/)codemap\.md$'
$paths = @(& git -c core.quotepath=false -C $RepoRoot ls-files --cached --others --exclude-standard --deduplicate)
if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate the public Git tree.' }
$violations = @(
    foreach ($path in $paths) {
        # A staged or pending deletion must not make a local preflight fail.
        if (-not (Test-Path -LiteralPath (Join-Path $RepoRoot $path))) { continue }
        $allowedDoc = $path -in $publicDocs -or $path -match '^docs/benchmarks/[^/]+\.md$'
        if ($path -match $privatePath -or
            ($path -match '\.(md|mdx|markdown|rst|txt|canvas|docx?|pdf)$' -and -not $allowedDoc) -or
            ($path.StartsWith('docs/', [StringComparison]::OrdinalIgnoreCase) -and -not $allowedDoc)) {
            $path
        }
    }
)
if ($violations.Count) { throw "Files need relocation to private documentation:`n$($violations -join "`n")" }
'Public tree check passed. This path check is not a secret scanner or a history audit.'
