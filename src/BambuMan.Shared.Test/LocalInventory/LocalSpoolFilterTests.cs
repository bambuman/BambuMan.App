using BambuMan.Shared.Models;
using BambuMan.Shared.Services;

namespace BambuMan.Shared.Test.LocalInventory
{
    [Trait("Category", "LocalInventory")]
    public class LocalSpoolFilterTests
    {
        private static LocalSpool Spool(string trayUid, string material, string? name, string color, string? location, int day, string? manufacturer = "Bambu Lab", string? filamentCode = null) => new()
        {
            Info = new BambuFilamentInfo { TrayUid = trayUid, SerialNumber = "5B1449F6", DetailedFilamentType = material, FilamentType = material.Split(' ')[0], Color = color },
            Name = name,
            Manufacturer = manufacturer,
            FilamentCode = filamentCode,
            Location = location,
            LastScanned = new DateTime(2026, 1, day)
        };

        private static readonly LocalSpool PlaBlackShelf = Spool("AAA1", "PLA Basic", "Black", "000000FF", "Shelf A", 1, filamentCode: "10101");
        private static readonly LocalSpool PlaBlackDryBox = Spool("AAA2", "PLA Basic", "Black", "000000FF", "Dry box", 3, filamentCode: "10101");
        private static readonly LocalSpool PetgRed = Spool("BBB1", "PETG HF", "Red", "FF0000FF", "shelf a", 2);
        private static readonly LocalSpool Unmatched = Spool("CCC1", "ABS", null, "FFFFFFFF", null, 4, manufacturer: null);

        private static List<LocalSpoolItem> Items() => LocalSpoolFilter.ToItems([PlaBlackShelf, PlaBlackDryBox, PetgRed, Unmatched]);

        #region Items

        [Fact(DisplayName = "Items are newest first and skip spools without a key")]
        public void ToItems_NewestFirstAndKeyed()
        {
            var keyless = new LocalSpool { Info = new BambuFilamentInfo(), LastScanned = new DateTime(2026, 2, 1) };

            var items = LocalSpoolFilter.ToItems([PlaBlackShelf, PlaBlackDryBox, PetgRed, Unmatched, keyless]);

            Assert.Equal(["CCC1", "AAA2", "BBB1", "AAA1"], items.Select(x => x.Key));
        }

        [Fact(DisplayName = "An item shows material and colour name, with location, tag and scan date below")]
        public void ToItem_TitleSubtitleColor()
        {
            var item = LocalSpoolFilter.ToItem(PlaBlackShelf);

            Assert.Equal("PLA Basic Black", item.Title);
            Assert.Equal("Shelf A · AAA1 · 2026-01-01", item.Subtitle);
            Assert.Equal("#FF000000", item.Color);
            Assert.Equal("Shelf A", item.Location);
            Assert.Same(PlaBlackShelf, item.Spool);
        }

        [Fact(DisplayName = "A spool not in the catalog is titled by its material")]
        public void ToItem_UnmatchedUsesMaterial()
        {
            Assert.Equal("ABS", LocalSpoolFilter.ToItem(Unmatched).Title);
            Assert.Equal("Unknown filament", LocalSpoolFilter.ToItem(new LocalSpool { Info = new BambuFilamentInfo { TrayUid = "DDD1" } }).Title);
        }

        [Fact(DisplayName = "Locations are distinct ignoring case, sorted, without empty ones")]
        public void Locations_DistinctSorted()
        {
            Assert.Equal(["Dry box", "shelf a"], LocalSpoolFilter.Locations(Items()));
        }

        #endregion

        #region Filter

        [Fact(DisplayName = "No search and no location lists every spool, in order")]
        public void Filter_NoneKeepsAll()
        {
            var items = Items();

            Assert.Equal(items, LocalSpoolFilter.Filter(items, null, null));
            Assert.Equal(items, LocalSpoolFilter.Filter(items, "  ", ""));
            Assert.False(LocalSpoolFilter.IsFiltered("  ", ""));
        }

        [Theory(DisplayName = "Search matches material, colour name, manufacturer, filament code and tag, ignoring case")]
        [InlineData("petg", "BBB1")]
        [InlineData("RED", "BBB1")]
        [InlineData("basic", "AAA2,AAA1")]
        [InlineData("10101", "AAA2,AAA1")]
        [InlineData("bambu", "AAA2,BBB1,AAA1")]
        [InlineData("ccc1", "CCC1")]
        [InlineData(" aaa2 ", "AAA2")]
        [InlineData("5b1449", "CCC1,AAA2,BBB1,AAA1")]
        [InlineData("nylon", "")]
        public void Filter_Search(string search, string expected)
        {
            var keys = LocalSpoolFilter.Filter(Items(), search, null).Select(x => x.Key);

            Assert.Equal(expected.Split(',', StringSplitOptions.RemoveEmptyEntries), keys);
        }

        [Fact(DisplayName = "The location filter ignores case and combines with the search")]
        public void Filter_Location()
        {
            Assert.Equal(["BBB1", "AAA1"], LocalSpoolFilter.Filter(Items(), null, "SHELF A").Select(x => x.Key));
            Assert.Equal(["AAA1"], LocalSpoolFilter.Filter(Items(), "black", "Shelf A").Select(x => x.Key));
            Assert.True(LocalSpoolFilter.IsFiltered(null, "Shelf A"));
            Assert.True(LocalSpoolFilter.IsFiltered("black", null));
        }

        #endregion

        #region Groups

        [Fact(DisplayName = "Spools of the same filament form one group, in the order of its newest spool")]
        public void Group_SameFilament()
        {
            var groups = LocalSpoolFilter.Group(Items());

            Assert.Equal(["ABS", "PLA Basic Black", "PETG HF Red"], groups.Select(x => x.Title));

            var pla = groups[1];

            Assert.Equal(2, pla.Count);
            Assert.Equal(["AAA2", "AAA1"], pla.Spools.Select(x => x.Key));
            Assert.Equal("Dry box, Shelf A", pla.Locations);
            Assert.Equal("", groups[0].Locations);
        }

        [Theory(DisplayName = "The count line says how many spools and filaments are shown")]
        [InlineData(4, 4, 3, false, "4 spools · 3 filaments")]
        [InlineData(1, 1, 1, false, "1 spool · 1 filament")]
        [InlineData(2, 4, 1, true, "2 of 4 spools · 1 filament")]
        [InlineData(0, 4, 0, true, "0 of 4 spools · 0 filaments")]
        public void CountText(int shown, int total, int groups, bool isFiltered, string expected)
        {
            Assert.Equal(expected, LocalSpoolFilter.CountText(shown, total, groups, isFiltered));
        }

        #endregion
    }
}
