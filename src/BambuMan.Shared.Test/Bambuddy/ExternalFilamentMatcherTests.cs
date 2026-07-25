using BambuMan.Shared;
using BambuMan.Shared.Models;
using Newtonsoft.Json;
using System.Text.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

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

        [Fact(DisplayName = "A remote override set forces a filament the internal set doesn't name")]
        public async Task FindExternalFilament_HonoursRemoteForcedId()
        {
            var catalog = ExternalFilamentMatcher.LoadEmbeddedFilaments();
            var info = JsonConvert.DeserializeObject<BambuFilamentInfo>(SampleTags.AbsBlack)!;

            // Stands in for a set fetched from the api after this build shipped: the tag's variant is not in
            // FilamentMatchOverrides.Internal, so only the newer set can redirect it.
            var remote = new FilamentOverrideSet
            {
                Version = FilamentMatchOverrides.CurrentVersion + 1,
                ForcedIds = [new(new(MaterialVariantIdentifier: info.MaterialVariantIdentifier), "bambulab_pla_jadewhite_1000_175_n")]
            };

            var overridden = await ExternalFilamentMatcher.FindExternalFilament(catalog, info, remote);

            Assert.Single(overridden);
            Assert.Equal("bambulab_pla_jadewhite_1000_175_n", overridden[0].Id);

            // Same tag, internal set: the override must not leak across calls.
            var internalOnly = await ExternalFilamentMatcher.FindExternalFilament(catalog, info);

            Assert.Single(internalOnly);
            Assert.Equal("Black", internalOnly[0].Name);
        }

        [Fact(DisplayName = "Omitting the override set is the same as passing the internal one")]
        public async Task FindExternalFilament_DefaultsToInternalOverrides()
        {
            var catalog = ExternalFilamentMatcher.LoadEmbeddedFilaments();

            // A00-W1 is an internal forced id: the tag reports plain white, the catalog calls it Jade White.
            var info = new BambuFilamentInfo([0x01, 0x02, 0x03, 0x04])
            {
                MaterialVariantIdentifier = "A00-W1",
                FilamentType = "PLA",
                DetailedFilamentType = "PLA Basic",
                Color = "FFFFFFFF"
            };

            var implicitSet = await ExternalFilamentMatcher.FindExternalFilament(catalog, info);
            var explicitSet = await ExternalFilamentMatcher.FindExternalFilament(catalog, info, FilamentMatchOverrides.Internal);

            Assert.Equal("bambulab_pla_jadewhite_1000_175_n", Assert.Single(implicitSet).Id);
            Assert.Equal(explicitSet.Select(x => x.Id), implicitSet.Select(x => x.Id));
        }

        [Fact(DisplayName = "The override set survives the json round trip used by the api and the disk cache")]
        public void FilamentOverrideSet_RoundTripsThroughJson()
        {
            // Web defaults are what both the minimal api response and FilamentOverrideService use.
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

            var round = JsonSerializer.Deserialize<FilamentOverrideSet>(JsonSerializer.Serialize(FilamentMatchOverrides.Internal, options), options);

            Assert.NotNull(round);
            Assert.Equal(FilamentMatchOverrides.Internal.Version, round.Version);
            Assert.Equal(FilamentMatchOverrides.Internal.Count, round.Count);

            // Spot-check one entry of each shape, since a silently-dropped list would still round trip the count.
            Assert.Contains(round.ForcedIds, x => x.Criteria.MaterialVariantIdentifier == "A00-W1" && x.FilamentId == "bambulab_pla_jadewhite_1000_175_n");
            Assert.Contains(round.ColorHexes, x => x.Criteria.FilamentType == "ASA" && x.CatalogColorHex == "FFFAF2");
            Assert.Contains(round.SupportForcedIds, x => x.Criteria.MaterialVariantIdentifier == "S05-C0");
            Assert.Contains(round.MultiColors, x => x.Criteria.MaterialVariantIdentifier == "A05-T1" && x.Colors.SequenceEqual(new[] { "FF9425", "FCA2BF" }));
            Assert.Contains(round.NameFilters, x => x.Names.Contains("Weiß"));
            Assert.Contains(round.TransparentFilamentIds, x => x == "bambulab_pva_clear_500_175_n");
        }

        [Fact(DisplayName = "A set deserialized without any lists falls back to empty, not null")]
        public void FilamentOverrideSet_MissingListsDeserializeEmpty()
        {
            // An older client reading a payload it only partly understands must not NRE in the matcher.
            var sparse = JsonSerializer.Deserialize<FilamentOverrideSet>("""{"version":99}""", new JsonSerializerOptions(JsonSerializerDefaults.Web));

            Assert.NotNull(sparse);
            Assert.Equal(99, sparse.Version);
            Assert.Equal(0, sparse.Count);
        }
    }
}
