using Newtonsoft.Json;
using Bimwright.Dwg.Server;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class DtoRoundTripTests
    {
        [Fact]
        public void UpdateTextsRequest_roundtrips_with_unicode_style_flag()
        {
            var original = new UpdateTextsRequest
            {
                Items = new[]
                {
                    new UpdateTextItem { Handle = "2A4F", NewText = "Ban ve ky thuat" }
                },
                ApplyUnicodeStyle = true
            };
            var json = JsonConvert.SerializeObject(original);
            var decoded = JsonConvert.DeserializeObject<UpdateTextsRequest>(json);

            Assert.Single(decoded.Items);
            Assert.Equal("2A4F", decoded.Items[0].Handle);
            Assert.Equal("Ban ve ky thuat", decoded.Items[0].NewText);
            Assert.True(decoded.ApplyUnicodeStyle);
        }
    }
}
