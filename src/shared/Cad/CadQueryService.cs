using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Cad
{
    internal sealed class CadEntityQueryOptions
    {
        internal CadEntityQueryOptions(
            string entityType,
            string layer,
            int? colorIndex,
            int? limit,
            bool includeGeometry)
        {
            EntityType = Normalize(entityType);
            Layer = Normalize(layer);
            ColorIndex = colorIndex;
            Limit = limit;
            IncludeGeometry = includeGeometry;
        }

        internal string EntityType { get; }
        internal string Layer { get; }
        internal int? ColorIndex { get; }
        internal int? Limit { get; }
        internal bool IncludeGeometry { get; }

        private static string Normalize(string value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    internal sealed class CadEntityQueryResult
    {
        internal CadEntityQueryResult(IReadOnlyList<object> entities, IReadOnlyList<string> handles)
        {
            Entities = entities;
            Handles = handles;
        }

        internal IReadOnlyList<object> Entities { get; }
        internal IReadOnlyList<string> Handles { get; }
    }

    internal static class CadQueryService
    {
        private const int DefaultLimit = 500;
        private const int MaxLimit = 5000;

        internal static CadEntityQueryResult QueryEntities(Database db, CadEntityQueryOptions options)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            options = options ?? new CadEntityQueryOptions(null, null, null, null, includeGeometry: false);
            var limit = ClampLimit(options.Limit);
            var entities = new List<object>();
            var handles = new List<string>();

            using (var tx = db.TransactionManager.StartTransaction())
            {
                var modelSpace = OpenModelSpace(db, tx);
                foreach (ObjectId objectId in modelSpace)
                {
                    if (entities.Count >= limit)
                    {
                        break;
                    }

                    var entity = tx.GetObject(objectId, OpenMode.ForRead) as Entity;
                    if (entity == null || !Matches(entity, options))
                    {
                        continue;
                    }

                    handles.Add(entity.Handle.ToString());
                    entities.Add(CadEntityProperties.Describe(entity, tx, options.IncludeGeometry));
                }

                tx.Commit();
            }

            return new CadEntityQueryResult(entities, handles);
        }

        internal static int CountEntities(Database db, CadEntityQueryOptions options, CadEntityTypeHint typeHint)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            options = options ?? new CadEntityQueryOptions(null, null, null, null, includeGeometry: false);
            var count = 0;

            using (var tx = db.TransactionManager.StartTransaction())
            {
                var modelSpace = OpenModelSpace(db, tx);
                foreach (ObjectId objectId in modelSpace)
                {
                    if (objectId.IsErased) continue;
                    var entity = tx.GetObject(objectId, OpenMode.ForRead) as Entity;
                    if (entity == null) continue;
                    if (Matches(entity, options)) count++;
                    else if (count == 0) typeHint.Observe(entity.GetType());
                }

                tx.Commit();
            }

            return count;
        }

        internal static IReadOnlyList<string> QueryHandles(Database db, CadEntityQueryOptions options, CadEntityTypeHint typeHint = null)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            options = options ?? new CadEntityQueryOptions(null, null, null, null, includeGeometry: false);
            var limit = ClampLimit(options.Limit);
            var handles = new List<string>();

            using (var tx = db.TransactionManager.StartTransaction())
            {
                var modelSpace = OpenModelSpace(db, tx);
                foreach (ObjectId objectId in modelSpace)
                {
                    if (handles.Count >= limit)
                    {
                        break;
                    }

                    if (objectId.IsErased) continue;
                    var entity = tx.GetObject(objectId, OpenMode.ForRead) as Entity;
                    if (entity == null) continue;
                    if (!Matches(entity, options))
                    {
                        if (handles.Count == 0) typeHint?.Observe(entity.GetType());
                        continue;
                    }

                    handles.Add(entity.Handle.ToString());
                }

                tx.Commit();
            }

            return handles;
        }

        private static int ClampLimit(int? limit)
        {
            if (!limit.HasValue)
            {
                return DefaultLimit;
            }

            if (limit.Value < 1)
            {
                return 1;
            }

            if (limit.Value > MaxLimit)
            {
                return MaxLimit;
            }

            return limit.Value;
        }

        private static BlockTableRecord OpenModelSpace(Database db, Transaction tx)
        {
            var blockTable = (BlockTable)tx.GetObject(db.BlockTableId, OpenMode.ForRead);
            return (BlockTableRecord)tx.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);
        }

        private static bool Matches(Entity entity, CadEntityQueryOptions options)
        {
            if (!string.IsNullOrEmpty(options.EntityType) &&
                !EntityQueryOptions.TypeMatches(entity.GetType(), options.EntityType))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(options.Layer) &&
                !string.Equals(entity.Layer, options.Layer, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (options.ColorIndex.HasValue && (int)entity.ColorIndex != options.ColorIndex.Value)
            {
                return false;
            }

            return true;
        }
    }

    internal sealed class CadEntityTypeHint
    {
        private const int Limit = 20;
        private readonly SortedSet<string> types = new SortedSet<string>(StringComparer.Ordinal);
        private bool truncated;

        internal void Observe(Type type)
        {
            var name = type.FullName ?? type.Name;
            if (types.Count == Limit && StringComparer.Ordinal.Compare(name, types.Max) > 0)
            {
                truncated = true;
                return;
            }
            if (types.Add(name) && types.Count > Limit)
            {
                types.Remove(types.Max);
                truncated = true;
            }
        }

        internal JObject ToJson(string space) => new JObject
        {
            ["space"] = space,
            ["available_types"] = new JArray(types),
            ["types_truncated"] = truncated,
            ["message"] = "These are exact managed types observed among top-level entities in the space before applying filters."
        };
    }
}
