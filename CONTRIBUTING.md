# Contributing to dwg-mcp

Contributions are welcome. This document covers the basics.

## Getting Started

1. Fork the repo and clone your fork.
2. Build: `dotnet build src/Bimwright.Dwg.sln -c Debug`
3. Run tests: `dotnet test tests/Bimwright.Dwg.Tests/Bimwright.Dwg.Tests.csproj`

## Plugin Development

The plugin requires AutoCAD 2024 for integration testing. Unit tests cover pure logic (clustering, rewriting, metrics) and run without AutoCAD.

Do not commit Autodesk DLLs. The plugin project references the local AutoCAD 2024 install by default at `C:\Program Files\Autodesk\AutoCAD 2024`; pass `/p:AutoCad2024Dir=<path>` when your install is elsewhere. GitHub-hosted CI skips the plugin build when those proprietary assemblies are not available.

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
