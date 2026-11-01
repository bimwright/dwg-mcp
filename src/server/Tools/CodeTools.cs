using System.ComponentModel;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin;
using ModelContextProtocol.Server;
using Newtonsoft.Json;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class CodeTools
    {
        [McpServerTool(Name = "dwg_send_code"), Description(
            "Execute a C# snippet through dwg_send_code against the AutoCAD .NET API as an escape hatch. " +
            "Enabled by default on the meta toolset; the AutoCAD MCPDISABLECODE command disables it " +
            "for the session (MCPENABLECODE re-enables). " +
            "WARNING: send_code runs arbitrary code with full access to the AutoCAD process " +
            "and local filesystem. Only use with trusted agents. " +
            "This is not a sandbox. Never use it to bypass a run_lisp refusal or execute untrusted LISP. " +
            "Globals available: Document doc, Database db, Editor ed. The script runs on the " +
            "document-lock thread; end with 'return <expr>;' or a trailing expression to return a " +
            "JSON-safe DTO value in the result field; AutoCAD/COM objects, including nested objects, are rejected. " +
            "Only inline synchronous snippets are supported: async/await and #load directives are rejected before execution. " +
            "Do not move AutoCAD API calls to Task.Run or other threads. Use System.Console.WriteLine for stdout output. " +
            "Optional data is JSON (512 KiB UTF-8 maximum), exposed as data (Newtonsoft JToken); null is omitted. Keep literals, coordinates and lists in data instead of code. " +
            "compile_only=true compiles without invoking script statements; an active drawing is still required. " +
            "Results add compiled, executed, compile_only, diagnostics[{source,line,column,code,message,severity}] and optional runtime location. Read result.ok for script success. " +
            "Enhanced calls require a matching plugin and must be direct calls, outside batch/baked tools; unsupported plugins are refused before code is sent. " +
            "Execution has cooperative 30s cancellation. " +
            "An active drawing is required: when none is open, use dwg_list_recent_drawings and dwg_open_drawing first; the host refuses before executing code. " +
            "doc, db and ed are supplied from that active drawing. " +
            "Put database writes in one using transaction from db.TransactionManager.StartTransaction(), and Commit only after validation succeeds. " +
            "Return host warnings and handled errors in your DTO. AutoCAD UNDO can reverse drawing edits; " +
            "filesystem writes are outside it, and the gateway does not roll back earlier committed transactions after a later script error. " +
            "Default namespaces: System, System.Collections.Generic, System.Linq, Autodesk.AutoCAD.ApplicationServices, " +
            "Autodesk.AutoCAD.DatabaseServices, Autodesk.AutoCAD.EditorInput, Autodesk.AutoCAD.Geometry. " +
            "Large result/stdout is stored as complete UTF-8 JSON before the 10 MiB plugin cutoff, with output_artifact path/bytes/SHA-256/preview when storage permits. " +
            "The final MCP budget may store a second artifact of the full MCP result it received; inspect scope and upstream truncation. Artifact retention is 7 days, 256 MiB total, 64 files; files above 128 MiB are unavailable. " +
            "Keep results small and never repeat a drawing mutation just to recover output.")]
        public static Task<string> SendCode(
            [Description("C# code to execute")] string code,
            [Description("Optional JSON input, up to 512 KiB UTF-8. Available as data (JToken); null is treated as omitted.")] JsonElement? data = null,
            [Description("Compile and return diagnostics without executing any script statement.")] bool compile_only = false)
        {
            var json = data.HasValue && data.Value.ValueKind != JsonValueKind.Null && data.Value.ValueKind != JsonValueKind.Undefined
                ? data.Value.GetRawText() : null;
            if (json == null && !compile_only) return ToolGateway.LoggedCall("send_code", new { code }, new { code });
            try { ScriptInputContract.ParseData(json); }
            catch (ArgumentException ex) { return Task.FromResult(JsonConvert.SerializeObject(new McpResponse { Ok = false, Error = ex.Message })); }
            var request = new Newtonsoft.Json.Linq.JObject {
                ["code"] = code, ["compile_only"] = compile_only, ["script_inputs_version"] = ScriptInputContract.Version
            };
            if (json != null) request["data_json"] = json;
            return ToolGateway.LoggedCall("send_code", new { code, compile_only, data_bytes = json == null ? 0 : System.Text.Encoding.UTF8.GetByteCount(json) }, request);
        }

        [McpServerTool(Name = "dwg_run_lisp", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false), Description(
            "Unavailable: LISP execution is blocked in this build. Retained for compatibility; " +
            "all file/code/command inputs are refused locally without reading files or contacting AutoCAD. " +
            "There is no isolated executor or approved-script trust mechanism. " +
            "Use dwg_inspect_lisp for static analysis only; clean/caution/dangerous never authorize execution. " +
            "MCPENABLECODE does not override this restriction. " +
            "Do not bypass refusal through dwg_send_code, batch, ToolBaker, or reformatted payloads.")]
        public static Task<string> RunLisp(
            [Description("Legacy file input; execution is blocked")] string file = null,
            [Description("Legacy source input; execution is blocked")] string code = null,
            [Description("Legacy command input; execution is blocked")] string command = null)
            => Task.FromResult(JsonConvert.SerializeObject(new
            {
                ok = false,
                blocked = true,
                error_code = LispExecutionPolicy.ErrorCode,
                execution_authorized = false,
                error = LispExecutionPolicy.Refusal
            }));
    }
}
