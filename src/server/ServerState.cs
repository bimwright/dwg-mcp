using Newtonsoft.Json;

namespace Bimwright.Dwg.Server
{
    public static class ServerState
    {
        public static DwgMcpConfig Config { get; set; } = new DwgMcpConfig();

        public static bool IsReadOnly => Config?.ReadOnlyOrDefault ?? false;

        public static string ReadOnlyError(string toolName)
            => JsonConvert.SerializeObject(new
            {
                ok = false,
                error = $"Tool '{toolName}' is disabled because BIMWRIGHT_DWG_READ_ONLY is enabled."
            });
    }
}
