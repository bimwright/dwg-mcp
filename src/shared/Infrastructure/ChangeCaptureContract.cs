namespace Bimwright.Dwg.Plugin
{
    internal static class ChangeCaptureContract
    {
        internal const int Version = 1;
        internal const int MaxDeltas = 200;
        internal const int MaxCaptureBytes = 64 * 1024;

        internal static bool Tracks(string command) => TargetContract.IsWrite(command)
            && !DrawingSessionContract.IsStandaloneDrawingCommand(command)
            && command is not ("save_drawing" or "export_dxf" or "capture_view_image" or "inspect_view_region"
                or "restore_view" or "apply_bake" or "run_lisp");

        internal static bool Supports(string command) => command is "create_line" or "create_circle" or "move_entities" or "erase_entities";
    }
}
