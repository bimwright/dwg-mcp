using System;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.ToolBaker;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class RunBakedToolHandler : IAcadCommand
    {
        private readonly Func<Document, string, JToken, CommandResult> _executeCommand;

        public RunBakedToolHandler(Func<Document, string, JToken, CommandResult> executeCommand)
        {
            _executeCommand = executeCommand ?? throw new ArgumentNullException(nameof(executeCommand));
        }

        public string Name => "run_baked_tool";
        public string Description => "Run a baked DWG tool from the plugin registry.";
        public CommandSchema Schema => CommandSchemas.RunBakedTool;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            var name = (string)parameters?["name"];
            var runtimeParams = parameters?["params"] as JObject ?? new JObject();
            var record = ReadRecord(parameters?["tool_record"] as JObject);
            if (record == null)
            {
                return CommandResult.Fail("baked tool record is required: " + name);
            }

            if (!string.Equals(record.Name, name, StringComparison.Ordinal))
            {
                return CommandResult.Fail("baked tool record name mismatch: " + name);
            }

            var validation = ToolCompiler.ValidateRecord(record);
            if (!validation.Ok)
            {
                return CommandResult.Fail(validation.Error);
            }

            if (record.Source == "macro")
            {
                return RunMacro(doc, record);
            }

            if (!BakedToolDispatchAuthorizer.IsAllowed(record.HandlerTool))
            {
                return CommandResult.Fail("baked tool target is not allowed: " + record.HandlerTool);
            }

            var fixedArgs = ParseObject(record.FixedArgs);
            var merged = BakedToolParameterDefaults.Merge(fixedArgs, runtimeParams);
            return _executeCommand(doc, record.HandlerTool, merged);
        }

        private static BakedToolRecord ReadRecord(JObject obj)
        {
            if (obj == null)
            {
                return null;
            }

            return obj.ToObject<BakedToolRecord>();
        }

        private CommandResult RunMacro(Document doc, BakedToolRecord record)
        {
            JArray sequence;
            try { sequence = JArray.Parse(string.IsNullOrWhiteSpace(record.Sequence) ? "[]" : record.Sequence); }
            catch { sequence = new JArray(); }

            var commands = new JArray();
            foreach (var token in sequence)
            {
                var step = token as JObject;
                var cmd = step != null ? (string)step["cmd"] : token.Value<string>();
                if (!BakedToolDispatchAuthorizer.IsAllowed(cmd))
                {
                    return CommandResult.Fail("baked tool target is not allowed: " + cmd);
                }
                commands.Add(new JObject
                {
                    ["cmd"] = cmd,
                    ["params"] = step?["params"] ?? new JObject()
                });
            }

            return BatchExecutor.Run(new JObject { ["commands"] = commands }, (cmd, p) => _executeCommand(doc, cmd, p));
        }

        private static JObject ParseObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new JObject();
            try { return JObject.Parse(json); }
            catch { return new JObject(); }
        }
    }
}
