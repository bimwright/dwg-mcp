# Contributing to dwg-mcp

Contributions are welcome. This document covers the basics.

## Getting Started

1. Fork the repo and clone your fork.
2. Build: `dotnet build src/Bimwright.Dwg.sln -c Debug`
3. Run tests: `dotnet test tests/Bimwright.Dwg.Tests/Bimwright.Dwg.Tests.csproj`
   Run the host-free rewrite executor regressions with `dotnet test tests/Bimwright.Dwg.Rewriting.Tests/Bimwright.Dwg.Rewriting.Tests.csproj`.
4. Toast window and animation changes: run the WPF harness on Windows, `dotnet run --project tests/Bimwright.Dwg.Toast.Tests`. It needs no AutoCAD.

## Plugin Development

The plugin requires AutoCAD 2024 for integration testing. Unit tests cover pure logic (clustering, rewriting, metrics) and run without AutoCAD.

Do not commit Autodesk DLLs. The plugin projects reference Autodesk's `AutoCAD.NET` NuGet packages at compile time only (`ExcludeAssets="runtime"`), so every AutoCAD year builds without a local install, and GitHub-hosted CI builds all six shells.

## Pull Requests

- Fork, create a feature branch, open a PR against `master`.
- Keep PRs focused — one logical change per PR.
- Include tests for new logic when feasible.
- Follow existing code style (C#, LangVersion=latest, consistent naming).

## Code Style

- Follow existing patterns in the codebase.
- Namespace: `Bimwright.Dwg.Server` (server), `Bimwright.Dwg.Plugin` (plugin/shared).
- One handler per file in `src/shared/Handlers/`.
- Pure logic (no AutoCAD dependency) goes in Clustering/, Rewriting/, Unicode/ for testability.

## License

By contributing, you agree that your contributions will be licensed under the Apache License 2.0.
