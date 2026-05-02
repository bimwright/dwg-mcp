using Bimwright.Dwg.Server;
using Newtonsoft.Json;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class TranslateAndRewriteDtoTests
    {
        [Fact]
        public void TranslationItem_roundtrips()
        {
            var original = new TranslationItem { Id = 3, NewText = "Be tong da dam C30" };
            var json = JsonConvert.SerializeObject(original);
            var decoded = JsonConvert.DeserializeObject<TranslationItem>(json);
            Assert.Equal(3, decoded.Id);
            Assert.Equal("Be tong da dam C30", decoded.NewText);
        }

        [Fact]
        public void TranslateRequest_roundtrips()
        {
            var original = new TranslateRequest
            {
                Translations = new[]
                {
                    new TranslationItem { Id = 0, NewText = "Mat cat" },
                    new TranslationItem { Id = 2, NewText = "Dat dam" }
                }
            };
            var json = JsonConvert.SerializeObject(original);
            var decoded = JsonConvert.DeserializeObject<TranslateRequest>(json);
            Assert.Equal(2, decoded.Translations.Length);
            Assert.Equal(0, decoded.Translations[0].Id);
            Assert.Equal("Mat cat", decoded.Translations[0].NewText);
        }
    }
}
