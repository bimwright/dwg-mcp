# AutoCAD 2024 Reference Assemblies

These DLLs are from the AutoCAD 2024 installation and are included here solely for compilation (CI builds).
They are NOT redistributed — AutoCAD provides them at runtime.

- `acmgd.dll` — Managed wrapper for AutoCAD application services
- `accoremgd.dll` — Core managed services
- `acdbmgd.dll` — Database/entity managed services

These files are referenced with `<Private>false</Private>` (CopyLocal=No).
