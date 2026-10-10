using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Bimwright.Dwg.Server;
using Bimwright.Dwg.Server.Logging;
using Bimwright.Dwg.Server.Memory;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    [Collection("Script handlers")]
    public class CallLogTests
    {
        [Fact]
        public void Default_off_writers_do_not_create_directories_or_files()
        {
            var previous = ServerState.Config;
            var root = NewRoot();
            try
            {
                ServerState.Config = new DwgMcpConfig();
                new CallLogWriter(Path.Combine(root, "calls.jsonl")).Append(new ThrowingParameters());
                var server = new ServerLogger(Path.Combine(root, "server-calls.jsonl"));
                server.LogStart("request", "send_code", new ThrowingParameters());
                server.LogFinish("request", "send_code", false, 12, "code-body-marker");
                new JournalLogger(root).Append(new JournalEntry { Detail = "password=hidden" });
                new McpSessionLog(root, "session").Append(new McpSessionLogEntry { Tool = "test" });
                new McpLogger(Path.Combine(root, "debug-calls.log")).Append("test");
                Assert.False(Directory.Exists(root));
            }
            finally { ServerState.Config = previous; Delete(root); }
        }

        [Fact]
        public void Switch_off_neither_appends_nor_deletes_an_existing_file()
        {
            var root = NewRoot();
            var enabled = true;
            try
            {
                var writer = new CallLogWriter(Path.Combine(root, "calls.jsonl"), () => enabled);
                writer.Append(new { tool = "test" });
                var bytes = File.ReadAllBytes(writer.Path);
                enabled = false;
                writer.Append(new ThrowingParameters());
                Assert.Equal(bytes, File.ReadAllBytes(writer.Path));
                enabled = true;
                writer.Append(new { tool = "again" });
                Assert.Equal(2, File.ReadAllLines(writer.Path).Length);
            }
            finally { Delete(root); }
        }

        [Theory]
        [InlineData("return 42;")]
        [InlineData("// Nguyễn\nreturn \"秘密🔐\";")]
        [InlineData("")]
        public void Code_is_length_and_sha256_only_including_nested_batch(string body)
        {
            var input = new JObject
            {
                ["commands"] = new JArray(new JObject
                {
                    ["cmd"] = "send_code",
                    ["params"] = new JObject { ["code"] = body }
                })
            };
            var before = input.ToString();
            var root = NewRoot();
            try
            {
                var writer = new CallLogWriter(Path.Combine(root, "calls.jsonl"), () => true);
                writer.Append(new { tool = "batch_execute", @params = input });
                var logged = JObject.Parse(File.ReadAllText(writer.Path));
                var code = logged["params"]["commands"][0]["params"]["code"];
                Assert.IsType<JObject>(code);
                Assert.Equal(body.Length, code["length"].Value<int>());
                Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant(), code["sha256"].Value<string>());
                Assert.Equal(2, ((JObject)code).Count);
                Assert.Equal(before, input.ToString()); // Never change wire parameters.
            }
            finally { Delete(root); }
        }

        [Fact]
        public void Redaction_happens_before_persistence_for_structured_and_embedded_secrets()
        {
            var json = CallLogPrivacy.Serialize(new
            {
                password = "not-a-pattern-secret",
                access_token = "token-value",
                nested = new { apiKey = "key-value", text = "password=inline-value", path = @"C:\Users\Private User\drawing.dwg" },
                code = "return \"raw-body-marker\";"
            });
            Assert.DoesNotContain("not-a-pattern-secret", json);
            Assert.DoesNotContain("token-value", json);
            Assert.DoesNotContain("key-value", json);
            Assert.DoesNotContain("inline-value", json);
            Assert.DoesNotContain("Private User", json);
            Assert.DoesNotContain("raw-body-marker", json);
        }

        [Fact]
        public void Legacy_journal_and_session_writers_redact_when_explicitly_enabled()
        {
            var root = NewRoot();
            try
            {
                new JournalLogger(root, () => true).Append(new JournalEntry { Detail = "password=journal-secret" });
                new McpSessionLog(root, "session", () => true).Append(new McpSessionLogEntry { Error = "token=session-secret" });
                Assert.DoesNotContain("journal-secret", File.ReadAllText(Path.Combine(root, "journal.jsonl")));
                Assert.DoesNotContain("session-secret", File.ReadAllText(Path.Combine(root, "session.jsonl")));
            }
            finally { Delete(root); }
        }

        [Fact]
        public void Serialization_failure_does_not_create_a_directory()
        {
            var root = NewRoot();
            try
            {
                Assert.ThrowsAny<Exception>(() => new CallLogWriter(Path.Combine(root, "calls.jsonl"), () => true).Append(new ThrowingParameters()));
                Assert.False(Directory.Exists(root));
            }
            finally { Delete(root); }
        }

        [Theory]
        [InlineData("send_code")]
        [InlineData("dwg_send_code")]
        [InlineData("batch_execute")]
        public void Server_logger_keeps_metadata_but_not_code_or_error_echoes(string tool)
        {
            var root = NewRoot();
            try
            {
                var path = Path.Combine(root, "calls.jsonl");
                var logger = new ServerLogger(path, () => true);
                logger.LogStart("request-1", tool, new { code = "return \"raw-code-marker\";" });
                logger.LogFinish("request-1", tool, false, 123, "compile error: raw-code-marker");
                var lines = File.ReadAllLines(path);
                Assert.Equal(2, lines.Length);
                Assert.DoesNotContain("raw-code-marker", File.ReadAllText(path));
                var start = JObject.Parse(lines[0]);
                var finish = JObject.Parse(lines[1]);
                Assert.Equal("start", start.Value<string>("phase"));
                Assert.Equal("request-1", finish.Value<string>("request_id"));
                Assert.Equal("finish", finish.Value<string>("phase"));
                Assert.False(finish.Value<bool>("success"));
                Assert.Equal(123, finish.Value<long>("duration_ms"));
                Assert.IsType<JObject>(start["params"]["code"]);
            }
            finally { Delete(root); }
        }

        [Fact]
        public void Server_logger_never_throws_on_unwritable_path()
        {
            var root = NewRoot();
            try
            {
                Directory.CreateDirectory(root);
                var blocker = Path.Combine(root, "file-not-directory");
                File.WriteAllText(blocker, "preserve");
                var logger = new ServerLogger(Path.Combine(blocker, "calls.jsonl"), () => true);
                logger.LogStart("id", "query", new { value = 1 });
                logger.LogFinish("id", "query", true, 12);
                Assert.Equal("preserve", File.ReadAllText(blocker));
            }
            finally { Delete(root); }
        }

        private sealed class ThrowingParameters
        {
            public string Value => throw new InvalidOperationException("must not serialize while disabled");
        }

        private static string NewRoot() => Path.Combine(Path.GetTempPath(), "dwg-calllog-" + Guid.NewGuid().ToString("N"));
        private static void Delete(string root) { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
