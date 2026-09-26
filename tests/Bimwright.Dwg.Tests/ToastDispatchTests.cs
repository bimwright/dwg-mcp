using System;
using System.Collections.Generic;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Views.Toast;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class ToastDispatchTests
    {
        [Theory]
        [InlineData("count_entities", true)]
        [InlineData("explode_block", false)]
        [InlineData("send_code", false)]
        [InlineData("batch_execute", false)]
        [InlineData("run_baked_tool", false)]
        public void Actual_dispatch_preserves_response_and_emits_once_after_lock(string cmd, bool success)
        {
            CommandDispatcher.SetSendCodeEnabled(true);
            var wire = new JObject { ["id"] = "a", ["auth"] = "secret", ["cmd"] = cmd, ["params"] = new JObject() }.ToString();
            var expected = new CommandDispatcher("secret").Dispatch(wire);
            var events = new List<McpToastViewModel>();
            bool locked = false;
            DocumentInvoker.Calls = 0;
            var actual = new CommandDispatcher("secret", vm => { locked |= DocumentInvoker.Locked; events.Add(vm); }).Dispatch(wire);
            Assert.Equal(expected, actual);
            Assert.Equal(1, DocumentInvoker.Calls);
            Assert.False(locked);
            Assert.Equal(success, Assert.Single(events).Success);
            Assert.DoesNotContain("private", events[0].Detail ?? "");
            Assert.Equal(expected, new CommandDispatcher("secret", _ => throw new Exception("UI unavailable")).Dispatch(wire));
        }

        [Theory]
        [InlineData("broken", 0)]
        [InlineData("{\"auth\":\"wrong\",\"cmd\":\"count_entities\"}", 0)]
        [InlineData("{\"auth\":\"secret\",\"cmd\":{}}", 0)]
        [InlineData("{\"auth\":\"secret\",\"cmd\":\"unknown\"}", 0)]
        [InlineData("{\"auth\":\"secret\",\"cmd\":\"run_lisp\"}", 1)]
        public void Rejected_requests_do_not_enter_the_document(string wire, int expectedEvents)
        {
            int events = 0;
            DocumentInvoker.Calls = 0;
            var result = new CommandDispatcher("secret", _ => events++).Dispatch(wire);
            Assert.False((bool)JObject.Parse(result)["ok"]);
            Assert.Equal(0, DocumentInvoker.Calls);
            Assert.Equal(expectedEvents, events);
        }
    }
}
