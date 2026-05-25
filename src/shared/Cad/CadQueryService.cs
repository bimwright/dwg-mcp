using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;

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

        internal static int CountEntities(Database db, CadEntityQueryOptions options)
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
                    var entity = tx.GetObject(objectId, OpenMode.ForRead) as Entity;
                    if (entity != null && Matches(entity, options))
                    {
                        count++;
                    }
                }

                tx.Commit();
            }

            return count;
        }

        internal static IReadOnlyList<string> QueryHandles(Database db, CadEntityQueryOptions options)
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

                    var entity = tx.GetObject(objectId, OpenMode.ForRead) as Entity;
                    if (entity == null || !Matches(entity, options))
                    {
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
                !string.Equals(entity.GetType().Name, options.EntityType, StringComparison.OrdinalIgnoreCase))
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
}
