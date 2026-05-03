# AutoCAD 2024 Managed Assembly References

Do not commit Autodesk binaries to this repository.

The plugin project references these managed assemblies from a local AutoCAD
2024 installation:

- `acmgd.dll`
- `accoremgd.dll`
- `acdbmgd.dll`

Default local path:

```text
C:\Program Files\Autodesk\AutoCAD 2024
```

Override the path when building:

```powershell
dotnet build src/plugin-acad24/Bimwright.Dwg.Plugin.Acad24.csproj -p:AutoCad2024Dir="D:\Path\To\AutoCAD 2024"
```

GitHub-hosted runners do not include AutoCAD. CI skips the plugin build unless
the required local assemblies are present.
