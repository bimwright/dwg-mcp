using ModelContextProtocol.Protocol;

namespace Bimwright.Dwg.Server;

/// <summary>Fit whole read-only text clusters using the host's per-cluster checkpoints.</summary>
internal static class TextReadResponse
{
    internal static int ReadRequestedCap(CallToolRequestParams request)
        => EntityQueryResponse.ReadRequestedCap(request);

    internal static CallToolResult Fit(CallToolResult result, int cap)
        => EntityQueryResponse.Fit(result, cap, toolName: "dwg_get_selected_texts", collection: "clusters",
            versionKey: "text_page_version", identityField: "cluster_key");
}
