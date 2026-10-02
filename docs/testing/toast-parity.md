# AutoCAD toast behavior

The toast follows the RVT activity-card behavior: Success, Failed and Capture counters; a latest-image thumbnail; a hover timeline with three visible rows; sanitized outcome and measured-duration tooltips; and motion that respects reduced-motion preferences. The timeline follows incoming results at the bottom and preserves the reading position when scrolled back.

MCPSETTINGS provides independent horizontal/vertical corners, optional title-row dragging and reset. Position is relative to the owner window, persists atomically and is clamped to the visible area. Toast display does not activate AutoCAD. Clicking the card opens History; MCPHISTORY also opens it. History continues recording when toast display is disabled.

## Automated verification

```powershell
dotnet test tests/Bimwright.Dwg.Tests/Bimwright.Dwg.Tests.csproj
dotnet run --project tests/Bimwright.Dwg.Toast.Tests/Bimwright.Dwg.Toast.Tests.csproj
```

The WPF executable checks hover/collapse, live arrivals, scrolling, reduced motion, four corners, dragging/reset, History and settings save failures. These checks exercise the Windows UI but do not establish live acceptance inside each AutoCAD release. Build and live checks require the matching installed Autodesk SDK/application.
