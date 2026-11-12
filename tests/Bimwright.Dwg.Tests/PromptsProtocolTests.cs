using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bimwright.Dwg.Server;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    /// <summary>MCP prompts over a real stdio server; no AutoCAD calls.</summary>
    public class PromptsProtocolTests
    {
        private static readonly string[] PromptNames = { "dwg_review_large_drawing", "dwg_translate_text" };

        [Fact]
        public async Task Prompts_are_listed_and_render_their_arguments()
        {
            await WithServer("", async process =>
            {
                var listed = await Request(process, 2, "prompts/list", new { });
                Assert.Equal(PromptNames, ((JArray)listed["result"]["prompts"]).Select(p => p.Value<string>("name")).OrderBy(n => n, StringComparer.Ordinal));

                var translate = Text(await Request(process, 3, "prompts/get", new { name = "dwg_translate_text", arguments = new { target_language = "English" } }));
                Assert.Contains("into English", translate);
                Assert.Contains("detect it from the selected text", translate);
                Assert.Contains("Writes need my confirmation", translate);
                Assert.DoesNotMatch(@"\{[a-z_]+\}", translate);

                var review = Text(await Request(process, 4, "prompts/get", new { name = "dwg_review_large_drawing", arguments = new { question = "Where are the pumps?" } }));
                Assert.Contains("Question: Where are the pumps?", review);
                Assert.Contains("space='model'", review);
                Assert.DoesNotMatch(@"\{[a-z_]+\}", review);
            });
        }

        [Fact]
        public async Task Read_only_translation_stops_at_the_table()
        {
            await WithServer("--read-only", async process =>
            {
                var translate = Text(await Request(process, 2, "prompts/get", new { name = "dwg_translate_text", arguments = new { } }));
                Assert.Contains("READ-ONLY: read and translate only", translate);
                Assert.Contains("into Vietnamese", translate);
            });
        }

        [Fact]
        public async Task A_prompt_whose_toolsets_are_off_renders_an_enable_notice()
        {
            await WithServer("--toolsets query", async process =>
            {
                var translate = Text(await Request(process, 2, "prompts/get", new { name = "dwg_translate_text", arguments = new { } }));
                Assert.StartsWith("This prompt needs toolsets that are not enabled", translate);
                Assert.Contains("Missing: meta, modify.", translate);
                Assert.Contains("--toolsets query,modify,meta", translate);
                Assert.DoesNotContain("Steps", translate);
            });
        }

        [Fact]
        public void Prompt_bodies_name_only_existing_tools()
        {
            var tools = typeof(Program).Assembly.GetTypes().SelectMany(t => t.GetMethods())
                .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name).Where(n => n != null).ToHashSet();
            var assembly = typeof(Program).Assembly;
            var bodies = assembly.GetManifestResourceNames().Where(n => n.StartsWith("Bimwright.Dwg.Prompts.", StringComparison.Ordinal)).ToArray();
            Assert.Equal(2, bodies.Length);
            foreach (var resource in bodies)
            {
                using var reader = new StreamReader(assembly.GetManifestResourceStream(resource));
                var named = Regex.Matches(reader.ReadToEnd(), @"\bdwg_[a-z_]+\b").Select(m => m.Value).Distinct();
                Assert.All(named, name => Assert.True(tools.Contains(name), resource + " names unknown tool " + name));
            }
        }

        private static string Text(JObject response) => response["result"]["messages"][0]["content"].Value<string>("text");

        private static async Task WithServer(string flags, Func<Process, Task> body)
        {
            var root = Path.Combine(Path.GetTempPath(), "dwg-prompts-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8
            };
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
            start.ArgumentList.Add("--config");
            start.ArgumentList.Add(Path.Combine(root, "absent-config.json"));
            start.ArgumentList.Add("--disable-call-log");
            foreach (var flag in flags.Split(' ', StringSplitOptions.RemoveEmptyEntries)) start.ArgumentList.Add(flag);
            foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("BIMWRIGHT_DWG_", StringComparison.OrdinalIgnoreCase)).ToArray())
                start.Environment.Remove(key);
            start.Environment["LOCALAPPDATA"] = root;
            using var process = Process.Start(start);
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                await Request(process, 1, "initialize", new
                {
                    protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = "dwg-prompt-test", version = "1.0.0" }
                });
                await process.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
                await process.StandardInput.FlushAsync();
                await body(process);
                process.StandardInput.Close();
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            }
            finally
            {
                if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
                await stderr;
                Directory.Delete(root, recursive: true);
            }
        }

        private static async Task<JObject> Request(Process process, int id, string method, object parameters)
        {
            await process.StandardInput.WriteLineAsync(JsonConvert.SerializeObject(new { jsonrpc = "2.0", id, method, @params = parameters }));
            await process.StandardInput.FlushAsync();
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
                Assert.NotNull(line);
                var message = JObject.Parse(line);
                if (message.Value<int?>("id") == id)
                {
                    Assert.Null(message["error"]);
                    return message;
                }
            }
        }
    }
}
