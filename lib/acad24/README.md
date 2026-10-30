# AutoCAD 2024 Managed Assembly References

Do not commit Autodesk binaries to this repository.

The plugin projects get these managed assemblies from Autodesk's `AutoCAD.NET`
NuGet package (AutoCAD 2024: version 24.3.0):

- `acmgd.dll`
- `accoremgd.dll`
- `acdbmgd.dll`

The reference is compile-time only (`ExcludeAssets="runtime"`): AutoCAD loads
its own copies, and the DLLs are not copied into the build output or the Setup
ZIP. No local AutoCAD install is needed to build, and GitHub-hosted CI builds
every shell.
