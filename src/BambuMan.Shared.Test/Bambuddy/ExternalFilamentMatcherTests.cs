using BambuMan.Shared;
using Newtonsoft.Json;

namespace BambuMan.Shared.Test.Bambuddy
{
    [Trait("Category", "Bambuddy")]
    public class ExternalFilamentMatcherTests
    {
        [Fact(DisplayName = "LoadEmbeddedFilaments returns the bundled catalog")]
        public void LoadEmbeddedFilaments_ReturnsCatalog()
        {
            var catalog = ExternalFilamentMatcher.LoadEmbeddedFilaments();

            Assert.NotEmpty(catalog);
        }

        [Fact(DisplayName = "GenerateUnknownFilament produces the UNKNOWN fallback")]
        public void GenerateUnknownFilament_HasUnknownMaterial()
        {
            var unknown = ExternalFilamentMatcher.GenerateUnknownFilament();

            Assert.Equal("Unknown", unknown.Name);
            Assert.Equal("UNKNOWN", unknown.Material);
            Assert.Equal("Bambu Lab", unknown.Manufacturer);
        }

        [Fact(DisplayName = "FindExternalFilament matches ABS Black against the embedded catalog")]
        public async Task FindExternalFilament_MatchesAbsBlack()
        {
            var catalog = ExternalFilamentMatcher.LoadEmbeddedFilaments();
            var info = JsonConvert.DeserializeObject<BambuFilamentInfo>(SampleTags.AbsBlack)!;

            var result = await ExternalFilamentMatcher.FindExternalFilament(catalog, info);

            Assert.Single(result);
            Assert.Equal("ABS", result[0].Material);
            Assert.Equal("Black", result[0].Name);
        }
    }
}
