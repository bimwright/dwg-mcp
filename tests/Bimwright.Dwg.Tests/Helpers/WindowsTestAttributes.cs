using System;
using System.Runtime.InteropServices;
using Xunit;

namespace Bimwright.Dwg.Tests.Helpers;

// Registry and Windows drive/UNC path semantics are host-specific; keep the
// server's Linux CI runnable without pretending those cases were exercised.
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) Skip = "Requires Windows registry/path semantics.";
    }
}

public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) Skip = "Requires Windows registry/path semantics.";
    }
}
