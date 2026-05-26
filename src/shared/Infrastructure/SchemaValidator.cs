using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin
{
    public sealed class CommandSchema
    {
        public static readonly CommandSchema Empty = new CommandSchema(false, false, Array.Empty<SchemaProperty>());

        public CommandSchema(bool requireObjectRoot, bool allowArrayRoot, IEnumerable<SchemaProperty> properties)
        {
            RequireObjectRoot = requireObjectRoot;
            AllowArrayRoot = allowArrayRoot;
            Properties = properties?.ToArray() ?? Array.Empty<SchemaProperty>();
        }

        public bool RequireObjectRoot { get; }
        public bool AllowArrayRoot { get; }
        public IReadOnlyList<SchemaProperty> Properties { get; }

        public static CommandSchema Object(params SchemaProperty[] properties)
            => new CommandSchema(true, false, properties);

        public CommandSchema WithArrayRoot()
            => new CommandSchema(RequireObjectRoot, true, Properties);
    }

    public sealed class SchemaProperty
    {
        private SchemaProperty(string name, bool required, bool requireNonWhiteSpace, JTokenType[] acceptedTypes)
        {
            Name = name;
            IsRequired = required;
            RequireNonWhiteSpace = requireNonWhiteSpace;
            AcceptedTypes = acceptedTypes ?? Array.Empty<JTokenType>();
        }

        public string Name { get; }
        public bool IsRequired { get; }
        public bool RequireNonWhiteSpace { get; }
        public IReadOnlyList<JTokenType> AcceptedTypes { get; }

        public static SchemaProperty Required(string name, params JTokenType[] acceptedTypes)
            => new SchemaProperty(name, true, false, acceptedTypes);

        public static SchemaProperty RequiredNonEmptyString(string name)
            => new SchemaProperty(name, true, true, new[] { JTokenType.String });

        public static SchemaProperty Optional(string name, params JTokenType[] acceptedTypes)
            => new SchemaProperty(name, false, false, acceptedTypes);
    }

    public sealed class SchemaValidationResult
    {
        private SchemaValidationResult(bool ok, string error)
        {
            Ok = ok;
            Error = error;
        }

        public bool Ok { get; }
        public string Error { get; }

        public static SchemaValidationResult Success()
            => new SchemaValidationResult(true, null);

        public static SchemaValidationResult Fail(string error)
            => new SchemaValidationResult(false, error);
    }

    public static class SchemaValidator
    {
        public static SchemaValidationResult Validate(string commandName, JToken parameters, CommandSchema schema)
        {
            schema = schema ?? CommandSchema.Empty;
            if ((parameters == null || parameters.Type == JTokenType.Null) && schema.Properties.All(p => !p.IsRequired))
            {
                return SchemaValidationResult.Success();
            }

            if (schema.AllowArrayRoot && parameters?.Type == JTokenType.Array)
            {
                return SchemaValidationResult.Success();
            }

            JObject obj = null;
            if (schema.RequireObjectRoot || schema.Properties.Count > 0)
            {
                obj = parameters as JObject;
                if (obj == null)
                {
                    return SchemaValidationResult.Fail($"{commandName} params must be an object");
                }
            }

            if (obj == null)
            {
                return SchemaValidationResult.Success();
            }

            foreach (var property in schema.Properties)
            {
                var token = obj[property.Name];
                if (token == null || token.Type == JTokenType.Null)
                {
                    if (property.IsRequired)
                    {
                        return SchemaValidationResult.Fail($"{commandName} params missing required field '{property.Name}'");
                    }
                    continue;
                }

                if (property.AcceptedTypes.Count > 0 && !property.AcceptedTypes.Contains(token.Type))
                {
                    return SchemaValidationResult.Fail(
                        $"{commandName} field '{property.Name}' must be {DescribeTypes(property.AcceptedTypes)}");
                }

                if (property.RequireNonWhiteSpace &&
                    token.Type == JTokenType.String &&
                    string.IsNullOrWhiteSpace(token.Value<string>()))
                {
                    return SchemaValidationResult.Fail(
                        $"{commandName} field '{property.Name}' must be a non-empty string");
                }
            }

            return SchemaValidationResult.Success();
        }

        private static string DescribeTypes(IReadOnlyList<JTokenType> types)
            => string.Join(" or ", types.Select(DescribeType));

        private static string DescribeType(JTokenType type)
        {
            switch (type)
            {
                case JTokenType.Array: return "array";
                case JTokenType.Boolean: return "boolean";
                case JTokenType.Float:
                case JTokenType.Integer: return "number";
                case JTokenType.Object: return "object";
                case JTokenType.String: return "string";
                default: return type.ToString().ToLowerInvariant();
            }
        }
    }

    public static class CommandSchemas
    {
        public static readonly CommandSchema GetDrawingInfo = CommandSchema.Empty;

        public static readonly CommandSchema GetEntityProperties = CommandSchema.Object(
            SchemaProperty.Required("handles", JTokenType.Array),
            SchemaProperty.Optional("include_geometry", JTokenType.Boolean));

        public static readonly CommandSchema ListLayers = CommandSchema.Empty;

        public static readonly CommandSchema QueryEntities = CommandSchema.Object(
            SchemaProperty.Optional("entity_type", JTokenType.String),
            SchemaProperty.Optional("layer", JTokenType.String),
            SchemaProperty.Optional("color_index", JTokenType.Integer),
            SchemaProperty.Optional("limit", JTokenType.Integer),
            SchemaProperty.Optional("include_geometry", JTokenType.Boolean));

        public static readonly CommandSchema CountEntities = CommandSchema.Object(
            SchemaProperty.Optional("entity_type", JTokenType.String),
            SchemaProperty.Optional("layer", JTokenType.String),
            SchemaProperty.Optional("color_index", JTokenType.Integer));

        public static readonly CommandSchema SelectByLayer = CommandSchema.Object(
            SchemaProperty.RequiredNonEmptyString("layer"),
            SchemaProperty.Optional("entity_type", JTokenType.String),
            SchemaProperty.Optional("color_index", JTokenType.Integer),
            SchemaProperty.Optional("limit", JTokenType.Integer));

        public static readonly CommandSchema SelectByType = CommandSchema.Object(
            SchemaProperty.RequiredNonEmptyString("entity_type"),
            SchemaProperty.Optional("layer", JTokenType.String),
            SchemaProperty.Optional("color_index", JTokenType.Integer),
            SchemaProperty.Optional("limit", JTokenType.Integer));

        public static readonly CommandSchema CreateLayer = CommandSchema.Object(
            SchemaProperty.Required("name", JTokenType.String),
            SchemaProperty.Optional("color_index", JTokenType.Integer));

        public static readonly CommandSchema CreateLine = CommandSchema.Object(
            SchemaProperty.Required("start", JTokenType.Object),
            SchemaProperty.Required("end", JTokenType.Object),
            SchemaProperty.Optional("layer", JTokenType.String),
            SchemaProperty.Optional("color_index", JTokenType.Integer));

        public static readonly CommandSchema CreateCircle = CommandSchema.Object(
            SchemaProperty.Required("center", JTokenType.Object),
            SchemaProperty.Required("radius", JTokenType.Float, JTokenType.Integer),
            SchemaProperty.Optional("layer", JTokenType.String),
            SchemaProperty.Optional("color_index", JTokenType.Integer));

        public static readonly CommandSchema CreatePoint = CommandSchema.Object(
            SchemaProperty.Required("point", JTokenType.Object),
            SchemaProperty.Optional("layer", JTokenType.String),
            SchemaProperty.Optional("color_index", JTokenType.Integer));

        public static readonly CommandSchema CreatePolyline = CommandSchema.Object(
            SchemaProperty.Required("points", JTokenType.Array),
            SchemaProperty.Optional("closed", JTokenType.Boolean),
            SchemaProperty.Optional("layer", JTokenType.String),
            SchemaProperty.Optional("color_index", JTokenType.Integer));

        public static readonly CommandSchema CreateRectangle = CommandSchema.Object(
            SchemaProperty.Required("corner1", JTokenType.Object),
            SchemaProperty.Required("corner2", JTokenType.Object),
            SchemaProperty.Optional("layer", JTokenType.String),
            SchemaProperty.Optional("color_index", JTokenType.Integer));

        public static readonly CommandSchema CreateArc = CommandSchema.Object(
            SchemaProperty.Required("center", JTokenType.Object),
            SchemaProperty.Required("radius", JTokenType.Float, JTokenType.Integer),
            SchemaProperty.Required("start_angle", JTokenType.Float, JTokenType.Integer),
            SchemaProperty.Required("end_angle", JTokenType.Float, JTokenType.Integer),
            SchemaProperty.Optional("layer", JTokenType.String),
            SchemaProperty.Optional("color_index", JTokenType.Integer));

        public static readonly CommandSchema CreateEllipse = CommandSchema.Object(
            SchemaProperty.Required("center", JTokenType.Object),
            SchemaProperty.Required("major_radius", JTokenType.Float, JTokenType.Integer),
            SchemaProperty.Required("minor_radius", JTokenType.Float, JTokenType.Integer),
            SchemaProperty.Required("rotation", JTokenType.Float, JTokenType.Integer),
            SchemaProperty.Optional("layer", JTokenType.String),
            SchemaProperty.Optional("color_index", JTokenType.Integer));

        public static readonly CommandSchema CreateText = CommandSchema.Object(
            SchemaProperty.Required("text", JTokenType.String),
            SchemaProperty.Required("position", JTokenType.Object),
            SchemaProperty.Optional("height", JTokenType.Float, JTokenType.Integer),
            SchemaProperty.Optional("rotation", JTokenType.Float, JTokenType.Integer),
            SchemaProperty.Optional("layer", JTokenType.String),
            SchemaProperty.Optional("color_index", JTokenType.Integer));

        public static readonly CommandSchema CreateMText = CommandSchema.Object(
            SchemaProperty.Required("text", JTokenType.String),
            SchemaProperty.Required("location", JTokenType.Object),
            SchemaProperty.Optional("width", JTokenType.Float, JTokenType.Integer),
            SchemaProperty.Optional("height", JTokenType.Float, JTokenType.Integer),
            SchemaProperty.Optional("rotation", JTokenType.Float, JTokenType.Integer),
            SchemaProperty.Optional("layer", JTokenType.String),
            SchemaProperty.Optional("color_index", JTokenType.Integer));

        public static readonly CommandSchema CreateLeader = CommandSchema.Object(
            SchemaProperty.Required("points", JTokenType.Array),
            SchemaProperty.Optional("text", JTokenType.String),
            SchemaProperty.Optional("layer", JTokenType.String),
            SchemaProperty.Optional("color_index", JTokenType.Integer));

        public static readonly CommandSchema CreateTable = CommandSchema.Object(
            SchemaProperty.Required("insertion_point", JTokenType.Object),
            SchemaProperty.Required("rows", JTokenType.Integer),
            SchemaProperty.Required("columns", JTokenType.Integer),
            SchemaProperty.Required("cells", JTokenType.Array),
            SchemaProperty.Optional("layer", JTokenType.String));

        public static readonly CommandSchema ChangeLayer = CommandSchema.Object(
            SchemaProperty.Required("handles", JTokenType.Array),
            SchemaProperty.Required("layer", JTokenType.String),
            SchemaProperty.Optional("create_layer", JTokenType.Boolean),
            SchemaProperty.Optional("color_index", JTokenType.Integer));

        public static readonly CommandSchema ChangeColor = CommandSchema.Object(
            SchemaProperty.Required("handles", JTokenType.Array),
            SchemaProperty.Required("color_index", JTokenType.Integer));

        public static readonly CommandSchema MoveEntities = CommandSchema.Object(
            SchemaProperty.Required("handles", JTokenType.Array),
            SchemaProperty.Required("vector", JTokenType.Object));

        public static readonly CommandSchema RotateEntities = CommandSchema.Object(
            SchemaProperty.Required("handles", JTokenType.Array),
            SchemaProperty.Required("basePoint", JTokenType.Object),
            SchemaProperty.Required("angleDegrees", JTokenType.Float, JTokenType.Integer));

        public static readonly CommandSchema ScaleEntities = CommandSchema.Object(
            SchemaProperty.Required("handles", JTokenType.Array),
            SchemaProperty.Required("basePoint", JTokenType.Object),
            SchemaProperty.Required("scale", JTokenType.Float, JTokenType.Integer));

        public static readonly CommandSchema CopyEntities = CommandSchema.Object(
            SchemaProperty.Required("handles", JTokenType.Array),
            SchemaProperty.Required("vector", JTokenType.Object));

        public static readonly CommandSchema EraseEntities = CommandSchema.Object(
            SchemaProperty.Required("handles", JTokenType.Array));

        public static readonly CommandSchema OffsetEntities = CommandSchema.Object(
            SchemaProperty.Required("handles", JTokenType.Array),
            SchemaProperty.Required("distance", JTokenType.Float, JTokenType.Integer));

        public static readonly CommandSchema GetSelectedTexts = CommandSchema.Object(
            SchemaProperty.Optional("grouping_strength", JTokenType.String),
            SchemaProperty.Optional("include_entities", JTokenType.Boolean));

        public static readonly CommandSchema ListBlocks = CommandSchema.Empty;

        public static readonly CommandSchema GetBlockAttributes = CommandSchema.Object(
            SchemaProperty.Required("handle", JTokenType.String));

        public static readonly CommandSchema InsertBlock = CommandSchema.Object(
            SchemaProperty.Required("block_name", JTokenType.String),
            SchemaProperty.Required("insertion_point", JTokenType.Object),
            SchemaProperty.Optional("block_path", JTokenType.String),
            SchemaProperty.Optional("scale", JTokenType.Float, JTokenType.Integer),
            SchemaProperty.Optional("rotation", JTokenType.Float, JTokenType.Integer),
            SchemaProperty.Optional("attributes", JTokenType.Object));

        public static readonly CommandSchema SetBlockAttributes = CommandSchema.Object(
            SchemaProperty.Required("handle", JTokenType.String),
            SchemaProperty.Required("attributes", JTokenType.Object),
            SchemaProperty.Optional("strict_tags", JTokenType.Boolean));

        public static readonly CommandSchema ExplodeBlock = CommandSchema.Object(
            SchemaProperty.Required("handle", JTokenType.String));

        public static readonly CommandSchema CreateLinearDimension = CommandSchema.Object(
            SchemaProperty.Required("start", JTokenType.Object),
            SchemaProperty.Required("end", JTokenType.Object),
            SchemaProperty.Required("dimension_line_point", JTokenType.Object),
            SchemaProperty.Optional("rotation", JTokenType.Float, JTokenType.Integer),
            SchemaProperty.Optional("layer", JTokenType.String),
            SchemaProperty.Optional("style_name", JTokenType.String));

        public static readonly CommandSchema CreateAlignedDimension = CommandSchema.Object(
            SchemaProperty.Required("start", JTokenType.Object),
            SchemaProperty.Required("end", JTokenType.Object),
            SchemaProperty.Required("dimension_line_point", JTokenType.Object),
            SchemaProperty.Optional("layer", JTokenType.String),
            SchemaProperty.Optional("style_name", JTokenType.String));

        public static readonly CommandSchema CreateRadialDimension = CommandSchema.Object(
            SchemaProperty.Required("entity_handle", JTokenType.String),
            SchemaProperty.Required("dimension_line_point", JTokenType.Object),
            SchemaProperty.Optional("layer", JTokenType.String),
            SchemaProperty.Optional("style_name", JTokenType.String));

        public static readonly CommandSchema CreateDiameterDimension = CommandSchema.Object(
            SchemaProperty.Required("entity_handle", JTokenType.String),
            SchemaProperty.Required("dimension_line_point", JTokenType.Object),
            SchemaProperty.Optional("layer", JTokenType.String),
            SchemaProperty.Optional("style_name", JTokenType.String));

        public static readonly CommandSchema UpdateTexts = CommandSchema.Object(
            SchemaProperty.Required("items", JTokenType.Array),
            SchemaProperty.Optional("apply_unicode_style", JTokenType.Boolean)).WithArrayRoot();

        public static readonly CommandSchema SendCode = CommandSchema.Object(
            SchemaProperty.Required("code", JTokenType.String));

        public static readonly CommandSchema ApplyUnicodeStyle = CommandSchema.Object(
            SchemaProperty.Optional("handles", JTokenType.Array));

        public static readonly CommandSchema CollapseAndRewrite = CommandSchema.Object(
            SchemaProperty.Required("clusters", JTokenType.Array),
            SchemaProperty.Optional("apply_unicode_style", JTokenType.Boolean),
            SchemaProperty.Optional("final_scale", JTokenType.Float, JTokenType.Integer));

        public static readonly CommandSchema TranslateAndRewrite = CommandSchema.Object(
            SchemaProperty.Optional("translations", JTokenType.Array),
            SchemaProperty.Optional("final_scale", JTokenType.Float, JTokenType.Integer));

        public static readonly CommandSchema BatchExecute = CommandSchema.Object(
            SchemaProperty.Required("commands", JTokenType.Array));

        public static readonly CommandSchema RunBakedTool = CommandSchema.Object(
            SchemaProperty.Required("name", JTokenType.String),
            SchemaProperty.Optional("params", JTokenType.Object),
            SchemaProperty.Optional("tool_record", JTokenType.Object));

        public static readonly CommandSchema ApplyBake = CommandSchema.Object(
            SchemaProperty.Required("tool_name", JTokenType.String),
            SchemaProperty.Required("source", JTokenType.String),
            SchemaProperty.Optional("handler_tool", JTokenType.String),
            SchemaProperty.Optional("fixed_args", JTokenType.Object),
            SchemaProperty.Optional("sequence", JTokenType.Array),
            SchemaProperty.Optional("params_schema", JTokenType.Object, JTokenType.String),
            SchemaProperty.Optional("source_code", JTokenType.String));

        public static readonly CommandSchema ZoomExtents = CommandSchema.Empty;

        public static readonly CommandSchema ZoomWindow = CommandSchema.Object(
            SchemaProperty.Required("corner1", JTokenType.Object),
            SchemaProperty.Required("corner2", JTokenType.Object));

        public static readonly CommandSchema ZoomToEntity = CommandSchema.Object(
            SchemaProperty.Required("handle", JTokenType.String));

        public static readonly CommandSchema ExportDxf = CommandSchema.Object(
            SchemaProperty.Required("output_path", JTokenType.String),
            SchemaProperty.Optional("overwrite_existing", JTokenType.Boolean),
            SchemaProperty.Optional("allow_repo_output", JTokenType.Boolean));

        public static readonly CommandSchema GetVariables = CommandSchema.Empty;

        public static readonly CommandSchema SetSystemVariable = CommandSchema.Object(
            SchemaProperty.Required("name", JTokenType.String),
            SchemaProperty.Required("value", JTokenType.String, JTokenType.Integer, JTokenType.Float, JTokenType.Boolean));

        public static readonly CommandSchema SaveDrawing = CommandSchema.Object(
            SchemaProperty.Optional("output_path", JTokenType.String),
            SchemaProperty.Optional("confirm", JTokenType.Boolean),
            SchemaProperty.Optional("overwrite_existing", JTokenType.Boolean),
            SchemaProperty.Optional("allow_repo_output", JTokenType.Boolean));

        public static readonly CommandSchema PurgeDrawing = CommandSchema.Object(
            SchemaProperty.Optional("dry_run", JTokenType.Boolean),
            SchemaProperty.Optional("confirm", JTokenType.Boolean));

        public static readonly CommandSchema SetupLayers = CommandSchema.Object(
            SchemaProperty.Optional("include_wwtp_layers", JTokenType.Boolean));

        public static readonly CommandSchema ListCategories = CommandSchema.Empty;

        public static readonly CommandSchema ListSymbols = CommandSchema.Object(
            SchemaProperty.RequiredNonEmptyString("category"));

        public static readonly CommandSchema DrawPipe = CommandSchema.Object(
            SchemaProperty.Required("start", JTokenType.Object),
            SchemaProperty.Required("end", JTokenType.Object),
            SchemaProperty.Optional("layer", JTokenType.String));

        public static readonly CommandSchema InsertSymbol = CommandSchema.Object(
            SchemaProperty.RequiredNonEmptyString("category"),
            SchemaProperty.RequiredNonEmptyString("symbol"),
            SchemaProperty.Required("position", JTokenType.Object),
            SchemaProperty.Optional("scale", JTokenType.Float, JTokenType.Integer),
            SchemaProperty.Optional("rotation", JTokenType.Float, JTokenType.Integer),
            SchemaProperty.Optional("text_content", JTokenType.String));

        public static readonly CommandSchema AddFlowArrow = CommandSchema.Object(
            SchemaProperty.Required("position", JTokenType.Object),
            SchemaProperty.Required("direction", JTokenType.Object),
            SchemaProperty.Optional("layer", JTokenType.String));

        public static readonly CommandSchema AddEquipmentTag = CommandSchema.Object(
            SchemaProperty.Required("position", JTokenType.Object),
            SchemaProperty.RequiredNonEmptyString("tag_text"),
            SchemaProperty.Optional("layer", JTokenType.String));

        public static readonly CommandSchema AddLineNumber = CommandSchema.Object(
            SchemaProperty.Required("position", JTokenType.Object),
            SchemaProperty.RequiredNonEmptyString("line_text"),
            SchemaProperty.Optional("layer", JTokenType.String));
    }
}
