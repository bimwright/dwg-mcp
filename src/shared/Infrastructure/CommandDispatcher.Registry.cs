using System.Collections.Generic;
using Bimwright.Dwg.Plugin.Handlers;

namespace Bimwright.Dwg.Plugin
{
    public partial class CommandDispatcher
    {
        private void RegisterCommands()
        {
            _commands = new Dictionary<string, IAcadCommand>
            {
                { "get_drawing_info",        new GetDrawingInfoHandler() },
                { "get_entity_properties",   new GetEntityPropertiesHandler() },
                { "get_selected_texts",      new GetSelectedTextsHandler() },
                { "list_layers",             new ListLayersHandler() },
                { "query_entities",          new QueryEntitiesHandler() },
                { "count_entities",          new CountEntitiesHandler() },
                { "select_by_layer",         new SelectByLayerHandler() },
                { "select_by_type",          new SelectByTypeHandler() },
                { "create_layer",            new CreateLayerHandler() },
                { "create_line",             new CreateLineHandler() },
                { "create_circle",           new CreateCircleHandler() },
                { "create_point",            new CreatePointHandler() },
                { "create_polyline",         new CreatePolylineHandler() },
                { "create_rectangle",        new CreateRectangleHandler() },
                { "create_arc",              new CreateArcHandler() },
                { "create_ellipse",          new CreateEllipseHandler() },
                { "create_text",             new CreateTextHandler() },
                { "create_mtext",            new CreateMTextHandler() },
                { "create_leader",           new CreateLeaderHandler() },
                { "create_table",            new CreateTableHandler() },
                { "list_blocks",             new ListBlocksHandler() },
                { "get_block_attributes",    new GetBlockAttributesHandler() },
                { "insert_block",            new InsertBlockHandler() },
                { "set_block_attributes",    new SetBlockAttributesHandler() },
                { "explode_block",           new ExplodeBlockHandler() },
                { "create_linear_dimension", new CreateLinearDimensionHandler() },
                { "create_aligned_dimension", new CreateAlignedDimensionHandler() },
                { "create_radial_dimension", new CreateRadialDimensionHandler() },
                { "create_diameter_dimension", new CreateDiameterDimensionHandler() },
                { "change_layer",            new ChangeLayerHandler() },
                { "change_color",            new ChangeColorHandler() },
                { "move_entities",           new MoveEntitiesHandler() },
                { "rotate_entities",         new RotateEntitiesHandler() },
                { "scale_entities",          new ScaleEntitiesHandler() },
                { "copy_entities",           new CopyEntitiesHandler() },
                { "erase_entities",          new EraseEntitiesHandler() },
                { "offset_entities",         new OffsetEntitiesHandler() },
                { "update_texts",            new UpdateTextsHandler() },
                { "send_code",               new SendCodeHandler() },
                { "run_lisp",                new RunLispHandler() },
                { "apply_unicode_style",     new ApplyUnicodeStyleHandler() },
                { "collapse_and_rewrite",    new CollapseAndRewriteHandler() },
                { "translate_and_rewrite",   new TranslateAndRewriteHandler() },
                { "list_baked_tools",        new ListBakedToolsHandler() },
                { "zoom_extents",            new ZoomExtentsHandler() },
                { "zoom_window",             new ZoomWindowHandler() },
                { "zoom_to_entity",          new ZoomToEntityHandler() },
                { "capture_view_image",      new CaptureViewImageHandler() },
                { "export_dxf",              new ExportDxfHandler() },
                { "get_variables",           new GetVariablesHandler() },
                { "set_system_variable",     new SetSystemVariableHandler() },
                { "save_drawing",            new SaveDrawingHandler() },
                { "purge_drawing",           new PurgeDrawingHandler() },
            };
            _commands.Add("apply_bake", new ApplyBakeSuggestionHandler((cmd, parameters) => ValidateCommand(cmd, parameters, out _)));
            _commands.Add("batch_execute", new BatchExecuteHandler(ExecuteCommand));
            _commands.Add("run_baked_tool", new RunBakedToolHandler(ExecuteCommand));
        }
    }
}
