using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Bimwright.Dwg.Server.Tools;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class ToolsListSnapshotTests
    {
        [Fact]
        public void CurrentBackedMcpToolsUseDwgPrefix()
        {
            var names = typeof(QueryTools).Assembly.GetTypes()
                .Where(IsMcpToolType)
                .SelectMany(GetMcpToolNames)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(new[]
            {
                "dwg_accept_bake_suggestion",
                "dwg_apply_unicode_style",
                "dwg_batch_execute",
                "dwg_change_color",
                "dwg_change_layer",
                "dwg_collapse_and_rewrite",
                "dwg_copy_entities",
                "dwg_count_entities",
                "dwg_create_aligned_dimension",
                "dwg_create_arc",
                "dwg_create_bake_issue_draft",
                "dwg_create_circle",
                "dwg_create_diameter_dimension",
                "dwg_create_ellipse",
                "dwg_create_layer",
                "dwg_create_leader",
                "dwg_create_line",
                "dwg_create_linear_dimension",
                "dwg_create_mtext",
                "dwg_create_point",
                "dwg_create_polyline",
                "dwg_create_radial_dimension",
                "dwg_create_rectangle",
                "dwg_create_table",
                "dwg_create_text",
                "dwg_dismiss_bake_suggestion",
                "dwg_erase_entities",
                "dwg_explode_block",
                "dwg_export_dxf",
                "dwg_get_block_attributes",
                "dwg_get_current_target",
                "dwg_get_drawing_info",
                "dwg_get_entity_properties",
                "dwg_get_selected_texts",
                "dwg_get_variables",
                "dwg_insert_block",
                "dwg_list_available_targets",
                "dwg_list_bake_suggestions",
                "dwg_list_baked_tools",
                "dwg_list_blocks",
                "dwg_list_layers",
                "dwg_move_entities",
                "dwg_offset_entities",
                "dwg_purge_drawing",
                "dwg_query_entities",
                "dwg_rotate_entities",
                "dwg_run_baked_tool",
                "dwg_save_drawing",
                "dwg_scale_entities",
                "dwg_select_by_layer",
                "dwg_select_by_type",
                "dwg_send_code",
                "dwg_set_block_attributes",
                "dwg_set_system_variable",
                "dwg_switch_target",
                "dwg_translate_and_rewrite",
                "dwg_update_texts",
                "dwg_zoom_extents",
                "dwg_zoom_to_entity",
                "dwg_zoom_window"
            }, names);
            Assert.All(names, name => Assert.StartsWith("dwg_", name, StringComparison.Ordinal));
        }

        [Fact]
        public void ChangeColorWrapperUsesSnakeCaseColorIndexParameter()
        {
            var method = typeof(ModifyTools).GetMethod(nameof(ModifyTools.ChangeColor));

            Assert.NotNull(method);
            Assert.Equal(new[] { "handles", "color_index" }, method.GetParameters().Select(p => p.Name).ToArray());
        }

        [Fact]
        public void CreateMTextWrapperForwardsRotationParameter()
        {
            var method = typeof(AnnotationTools).GetMethod(nameof(AnnotationTools.CreateMText));

            Assert.NotNull(method);
            Assert.Equal(new[]
            {
                "text",
                "location",
                "width",
                "height",
                "rotation",
                "layer",
                "color_index"
            }, method.GetParameters().Select(p => p.Name).ToArray());
        }

        [Fact]
        public void SetBlockAttributesWrapperForwardsStrictTagsParameter()
        {
            var method = typeof(BlockWriteTools).GetMethod(nameof(BlockWriteTools.SetBlockAttributes));

            Assert.NotNull(method);
            Assert.Equal(new[] { "handle", "attributes", "strict_tags" }, method.GetParameters().Select(p => p.Name).ToArray());
        }

        [Fact]
        public void CreateLinearDimensionWrapperForwardsRotationParameter()
        {
            var method = typeof(DimensionTools).GetMethod(nameof(DimensionTools.CreateLinearDimension));

            Assert.NotNull(method);
            Assert.Equal(new[]
            {
                "start",
                "end",
                "dimension_line_point",
                "rotation",
                "layer",
                "style_name"
            }, method.GetParameters().Select(p => p.Name).ToArray());
            var rotation = Assert.Single(method.GetParameters(), p => p.Name == "rotation");
            Assert.Equal(typeof(double), rotation.ParameterType);
            Assert.True(rotation.HasDefaultValue);
            Assert.Equal(0d, rotation.DefaultValue);

            var source = ReadServerSource("Tools", "DimensionTools.cs");
            Assert.Contains("double rotation = 0d", source, StringComparison.Ordinal);
            Assert.Contains("request[\"rotation\"] = rotation;", source, StringComparison.Ordinal);
        }

        [Fact]
        public void BlockToolClassesSplitReadOnlyAndWriteSurfaces()
        {
            var assembly = typeof(QueryTools).Assembly;
            var blockTools = GetToolType(assembly, "Bimwright.Dwg.Server.Tools.BlockTools");
            var blockWriteTools = GetToolType(assembly, "Bimwright.Dwg.Server.Tools.BlockWriteTools");

            Assert.True(IsMcpToolType(blockTools), $"{blockTools.FullName} must be an MCP tool type.");
            Assert.True(IsMcpToolType(blockWriteTools), $"{blockWriteTools.FullName} must be an MCP tool type.");
            Assert.Equal(new[]
            {
                "dwg_get_block_attributes",
                "dwg_list_blocks"
            }, GetSortedMcpToolNames(blockTools));
            Assert.Equal(new[]
            {
                "dwg_explode_block",
                "dwg_insert_block",
                "dwg_set_block_attributes"
            }, GetSortedMcpToolNames(blockWriteTools));
        }

        [Fact]
        public void AnnotationAndDimensionToolClassesExposeExactWriteSurfaces()
        {
            var assembly = typeof(QueryTools).Assembly;
            var annotationTools = GetToolType(assembly, "Bimwright.Dwg.Server.Tools.AnnotationTools");
            var dimensionTools = GetToolType(assembly, "Bimwright.Dwg.Server.Tools.DimensionTools");

            Assert.True(IsMcpToolType(annotationTools), $"{annotationTools.FullName} must be an MCP tool type.");
            Assert.True(IsMcpToolType(dimensionTools), $"{dimensionTools.FullName} must be an MCP tool type.");
            Assert.Equal(new[]
            {
                "dwg_create_leader",
                "dwg_create_mtext",
                "dwg_create_table",
                "dwg_create_text"
            }, GetSortedMcpToolNames(annotationTools));
            Assert.Equal(new[]
            {
                "dwg_create_aligned_dimension",
                "dwg_create_diameter_dimension",
                "dwg_create_linear_dimension",
                "dwg_create_radial_dimension"
            }, GetSortedMcpToolNames(dimensionTools));
        }

        [Fact]
        public void ResolveToolTypesForRegistration_EnforcesPlan3ExplicitAndReadOnlyToolsets()
        {
            var method = typeof(Bimwright.Dwg.Server.Program).GetMethod(
                "ResolveToolTypesForRegistration",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.True(
                method != null,
                "Program must expose non-public static ResolveToolTypesForRegistration(HashSet<string> enabled, bool readOnly) returning tool Types so read-only registration can be unit-tested.");
            Assert.Equal(
                new[] { typeof(HashSet<string>), typeof(bool) },
                method.GetParameters().Select(p => p.ParameterType).ToArray());

            var defaultTypeNames = InvokeToolTypeResolver(
                method,
                Bimwright.Dwg.Server.ToolsetFilter.DefaultOn,
                readOnly: false);
            Assert.Equal(new[]
            {
                "BatchTools",
                "MetaTools",
                "ModifyTools",
                "QueryTools",
                "ViewTools"
            }, defaultTypeNames);

            var defaultReadOnlyTypeNames = InvokeToolTypeResolver(
                method,
                Bimwright.Dwg.Server.ToolsetFilter.DefaultOn,
                readOnly: true);
            Assert.Equal(new[]
            {
                "MetaTools",
                "QueryTools",
                "ViewTools"
            }, defaultReadOnlyTypeNames);

            var codeWriteTypeNames = InvokeToolTypeResolver(method, new[] { "code" }, readOnly: false);
            var codeReadOnlyTypeNames = InvokeToolTypeResolver(method, new[] { "code" }, readOnly: true);
            Assert.Equal(new[] { "CodeTools" }, codeWriteTypeNames);
            Assert.Equal(Array.Empty<string>(), codeReadOnlyTypeNames);

            var toolBakerWriteTypeNames = InvokeToolTypeResolver(method, new[] { "toolbaker" }, readOnly: false);
            var toolBakerReadOnlyTypeNames = InvokeToolTypeResolver(method, new[] { "toolbaker" }, readOnly: true);
            Assert.Equal(new[]
            {
                "ToolBakerTools",
                "ToolBakerWriteTools"
            }, toolBakerWriteTypeNames);
            Assert.Equal(new[] { "ToolBakerTools" }, toolBakerReadOnlyTypeNames);

            var annotationWriteTypeNames = InvokeToolTypeResolver(method, new[] { "annotation" }, readOnly: false);
            var annotationReadOnlyTypeNames = InvokeToolTypeResolver(method, new[] { "annotation" }, readOnly: true);
            Assert.Equal(new[] { "AnnotationTools" }, annotationWriteTypeNames);
            Assert.Equal(Array.Empty<string>(), annotationReadOnlyTypeNames);

            var dimensionWriteTypeNames = InvokeToolTypeResolver(method, new[] { "dimension" }, readOnly: false);
            var dimensionReadOnlyTypeNames = InvokeToolTypeResolver(method, new[] { "dimension" }, readOnly: true);
            Assert.Equal(new[] { "DimensionTools" }, dimensionWriteTypeNames);
            Assert.Equal(Array.Empty<string>(), dimensionReadOnlyTypeNames);

            var blockReadOnlyTypeNames = InvokeToolTypeResolver(method, new[] { "block" }, readOnly: true);
            var blockWriteTypeNames = InvokeToolTypeResolver(method, new[] { "block" }, readOnly: false);

            Assert.Equal(new[]
            {
                "BlockTools"
            }, blockReadOnlyTypeNames);
            Assert.Equal(new[]
            {
                "BlockTools",
                "BlockWriteTools"
            }, blockWriteTypeNames);

            var viewWriteTypeNames = InvokeToolTypeResolver(method, new[] { "view" }, readOnly: false);
            var viewReadOnlyTypeNames = InvokeToolTypeResolver(method, new[] { "view" }, readOnly: true);
            Assert.Equal(new[] { "ViewTools" }, viewWriteTypeNames);
            Assert.Equal(new[] { "ViewTools" }, viewReadOnlyTypeNames);

            var exportWriteTypeNames = InvokeToolTypeResolver(method, new[] { "export" }, readOnly: false);
            var exportReadOnlyTypeNames = InvokeToolTypeResolver(method, new[] { "export" }, readOnly: true);
            Assert.Equal(new[] { "ExportTools" }, exportWriteTypeNames);
            Assert.Equal(Array.Empty<string>(), exportReadOnlyTypeNames);

            var drawingWriteTypeNames = InvokeToolTypeResolver(method, new[] { "drawing" }, readOnly: false);
            var drawingReadOnlyTypeNames = InvokeToolTypeResolver(method, new[] { "drawing" }, readOnly: true);
            Assert.Equal(new[] { "DrawingTools", "DrawingWriteTools" }, drawingWriteTypeNames);
            Assert.Equal(new[] { "DrawingTools" }, drawingReadOnlyTypeNames);
        }

        private static string[] GetMcpToolNames(Type type)
        {
            return type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Select(method => method.GetCustomAttributes().FirstOrDefault(a => a.GetType().Name == "McpServerToolAttribute"))
                .Where(attr => attr != null)
                .Select(attr => (string)attr.GetType().GetProperty("Name")?.GetValue(attr))
                .Select(name => string.IsNullOrWhiteSpace(name) ? throw new InvalidOperationException("MCP tool must set an explicit Name.") : name)
                .ToArray();
        }

        private static string[] GetSortedMcpToolNames(Type type)
            => GetMcpToolNames(type)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

        private static Type GetToolType(Assembly assembly, string fullName)
        {
            var type = assembly.GetType(fullName);

            Assert.True(type != null, $"{fullName} is missing.");
            return type;
        }

        private static string[] InvokeToolTypeResolver(
            MethodInfo method,
            IEnumerable<string> enabledToolsets,
            bool readOnly)
        {
            var enabled = new HashSet<string>(enabledToolsets, StringComparer.OrdinalIgnoreCase);
            var result = method.Invoke(null, new object[] { enabled, readOnly });

            Assert.True(
                result is IEnumerable<Type>,
                "Program.ResolveToolTypesForRegistration must return IEnumerable<Type>.");
            return ((IEnumerable<Type>)result)
                .Select(type => type.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
        }

        private static bool IsMcpToolType(Type type)
            => type.GetCustomAttributes().Any(a => a.GetType().Name == "McpServerToolTypeAttribute");

        private static string ReadServerSource(params string[] parts)
        {
            var pathParts = new string[parts.Length + 7];
            pathParts[0] = AppContext.BaseDirectory;
            pathParts[1] = "..";
            pathParts[2] = "..";
            pathParts[3] = "..";
            pathParts[4] = "..";
            pathParts[5] = "..";
            pathParts[6] = Path.Combine("src", "server");
            Array.Copy(parts, 0, pathParts, 7, parts.Length);

            return File.ReadAllText(Path.GetFullPath(Path.Combine(pathParts)));
        }
    }
}
