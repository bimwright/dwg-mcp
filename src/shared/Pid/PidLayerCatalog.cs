namespace Bimwright.Dwg.Pid
{
    public class PidLayerInfo
    {
        public string Name { get; }
        public int ColorIndex { get; }

        public PidLayerInfo(string name, int colorIndex)
        {
            Name = name;
            ColorIndex = colorIndex;
        }
    }

    public static class PidLayerCatalog
    {
        public static readonly PidLayerInfo[] StandardLayers = new[]
        {
            new PidLayerInfo("PID-EQUIPMENT", 6),
            new PidLayerInfo("PID-PROCESS-PIPING", 4),
            new PidLayerInfo("PID-UTILITY-PIPING", 3),
            new PidLayerInfo("PID-INSTRUMENTS", 5),
            new PidLayerInfo("PID-ELECTRICAL", 1),
            new PidLayerInfo("PID-ANNOTATION", 7),
            new PidLayerInfo("PID-VALVES", 2)
        };

        public static readonly PidLayerInfo[] WwtpLayers = new[]
        {
            new PidLayerInfo("PID-CHEMICAL-DOSING", 30),
            new PidLayerInfo("PID-AIR-DIFFUSION", 151),
            new PidLayerInfo("PID-SLUDGE", 34),
            new PidLayerInfo("PID-EFFLUENT", 130)
        };
    }
}
