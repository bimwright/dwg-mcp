using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Dimensions
{
    internal static class DimensionEntityFactory
    {
        internal static bool TryCreateLinear(
            JObject obj,
            ObjectId dimensionStyleId,
            out Dimension entity,
            out string error)
        {
            entity = null;
            error = null;

            if (!TryParseTwoPointDimension(
                obj,
                "create_linear_dimension",
                out var start,
                out var end,
                out var dimensionLinePoint,
                out error))
            {
                return false;
            }

            if (!TryReadOptionalFiniteDouble(obj, "rotation", 0d, out var rotation, out error))
            {
                return false;
            }

            RotatedDimension created = null;
            var ownsEntity = false;
            try
            {
                created = new RotatedDimension(
                    DimensionRequestValidator.DegreesToRadians(rotation),
                    ToPoint3d(start),
                    ToPoint3d(end),
                    ToPoint3d(dimensionLinePoint),
                    string.Empty,
                    dimensionStyleId);
                ownsEntity = true;
                return TransferToCaller(created, out entity, ref ownsEntity);
            }
            catch (Exception ex)
            {
                error = "failed to create linear dimension entity: " + ex.Message;
                return false;
            }
            finally
            {
                DisposeIfOwned(created, ownsEntity);
            }
        }

        internal static bool TryCreateAligned(
            JObject obj,
            ObjectId dimensionStyleId,
            out Dimension entity,
            out string error)
        {
            entity = null;
            error = null;

            if (!TryParseTwoPointDimension(
                obj,
                "create_aligned_dimension",
                out var start,
                out var end,
                out var dimensionLinePoint,
                out error))
            {
                return false;
            }

            AlignedDimension created = null;
            var ownsEntity = false;
            try
            {
                created = new AlignedDimension(
                    ToPoint3d(start),
                    ToPoint3d(end),
                    ToPoint3d(dimensionLinePoint),
                    string.Empty,
                    dimensionStyleId);
                ownsEntity = true;
                return TransferToCaller(created, out entity, ref ownsEntity);
            }
            catch (Exception ex)
            {
                error = "failed to create aligned dimension entity: " + ex.Message;
                return false;
            }
            finally
            {
                DisposeIfOwned(created, ownsEntity);
            }
        }

        internal static bool TryCreateRadial(
            JObject obj,
            ObjectId dimensionStyleId,
            RadialDimensionTarget target,
            out Dimension entity,
            out string error)
        {
            entity = null;
            error = null;

            if (!TryParseRadialDimension(
                obj,
                target,
                out var chordPoint,
                out var leaderLength,
                out error))
            {
                return false;
            }

            RadialDimension created = null;
            var ownsEntity = false;
            try
            {
                created = new RadialDimension(
                    ToPoint3d(target.Center),
                    ToPoint3d(chordPoint),
                    leaderLength,
                    string.Empty,
                    dimensionStyleId);
                ownsEntity = true;
                return TransferToCaller(created, out entity, ref ownsEntity);
            }
            catch (Exception ex)
            {
                error = "failed to create radial dimension entity: " + ex.Message;
                return false;
            }
            finally
            {
                DisposeIfOwned(created, ownsEntity);
            }
        }

        internal static bool TryCreateDiameter(
            JObject obj,
            ObjectId dimensionStyleId,
            RadialDimensionTarget target,
            out Dimension entity,
            out string error)
        {
            entity = null;
            error = null;

            if (!TryParseRadialDimension(
                obj,
                target,
                out var chordPoint,
                out var leaderLength,
                out error))
            {
                return false;
            }

            var farChordPoint = DimensionRequestValidator.OppositePointOnRadius(target.Center, chordPoint);

            DiametricDimension created = null;
            var ownsEntity = false;
            try
            {
                created = new DiametricDimension(
                    ToPoint3d(chordPoint),
                    ToPoint3d(farChordPoint),
                    leaderLength,
                    string.Empty,
                    dimensionStyleId);
                ownsEntity = true;
                return TransferToCaller(created, out entity, ref ownsEntity);
            }
            catch (Exception ex)
            {
                error = "failed to create diameter dimension entity: " + ex.Message;
                return false;
            }
            finally
            {
                DisposeIfOwned(created, ownsEntity);
            }
        }

        private static bool TryParseTwoPointDimension(
            JObject obj,
            string commandName,
            out CadPointInput start,
            out CadPointInput end,
            out CadPointInput dimensionLinePoint,
            out string error)
        {
            start = default;
            end = default;
            dimensionLinePoint = default;
            error = null;

            if (!CadWire.TryParsePoint(obj["start"], out start, out var startError))
            {
                error = "start " + startError;
                return false;
            }

            if (!CadWire.TryParsePoint(obj["end"], out end, out var endError))
            {
                error = "end " + endError;
                return false;
            }

            if (!CadWire.TryParsePoint(obj["dimension_line_point"], out dimensionLinePoint, out var dimLineError))
            {
                error = "dimension_line_point " + dimLineError;
                return false;
            }

            return DimensionRequestValidator.TryValidateTwoPointDimension(commandName, start, end, out error);
        }

        private static bool TryParseRadialDimension(
            JObject obj,
            RadialDimensionTarget target,
            out CadPointInput chordPoint,
            out double leaderLength,
            out string error)
        {
            chordPoint = default;
            leaderLength = 0d;
            error = null;

            if (!DimensionRequestValidator.TryValidateRadialTargetType(target.EntityTypeName, out error))
            {
                return false;
            }

            if (!CadWire.TryParsePoint(obj["dimension_line_point"], out var dimensionLinePoint, out var pointError))
            {
                error = "dimension_line_point " + pointError;
                return false;
            }

            if (!DimensionRequestValidator.TryValidateRadialDimensionGeometry(
                target.Center,
                target.Radius,
                dimensionLinePoint,
                out leaderLength,
                out error))
            {
                return false;
            }

            chordPoint = DimensionRequestValidator.PointOnRadius(target.Center, dimensionLinePoint, target.Radius);
            return true;
        }

        private static bool TryReadOptionalFiniteDouble(
            JObject obj,
            string fieldName,
            double fallback,
            out double value,
            out string error)
        {
            value = fallback;
            error = null;

            var token = obj[fieldName];
            if (token == null || token.Type == JTokenType.Null)
            {
                return true;
            }

            if (token.Type != JTokenType.Float && token.Type != JTokenType.Integer)
            {
                error = fieldName + " must be numeric";
                return false;
            }

            value = token.Value<double>();
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                error = fieldName + " must be finite";
                return false;
            }

            return true;
        }

        private static bool TransferToCaller<T>(T created, out Dimension entity, ref bool ownsEntity)
            where T : Dimension
        {
            entity = created;
            // Caller owns the dimension after a successful factory return.
            ownsEntity = false;
            return true;
        }

        private static void DisposeIfOwned(Dimension entity, bool ownsEntity)
        {
            if (ownsEntity)
            {
                entity?.Dispose();
            }
        }

        private static Point3d ToPoint3d(CadPointInput point)
            => new Point3d(point.X, point.Y, point.Z);
    }
}
