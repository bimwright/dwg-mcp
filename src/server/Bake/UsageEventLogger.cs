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
            using (var db = new BakeDb(_paths))
            {
                db.Migrate();
                db.InsertUsageEvent(usageEvent);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_paths.UsageJsonl) ?? ".");
            File.AppendAllText(_paths.UsageJsonl, JsonConvert.SerializeObject(new
            {
                ts_utc = usageEvent?.Timestamp ?? DateTimeOffset.UtcNow.ToString("o"),
                usageEvent?.SessionId,
                usageEvent?.Tool,
                params_hash = usageEvent?.ParamsHash ?? usageEvent?.NormalizedKey,
                usageEvent?.Success,
                usageEvent?.DurationMs
            }) + Environment.NewLine);
        }
    }
}
