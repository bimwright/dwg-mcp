using System.Runtime.CompilerServices;
using Bimwright.Dwg.Plugin;

namespace Bimwright.Dwg.Tests.Helpers;

internal static class LogRetentionTestPin
{
    /// <summary>
    /// Retention tests must not depend on BIMWRIGHT_KEEP_ALL_LOGS on the test machine: pin keep-all off for
    /// the whole test process. Keep-all tests turn it on explicitly and restore this value.
    /// </summary>
    [ModuleInitializer]
    internal static void PinKeepAllOff() => LogRetentionPolicy.KeepAllOverride = false;
}
