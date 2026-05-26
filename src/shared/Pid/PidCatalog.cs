using System;
using System.Collections.Generic;

namespace Bimwright.Dwg.Pid
{
    public static class PidCatalog
    {
        private static readonly Dictionary<string, List<string>> CategorySymbols = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["ACTUATORS"] = new List<string> { "ACT-MOTOR", "ACT-PNEUMATIC", "ACT-SOLENOID" },
            ["ANNOTATION"] = new List<string> { "ANNOT-FLOWARROW", "ANNOT-EQUIPTAG", "ANNOT-LINENUMBER" },
            ["EQUIPMENT"] = new List<string> { "EQUIP-CLARIFIER", "EQUIP-MIXER", "EQUIP-SCRAPING", "EQUIP-SCREEN", "EQUIP-MEMBRANE" },
            ["PUMPS-BLOWERS"] = new List<string> { "PUMP-METERING", "PUMP-CENTRIFUGAL", "PUMP-DIAPHRAGM", "PUMP-SUBMERSIBLE", "BLOWER-CENTRIFUGAL" },
            ["TANKS"] = new List<string> { "TANK-VERTICAL", "TANK-HORIZONTAL", "TANK-CONICAL" },
            ["VALVES"] = new List<string> { "VA-KNIFEGATE", "VA-BALL", "VA-BUTTERFLY", "VA-CHECK", "VA-DIAPHRAGM", "VA-GLOBE", "VA-SOLENOID" }
        };

        public static List<string> GetCategories()
        {
            return new List<string>(CategorySymbols.Keys);
        }

        public static List<string> GetSymbols(string category)
        {
            if (category != null && CategorySymbols.TryGetValue(category, out var symbols))
            {
                return new List<string>(symbols);
            }
            return new List<string>();
        }
    }
}
