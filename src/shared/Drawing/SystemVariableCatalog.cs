using System;
using System.Collections.Generic;

namespace Bimwright.Dwg.Plugin.Drawing
{
    public static class SystemVariableCatalog
    {
        public enum VariableType
        {
            String,
            Int,
            Double
        }

        public class VariableMetadata
        {
            public string Name { get; }
            public VariableType Type { get; }
            public bool CanWrite { get; }

            public VariableMetadata(string name, VariableType type, bool canWrite)
            {
                Name = name;
                Type = type;
                CanWrite = canWrite;
            }
        }

        private static readonly Dictionary<string, VariableMetadata> Catalog =
            new Dictionary<string, VariableMetadata>(StringComparer.OrdinalIgnoreCase)
            {
                { "CLAYER",    new VariableMetadata("CLAYER",    VariableType.String, true) },
                { "INSUNITS",  new VariableMetadata("INSUNITS",  VariableType.Int,    false) },
                { "LUNITS",    new VariableMetadata("LUNITS",    VariableType.Int,    false) },
                { "DIMSCALE",  new VariableMetadata("DIMSCALE",  VariableType.Double, true) },
                { "TEXTSIZE",  new VariableMetadata("TEXTSIZE",  VariableType.Double, true) },
                { "OSMODE",    new VariableMetadata("OSMODE",    VariableType.Int,    true) },
                { "ORTHOMODE", new VariableMetadata("ORTHOMODE", VariableType.Int,    true) }
            };

        public static bool IsReadable(string name) => Catalog.ContainsKey(name);

        public static bool IsWritable(string name)
        {
            return Catalog.TryGetValue(name, out var meta) && meta.CanWrite;
        }

        public static VariableMetadata GetMetadata(string name)
        {
            Catalog.TryGetValue(name, out var meta);
            return meta;
        }

        public static bool TryCoerceValue(string name, object rawValue, out object coercedValue, out string error)
        {
            coercedValue = null;
            error = null;

            var meta = GetMetadata(name);
            if (meta == null)
            {
                error = $"unknown system variable: {name}";
                return false;
            }

            if (rawValue == null)
            {
                error = "value cannot be null";
                return false;
            }

            try
            {
                switch (meta.Type)
                {
                    case VariableType.String:
                        coercedValue = rawValue.ToString();
                        return true;

                    case VariableType.Int:
                        if (rawValue is double d)
                        {
                            coercedValue = Convert.ToInt32(d);
                            return true;
                        }
                        coercedValue = Convert.ToInt32(rawValue);
                        return true;

                    case VariableType.Double:
                        coercedValue = Convert.ToDouble(rawValue);
                        return true;
                }
            }
            catch (Exception)
            {
                error = $"value '{rawValue}' cannot be coerced to variable type {meta.Type}";
                return false;
            }

            error = "unsupported variable type";
            return false;
        }
    }
}
