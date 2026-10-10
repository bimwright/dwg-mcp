using System;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Dwg.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests.Helpers
{
    /// <summary>Captures the wire call a tool shell sends through ToolGateway.SendOverride.
    /// SendOverride is a process-wide static and xUnit runs test classes in parallel,
    /// so every capture holds a shared gate for the duration of the call.</summary>
    internal sealed class Capture
    {
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);

        public string Command { get; private set; }
        public object Parameters { get; private set; }
        public bool Sent { get; private set; }

        public static async Task<Capture> Send(Func<Task<string>> call)
        {
            var captured = new Capture();
            await Gate.WaitAsync();
            try
            {
                ToolGateway.SendOverride = (command, parameters) =>
                {
                    captured.Command = command;
                    captured.Parameters = parameters;
                    captured.Sent = true;
                    return Task.FromResult(new McpResponse { Ok = true, Result = new JObject() });
                };

                var text = await call();
                var json = JObject.Parse(text);
                Assert.True(captured.Sent, text);
                Assert.True(json.Value<bool>("ok"), text);
                return captured;
            }
            finally
            {
                ToolGateway.SendOverride = null;
                Gate.Release();
            }
        }

        /// <summary>Run a tool that must answer locally. A wire send fails the test.</summary>
        public static async Task<JObject> Local(Func<Task<string>> call)
        {
            await Gate.WaitAsync();
            try
            {
                var sent = false;
                ToolGateway.SendOverride = (command, parameters) =>
                {
                    sent = true;
                    return Task.FromResult(new McpResponse { Ok = true });
                };

                var text = await call();
                Assert.False(sent, "tool contacted the plugin: " + text);
                return JObject.Parse(text);
            }
            finally
            {
                ToolGateway.SendOverride = null;
                Gate.Release();
            }
        }

        public JObject Json()
        {
            if (Parameters == null)
                return new JObject();
            if (Parameters is JObject obj)
                return (JObject)obj.DeepClone();
            return JObject.FromObject(Parameters, JsonSerializer.Create(new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            }));
        }
    }
}
