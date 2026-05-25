using System;

namespace Bimwright.Dwg.Plugin.ToolBaker
{
    public sealed class BakePolicyResult
    {
        public bool Ok { get; set; }
        public string Error { get; set; }
    }

    public static class BakeCompilerPolicy
    {
        private static readonly string[] ForbiddenTokens =
        {
            "System.IO",
            "System.Net",
            "System.Diagnostics",
            "System.Reflection",
            "File.",
            "Directory.",
            "Process.",
            "Environment.",
            "Microsoft.Win32",
            "Activator.",
            "Assembly.",
            "MethodInfo",
            "PropertyInfo",
            "FieldInfo",
            "GetType(",
            "typeof(",
            "Bimwright.Dwg.Plugin.ToolBaker"
        };

        public static BakePolicyResult ValidateSource(string source)
        {
            source = source ?? string.Empty;
            foreach (var token in ForbiddenTokens)
            {
                if (source.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return new BakePolicyResult { Ok = false, Error = "Baked tool source uses forbidden token: " + token };
                }
            }

            return new BakePolicyResult { Ok = true };
        }
    }
}
