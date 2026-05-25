using System;
using System.IO;
using Newtonsoft.Json;

namespace Bimwright.Dwg.Server.Bake
{
    public sealed class UsageEventLogger
    {
        private readonly BakePaths _paths;

        public UsageEventLogger(BakePaths paths)
        {
            _paths = paths;
        }

        public void Append(UsageEvent usageEvent)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_paths.UsageJsonl) ?? ".");
            File.AppendAllText(_paths.UsageJsonl, JsonConvert.SerializeObject(new
            {
                ts_utc = DateTimeOffset.UtcNow.ToString("o"),
                usageEvent?.Tool,
                usageEvent?.NormalizedKey,
                usageEvent?.Success
            }) + Environment.NewLine);
        }
    }
}
