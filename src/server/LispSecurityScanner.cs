using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server
{
    /// <summary>
    /// Static triage for AutoLISP payloads before dwg_run_lisp executes them. AutoLISP is
    /// not sandboxed — it can spawn processes (startapp/shell), automate COM
    /// (WScript.Shell, XMLHTTP), write persistence hooks (acad.lsp/acaddoc.lsp/registry),
    /// and stage further payloads (load/eval/read). This scanner reports findings so an
    /// agent can refuse dangerous content; it is a lint, not a guarantee — obfuscated or
    /// compiled (.fas/.vlx) payloads cannot be fully inspected and are flagged dangerous.
    /// </summary>
    public static class LispSecurityScanner
    {
        public const int MaxScanChars = 2_000_000;
        private const int MaxFindings = 200;

        public sealed class Report
        {
            public string Verdict = "clean";   // clean | caution | dangerous
            public bool Opaque;
            public List<JObject> Findings = new List<JObject>();
            public int High, Medium, Low;
        }

        private sealed class Rule
        {
            public Regex Pattern;
            public string Severity;   // high | medium | low
            public string Category;
            public string Detail;
        }

        private static readonly RegexOptions Rx = RegexOptions.IgnoreCase | RegexOptions.Compiled;

        // Code tokens matched in call/quote position: ( token  or  ' token
        private static Rule Fn(string token, string severity, string category, string detail) => new Rule
        {
            Pattern = new Regex(@"[(']\s*" + Regex.Escape(token) + @"(?![\w-])", Rx),
            Severity = severity,
            Category = category,
            Detail = detail
        };

        // Strings/filenames matched anywhere (they usually sit inside quoted literals).
        private static Rule Any(string regex, string severity, string category, string detail) => new Rule
        {
            Pattern = new Regex(regex, Rx),
            Severity = severity,
            Category = category,
            Detail = detail
        };

        private static readonly Rule[] Rules =
        {
            // --- high: refuse ---
            Fn("startapp", "high", "process-exec", "Launches an external process (startapp)."),
            Fn("shell", "high", "process-exec", "Launches an OS shell (VisualLISP shell)."),
            Fn("arxload", "high", "native-load", "Loads a compiled ARX/.NET module — full unmanaged code execution."),
            Fn("arxunload", "medium", "native-load", "Unloads an ARX module."),
            Fn("vl-arx-import", "high", "native-load", "Imports ARX/.NET functions — native code execution."),
            Fn("dos_cmd", "high", "process-exec", "DOSLib dos_cmd — runs an OS command."),
            Fn("dos_execute", "high", "process-exec", "DOSLib dos_execute — runs an external program."),
            Fn("dos_shellexe", "high", "process-exec", "DOSLib dos_shellexe — shell-executes a file."),
            Fn("dos_run", "high", "process-exec", "DOSLib dos_run — runs an external program."),
            Fn("dos_exewait", "high", "process-exec", "DOSLib dos_exewait — runs an external program."),
            Fn("vl-file-delete", "high", "file-destructive", "Deletes files from disk."),
            Fn("vl-file-rename", "high", "file-destructive", "Renames/moves files on disk."),
            Fn("vl-registry-write", "high", "persistence", "Writes the Windows registry — classic persistence (e.g. Run keys)."),
            Fn("vl-registry-delete", "high", "persistence", "Deletes Windows registry keys."),

            // Dangerous COM progIds / executables referenced as strings.
            Any(@"wscript\.shell", "high", "com-target", "WScript.Shell — process exec + registry access via COM."),
            Any(@"shell\.application", "high", "com-target", "Shell.Application — shell exec via COM."),
            Any(@"scripting\.filesystemobject", "high", "com-target", "Scripting.FileSystemObject — unrestricted file read/write via COM."),
            Any(@"msxml2\.|winhttp\.|adodb\.stream", "high", "com-target", "HTTP/stream COM object — download/upload exfiltration path."),
            Any(@"wscript\.network", "high", "com-target", "WScript.Network — machine/user/network recon via COM."),
            Any(@"powershell|cmd\.exe|wmic\b|rundll32|regsvr32|mshta", "high", "process-exec", "Names a system executable used to run payloads."),

            // AutoCAD startup/persistence files — the classic acad.lsp worm vector.
            Any(@"acaddoc\.lsp|acad20\d\ddoc\.lsp", "high", "persistence", "Touches acaddoc.lsp — auto-loaded into EVERY drawing session."),
            Any(@"acad20\d\d\.lsp|\bacad\.lsp|acad\.mnl|acaddoc\.mnl|acad\.vlx|\bacad\.fas", "high", "persistence", "Touches an AutoCAD startup file — persists code across sessions."),

            // --- medium: caution (run_lisp still executes but surfaces warnings) ---
            Fn("vlax-create-object", "medium", "com-automation", "Creates a COM object — check which progId it instantiates."),
            Fn("vlax-get-or-create-object", "medium", "com-automation", "Gets/creates a COM object — check which progId it instantiates."),
            Fn("vlax-get-object", "low", "com-automation", "Attaches to a running COM object."),
            Fn("vlax-invoke-method", "low", "com-automation", "Invokes a COM method — benign for AutoCAD objects, dangerous for shell/FS objects."),
            Fn("vla-sendcommand", "medium", "command-injection", "vla-SendCommand posts a command string to the document — can run further code."),
            Fn("vl-file-copy", "medium", "file-op", "Copies files — can duplicate drawings or stage payloads."),
            Fn("vl-registry-read", "medium", "recon", "Reads the Windows registry."),
            Fn("vl-registry-descendents", "medium", "recon", "Enumerates registry subkeys."),
            Fn("load", "medium", "staged-payload", "(load ...) pulls in another LISP/FAS/VLX file — second stage not scanned here."),
            Fn("eval", "medium", "obfuscation", "(eval ...) executes constructed expressions — can hide intent."),
            Fn("read", "medium", "obfuscation", "(read ...) parses strings into executable expressions — obfuscation primitive."),
            Fn("quit", "medium", "destructive", "(quit) terminates the AutoCAD session."),
            Fn("exit", "medium", "destructive", "(exit) terminates the VisualLISP/host session."),

            // --- low: informational ---
            Fn("getenv", "low", "recon", "Reads environment variables."),
            Fn("vl-file-directory-list", "low", "recon", "Lists directory contents — filesystem recon."),
            Any(@"\(\s*open\s+""[^""]*""\s+""[wa]""", "medium", "file-op", "Opens a file for write/append — check the target path."),
            Any(@"\(\s*setvar\s+""(cmdecho|expert|cmddia|filedia|attdia)""", "low", "stealth", "Suppresses command echo/dialogs — common stealth indicator."),
        };

        /// <summary>Scan raw LISP source text. Source name is only used for reporting.</summary>
        public static Report ScanText(string text)
        {
            var report = new Report();
            if (string.IsNullOrEmpty(text))
            {
                return report;
            }

            if (text.Length > MaxScanChars)
            {
                report.High = 1;
                report.Verdict = "dangerous";
                report.Findings.Add(JObject.FromObject(new
                {
                    severity = "high",
                    category = "truncated",
                    line = 0,
                    match = (string)null,
                    detail = $"Source exceeds {MaxScanChars} chars and cannot be fully inspected; execution is refused."
                }));
                return report;
            }
            var scan = text;

            foreach (var rule in Rules)
            {
                foreach (Match m in rule.Pattern.Matches(scan))
                {
                    // Cap only the response detail, never the security decision.
                    Count(report, rule.Severity);
                    if (report.Findings.Count >= MaxFindings) continue;
                    report.Findings.Add(JObject.FromObject(new
                    {
                        severity = rule.Severity,
                        category = rule.Category,
                        line = LineOf(scan, m.Index),
                        match = Truncate(m.Value.Trim(), 100),
                        detail = rule.Detail
                    }));
                }
            }

            report.Verdict = report.High > 0 ? "dangerous" : (report.Medium + report.Low > 0 ? "caution" : "clean");
            return report;
        }

        /// <summary>Fold one report into another (worst verdict wins, findings concat).</summary>
        public static void AbsorbInto(Report dst, Report src)
        {
            if (dst == null || src == null) return;
            dst.Findings.AddRange(src.Findings);
            dst.High += src.High;
            dst.Medium += src.Medium;
            dst.Low += src.Low;
            dst.Opaque = dst.Opaque || src.Opaque;
            dst.Verdict = dst.High > 0 ? "dangerous" : (dst.Medium + dst.Low > 0 ? "caution" : "clean");
        }

        /// <summary>
        /// Scan a LISP file by path. Compiled payloads (.fas/.vlx) are opaque to static
        /// analysis and are reported dangerous by policy — same treatment as flagged content.
        /// </summary>
        public static Report ScanFile(string path, out string error)
        {
            error = null;
            var report = new Report();
            var ext = Path.GetExtension(path ?? "") ?? "";
            if (ext.Equals(".fas", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".vlx", StringComparison.OrdinalIgnoreCase))
            {
                report.Opaque = true;
                report.Verdict = "dangerous";
                report.High = 1;
                report.Findings.Add(JObject.FromObject(new
                {
                    severity = "high",
                    category = "opaque-payload",
                    line = 0,
                    match = ext.ToLowerInvariant(),
                    detail = "Compiled " + ext.ToUpperInvariant() + " payload cannot be statically inspected — treated as unsafe by policy."
                }));
                return report;
            }

            string text;
            try
            {
                // One extra character distinguishes a complete source from an
                // oversized file without allocating the entire untrusted file.
                using (var reader = new StreamReader(path))
                {
                    var buffer = new char[MaxScanChars + 1];
                    var count = reader.ReadBlock(buffer, 0, buffer.Length);
                    text = new string(buffer, 0, count);
                }
            }
            catch (Exception ex)
            {
                error = $"cannot read file for inspection: {ex.Message}";
                return null;
            }

            report = ScanText(text);
            return report;
        }

        public static JObject ToJson(Report report, string source)
        {
            return JObject.FromObject(new
            {
                ok = true,
                verdict = report.Verdict,
                opaque = report.Opaque,
                summary = new { high = report.High, medium = report.Medium, low = report.Low },
                findings = report.Findings,
                scanned = source
            });
        }

        private static void Count(Report r, string severity)
        {
            if (severity == "high") r.High++;
            else if (severity == "medium") r.Medium++;
            else r.Low++;
        }

        private static int LineOf(string text, int index)
        {
            var line = 1;
            for (var i = 0; i < index; i++)
            {
                if (text[i] == '\n') line++;
            }
            return line;
        }

        private static string Truncate(string s, int max)
            => s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
