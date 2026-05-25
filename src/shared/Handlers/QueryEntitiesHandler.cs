using System;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class QueryEntitiesHandler : IAcadCommand
    {
        public string Name => "query_entities";
        public string Description => "Query model-space entities by optional type, layer, color, and limit filters.";
        public CommandSchema Schema => CommandSchemas.QueryEntities;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            var includeGeometry = parameters?["include_geometry"]?.Value<bool>() ?? false;
            if (!QueryHandlerInput.TryCreateOptions(
                parameters,
                includeGeometry,
                readLimit: true,
                out var options,
                out var error))
            {
                return CommandResult.Fail(error);
            }

            try
            {
                var result = CadQueryService.QueryEntities(doc.Database, options);
                return CommandResult.Success(new { count = result.Entities.Count, entities = result.Entities });
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("failed to query entities: " + ex.Message);
            }
        }
    }

    internal static class QueryHandlerInput
    {
        internal static bool TryCreateOptions(
            JToken parameters,
            bool includeGeometry,
            bool readLimit,
            out CadEntityQueryOptions options,
            out string error)
        {
            options = null;
            error = null;

            if (!TryReadOptionalColorIndex(parameters, out var colorIndex, out error) ||
                !TryReadOptionalLimit(parameters, readLimit, out var limit, out error))
            {
                return false;
            }

            options = new CadEntityQueryOptions(
                ReadOptionalString(parameters, "entity_type"),
                ReadOptionalString(parameters, "layer"),
                colorIndex,
                limit,
                includeGeometry);
            return true;
        }

        internal static string ReadOptionalString(JToken parameters, string fieldName)
        {
            var obj = parameters as JObject;
            return obj?[fieldName]?.Value<string>();
        }

        internal static bool TryReadOptionalColorIndex(JToken parameters, out int? colorIndex, out string error)
        {
            colorIndex = null;
            error = null;

            var obj = parameters as JObject;
            var token = obj?["color_index"];
            if (token == null || token.Type == JTokenType.Null)
            {
                return true;
            }

            if (!CadWire.TryReadAciColor(parameters, "color_index", 7, out var value, out error))
            {
                return false;
            }

            colorIndex = value;
            return true;
        }

        internal static bool TryReadOptionalLimit(
            JToken parameters,
            bool readLimit,
            out int? limit,
            out string error)
        {
            limit = null;
            error = null;
            if (!readLimit)
            {
                return true;
            }

            var obj = parameters as JObject;
            var token = obj?["limit"];
            if (token == null || token.Type == JTokenType.Null)
            {
                return true;
            }

            if (token.Type != JTokenType.Integer)
            {
                error = "limit must be an integer";
                return false;
            }

            long value;
            try
            {
                value = token.Value<long>();
            }
            catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException)
            {
                error = "limit must be an integer";
                return false;
            }

            if (value < int.MinValue)
            {
                limit = int.MinValue;
            }
            else if (value > int.MaxValue)
            {
                limit = int.MaxValue;
            }
            else
            {
                limit = (int)value;
            }

            return true;
        }
    }
}
