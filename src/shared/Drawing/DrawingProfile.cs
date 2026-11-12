using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin
{
    /// <summary>Host-free request rules shared by the gateway and database collector.</summary>
    public sealed class DrawingProfileOptions
    {
        public const int DefaultMaxOutputBytes = 16384;
        public const int MinMaxOutputBytes = 4096;
        public const int MaxMaxOutputBytes = 65536;
        public const int MaxCandidateRows = 128;

        private DrawingProfileOptions(string section, int offset, int maxOutputBytes, string expectedSnapshot, string space)
        { Section = section; Offset = offset; MaxOutputBytes = maxOutputBytes; ExpectedSnapshot = expectedSnapshot; Space = space; }

        public string Section { get; }
        public int Offset { get; }
        public int MaxOutputBytes { get; }
        public string ExpectedSnapshot { get; }
        public string Space { get; }
        internal bool IncludeUnusedBlocks => Space == "all" && (Section == "summary" || Section == "unused_blocks");

        public static DrawingProfileOptions Create(string section = "summary", int offset = 0,
            int maxOutputBytes = DefaultMaxOutputBytes, string expectedSnapshot = null, string space = "all")
        {
            if (section != "summary" && section != "types" && section != "layers" && section != "spaces" && section != "unused_blocks")
                throw new ArgumentException("section must be summary, types, layers, spaces, or unused_blocks.", nameof(section));
            if (offset < 0) throw new ArgumentException("offset must not be negative.", nameof(offset));
            if (section == "summary" && offset != 0)
                throw new ArgumentException("summary requires offset=0.", nameof(offset));
            if (maxOutputBytes < MinMaxOutputBytes || maxOutputBytes > MaxMaxOutputBytes)
                throw new ArgumentException("max_output_bytes must be between 4096 and 65536.", nameof(maxOutputBytes));
            if (expectedSnapshot != null && string.IsNullOrWhiteSpace(expectedSnapshot))
                throw new ArgumentException("expected_snapshot must be nonempty when supplied.", nameof(expectedSnapshot));
            if (offset > 0 && expectedSnapshot == null)
                throw new ArgumentException("offset>0 requires expected_snapshot from the previous profile.", nameof(expectedSnapshot));
            if (string.Equals(space, "all", StringComparison.OrdinalIgnoreCase)) space = "all";
            else if (string.Equals(space, "model", StringComparison.OrdinalIgnoreCase)) space = "model";
            else if (space != null && space.StartsWith("layout:", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(space.Substring(7)) && space.Length <= 1024) space = "layout:" + space.Substring(7);
            else throw new ArgumentException("space must be all, model or layout:<exact layout name>.", nameof(space));
            if (section == "unused_blocks" && space != "all")
                throw new ArgumentException("unused_blocks requires space=all to check references across the drawing.", nameof(space));
            return new DrawingProfileOptions(section, offset, maxOutputBytes, expectedSnapshot, space);
        }
    }

    /// <summary>Counts observed database facts; it never interprets names as entity types.</summary>
    internal sealed class DrawingProfile
    {
        internal sealed class Space
        {
            internal string Id, Name, Kind, RecordName, LayoutName, NameSource;
            internal long Count;
            internal bool Complete;
        }

        private readonly Dictionary<string, long> types = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> layers = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly List<Space> spaces = new List<Space>();
        private readonly List<(string Name, string Handle)> unusedBlocks = new List<(string, string)>();
        private bool blockUsageComplete = true;
        private long idsSeen, erasedSkipped, entitiesRead, entityReadFailed, spaceReadFailed, spaceEnumerationFailed;
        private long typeUnknown, layerUnknown, xrefUnknown, blockReferences, xrefInserts;

        internal Space AddSpace(string id, string name, string kind, string recordName,
            string layoutName, string nameSource, bool complete)
        {
            var space = new Space { Id = id, Name = name, Kind = kind, RecordName = recordName,
                LayoutName = layoutName, NameSource = nameSource, Complete = complete };
            spaces.Add(space);
            return space;
        }

        internal void SawId() => idsSeen++;
        internal void SkippedErased() => erasedSkipped++;
        internal void FailedSpaceRead() => spaceReadFailed++;
        internal void FailedEnumeration(Space space = null)
        { spaceEnumerationFailed++; if (space != null) space.Complete = false; }
        internal void FailedEntityRead(Space space)
        { entityReadFailed++; space.Complete = false; }
        internal void AddUnusedBlock(string name, string handle) => unusedBlocks.Add((name, handle));
        internal void FailedBlockUsageRead() => blockUsageComplete = false;

        internal void ReadEntity(Space space, string runtimeType, string layer, bool blockReference, bool? xref)
        {
            entitiesRead++; space.Count++;
            if (string.IsNullOrEmpty(runtimeType)) typeUnknown++; else Increment(types, runtimeType);
            if (string.IsNullOrEmpty(layer)) layerUnknown++; else Increment(layers, layer);
            if (!blockReference) return;
            blockReferences++;
            if (xref == null) xrefUnknown++;
            else if (xref.Value) xrefInserts++;
        }

        private static void Increment(Dictionary<string, long> histogram, string name)
        { histogram.TryGetValue(name, out var count); histogram[name] = count + 1; }

        internal JObject ToJson(string documentId, string snapshot, DrawingProfileOptions options)
        {
            var populationComplete = entityReadFailed == 0 && spaceReadFailed == 0 && spaceEnumerationFailed == 0;
            var complete = populationComplete && typeUnknown == 0 && layerUnknown == 0 && xrefUnknown == 0;
            var orderedSpaces = spaces.OrderBy(s => s.Name, StringComparer.Ordinal).ThenBy(s => s.Id, StringComparer.Ordinal);
            var result = new JObject
            {
                ["profile_version"] = 1, ["document_id"] = documentId, ["snapshot"] = snapshot,
                ["source"] = new JObject
                {
                    ["api"] = "AutoCAD managed database API", ["method"] = "ForRead BlockTable/BlockTableRecord enumeration",
                    ["type_basis"] = "Entity.GetType().FullName", ["counting"] = "full_enumeration",
                    ["sampling"] = false, ["estimated"] = false
                },
                ["scope"] = new JObject
                {
                    ["spaces"] = options.Space == "all" ? "model_and_layout_top_level" : options.Space == "model" ? "model_top_level" : "named_layout_top_level",
                    ["requested_space"] = options.Space, ["block_expansion"] = false,
                    ["xref_traversal"] = false, ["erased"] = "excluded", ["proxy_type"] = "actual_managed_wrapper"
                },
                ["coverage"] = new JObject
                {
                    ["complete"] = complete, ["entity_ids_seen"] = idsSeen, ["erased_ids_skipped"] = erasedSkipped,
                    ["entities_read"] = entitiesRead, ["entity_read_failed"] = entityReadFailed,
                    ["space_read_failed"] = spaceReadFailed, ["space_enumeration_failed"] = spaceEnumerationFailed,
                    ["type_unknown"] = typeUnknown, ["layer_unknown"] = layerUnknown,
                    ["xref_classification_unknown"] = xrefUnknown
                },
                ["exact_total_count"] = populationComplete ? new JValue(entitiesRead) : JValue.CreateNull(),
                ["observed_entity_count"] = entitiesRead,
                ["direct_block_reference_count"] = populationComplete ? new JValue(blockReferences) : JValue.CreateNull(),
                ["direct_xref_insert_count"] = populationComplete && xrefUnknown == 0 ? new JValue(xrefInserts) : JValue.CreateNull(),
                ["observed_direct_block_reference_count"] = blockReferences, ["observed_direct_xref_insert_count"] = xrefInserts,
                ["histograms"] = new JObject
                {
                    ["types"] = Table("types", types.OrderBy(p => p.Key, StringComparer.Ordinal)
                        .Select(p => new JObject { ["name"] = p.Key, ["count"] = p.Value }), types.Count,
                        populationComplete && typeUnknown == 0, options),
                    ["layers"] = Table("layers", layers.OrderBy(p => p.Key, StringComparer.Ordinal)
                        .Select(p => new JObject { ["name"] = p.Key, ["count"] = p.Value }), layers.Count,
                        populationComplete && layerUnknown == 0, options),
                    ["spaces"] = Table("spaces", orderedSpaces.Select(s => new JObject
                    {
                        ["name"] = s.Name, ["kind"] = s.Kind, ["id"] = s.Id, ["record_name"] = s.RecordName,
                        ["layout_name"] = s.LayoutName, ["name_source"] = s.NameSource,
                        ["count"] = s.Complete ? new JValue(s.Count) : JValue.CreateNull(),
                        ["observed_count"] = s.Count, ["complete"] = s.Complete
                    }), spaces.Count, populationComplete && spaces.All(s => s.Complete), options)
                },
                ["request"] = new JObject { ["section"] = options.Section, ["offset"] = options.Offset, ["max_output_bytes"] = options.MaxOutputBytes, ["space"] = options.Space },
                ["summary"] = "Observed " + entitiesRead.ToString(CultureInfo.InvariantCulture) + " top-level entities in "
                    + spaces.Count.ToString(CultureInfo.InvariantCulture) + " spaces; profile " + (complete ? "complete." : "incomplete; counts are observed facts, not an estimate of unreadable data.")
            };
            if (options.IncludeUnusedBlocks)
            {
                var rows = unusedBlocks.OrderBy(block => block.Name, StringComparer.Ordinal)
                    .ThenBy(block => block.Handle, StringComparer.Ordinal)
                    .Select(block => new JObject { ["name"] = block.Name, ["handle"] = block.Handle });
                result["unused_blocks"] = Table("unused_blocks", rows,
                    blockUsageComplete ? unusedBlocks.Count : (int?)null, blockUsageComplete, options);
            }
            return result;
        }

        private static JObject Table(string section, IEnumerable<JObject> rows, int? groups, bool complete, DrawingProfileOptions options)
        {
            var selected = options.Section == "summary" || options.Section == section;
            var offset = options.Section == section ? options.Offset : 0;
            return new JObject
            {
                ["offset"] = offset, ["total_groups"] = groups.HasValue ? new JValue(groups.Value) : JValue.CreateNull(), ["counts_complete"] = complete,
                ["rows"] = selected ? new JArray(rows.Skip(offset).Take(DrawingProfileOptions.MaxCandidateRows)) : new JArray()
            };
        }
    }
}
