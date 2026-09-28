using BambuMan.Shared.Managers;
using BambuMan.Shared.Models;
using BambuMan.Shared.Services;
using BambuMan.Shared.Test.Bambuddy;
using CsvHelper;
using Newtonsoft.Json;
using System.Globalization;

namespace BambuMan.Shared.Test.LocalInventory
{
    [Trait("Category", "LocalInventory")]
    public class LocalInventoryTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "bambuman-tests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        private static BambuFilamentInfo Tag(string json) => JsonConvert.DeserializeObject<BambuFilamentInfo>(json)!;

        private LocalInventoryStore Store() => new() { Directory = directory };

        private async Task<NoBackendManager> Manager(bool storeLocally)
        {
            var manager = new NoBackendManager(null, Store()) { StoreSpoolsLocally = storeLocally };
            await manager.Init();
            return manager;
        }

        #region Store

        [Fact(DisplayName = "Nothing stored yet reads as an empty inventory")]
        public async Task Store_MissingFileIsEmpty()
        {
            Assert.Empty(await Store().LoadAsync());
        }

        [Fact(DisplayName = "Stored spools read back with their tag data, and without the tag keys")]
        public async Task Store_RoundTrips()
        {
            var info = Tag(SampleTags.PlaWood);
            info.Keys = [0x01, 0x02];

            await Store().SaveAsync([new LocalSpool { Info = info, Name = "Classic Birch", Price = 12.5m, Location = "Shelf A" }]);

            var spool = Assert.Single(await Store().LoadAsync());

            Assert.Equal("4663E9ADF9CC454380EB58CE627BFE72", spool.Key);
            Assert.Equal("5B1449F6", spool.Info.SerialNumber);
            Assert.Equal("PLA Wood", spool.Info.DetailedFilamentType);
            Assert.Equal("Classic Birch", spool.Name);
            Assert.Equal(12.5m, spool.Price);
            Assert.Equal("Shelf A", spool.Location);
            Assert.Null(spool.Info.Keys);
            Assert.DoesNotContain("Keys", await File.ReadAllTextAsync(Path.Combine(directory, "local-inventory.json")));
        }

        [Fact(DisplayName = "A damaged file throws rather than reading as empty")]
        public async Task Store_DamagedFileThrows()
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "local-inventory.json"), "{ not json");

            await Assert.ThrowsAnyAsync<JsonException>(() => Store().LoadAsync());
        }

        #endregion

        #region NoBackendManager

        [Fact(DisplayName = "Scanning keeps the spool for this session, once per tray uid")]
        public async Task Scan_KeepsSessionSpoolsWithoutDuplicates()
        {
            var manager = await Manager(storeLocally: false);

            await manager.InventorySpool(Tag(SampleTags.AbsBlack), null, 12m, null, "Shelf A");
            await manager.InventorySpool(Tag(SampleTags.AbsWhite), null, 12m, null, null);

            // The other tag on the same spool: different tag uid, same tray uid.
            var otherSide = Tag(SampleTags.AbsBlack);
            otherSide.SerialNumber = "11223344";
            await manager.InventorySpool(otherSide, null, 99m, null, "Shelf B");

            Assert.False(manager.IsStoreLoaded);
            Assert.Equal(2, manager.Spools.Count);

            var black = manager.Spools.Single(x => x.Key == "5E755D3AE0FD409F913D5A89F817A248");
            Assert.Equal("Bambu Lab", black.Manufacturer);
            Assert.Equal("Black", black.Name);
            // A re-scan passes the defaults again, which must not replace what the spool already has.
            Assert.Equal(12m, black.Price);
            Assert.Equal("Shelf A", black.Location);

            Assert.False(File.Exists(Path.Combine(directory, "local-inventory.json")));
        }

        [Fact(DisplayName = "Stored spools survive a restart")]
        public async Task StoreLocally_SurvivesRestart()
        {
            var first = await Manager(storeLocally: true);
            await first.InventorySpool(Tag(SampleTags.AbsBlack), null, null, null, null);

            var restarted = await Manager(storeLocally: true);

            Assert.True(restarted.IsStoreLoaded);
            Assert.Equal("5E755D3AE0FD409F913D5A89F817A248", Assert.Single(restarted.Spools).Key);
        }

        [Fact(DisplayName = "Switching store-locally on keeps what was scanned before, switching it off leaves the store alone")]
        public async Task StoreLocally_Toggle()
        {
            var stored = await Manager(storeLocally: true);
            await stored.InventorySpool(Tag(SampleTags.AbsWhite), null, null, null, null);

            var manager = await Manager(storeLocally: false);
            await manager.InventorySpool(Tag(SampleTags.AbsBlack), null, null, null, null);

            manager.StoreSpoolsLocally = true;
            await manager.Init();

            Assert.Equal(2, manager.Spools.Count);
            Assert.Equal(2, (await Store().LoadAsync()).Count);

            manager.StoreSpoolsLocally = false;
            await manager.Init();

            Assert.False(manager.IsStoreLoaded);
            Assert.Equal("5E755D3AE0FD409F913D5A89F817A248", Assert.Single(manager.Spools).Key);
            Assert.Equal(2, (await Store().LoadAsync()).Count);
        }

        [Fact(DisplayName = "A damaged store is never saved over")]
        public async Task StoreLocally_DamagedFileIsNotOverwritten()
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "local-inventory.json");
            await File.WriteAllTextAsync(path, "{ not json");

            var manager = new NoBackendManager(null, Store()) { StoreSpoolsLocally = true };
            string? error = null;
            manager.OnShowMessage += (isError, message) => error = isError ? message : error;

            await manager.Init();
            await manager.InventorySpool(Tag(SampleTags.AbsBlack), null, null, null, null);

            Assert.False(manager.IsStoreLoaded);
            Assert.Equal("Could not read the locally stored spools. See logs.", error);
            Assert.Equal("{ not json", await File.ReadAllTextAsync(path));
        }

        [Fact(DisplayName = "Remove and clear also update the stored inventory")]
        public async Task StoreLocally_RemoveAndClear()
        {
            var manager = await Manager(storeLocally: true);
            await manager.InventorySpool(Tag(SampleTags.AbsBlack), null, null, null, null);
            await manager.InventorySpool(Tag(SampleTags.AbsWhite), null, null, null, null);

            await manager.RemoveSpoolAsync("5E755D3AE0FD409F913D5A89F817A248");
            Assert.Equal("658AF4C881A64A0781B32CFB5A7CB675", Assert.Single(await Store().LoadAsync()).Key);

            await manager.ClearSpoolsAsync();
            Assert.Empty(await Store().LoadAsync());
            Assert.Empty(manager.Spools);
        }

        [Fact(DisplayName = "Import adds new spools and replaces known ones, and needs the store")]
        public async Task Import_Upserts()
        {
            var sessionOnly = await Manager(storeLocally: false);
            await Assert.ThrowsAsync<InvalidOperationException>(() => sessionOnly.ImportSpoolsAsync([]));

            var manager = await Manager(storeLocally: true);
            await manager.InventorySpool(Tag(SampleTags.AbsBlack), null, null, null, null);

            var (added, updated) = await manager.ImportSpoolsAsync(
            [
                new LocalSpool { Info = Tag(SampleTags.AbsBlack), Location = "Imported" },
                new LocalSpool { Info = Tag(SampleTags.PlaWood) }
            ]);

            Assert.Equal(1, added);
            Assert.Equal(1, updated);
            Assert.Equal("Imported", (await Store().LoadAsync()).Single(x => x.Key == "5E755D3AE0FD409F913D5A89F817A248").Location);
        }

        #endregion

        #region Csv

        private static Dictionary<string, string> ExportRow(LocalSpool spool)
        {
            using var writer = new StringWriter();
            LocalSpoolCsv.Write(writer, [spool]);

            using var csv = new CsvReader(new StringReader(writer.ToString()), CultureInfo.InvariantCulture);
            csv.Read();
            csv.ReadHeader();
            csv.Read();

            return csv.HeaderRecord!.ToDictionary(x => x, x => csv.GetField(x)!);
        }

        private static LocalSpoolCsvReadResult Import(string csv) => LocalSpoolCsv.Read(new StringReader(csv));

        [Fact(DisplayName = "The csv leads with Bambuddy's columns in Bambuddy's order")]
        public void Csv_HeaderStartsWithBambuddyColumns()
        {
            using var writer = new StringWriter();
            LocalSpoolCsv.Write(writer, []);

            var header = writer.ToString().TrimEnd().Split(',');

            Assert.Equal(
                "material,brand,subtype,color_name,rgba,extra_colors,effect_type,label_weight,weight_used,remaining,cost_per_kg,nozzle_temp_min,nozzle_temp_max,last_used,note,storage_location,category,low_stock_threshold_pct",
                string.Join(",", header.Take(18)));
            Assert.Equal(["tag_uid", "tray_uuid", "slicer_filament", "slicer_filament_name", "core_weight", "data_origin", "tag_type"], header.Skip(18).Take(7));
        }

        [Fact(DisplayName = "Export writes the values a direct scan into Bambuddy would create")]
        public void Csv_ExportMatchesBambuddyMapping()
        {
            var row = ExportRow(new LocalSpool { Info = Tag(SampleTags.PlaWood), Name = "Classic Birch", Manufacturer = "Bambu Lab", Price = 25m, Location = "Shelf A" });

            Assert.Equal("PLA", row["material"]);
            Assert.Equal("Bambu", row["brand"]);
            Assert.Equal("Wood", row["subtype"]);
            Assert.Equal("wood", row["effect_type"]);
            Assert.Equal("Classic Birch", row["color_name"]);
            Assert.Equal("918669FF", row["rgba"]);
            Assert.Equal("", row["extra_colors"]);
            Assert.Equal("1000", row["label_weight"]);
            Assert.Equal("0", row["weight_used"]);
            Assert.Equal("25", row["cost_per_kg"]);
            Assert.Equal("190", row["nozzle_temp_min"]);
            Assert.Equal("230", row["nozzle_temp_max"]);
            Assert.Equal("Shelf A", row["storage_location"]);

            Assert.Equal("5B1449F6", row["tag_uid"]);
            Assert.Equal("4663E9ADF9CC454380EB58CE627BFE72", row["tray_uuid"]);
            Assert.Equal("GFA16", row["slicer_filament"]);
            Assert.Equal("Bambu PLA Wood", row["slicer_filament_name"]);
            Assert.Equal("250", row["core_weight"]);
            Assert.Equal("nfc_scan", row["data_origin"]);
            Assert.Equal("bambu_rfid", row["tag_type"]);

            Assert.Equal("A16-G0", row["variant_id"]);
            Assert.Equal("1.75", row["diameter"]);
            Assert.Equal("2025-03-11T00:38:00", row["production_date"]);
        }

        [Fact(DisplayName = "A dual-colour spool exports both stops as extra_colors")]
        public void Csv_ExportsExtraColors()
        {
            var info = Tag(SampleTags.AbsBlack);
            info.ColorCount = 2;
            info.SecondColor = "C16784FF";

            Assert.Equal("000000FF,C16784FF", ExportRow(new LocalSpool { Info = info })["extra_colors"]);
            Assert.Equal("307FE2FF,54FF9BFF", ExportRow(new LocalSpool { Info = info, ColorHexes = ["307FE2", "54FF9B"] })["extra_colors"]);
        }

        [Fact(DisplayName = "Export then import gives the same spools back")]
        public void Csv_RoundTrips()
        {
            var original = new LocalSpool
            {
                Info = Tag(SampleTags.PlaWood),
                Manufacturer = "Bambu Lab",
                Name = "Classic Birch",
                FilamentCode = "10501",
                Price = 19.99m,
                Location = "=Shelf, top",
                FirstScanned = new DateTime(2026, 9, 1, 10, 0, 0),
                LastScanned = new DateTime(2026, 9, 2, 11, 30, 0)
            };

            using var stream = new MemoryStream();
            LocalSpoolCsv.Write(stream, [original]);
            stream.Position = 0;

            var result = LocalSpoolCsv.Read(stream);

            Assert.Empty(result.Errors);
            var spool = Assert.Single(result.Spools);

            Assert.Equal(original.Key, spool.Key);
            Assert.Equal(original.Manufacturer, spool.Manufacturer);
            Assert.Equal(original.Name, spool.Name);
            Assert.Equal(original.FilamentCode, spool.FilamentCode);
            Assert.Equal(original.Price, spool.Price);
            Assert.Equal(original.Location, spool.Location);
            Assert.Equal(original.FirstScanned, spool.FirstScanned);
            Assert.Equal(original.LastScanned, spool.LastScanned);

            var a = original.Info;
            var b = spool.Info;

            Assert.Equal(a.SerialNumber, b.SerialNumber);
            Assert.Equal(a.TrayUid, b.TrayUid);
            Assert.Equal(a.MaterialVariantIdentifier, b.MaterialVariantIdentifier);
            Assert.Equal(a.UniqueMaterialIdentifier, b.UniqueMaterialIdentifier);
            Assert.Equal(a.FilamentType, b.FilamentType);
            Assert.Equal(a.DetailedFilamentType, b.DetailedFilamentType);
            Assert.Equal(a.Color, b.Color);
            Assert.Equal(a.SpoolWeight, b.SpoolWeight);
            Assert.Equal(a.FilamentDiameter, b.FilamentDiameter);
            Assert.Equal(a.MinTemperatureForHotend, b.MinTemperatureForHotend);
            Assert.Equal(a.MaxTemperatureForHotend, b.MaxTemperatureForHotend);
            Assert.Equal(a.DryingTemperature, b.DryingTemperature);
            Assert.Equal(a.DryingTime, b.DryingTime);
            Assert.Equal(a.NozzleDiameter, b.NozzleDiameter);
            Assert.Equal(a.SpoolWidth, b.SpoolWidth);
            Assert.Equal(a.FilamentLength, b.FilamentLength);
            Assert.Equal(a.ProductionDateTime, b.ProductionDateTime);
            Assert.Equal(a.ColorCount, b.ColorCount);
            Assert.Equal(a.SecondColor, b.SecondColor);
            Assert.Equal(a.SkuStart, b.SkuStart);
        }

        [Fact(DisplayName = "A formula-looking cell is quoted on export")]
        public void Csv_GuardsAgainstFormulaInjection()
        {
            Assert.Equal("'=SUM(A1)", ExportRow(new LocalSpool { Info = Tag(SampleTags.AbsBlack), Location = "=SUM(A1)" })["storage_location"]);
        }

        [Fact(DisplayName = "A csv without tag identifiers is refused as a whole")]
        public void Csv_RequiresTagColumns()
        {
            var result = Import("material,brand,color_name\nPLA,Bambu,Black\n");

            Assert.Empty(result.Spools);
            Assert.Contains("tag_uid or tray_uuid", Assert.Single(result.Errors));
        }

        [Fact(DisplayName = "Bad rows are reported by number and the good ones still import")]
        public void Csv_ReportsBadRows()
        {
            var result = Import(
                "Material,Tray UUID,Tag-UID,RGBA,Label Weight\n" +
                "PLA,AAAABBBBCCCCDDDDEEEEFFFF00001111,,#ff0000,1000\n" +
                "PLA,,,ff0000,1000\n" +
                "PLA,AAAABBBBCCCCDDDDEEEEFFFF00002222,,red,1000\n" +
                "PLA,,XYZ,ff0000,1000\n" +
                "PLA,AAAABBBBCCCCDDDDEEEEFFFF00003333,,ff0000,heavy\n");

            var spool = Assert.Single(result.Spools);
            Assert.Equal("AAAABBBBCCCCDDDDEEEEFFFF00001111", spool.Key);
            Assert.Equal("FF0000FF", spool.Info.Color);
            Assert.Equal("PLA", spool.Info.FilamentType);

            Assert.Equal(4, result.Errors.Count);
            Assert.StartsWith("Row 2: tag_uid or tray_uuid", result.Errors[0]);
            Assert.StartsWith("Row 3: rgba", result.Errors[1]);
            Assert.StartsWith("Row 4: tag_uid", result.Errors[2]);
            Assert.StartsWith("Row 5: label_weight", result.Errors[3]);
        }

        [Fact(DisplayName = "Without a price column the price comes back from cost_per_kg")]
        public void Csv_PriceFromCostPerKg()
        {
            var spool = Assert.Single(Import("tray_uuid,label_weight,cost_per_kg\nAAAABBBBCCCCDDDDEEEEFFFF00001111,500,30\n").Spools);

            Assert.Equal(15m, spool.Price);
        }

        #endregion
    }
}
