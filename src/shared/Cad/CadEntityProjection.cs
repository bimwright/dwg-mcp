using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Cad
{
    // The paged query deliberately does not call Describe(true): requesting text
    // must not read extents, curve geometry or every vertex of a polyline.
    internal static class CadEntityProjection
    {
        internal static JObject Project(Entity entity, string[] fields, Extents3d? knownExtents,
            bool extentsRead, out int extentsUnavailable, out int fieldReadFailed, string knownHandle = null)
        {
            extentsUnavailable = 0;
            fieldReadFailed = 0;
            var result = new JObject();
            var unavailable = new JArray();
            var reasons = new JObject();
            foreach (var field in fields)
            {
                try
                {
                    JToken value;
                    switch (field)
                    {
                        case "handle": value = knownHandle ?? entity.Handle.ToString(); break;
                        case "type": value = entity.GetType().Name; break;
                        case "runtime_type": value = entity.GetType().FullName; break;
                        case "layer": value = entity.Layer; break;
                        case "color_index": value = (int)entity.ColorIndex; break;
                        case "text":
                            if (entity is DBText text) value = text.TextString;
                            else if (entity is MText mtext) value = mtext.Contents;
                            else { Missing(result, unavailable, reasons, field, "not_supported_for_type"); continue; }
                            break;
                        case "position":
                            if (entity is DBText positionedText) value = Point(positionedText.Position);
                            else if (entity is MText positionedMtext) value = Point(positionedMtext.Location);
                            else if (entity is BlockReference block) value = Point(block.Position);
                            else { Missing(result, unavailable, reasons, field, "not_supported_for_type"); continue; }
                            break;
                        case "extents":
                            var bounds = knownExtents;
                            if (!extentsRead && TryReadExtents(entity, out var readBounds)) bounds = readBounds;
                            if (!bounds.HasValue)
                            {
                                extentsUnavailable++;
                                Missing(result, unavailable, reasons, field, "read_failed");
                                continue;
                            }
                            value = new JObject { ["min"] = Point(bounds.Value.MinPoint), ["max"] = Point(bounds.Value.MaxPoint) };
                            break;
                        default: throw new ArgumentException("Unsupported entity query field: " + field);
                    }
                    if (value == null || value.Type == JTokenType.Null || value is JValue scalar && scalar.Value == null)
                        Missing(result, unavailable, reasons, field, "unavailable");
                    else result[field] = value;
                }
                catch
                {
                    fieldReadFailed++;
                    Missing(result, unavailable, reasons, field, "read_failed");
                }
            }
            if (unavailable.Count > 0)
            {
                result["unavailable_fields"] = unavailable;
                result["field_unavailability"] = reasons;
            }
            return result;
        }

        internal static bool TryReadExtents(Entity entity, out Extents3d bounds)
        {
            bounds = default;
            try
            {
                bounds = entity.GeometricExtents;
                var min = bounds.MinPoint;
                var max = bounds.MaxPoint;
                return Finite(min.X) && Finite(min.Y) && Finite(min.Z)
                    && Finite(max.X) && Finite(max.Y) && Finite(max.Z)
                    && min.X <= max.X && min.Y <= max.Y && min.Z <= max.Z;
            }
            catch { return false; }
        }

        private static JObject Point(Point3d point)
        {
            if (!Finite(point.X) || !Finite(point.Y) || !Finite(point.Z))
                throw new InvalidOperationException("Position is not finite.");
            return new JObject { ["x"] = point.X, ["y"] = point.Y, ["z"] = point.Z };
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static void Missing(JObject result, JArray fields, JObject reasons, string field, string reason)
        {
            result[field] = JValue.CreateNull();
            fields.Add(field);
            reasons[field] = reason;
        }
    }
}
