using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin
{
    /// <summary>Host-free atomic batch contract shared by the gateway, plugin and tests.</summary>
    public static class BatchContract
    {
        /// <summary>Reported by plugin health; the gateway sends atomic batches only when it matches.</summary>
        public const int AtomicVersion = 1;

        // Commands whose only drawing effects are database writes made in a nested
        // db.TransactionManager transaction, so aborting the outer transaction undoes them.
        // Views, files, system variables, scripts, purge and rewrite/selection state are excluded.
        private static readonly HashSet<string> AtomicCommands = new HashSet<string>(StringComparer.Ordinal)
        {
            "get_drawing_info", "get_entity_properties", "list_layers", "query_entities", "count_entities",
            "select_by_layer", "select_by_type", "list_blocks", "get_block_attributes", "get_variables",
            "create_layer", "create_line", "create_circle", "create_point", "create_polyline", "create_rectangle",
            "create_arc", "create_ellipse", "create_text", "create_mtext", "create_leader", "create_table",
            "insert_block", "set_block_attributes", "explode_block",
            "create_linear_dimension", "create_aligned_dimension", "create_radial_dimension", "create_diameter_dimension",
            "change_layer", "change_color", "move_entities", "rotate_entities", "scale_entities", "copy_entities",
            "erase_entities", "offset_entities", "update_texts"
        };

        public static bool IsAtomic(JToken parameters)
            => parameters is JObject obj && obj["atomic"]?.Type == JTokenType.Boolean && obj.Value<bool>("atomic");

        public static bool SupportsAtomic(string cmd) => cmd != null && AtomicCommands.Contains(cmd);
    }
}
