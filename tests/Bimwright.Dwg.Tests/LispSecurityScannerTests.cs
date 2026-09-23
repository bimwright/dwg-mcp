using System.IO;
using System;
using System.Linq;
using Bimwright.Dwg.Server;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class LispSecurityScannerTests
    {
        [Fact]
        public void Finding_limit_does_not_hide_later_high_severity_rules()
        {
            var code = string.Concat(Enumerable.Repeat("; (arxunload \"unused\")\n", 200))
                + "(vl-file-delete \"C:/never-executed.dwg\")";
            var report = LispSecurityScanner.ScanText(code);
            Assert.Equal("dangerous", report.Verdict);
            Assert.True(report.High > 0);
            Assert.True(report.Findings.Count <= 200);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Incomplete_scan_is_refused_for_inline_and_file_inputs(bool useFile)
        {
            var code = new string(' ', LispSecurityScanner.MaxScanChars)
                + "(vl-file-delete \"C:/never-executed.dwg\")";
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".lsp");
            try
            {
                if (useFile) File.WriteAllText(path, code);
                var report = useFile
                    ? LispSecurityScanner.ScanFile(path, out _)
                    : LispSecurityScanner.ScanText(code);
                Assert.Equal("dangerous", report.Verdict);
                Assert.Contains(report.Findings, f => (string)f["category"] == "truncated");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void Clean_lisp_returns_clean()
        {
            var rep = LispSecurityScanner.ScanText("(defun c:HELLO () (princ \"hi\") (princ))");
            Assert.Equal("clean", rep.Verdict);
            Assert.Empty(rep.Findings);
        }

        [Theory]
        [InlineData("(startapp \"calc.exe\")")]
        [InlineData("(shell \"dir\")")]
        [InlineData("(vl-file-delete \"c:/x.dwg\")")]
        [InlineData("(vl-registry-write key \"val\" \"data\")")]
        [InlineData("(arxload \"evil.arx\")")]
        [InlineData("(vlax-create-object \"WScript.Shell\")")]
        [InlineData("(setq f (open \"acaddoc.lsp\" \"w\"))")]
        public void Dangerous_tokens_verdict_dangerous(string code)
        {
            var rep = LispSecurityScanner.ScanText(code);
            Assert.Equal("dangerous", rep.Verdict);
            Assert.True(rep.High > 0);
        }

        [Theory]
        [InlineData("SHELL notepad.exe")]
        [InlineData("_.shell\nnotepad.exe")]
        [InlineData("(princ)\n'SHELL notepad.exe")]
        [InlineData("(command \"_.SHELL\" \"notepad.exe\")")]
        [InlineData("(command-s \".SHELL\" \"notepad.exe\")")]
        [InlineData("(vl-cmdf \"_SHELL\" \"notepad.exe\")")]
        public void Shell_command_forms_are_flagged_without_execution(string code)
        {
            var report = LispSecurityScanner.ScanText(code);
            Assert.Equal("dangerous", report.Verdict);
            Assert.Contains(report.Findings, f => (string)f["category"] == "process-exec");
        }

        [Fact]
        public void Clean_report_does_not_authorize_execution_or_certify_safety()
        {
            var json = LispSecurityScanner.ToJson(LispSecurityScanner.ScanText("(+ 1 2)"), "code");
            Assert.Equal("clean", (string)json["verdict"]);
            Assert.Equal(false, (bool?)json["execution_authorized"]);
            Assert.Equal(false, (bool?)json["safety_assured"]);
            Assert.Contains("not a sandbox", (string)json["limitations"]);
        }

        [Theory]
        [InlineData("(load \"helpers.lsp\")")]
        [InlineData("(eval (read expr))")]
        [InlineData("(vl-file-copy a b)")]
        [InlineData("(vl-registry-read key)")]
        public void Caution_tokens_verdict_caution(string code)
        {
            var rep = LispSecurityScanner.ScanText(code);
            Assert.Equal("caution", rep.Verdict);
            Assert.Equal(0, rep.High);
            Assert.True(rep.Medium + rep.Low > 0);
        }

        [Fact]
        public void Findings_carry_line_numbers()
        {
            var rep = LispSecurityScanner.ScanText("(princ 1)\n(startapp \"x\")\n");
            var finding = rep.Findings[0];
            Assert.Equal(2, (int)finding["line"]);
            Assert.Equal("high", (string)finding["severity"]);
            Assert.Equal("process-exec", (string)finding["category"]);
        }

        [Fact]
        public void Compiled_fas_is_opaque_dangerous()
        {
            var path = Path.Combine(Path.GetTempPath(), "scan-test.fas");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            try
            {
                var rep = LispSecurityScanner.ScanFile(path, out var err);
                Assert.Null(err);
                Assert.True(rep.Opaque);
                Assert.Equal("dangerous", rep.Verdict);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Missing_file_fails_closed()
        {
            var rep = LispSecurityScanner.ScanFile(@"C:\no\such\file-xyz.lsp", out var err);
            Assert.Null(rep);
            Assert.Contains("cannot read file", err);
        }

        [Fact]
        public void Absorb_merges_worst_verdict()
        {
            var a = LispSecurityScanner.ScanText("(load \"x.lsp\")");       // caution
            var b = LispSecurityScanner.ScanText("(startapp \"x\")");      // dangerous
            LispSecurityScanner.AbsorbInto(a, b);
            Assert.Equal("dangerous", a.Verdict);
            Assert.True(a.Findings.Count >= 2);
        }

        [Fact]
        public void Word_boundary_avoids_read_line_false_positive()
        {
            var rep = LispSecurityScanner.ScanText("(setq l (read-line f))");
            Assert.Equal("clean", rep.Verdict);
        }
    }
}
