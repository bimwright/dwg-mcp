using System;
using Bimwright.Dwg.Plugin;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class HealthSnapshotTests
    {
        [Fact]
        public void Host_with_no_drawing_is_healthy_and_identity_cannot_be_overwritten_by_state_or_reply_mutations()
        {
            var identity = new JObject { ["host_year"] = 2027, ["process_id"] = 42, ["assembly_informational_version"] = "2.1.0+loaded" };
            var reply = HealthSnapshot.Capture(identity, () => new JObject
            {
                ["has_active_document"] = false,
                ["setup_plugin"] = new JObject { ["assembly_informational_version"] = "0.0.0" }
            });
            Assert.True(reply.Value<bool>("ok"));
            Assert.False(reply.Value<bool>("has_active_document"));
            Assert.Equal(2027, reply.Value<int>("autocad_year"));
            Assert.Equal("available", reply.Value<string>("state_probe"));
            Assert.Equal("2.1.0+loaded", reply["setup_plugin"].Value<string>("assembly_informational_version"));
            reply["setup_plugin"]["assembly_informational_version"] = "0.0.0";
            Assert.Equal("2.1.0+loaded", identity.Value<string>("assembly_informational_version"));
        }

        [Theory]
        [InlineData(true, "UI_BUSY", "busy")]
        [InlineData(false, "HOST_STATE_UNAVAILABLE", "unavailable")]
        public void Unavailable_UI_state_preserves_the_loaded_plugin_identity_without_inventing_document_state(bool timeout, string code, string state)
        {
            var identity = new JObject { ["host_year"] = 2027, ["process_id"] = 42 };
            var reply = HealthSnapshot.Capture(identity, () => throw (timeout ? (Exception)new TimeoutException() : new InvalidOperationException()));
            Assert.False(reply.Value<bool>("ok"));
            Assert.False(reply.Value<bool>("healthy"));
            Assert.Equal(code, reply["error"].Value<string>("code"));
            Assert.Equal(state, reply.Value<string>("state_probe"));
            Assert.Equal(JTokenType.Null, reply["has_active_document"].Type);
            Assert.Equal(42, reply["setup_plugin"].Value<int>("process_id"));
        }
    }
}
