using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Dwg.Server.Bake;

namespace Bimwright.Dwg.Server.Memory
{
    public sealed class PatternDetector
    {
        public IReadOnlyList<ClusterCandidate> Detect(SessionContext context, IEnumerable<UsageEvent> events)
        {
            return (events ?? Array.Empty<UsageEvent>())
                .Where(e => e.Success && !string.IsNullOrWhiteSpace(e.Tool))
                .GroupBy(e => e.Tool + ":" + (e.ParamsHash ?? e.NormalizedKey ?? string.Empty))
                .Where(g => g.Count() >= 3)
                .Select(g => new ClusterCandidate
                {
                    ClusterKey = g.Key,
                    Source = "usage",
                    Tool = g.First().Tool,
                    Count = g.Count()
                })
                .ToArray();
        }
    }
}
