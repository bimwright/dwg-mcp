using System.Collections.Generic;
using System.Linq;

namespace Bimwright.Dwg.Plugin
{
    public sealed class ClusterState
    {
        public int Id { get; set; }
        public string AnchorHandle { get; set; }
        public List<EntityRecord> Entities { get; set; } = new List<EntityRecord>();
        public List<string> AllHandles { get; set; }
        public List<string> DeleteHandles { get; set; }
        public string CombinedText { get; set; }
        public bool InBlock { get; set; }
        public double MtextWidth { get; set; }
        public double MtextHeight { get; set; }
        public double MedianHeight { get; set; }
        public string Layer { get; set; }
        public bool CanPromoteSingleToMText { get; set; }
        public double BlockScale { get; set; } = 1.0;
    }

    public static class ClusterStateStore
    {
        private static readonly object Lock = new object();
        private static Dictionary<int, ClusterState> _clusters
            = new Dictionary<int, ClusterState>();

        public static void Replace(IEnumerable<ClusterState> clusters)
        {
            lock (Lock)
            {
                _clusters = clusters.ToDictionary(c => c.Id);
            }
        }

        public static ClusterState Get(int id)
        {
            lock (Lock)
            {
                _clusters.TryGetValue(id, out var state);
                return state;
            }
        }

        public static IReadOnlyList<ClusterState> GetAll()
        {
            lock (Lock)
            {
                return _clusters.Values.ToList();
            }
        }

        public static bool HasState()
        {
            lock (Lock) { return _clusters.Count > 0; }
        }
    }
}
