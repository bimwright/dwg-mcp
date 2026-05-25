using Bimwright.Dwg.Plugin;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class ResponseSizeGuardTests
    {
        [Fact]
        public void DefaultLimit_IsTenMegabytes()
        {
            Assert.Equal(10 * 1024 * 1024, ResponseSizeGuard.DefaultMaxSerializedBytes);
        }

        [Fact]
        public void ApplyResult_TruncatesOversizedSerializedResult()
        {
            var result = ResponseSizeGuard.ApplyResult(
                new { text = new string('x', 80) },
                maxSerializedBytes: 40);

            var json = JObject.FromObject(result);

            Assert.True(json.Value<bool>("truncated"));
            Assert.True(json.Value<int>("original_size_bytes") > 40);
            Assert.True(json.Value<string>("preview").Length <= 40);
        }

        [Fact]
        public void ApplyResult_UsesUtf8ByteCountForUnicode()
        {
            var result = ResponseSizeGuard.ApplyResult(
                new { text = new string('ắ', 20) },
                maxSerializedBytes: 50);

            var json = JObject.FromObject(result);

            Assert.True(json.Value<bool>("truncated"));
            Assert.True(json.Value<int>("original_size_bytes") > 50);
        }

        [Fact]
        public void ApplyResult_LeavesSmallResultUnchanged()
        {
            var result = ResponseSizeGuard.ApplyResult(new { ok = true }, maxSerializedBytes: 1000);

            var json = JObject.FromObject(result);

            Assert.True(json.Value<bool>("ok"));
            Assert.Null(json["truncated"]);
        }
    }
}
