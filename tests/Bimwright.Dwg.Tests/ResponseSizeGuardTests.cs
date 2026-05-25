using Bimwright.Dwg.Plugin;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class ResponseSizeGuardTests
    {
        [Fact]
        public void ApplyResult_TruncatesOversizedSerializedResult()
        {
            var result = ResponseSizeGuard.ApplyResult(
                new { text = new string('x', 80) },
                maxSerializedChars: 40);

            var json = JObject.FromObject(result);

            Assert.True(json.Value<bool>("truncated"));
            Assert.True(json.Value<int>("original_length") > 40);
            Assert.True(json.Value<string>("preview").Length <= 40);
        }

        [Fact]
        public void ApplyResult_LeavesSmallResultUnchanged()
        {
            var result = ResponseSizeGuard.ApplyResult(new { ok = true }, maxSerializedChars: 1000);

            var json = JObject.FromObject(result);

            Assert.True(json.Value<bool>("ok"));
            Assert.Null(json["truncated"]);
        }
    }
}
