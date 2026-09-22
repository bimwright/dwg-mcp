using System;
using System.IO;
using System.Text;
using System.Threading;
using Bimwright.Dwg.Plugin;
using Autodesk.AutoCAD.ApplicationServices;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    /// <summary>
    /// Runs AutoLISP through the command line: the generated wrapper file is queued via
    /// SendStringToExecute (safe from any thread; CommandAsync input executes in command
    /// context on the main thread, not under our document lock). The wrapper evaluates the
    /// payload inside vl-catch-all-apply and writes the last expression's value — or the
    /// error message — to a result file we poll for.
    /// </summary>
    public class RunLispHandler : IAcadCommand
    {
        private const int ResultTimeoutMilliseconds = 30000;
        private const int PollIntervalMilliseconds = 100;

        public string Name => "run_lisp";
        public string Description => "Load and run AutoLISP code/files/commands in the active document.";
        public CommandSchema Schema => CommandSchemas.RunLisp;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            var file = (string)parameters?["file"];
            var code = (string)parameters?["code"];
            var command = (string)parameters?["command"];

            if (string.IsNullOrWhiteSpace(file) && string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(command))
                return CommandResult.Fail("at least one of file, code, or command is required");

            if (!string.IsNullOrWhiteSpace(file) && !Path.IsPathRooted(file))
                return CommandResult.Fail("file must be an absolute path");

            // A command starting with "(" is embedded in the wrapper so its value is
            // captured; a bare name (c:FOO) is queued as a command token after the load —
            // fire-and-forget, its output is not captured.
            var commandExpr = command != null && command.Trim().StartsWith("(", StringComparison.Ordinal)
                ? command.Trim()
                : null;
            var commandToken = commandExpr == null && !string.IsNullOrWhiteSpace(command)
                ? command.Trim()
                : null;

            var workDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Bimwright", "Dwg", "lisp");
            var stamp = Guid.NewGuid().ToString("N");
            var scriptPath = Path.Combine(workDir, stamp + ".lsp");
            var resultPath = Path.Combine(workDir, stamp + ".result.txt");

            try
            {
                Directory.CreateDirectory(workDir);
                File.WriteAllText(scriptPath, BuildWrapper(file, code, commandExpr, resultPath), new UTF8Encoding(true));

                var input = "(load \"" + ToLispPath(scriptPath) + "\")"
                    + (commandToken != null ? " " + commandToken : "")
                    + " ";
                doc.SendStringToExecute(input, true, false, true);

                if (!WaitForResult(resultPath))
                    return CommandResult.Fail("lisp execution timed out after 30s; the command may still be queued at the AutoCAD command line");

                var payload = File.ReadAllText(resultPath);
                if (payload.StartsWith("ERROR:", StringComparison.Ordinal))
                    return CommandResult.Success(new { ok = false, result = (object)null, error = ErrorSanitizer.Sanitize(payload.Substring(6)) });

                return CommandResult.Success(new
                {
                    ok = true,
                    result = payload.Length == 0 ? (object)null : payload,
                    error = (string)null
                });
            }
            catch (Exception ex)
            {
                return CommandResult.Fail($"{ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                try { if (File.Exists(scriptPath)) File.Delete(scriptPath); } catch { }
                try { if (File.Exists(resultPath)) File.Delete(resultPath); } catch { }
            }
        }

        private static string BuildWrapper(string file, string code, string commandExpr, string resultPath)
        {
            var body = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(file))
                body.Append("  (load \"").Append(ToLispPath(file)).Append("\")\n");
            if (!string.IsNullOrWhiteSpace(code))
                body.Append(code.Trim()).Append('\n');
            if (commandExpr != null)
                body.Append("  ").Append(commandExpr).Append('\n');
            if (body.Length == 0)
                body.Append("  nil\n");

            var sb = new StringBuilder();
            sb.Append("(vl-load-com)\n");
            sb.Append("(setq $bmwr$ (vl-catch-all-apply '(lambda () (progn\n");
            sb.Append(body);
            sb.Append(")) '()))\n");
            sb.Append("(setq $bmwf$ (open \"").Append(ToLispPath(resultPath)).Append("\" \"w\"))\n");
            sb.Append("(if (vl-catch-all-error-p $bmwr$)\n");
            sb.Append("  (princ (strcat \"ERROR:\" (vl-catch-all-error-message $bmwr$)) $bmwf$)\n");
            sb.Append("  (princ (vl-prin1-to-string $bmwr$) $bmwf$))\n");
            sb.Append("(close $bmwf$)\n");
            return sb.ToString();
        }

        private static bool WaitForResult(string resultPath)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(ResultTimeoutMilliseconds);
            while (DateTime.UtcNow < deadline)
            {
                if (File.Exists(resultPath))
                {
                    // Give (close) a beat to flush before reading.
                    Thread.Sleep(50);
                    return true;
                }
                Thread.Sleep(PollIntervalMilliseconds);
            }
            return false;
        }

        private static string ToLispPath(string path)
            => path.Replace('\\', '/').Replace("\"", "\\\"");
    }
}
