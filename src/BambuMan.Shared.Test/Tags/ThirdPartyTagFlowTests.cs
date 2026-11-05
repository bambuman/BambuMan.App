using BambuMan.Shared.Enums;
using BambuMan.Shared.Managers;
using BambuMan.Shared.Matcher;
using BambuMan.Shared.Models;
using BambuMan.Shared.Nfc;
using BambuMan.Shared.Services;
using CsvHelper;
using System.Globalization;

namespace BambuMan.Shared.Test.Tags
{
    /// <summary>A third-party tag through the matcher, the managers' pure mapping and the local inventory.</summary>
    [Trait("Category", "SpoolTags")]
    public class ThirdPartyTagFlowTests : IDisposable
    {
        private static readonly byte[] Uid = [0x04, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66];

        private readonly string directory = Path.Combine(Path.GetTempPath(), "bambuman-tests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        private static SpoolTag OpenSpoolTag(string brand, string type, string colorHex, string? subtype = null) =>
            OpenSpool.TryParse(Uid, $$"""{"protocol":"openspool","version":"1.0","type":"{{type}}","color_hex":"{{colorHex}}","brand":"{{brand}}","min_temp":"190","max_temp":"230"{{(subtype == null ? "" : $",\"subtype\":\"{subtype}\"")}}}""")!;

        private static readonly List<SpoolMan.Api.Model.ExternalFilament> Catalog = ExternalFilamentMatcher.LoadEmbeddedFilaments();

        #region Matcher

        [Theory(DisplayName = "Matcher: an OpenSpool tag branded Bambu Lab matches the plain catalog filament")]
        [InlineData("Bambu")]
        [InlineData("Bambu Lab")]
        [InlineData("bambulab")]
        [InlineData("BBL")]
        public async Task Match_BambuBrandedTag(string brand)
        {
            var result = await ExternalFilamentMatcher.FindExternalFilament(Catalog, OpenSpoolTag(brand, "PLA", "FFFFFF"));

            Assert.Equal("bambulab_pla_jadewhite_1000_175_n", Assert.Single(result).Id);
        }

        [Fact(DisplayName = "Matcher: the tag's finish picks the variant of that colour")]
        public async Task Match_UsesSubtype()
        {
            var result = await ExternalFilamentMatcher.FindExternalFilament(Catalog, OpenSpoolTag("Bambu Lab", "PLA", "FFFFFF", "Matte"));

            Assert.Equal("bambulab_pla_matteivorywhite_1000_175_n", Assert.Single(result).Id);
        }

        [Theory(DisplayName = "Matcher: a brand, material or colour the catalog doesn't have matches nothing")]
        [InlineData("Generic", "PLA", "FFFFFF")]
        [InlineData("Bambu Lab", "PLA", "123456")]
        [InlineData("Bambu Lab", "NOTAMATERIAL", "FFFFFF")]
        public async Task Match_NoCandidate(string brand, string type, string color)
        {
            Assert.Empty(await ExternalFilamentMatcher.FindExternalFilament(Catalog, OpenSpoolTag(brand, type, color)));
        }

        [Fact(DisplayName = "Matcher: a tag without a brand matches nothing")]
        public void Match_NoBrand()
        {
            Assert.Empty(ExternalFilamentMatcher.FindThirdPartyFilament(Catalog, new SpoolTag(SpoolTagFormat.OpenSpool, Uid) { FilamentType = "PLA", Color = "FFFFFFFF" }));
        }

        #endregion

        #region Bambuddy mapping

        [Fact(DisplayName = "Bambuddy: a third-party tag keeps its brand, material, finish and tag type")]
        public void Bambuddy_BuildSpoolCreate()
        {
            var create = BambuddyManager.BuildSpoolCreate(OpenSpoolTag("Polymaker", "PETG", "1A2B3C", "Matte"), matched: null, price: 20m, location: "Shelf B");

            Assert.Equal("PETG", create.Material);
            Assert.Equal("Matte", create.Subtype);
            Assert.Equal("Polymaker", create.Brand);
            Assert.Equal("1A2B3CFF", create.Rgba);
            Assert.Null(create.SlicerFilament);
            Assert.Equal("openspool", create.TagType);
            Assert.Equal(1000, create.LabelWeight);
            Assert.Equal(190, create.NozzleTempMin);
            Assert.Equal(230, create.NozzleTempMax);
        }

        [Fact(DisplayName = "Bambuddy: a third-party tag branded Bambu Lab gets Bambuddy's \"Bambu\" brand")]
        public void Bambuddy_BambuBrand()
        {
            Assert.Equal("Bambu", BambuddyManager.BrandOf(OpenSpoolTag("Bambu Lab", "PLA", "FFFFFF")));
            Assert.Equal("Generic", BambuddyManager.BrandOf(OpenSpoolTag("Generic", "PLA", "FFFFFF")));
        }

        #endregion

        #region SpoolDisplayInfo

        [Fact(DisplayName = "SpoolDisplayInfo: an unmatched third-party tag shows its format and brand")]
        public void Display_ThirdPartyTag()
        {
            var display = SpoolDisplayInfo.From(OpenSpoolTag("Generic", "PLA", "FFAABB"), null);
            var rows = display.Rows.ToDictionary(x => x.Label, x => x.Value);

            Assert.False(display.Matched);
            Assert.Equal("OpenSpool", rows["Tag format"]);
            Assert.Equal("Generic", rows["Manufacturer"]);
            Assert.Equal("PLA", rows["Material"]);
            Assert.Equal("#FFAABB", rows["Colour"]);
            Assert.Equal("190–230 °C", rows["Hotend temperature"]);
            Assert.Equal("04112233445566", rows["Tag serial"]);
            Assert.DoesNotContain("Tray uid", rows.Keys);
        }

        #endregion

        #region No backend / local inventory

        private async Task<NoBackendManager> Manager()
        {
            var manager = new NoBackendManager(null, new LocalInventoryStore { Directory = directory }) { StoreSpoolsLocally = true };
            await manager.Init();
            return manager;
        }

        [Fact(DisplayName = "No backend: a third-party tag is kept once, keyed by its tag uid")]
        public async Task NoBackend_KeepsByTagUid()
        {
            var manager = await Manager();

            var found = new List<(SpoolFound Found, ISpoolTag Tag)>();
            SpoolDisplayInfo? shown = null;
            manager.OnSpoolFound += (spool, tag) => found.Add((spool, tag));
            manager.OnSpoolInfoRead += info => shown = info;

            var tag = OpenSpoolTag("Generic", "PLA", "FFAABB");

            await manager.InventorySpool(tag, null, 10m, null, "Shelf C");
            await manager.InventorySpool(OpenSpoolTag("Generic", "PLA", "FFAABB"), null, null, null, null);

            var spool = Assert.Single(manager.Spools);

            Assert.Equal("04112233445566", spool.Key);
            Assert.Equal(SpoolTagFormat.OpenSpool, spool.Format);
            Assert.Equal("Generic", spool.Brand);
            Assert.Equal("Shelf C", spool.Location);
            Assert.Equal(10m, spool.Price);

            Assert.Equal(2, found.Count);
            Assert.Same(tag, found[0].Tag); // the source tag is passed along, not a projection
            Assert.Equal("04112233445566", found[0].Found.TrayUid);
            Assert.Equal("PLA", found[0].Found.Material);
            Assert.Equal("OpenSpool", shown!.Rows.Single(x => x.Label == "Tag format").Value);
        }

        [Fact(DisplayName = "No backend: a stored third-party spool reads back as that format")]
        public async Task NoBackend_StoreRoundTrips()
        {
            var manager = await Manager();
            await manager.InventorySpool(OpenSpoolTag("Bambu Lab", "PLA", "FFFFFF", "Matte"), null, null, null, null);

            var spool = Assert.Single(await new LocalInventoryStore { Directory = directory }.LoadAsync());

            Assert.Equal(SpoolTagFormat.OpenSpool, spool.Format);
            Assert.Equal("Bambu Lab", spool.Brand);
            Assert.Equal("Matte Ivory White", spool.Name);
            Assert.Contains("\"Format\": \"OpenSpool\"", await File.ReadAllTextAsync(Path.Combine(directory, "local-inventory.json")));

            var tag = Assert.IsType<SpoolTag>(spool.Tag);
            Assert.Equal("Matte", tag.Subtype);

            var display = await manager.DescribeAsync(spool);
            Assert.True(display.Matched);
        }

        [Fact(DisplayName = "Csv: a third-party spool exports its brand and tag type and imports back as that format")]
        public void Csv_RoundTrips()
        {
            var tag = OpenSpoolTag("Polymaker", "PETG", "1A2B3C");
            var source = new LocalSpool { Info = tag.ToFilamentInfo(), Format = tag.Format, Brand = tag.Brand };

            using var writer = new StringWriter();
            LocalSpoolCsv.Write(writer, [source]);

            using (var csv = new CsvReader(new StringReader(writer.ToString()), CultureInfo.InvariantCulture))
            {
                csv.Read();
                csv.ReadHeader();
                csv.Read();

                Assert.Equal("Polymaker", csv.GetField("brand"));
                Assert.Equal("openspool", csv.GetField("tag_type"));
                Assert.Equal("04112233445566", csv.GetField("tag_uid"));
                Assert.Equal("", csv.GetField("tray_uuid"));
            }

            var imported = Assert.Single(LocalSpoolCsv.Read(new StringReader(writer.ToString())).Spools);

            Assert.Equal(SpoolTagFormat.OpenSpool, imported.Format);
            Assert.Equal("Polymaker", imported.Brand);
            Assert.Equal("04112233445566", imported.Key);
        }

        #endregion
    }
}
