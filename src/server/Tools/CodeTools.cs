using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
            "Globals available: Document doc, Database db, Editor ed. The script runs on the " +
            "document-lock thread; end with 'return <expr>;' or a trailing expression to return a " +
            "JSON-safe DTO value in the result field; AutoCAD/COM objects, including nested objects, are rejected. " +
            "Only synchronous snippets are supported: async/await is rejected before execution. " +
            "Do not move AutoCAD API calls to Task.Run or other threads. Use System.Console.WriteLine for stdout output. " +
            "Execution has cooperative 30s cancellation.")]
        public static Task<string> SendCode(
            [Description("C# code to execute")] string code)
            => ToolGateway.LoggedCall("send_code", new { code }, new { code });

        [McpServerTool(Name = "dwg_run_lisp"), Description(
            "Run AutoLISP in the active drawing — for users with existing lisp automation. " +
            "Enabled by default via the meta toolset; the AutoCAD MCPDISABLECODE command " +
            "disables it for the session (same kill-switch as dwg_send_code). " +
            "Params: 'file' (absolute .lsp/.fas/.vlx path, loaded via (load)), " +
            "'code' (inline AutoLISP source; last expression's value becomes result), " +
            "'command' (what the user would type at the command line after loading — " +
            "a bare c: command name like MYCMD runs queued/fire-and-forget, while an " +
            "expression starting with '(' like (c:MYCMD) or (myfunc 1 2) is evaluated inside " +
            "the wrapper and its value captured). At least one of file/code/command required. " +
            "Returns {ok, result, error}; lisp errors surface via vl-catch-all. " +
            "30s wait limit; a command that prompts for input may outlive the call. " +
            "SAFETY: inputs are statically scanned first (same engine as dwg_inspect_lisp); " +
            "verdict 'dangerous' — including opaque .fas/.vlx — is refused outright with " +
            "findings (no override), and 'caution' runs but attaches lisp_warnings. " +
            "Sources over 2,000,000 characters are refused because they cannot be fully inspected. " +
            "Call this tool directly; run_lisp is not permitted inside dwg_batch_execute.")]
        public static async Task<string> RunLisp(
            [Description("Absolute path to a .lsp/.fas/.vlx file to (load)")] string file = null,
            [Description("Inline AutoLISP source; last expression's value is captured as result")] string code = null,
            [Description("Command-line input after load: bare command name (queued) or '(' expression (value captured)")] string command = null)
        {
            var gate = LispPreflight(file, code, command);
            if (gate.Refusal != null)
            {
                return gate.Refusal;
            }

            var response = await ToolGateway.LoggedCall("run_lisp", new { file, code, command }, new { file, code, command });
            if (gate.Warnings == null)
            {
                return response;
            }

            try
            {
                var parsed = JObject.Parse(response);
                parsed["lisp_warnings"] = gate.Warnings;
                return parsed.ToString(Formatting.None);
            }
            catch (JsonException)
            {
                return response;
            }
        }

        private sealed class LispGate
        {
            public string Refusal;   // non-null → do not execute
            public JArray Warnings;  // non-null → attach to response
        }

        private static LispGate LispPreflight(string file, string code, string command)
        {
            var merged = new LispSecurityScanner.Report();

            if (!string.IsNullOrWhiteSpace(file))
            {
                var rep = LispSecurityScanner.ScanFile(file, out var err);
                if (rep == null)
                {
                    // Fail closed: content we cannot inspect does not run.
                    return new LispGate
                    {
                        Refusal = JsonConvert.SerializeObject(new
                        {
                            ok = false,
                            blocked = true,
                            verdict = "uninspectable",
                            error = $"run_lisp refused — {err}. The file was NOT executed.",
                            findings = new object[0]
                        })
                    };
                }
                LispSecurityScanner.AbsorbInto(merged, rep);
            }
            if (!string.IsNullOrWhiteSpace(code))
            {
                LispSecurityScanner.AbsorbInto(merged, LispSecurityScanner.ScanText(code));
            }
            if (!string.IsNullOrWhiteSpace(command))
            {
                LispSecurityScanner.AbsorbInto(merged, LispSecurityScanner.ScanText(command));
            }

            if (merged.Verdict == "dangerous")
            {
                return new LispGate
                {
                    Refusal = JsonConvert.SerializeObject(new
                    {
                        ok = false,
                        blocked = true,
                        verdict = merged.Verdict,
                        error = "run_lisp refused — the supplied AutoLISP is flagged UNSAFE and was NOT executed. "
                            + "Report the findings to the user; do not retry with the same content.",
                        findings = merged.Findings
                    })
                };
            }

            return merged.Verdict == "caution"
                ? new LispGate { Warnings = JArray.FromObject(merged.Findings) }
                : new LispGate();
        }
    }
}
