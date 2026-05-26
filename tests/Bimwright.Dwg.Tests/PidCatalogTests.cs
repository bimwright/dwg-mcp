using System.Linq;
using Bimwright.Dwg.Pid;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class PidCatalogTests
    {
        [Fact]
        public void GetCategories_ReturnsAllStandardCategories()
        {
            var categories = PidCatalog.GetCategories();

            Assert.Contains("ACTUATORS", categories);
            Assert.Contains("ANNOTATION", categories);
            Assert.Contains("EQUIPMENT", categories);
            Assert.Contains("PUMPS-BLOWERS", categories);
            Assert.Contains("TANKS", categories);
            Assert.Contains("VALVES", categories);
        }

        [Fact]
        public void GetSymbols_ReturnsKnownSymbolsForCategories()
        {
            var pumpSymbols = PidCatalog.GetSymbols("PUMPS-BLOWERS");
            Assert.Contains("PUMP-METERING", pumpSymbols);

            var valveSymbols = PidCatalog.GetSymbols("VALVES");
            Assert.Contains("VA-KNIFEGATE", valveSymbols);

            var equipSymbols = PidCatalog.GetSymbols("EQUIPMENT");
            Assert.Contains("EQUIP-CLARIFIER", equipSymbols);

            var annotSymbols = PidCatalog.GetSymbols("ANNOTATION");
            Assert.Contains("ANNOT-FLOWARROW", annotSymbols);
        }

        [Fact]
        public void GetSymbols_WithUnknownCategory_ReturnsEmpty()
        {
            var symbols = PidCatalog.GetSymbols("BOGUS-CATEGORY");
            Assert.Empty(symbols);
        }
    }
}
