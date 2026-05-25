namespace Bimwright.Dwg.Server.Bake
{
    public sealed class UsageEvent
    {
        public string Tool { get; set; }
        public string NormalizedKey { get; set; }
        public bool Success { get; set; }
    }
}
