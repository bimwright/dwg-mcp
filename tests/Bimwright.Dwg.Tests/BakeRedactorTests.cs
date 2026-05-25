using Bimwright.Dwg.Plugin;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class BakeRedactorTests
    {
        [Fact]
        public void RedactSource_MasksSecretsBeforePersistence()
        {
            var source = "var api_key = \"sk-live\"; var auth_token = \"abc123\";";

            var redacted = BakeRedactor.RedactSource(source);

            Assert.DoesNotContain("sk-live", redacted);
            Assert.DoesNotContain("abc123", redacted);
            Assert.Contains("<secret>", redacted);
        }
    }
}
