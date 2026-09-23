namespace Bimwright.Dwg.Plugin
{
    /// <summary>
    /// Static scanning cannot authorize arbitrary code in the user's AutoCAD process.
    /// Keep execution closed until an enforceable isolation/trust design is implemented.
    /// Shared by the server and every plugin year; no CLI, input or session override.
    /// </summary>
    public static class LispExecutionPolicy
    {
        public const string ErrorCode = "lisp_execution_blocked";
        public const string Refusal =
            "LISP execution is blocked: this build has no isolated executor or approved-script trust mechanism. "
            + "No LISP was queued or executed. Use dwg_inspect_lisp for static analysis only; "
            + "a clean verdict does not authorize execution. Do not bypass this refusal through "
            + "send_code, batch, ToolBaker, or reformatted payloads.";
    }
}
