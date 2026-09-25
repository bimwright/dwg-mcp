using System;
using System.Collections.Generic;
using System.Text;

namespace Bimwright.Dwg.Plugin.Views.Toast
{
    public static class ToolNameFormatter
    {
        private static readonly HashSet<string> PreservedAcronyms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "mep", "ifc", "dwg", "nwc", "pdf", "dxf", "fbx", "gbxml", "dgn", "dwf", "csv", "cad", "api", "id", "guid", "lisp", "mtext"
        };

        public static string Format(string commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName))
                return string.Empty;

            var parts = commandName.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return commandName;

            var sb = new StringBuilder();
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                    sb.Append(' ');
                sb.Append(FormatToken(parts[i]));
            }

            return sb.ToString();
        }

        private static string FormatToken(string token)
        {
            if (string.IsNullOrEmpty(token))
                return token;

            if (PreservedAcronyms.Contains(token))
                return token.ToUpperInvariant();

            if (token.Length == 1)
                return token.ToUpperInvariant();

            return char.ToUpperInvariant(token[0]) + token.Substring(1).ToLowerInvariant();
        }
    }
}
