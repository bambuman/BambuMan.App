using BambuMan.Shared.Enums;
using BambuMan.Shared.Managers;
using BambuMan.Shared.Matcher;
using BambuMan.Shared.Models;
using Newtonsoft.Json;
using SpoolMan.Api.Model;

namespace BambuMan.Shared.Test.Bambuddy
{
    [Trait("Category", "NoBackend")]
    public class NoBackendManagerTests
    {
        private static BambuFilamentInfo Tag(string json) => JsonConvert.DeserializeObject<BambuFilamentInfo>(json)!;

        #region SpoolDisplayInfo

        [Fact(DisplayName = "SpoolDisplayInfo lists the matched catalog name alongside the tag data")]
        public async Task From_BuildsRowsForAMatchedTag()
        {
            var info = Tag(SampleTags.AbsBlack);
            var matched = (await ExternalFilamentMatcher.FindExternalFilament(ExternalFilamentMatcher.LoadEmbeddedFilaments(), info)).Single();

            var display = SpoolDisplayInfo.From(info, matched);

            Assert.True(display.Matched);

            var rows = display.Rows.ToDictionary(x => x.Label, x => x.Value);

            Assert.Equal("Bambu Lab", rows["Manufacturer"]);
            Assert.Equal("ABS", rows["Material"]);
            Assert.Equal("Black", rows["Name"]);
            Assert.Equal("#000000", rows["Colour"]);
            Assert.Equal("B00-K0", rows["Variant"]);
            Assert.Equal("1.75 mm", rows["Diameter"]);
            Assert.Equal("1000 g", rows["Spool weight"]);
            Assert.Equal("2024-11-27 09:19", rows["Manufactured"]);
            Assert.Equal("80 °C", rows["Drying temperature"]);
            Assert.Equal("8 h", rows["Drying time"]);
            Assert.Equal("240–270 °C", rows["Hotend temperature"]);
            Assert.Equal("94B0C6D5", rows["Tag serial"]);
        }

        [Fact(DisplayName = "SpoolDisplayInfo omits rows the tag has no value for")]
        public void From_OmitsEmptyRows()
        {
            // Nothing but a variant — every other row must simply not be there rather than render blank.
            var display = SpoolDisplayInfo.From(new BambuFilamentInfo([0x01, 0x02, 0x03, 0x04]) { MaterialVariantIdentifier = "A00-A1" }, null);

            Assert.False(display.Matched);
            Assert.False(display.HasColor);
            Assert.DoesNotContain(display.Rows, x => x.Label == "Manufacturer");
            Assert.DoesNotContain(display.Rows, x => x.Label == "Drying time");
            Assert.DoesNotContain(display.Rows, x => string.IsNullOrWhiteSpace(x.Value));
        }

        [Fact(DisplayName = "SpoolDisplayInfo carries the Bambu filament code for a known variant")]
        public void From_IncludesFilamentCode()
        {
            var withCode = SpoolDisplayInfo.From(new BambuFilamentInfo([0x01, 0x02, 0x03, 0x04]) { MaterialVariantIdentifier = "A00-A1" }, null);

            Assert.Equal("10301", withCode.Rows.Single(x => x.Label == "Filament code").Value);

            var unknown = SpoolDisplayInfo.From(new BambuFilamentInfo([0x01, 0x02, 0x03, 0x04]) { MaterialVariantIdentifier = "ZZZ-Z9" }, null);

            Assert.DoesNotContain(unknown.Rows, x => x.Label == "Filament code");
        }

        [Theory(DisplayName = "Tag colours are re-ordered from RRGGBBAA to the #AARRGGBB the ui binds")]
        [InlineData("918669FF", "#FF918669")]
        [InlineData("68686580", "#80686865")]
        [InlineData("FFFFFF", "#FFFFFFFF")]
        public void From_ConvertsColorToArgb(string tagColor, string expected)
        {
            var display = SpoolDisplayInfo.From(new BambuFilamentInfo([0x01, 0x02, 0x03, 0x04]) { Color = tagColor }, null);

            Assert.Equal(expected, Assert.Single(display.ColorHexes));
        }

        [Fact(DisplayName = "A colourless tag yields no swatch at all")]
        public void From_NoColorMeansNoSwatch()
        {
            var display = SpoolDisplayInfo.From(new BambuFilamentInfo([0x01, 0x02, 0x03, 0x04]), null);

            Assert.False(display.HasColor);
            Assert.Empty(display.ColorHexes);
            Assert.Null(display.ColorText);
        }

        [Fact(DisplayName = "A second colour only counts for a genuinely multi-colour spool")]
        public void From_SecondColorGatedOnColorCount()
        {
            // Single-colour tags still carry a SecondColor field, so the count is what decides.
            var single = SpoolDisplayInfo.From(Tag(SampleTags.AbsBlack), null);
            Assert.Equal("#000000", single.ColorText);
            Assert.Single(single.ColorHexes);

            var dual = SpoolDisplayInfo.From(new BambuFilamentInfo([0x01, 0x02, 0x03, 0x04])
            {
                Color = "FF9425FF",
                ColorCount = 2,
                SecondColor = "C16784FF"
            }, null);

            Assert.Equal(["#FFFF9425", "#FFC16784"], dual.ColorHexes);
            Assert.Equal("#FF9425, #C16784", dual.ColorText);
            Assert.Equal("#FF9425, #C16784", dual.Rows.Single(x => x.Label == "Colour").Value);
            // Nothing matched, so the catalog never told us how the colours sit on the filament.
            Assert.Null(dual.MultiColorDirection);
        }

        [Theory(DisplayName = "A matched multi-colour spool takes its colours and direction from the catalog")]
        // Same shape the web frontend's ColorSwatch renders: coaxial draws bands, longitudinal blends.
        [InlineData("Mystic Magenta (Green-Purple)", SpoolmanExternaldbMultiColorDirection.Coaxial, "#720062, #3A913F")]
        [InlineData("Ocean to Meadow", SpoolmanExternaldbMultiColorDirection.Longitudinal, "#307FE2, #54FF9B")]
        public void From_UsesCatalogColorsAndDirection(string name, SpoolmanExternaldbMultiColorDirection direction, string expectedText)
        {
            var matched = ExternalFilamentMatcher.LoadEmbeddedFilaments().Single(x => x.Name == name);

            // Deliberately give the tag a colour nothing else uses: the catalog list must win, since it is the
            // only source that can describe more than two stops.
            var display = SpoolDisplayInfo.From(new BambuFilamentInfo([0x01, 0x02, 0x03, 0x04]) { Color = "ABCDEFFF" }, matched);

            Assert.Equal(direction, display.MultiColorDirection);
            Assert.Equal(expectedText, display.ColorText);
            Assert.Equal(2, display.ColorHexes.Count);
            Assert.All(display.ColorHexes, x => Assert.StartsWith("#FF", x));
            Assert.Equal(direction.ToString(), display.Rows.Single(x => x.Label == "Colour type").Value);
        }

        [Fact(DisplayName = "The tag's second colour is byte-reversed, not hex-string-reversed")]
        public void ParseData_ReadsSecondColorInTheRightByteOrder()
        {
            // Block 16 of a real PLA Silk Multi-Color "Gilded Rose" tag: format 2, colour count 2, then the
            // second colour little-endian as AABBGGRR. Reversing the hex string instead would yield 1C7648
            // (a dark green) for a pink-gold spool.
            var blocks = Enumerable.Range(0, 64).Select(_ => new byte[16]).ToArray();
            blocks[16] = [0x02, 0x00, 0x02, 0x00, 0xFF, 0x84, 0x67, 0xC1, 0, 0, 0, 0, 0, 0, 0, 0];

            var info = new BambuFilamentInfo([0x01, 0x02, 0x03, 0x04]);
            info.ParseData(blocks, [], fullRead: false);

            Assert.Equal((ushort?)2, info.ColorCount);
            Assert.Equal("C16784FF", info.SecondColor);
            Assert.Equal("#000000, #C16784", SpoolDisplayInfo.From(info, null).ColorText);
        }

        #endregion

        #region NoBackendManager

        [Fact(DisplayName = "NoBackendManager reaches Ready with no api url set")]
        public async Task Init_ReadiesWithoutAnApiUrl()
        {
            var manager = new NoBackendManager(null);

            Assert.True(manager.IsReadOnly);
            Assert.Equal(InventoryBackend.NoBackend, manager.Backend);

            await manager.Init();

            Assert.Equal(ManagerStatusType.Ready, manager.Status);
            Assert.True(manager.IsInitialized);
            Assert.True(manager.IsHealth);
        }

        [Fact(DisplayName = "Scanning raises both the display info and the inventory event, with no network")]
        public async Task InventorySpool_RaisesDisplayAndInventoryEvents()
        {
            var manager = new NoBackendManager(null);
            await manager.Init();

            SpoolDisplayInfo? display = null;
            SpoolFound? found = null;

            manager.OnSpoolInfoRead += x => display = x;
            manager.OnSpoolFound += (x, _) => found = x;

            var matched = await manager.InventorySpool(Tag(SampleTags.AbsBlack), null, null, null, null);

            Assert.True(matched);
            Assert.NotNull(display);
            Assert.True(display.Matched);
            Assert.Equal("Black", display.Rows.Single(x => x.Label == "Name").Value);

            // The inventory chip strip keys off these two fields only.
            Assert.NotNull(found);
            Assert.Equal("ABS", found.Material);
            Assert.Equal("5E755D3AE0FD409F913D5A89F817A248", found.TrayUid);
        }

        [Fact(DisplayName = "An unmatched tag still shows its data and does not sound the error tone")]
        public async Task InventorySpool_UnmatchedTagIsNotAnError()
        {
            var manager = new NoBackendManager(null);
            await manager.Init();

            var tonePlayed = false;
            SpoolDisplayInfo? display = null;

            manager.OnPlayErrorTone += () => tonePlayed = true;
            manager.OnSpoolInfoRead += x => display = x;

            var info = Tag(SampleTags.AbsBlack);
            info.Color = "ABCDEFFF"; // a colour no catalog entry has, so nothing matches
            info.MaterialVariantIdentifier = "ZZZ-Z9";

            var matched = await manager.InventorySpool(info, null, null, null, null);

            Assert.False(matched);
            Assert.False(tonePlayed);
            Assert.NotNull(display);
            Assert.False(display.Matched);
            Assert.Equal("ABS", display.Rows.Single(x => x.Label == "Material").Value);
            Assert.DoesNotContain(display.Rows, x => x.Label == "Name");
        }

        [Fact(DisplayName = "There is nothing to save, so an edit submit is a no-op rather than a throw")]
        public async Task UpdateCurrentSpoolAsync_DoesNothing()
        {
            var manager = new NoBackendManager(null);

            await manager.UpdateCurrentSpoolAsync(new SpoolEditInput(null, null, null, null, null, null));
        }

        #endregion
    }
}
