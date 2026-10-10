# AutoCAD toast behavior

The toast follows the RVT activity-card behavior: Success, Failed and Capture counters; a latest-image thumbnail; a hover timeline with three visible rows; sanitized outcome and measured-duration tooltips; and motion that respects reduced-motion preferences. The timeline follows incoming results at the bottom and preserves the reading position when scrolled back.

MCPSETTINGS provides independent horizontal/vertical corners, optional title-row dragging and reset. Position is relative to the owner window, persists atomically and is clamped to the visible area. Toast display does not activate AutoCAD. Clicking the card opens History; MCPHISTORY also opens it. History continues recording when toast display is disabled.

Top-corner cards use the same additional 56 DIP title-bar/ribbon clearance as RVT; bottom corners retain the 16 DIP margin. Idle-duration changes take effect only after settings are saved successfully. A failed save preserves the active duration and staged choice, shows an error, and permits retry.

Toast and History Read/Write labels match the server permissions for all tools. Capture, region inspection and view restore are writes because they create image files; successful images still contribute to the Capture counter.

## Automated verification

```powershell
dotnet test tests/Bimwright.Dwg.Tests/Bimwright.Dwg.Tests.csproj
dotnet run --project tests/Bimwright.Dwg.Toast.Tests/Bimwright.Dwg.Toast.Tests.csproj
```

The WPF executable checks hover/collapse, live arrivals, scrolling, reduced motion, four corners, dragging/reset, History and settings save failures. These checks exercise the Windows UI but do not establish live acceptance inside each AutoCAD release. Build and live checks require the matching installed Autodesk SDK/application.
