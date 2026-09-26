using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin
{
    // Substitute native registration and lock acquisition only. Tests execute the real
    // Dispatch/ExecuteCommand/validation/serialization path, including nested dispatch.
    public partial class CommandDispatcher
    {
        private void RegisterCommands()
        {
            _commands = new Dictionary<string, IAcadCommand>
            {
                ["count_entities"] = new FixtureCommand("count_entities", (_, __) => CommandResult.Success(new { count = 3 })),
                ["send_code"] = new FixtureCommand("send_code", (_, __) => CommandResult.Success(new { ok = false, error = "compile failed" })),
                ["run_lisp"] = new FixtureCommand("run_lisp", (_, __) => throw new Exception("must never execute")),
                ["explode_block"] = new FixtureCommand("explode_block", (_, __) => throw new Exception("native failure"))
            };
            _commands["batch_execute"] = new FixtureCommand("batch_execute", (doc, p) =>
            {
                var child = ExecuteCommand(doc, "send_code", new JObject());
                return CommandResult.Success(new { partial_failure = false, results = new[] { new { cmd = "send_code", ok = child.Ok, result = child.Result } } });
            });
            _commands["run_baked_tool"] = new FixtureCommand("run_baked_tool", (doc, p) => ExecuteCommand(doc, "batch_execute", new JObject()));
        }
        private sealed class FixtureCommand : IAcadCommand
        {
            private readonly Func<Document, JToken, CommandResult> _run;
            public FixtureCommand(string name, Func<Document, JToken, CommandResult> run) { Name = name; _run = run; }
            public string Name { get; }
            public string Description => "test host boundary";
            public CommandSchema Schema => null;
            public CommandResult Execute(Document doc, JToken p) => _run(doc, p);
        }
    }
    public static class DocumentInvoker
    {
        [ThreadStatic] public static bool Locked;
        [ThreadStatic] public static int Calls;
        public static T Invoke<T>(Func<Document, T> action)
        {
            Calls++;
            Locked = true;
            try { return action(new Document()); }
            finally { Locked = false; }
        }
    }
}
