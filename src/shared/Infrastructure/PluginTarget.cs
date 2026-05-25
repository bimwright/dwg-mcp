namespace Bimwright.Dwg.Plugin
{
    public static class PluginTarget
    {
#if ACAD2022
        public const string AutoCadYear = "2022";
#elif ACAD2023
        public const string AutoCadYear = "2023";
#elif ACAD2025
        public const string AutoCadYear = "2025";
#elif ACAD2026
        public const string AutoCadYear = "2026";
#elif ACAD2027
        public const string AutoCadYear = "2027";
#else
        public const string AutoCadYear = "2024";
#endif
    }
}
