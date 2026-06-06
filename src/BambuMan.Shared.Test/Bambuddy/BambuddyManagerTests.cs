using BambuMan.Shared;
using Bambuddy.Api.Client;
using Bambuddy.Api.Model;
using Newtonsoft.Json;

namespace BambuMan.Shared.Test.Bambuddy
{
    [Trait("Category", "Bambuddy")]
    public class BambuddyManagerTests
    {
        [Fact(DisplayName = "BuildSpoolCreate maps tag fields and converts price")]
        public void BuildSpoolCreate_MapsFieldsAndConvertsPrice()
        {
            var info = JsonConvert.DeserializeObject<BambuFilamentInfo>(SampleTags.AbsWhite)!;

            var create = BambuddyManager.BuildSpoolCreate(info, matched: null, price: 25m, location: "Shelf A");

            Assert.Equal("ABS", create.Material);
            Assert.Equal("ABS", create.Subtype);
            Assert.Equal("FFFFFFFF", create.Rgba);
            Assert.Equal("FFFFFFFF", create.ColorName);
            Assert.Equal("Bambu Lab", create.Brand);
            Assert.Equal(1000, create.LabelWeight);
            Assert.Equal(250, create.CoreWeight);
            Assert.Equal(240, create.NozzleTempMin);
            Assert.Equal(270, create.NozzleTempMax);
            Assert.Equal("Shelf A", create.StorageLocation);
            Assert.Equal("nfc_scan", create.DataOrigin);
            Assert.Equal("bambu_rfid", create.TagType);
            Assert.Equal(25m, create.CostPerKg); // 25 / (1000 / 1000)
        }

        [Fact(DisplayName = "BuildSpoolCreate converts price by label weight")]
        public void BuildSpoolCreate_ConvertsPriceByLabelWeight()
        {
            var info = JsonConvert.DeserializeObject<BambuFilamentInfo>(SampleTags.AbsWhite)!;
            info.SpoolWeight = 500;

            var create = BambuddyManager.BuildSpoolCreate(info, matched: null, price: 20m, location: null);

            Assert.Equal(500, create.LabelWeight);
            Assert.Equal(40m, create.CostPerKg); // 20 / (500 / 1000)
        }

        [Fact(DisplayName = "FindSpoolByTag matches tray_uuid case-insensitively")]
        public void FindSpoolByTag_MatchesTrayUuid()
        {
            var manager = new BambuddyManager(null)
            {
                CachedSpools =
                [
                    Spool(1, trayUuid: "ABCDEF0123456789ABCDEF0123456789"),
                    Spool(2, trayUuid: "11112222333344445555666677778888")
                ]
            };

            var match = manager.FindSpoolByTag("abcdef0123456789abcdef0123456789", null);

            Assert.NotNull(match);
            Assert.Equal(1, match!.Id);
        }

        [Fact(DisplayName = "FindSpoolByTag falls back to tag_uid when tray_uuid misses")]
        public void FindSpoolByTag_FallsBackToTagUid()
        {
            var manager = new BambuddyManager(null)
            {
                CachedSpools = [Spool(7, trayUuid: "DEADBEEF", tagUid: "A1B2C3D4")]
            };

            // tray_uuid miss -> tag_uid hit (case-insensitive)
            var byTag = manager.FindSpoolByTag("NOMATCH", "a1b2c3d4");
            Assert.NotNull(byTag);
            Assert.Equal(7, byTag!.Id);

            // neither matches
            Assert.Null(manager.FindSpoolByTag("NOMATCH", "NOPE"));
        }

        private static SpoolResponse Spool(int id, string? trayUuid = null, string? tagUid = null) =>
            new("PLA", id, DateTime.MinValue, DateTime.MinValue,
                trayUuid: new Option<string?>(trayUuid),
                tagUid: new Option<string?>(tagUid));
    }
}
