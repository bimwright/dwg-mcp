using Bimwright.Dwg.Plugin;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class SecretMaskerTests
    {
        [Fact]
        public void Mask_HidesKnownSecretPatterns()
        {
            var masked = SecretMasker.Mask("auth_token=abc123 password: hunter2 Bearer token-value");

            Assert.DoesNotContain("abc123", masked);
            Assert.DoesNotContain("hunter2", masked);
            Assert.DoesNotContain("token-value", masked);
            Assert.Contains("<secret>", masked);
        }
    }
}
