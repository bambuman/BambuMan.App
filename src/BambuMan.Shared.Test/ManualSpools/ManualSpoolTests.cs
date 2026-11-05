using BambuMan.Shared.Managers;
using BambuMan.Shared.Matcher;
using BambuMan.Shared.Models;
using BambuMan.Shared.Services;
using BambuMan.Shared.Test.Bambuddy;
using CsvHelper;
using Newtonsoft.Json;
using System.Globalization;
using ExternalFilament = SpoolMan.Api.Model.ExternalFilament;

namespace BambuMan.Shared.Test.ManualSpools
{
    /// <summary>Spools added by hand because no tag could be read: picking the filament, the tagless model, and keeping it.</summary>
    [Trait("Category", "ManualSpool")]
    public class ManualSpoolTests : IDisposable
    {
        private const string AbsBlackId = "bambulab_abs_black_1000_175_n";
        private const string MatteIceBlueId = "bambulab_pla_matteiceblue_1000_175_n";
        private const string DawnRadianceId = "bambulab_pla_dawnradiance_1000_175_n";

        private static readonly List<ExternalFilament> Catalog = ExternalFilamentMatcher.LoadEmbeddedFilaments();

        private readonly string directory = Path.Combine(Path.GetTempPath(), "bambuman-tests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        private static ExternalFilament Filament(string id) => Catalog.Single(x => x.Id == id);

        private LocalInventoryStore Store() => new() { Directory = directory };

        private async Task<NoBackendManager> Manager(bool storeLocally)
        {
            var manager = new NoBackendManager(null, Store()) { StoreSpoolsLocally = storeLocally };
            await manager.Init();
            return manager;
        }

        private static readonly SpoolEditInput Card = new(1100m, 245m, 19.99m, new DateTime(2026, 10, 1), " L-1 ", " Dry box ");

        #region Search

        [Fact(DisplayName = "An empty search lists the whole catalog, sorted by material and name")]
        public void Search_EmptyListsAll()
        {
            var result = ExternalFilamentMatcher.Search(Catalog, "  ");

            Assert.Equal(Catalog.Count, result.Count);
            Assert.Equal(result.OrderBy(x => x.Material, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Select(x => x.Id), result.Select(x => x.Id));
        }

        [Fact(DisplayName = "Every search word has to match, in any order and case")]
        public void Search_MatchesAllWords()
        {
            var result = ExternalFilamentMatcher.Search(Catalog, "ice BLUE matte pla");

            Assert.Equal(MatteIceBlueId, Assert.Single(result).Id);
        }

        [Fact(DisplayName = "A colour hex finds the filaments of that colour, with or without #")]
        public void Search_MatchesColorHex()
        {
            Assert.Contains(ExternalFilamentMatcher.Search(Catalog, "abs #000000"), x => x.Id == AbsBlackId);
            Assert.Contains(ExternalFilamentMatcher.Search(Catalog, "a3d8e1"), x => x.Id == MatteIceBlueId);

            // A gradient's stops count too.
            Assert.Contains(ExternalFilamentMatcher.Search(Catalog, "6CD4BC"), x => x.Id == DawnRadianceId);
        }

        [Fact(DisplayName = "Finish and translucent flags are searchable")]
        public void Search_MatchesFinishAndFlags()
        {
            Assert.Contains(ExternalFilamentMatcher.Search(Catalog, "translucent ice blue"), x => x.Id == "bambulab_pla_translucenticeblue_1000_175_n");
            Assert.All(ExternalFilamentMatcher.Search(Catalog, "glow"), x => Assert.True(x.Glow == true || x.Name.Contains("Glow", StringComparison.OrdinalIgnoreCase)));
        }

        [Fact(DisplayName = "A search nothing matches returns nothing")]
        public void Search_NoMatch()
        {
            Assert.Empty(ExternalFilamentMatcher.Search(Catalog, "pla unobtainium"));
        }

        #endregion

        #region ManualSpool

        [Fact(DisplayName = "A catalog pick becomes tag-shaped data without any tag identity")]
        public void FromCatalog_BuildsTaglessInfo()
        {
            var spool = ManualSpool.FromCatalog(Filament(MatteIceBlueId));
            var info = spool.Info;

            Assert.False(spool.IsUnknown);
            Assert.Equal("PLA", info.FilamentType);
            Assert.Equal("PLA Matte", info.DetailedFilamentType);
            Assert.Equal("A3D8E1FF", info.Color);
            Assert.Equal((ushort)1000, info.SpoolWeight);
            Assert.Equal(1.75f, info.FilamentDiameter);
            Assert.Equal((ushort)1, info.ColorCount);
            Assert.Equal("PLA Matte Ice Blue", spool.Label);

            Assert.False(info.HasTag);
            Assert.Null(info.TrayUid);
            Assert.Equal("", info.SerialNumber);
            Assert.Null(LocalSpool.KeyOf(info));
        }

        [Theory(DisplayName = "The detailed type comes from the product line the catalog name starts with")]
        [InlineData(MatteIceBlueId, "PLA Matte")]
        [InlineData("bambulab_pla_silk+rosegold_1000_175_n", "PLA Silk+")]
        [InlineData("bambulab_pla_tough+gray_1000_175_n", "PLA Tough+")]
        [InlineData("bambulab_petg_matteblack_1000_175_n", "PETG Matte")]
        [InlineData("bambulab_pc_frwhite_1000_175_n", "PC FR")]
        [InlineData(AbsBlackId, null)]
        [InlineData(DawnRadianceId, null)]
        public void FromCatalog_DetailedTypeFromName(string id, string? detailed)
        {
            Assert.Equal(detailed, ManualSpool.FromCatalog(Filament(id)).Info.DetailedFilamentType);
        }

        [Fact(DisplayName = "A gradient pick keeps its first two stops on the tag data and the colour count")]
        public void FromCatalog_MultiColor()
        {
            var info = ManualSpool.FromCatalog(Filament(DawnRadianceId)).Info;

            Assert.Equal("EC984CFF", info.Color);
            Assert.Equal("6CD4BCFF", info.SecondColor);
            Assert.Equal((ushort)4, info.ColorCount);

            // The card shows all four catalog stops.
            Assert.Equal(4, SpoolDisplayInfo.From(info, Filament(DawnRadianceId)).ColorHexes.Count);
        }

        [Fact(DisplayName = "An unknown filament keeps the entered material, colour and label weight")]
        public void Unknown_BuildsInfo()
        {
            var spool = ManualSpool.Unknown(" PETG ", "#ff8800", 750);

            Assert.True(spool.IsUnknown);
            Assert.Equal("PETG", spool.Info.FilamentType);
            Assert.Equal("PETG", spool.Info.DetailedFilamentType);
            Assert.Equal("FF8800FF", spool.Info.Color);
            Assert.Equal((ushort)750, spool.Info.SpoolWeight);
            Assert.Equal(ManualSpool.DefaultDiameter, spool.Info.FilamentDiameter);
            Assert.False(spool.Info.HasTag);
            Assert.Equal("PETG", spool.Label);

            Assert.Null(ManualSpool.Unknown("PLA", "red", 1000).Info.Color);
        }

        [Fact(DisplayName = "An unknown filament needs a material and a label weight")]
        public void Unknown_Validates()
        {
            Assert.Throws<ArgumentException>(() => ManualSpool.Unknown(" ", null, 1000));
            Assert.Throws<ArgumentOutOfRangeException>(() => ManualSpool.Unknown("PLA", null, 0));
        }

        #endregion

        #region Bambuddy

        [Fact(DisplayName = "A spool added by hand is created in Bambuddy with its weight and without tag fields")]
        public void Bambuddy_BuildManualSpoolCreate()
        {
            var create = BambuddyManager.BuildManualSpoolCreate(ManualSpool.FromCatalog(Filament(MatteIceBlueId)), Card);

            Assert.Equal("PLA", create.Material);
            Assert.Equal("Matte", create.Subtype);
            Assert.Equal("Matte Ice Blue", create.ColorName);
            Assert.Equal("A3D8E1FF", create.Rgba);
            Assert.Equal("Bambu", create.Brand);
            Assert.Equal(1000, create.LabelWeight);
            Assert.Equal(245, create.CoreWeight);
            Assert.Equal(145m, create.WeightUsed); // 245 + 1000 - 1100
            Assert.Equal(19.99m, create.CostPerKg);
            Assert.Equal("Dry box", create.StorageLocation);
            Assert.Null(create.ExtraColors);

            Assert.Null(create.TagUid);
            Assert.Null(create.TrayUuid);
            Assert.Null(create.DataOrigin);
            Assert.Null(create.TagType);
            Assert.Null(create.SlicerFilament);
        }

        [Fact(DisplayName = "A gradient added by hand carries every stop, and an unweighed one is left unused")]
        public void Bambuddy_BuildManualSpoolCreate_GradientUnweighed()
        {
            var create = BambuddyManager.BuildManualSpoolCreate(ManualSpool.FromCatalog(Filament(DawnRadianceId)), new SpoolEditInput(null, null, null, null, null, null));

            Assert.Equal("EC984CFF,6CD4BCFF,A66EB9FF,D87694FF", create.ExtraColors);
            Assert.Equal(BambuddyManager.CoreWeight, create.CoreWeight);
            Assert.Null(create.WeightUsed);
            Assert.Null(create.CostPerKg);
        }

        #endregion

        #region NoBackendManager

        [Fact(DisplayName = "Adding by hand needs the device store")]
        public async Task NoBackend_RequiresStore()
        {
            var manager = await Manager(storeLocally: false);

            Assert.False(await manager.AddManualSpoolAsync(ManualSpool.FromCatalog(Filament(AbsBlackId)), Card));
            Assert.Empty(manager.Spools);
        }

        [Fact(DisplayName = "A spool added by hand is kept with the edit card's fields and survives a restart")]
        public async Task NoBackend_AddsAndStores()
        {
            var manager = await Manager(storeLocally: true);

            var found = new List<(SpoolFound Found, BambuFilamentInfo Info)>();
            manager.OnSpoolFound += (spool, info) => found.Add((spool, info));

            Assert.True(await manager.AddManualSpoolAsync(ManualSpool.FromCatalog(Filament(AbsBlackId)), Card));

            var (spoolFound, spoolInfo) = Assert.Single(found);
            Assert.Equal("ABS", spoolFound.Material);
            Assert.Null(spoolFound.TrayUid);
            Assert.Equal(1100m, spoolFound.Weight);
            Assert.False(spoolInfo.HasTag);

            var spool = Assert.Single((await Manager(storeLocally: true)).Spools);

            Assert.True(spool.IsManual);
            Assert.StartsWith("MANUAL-", spool.Key);
            Assert.Equal(AbsBlackId, spool.FilamentId);
            Assert.Equal("Bambu Lab", spool.Manufacturer);
            Assert.Equal("Black", spool.Name);
            Assert.Equal(1100m, spool.Weight);
            Assert.Equal(245m, spool.EmptyWeight);
            Assert.Equal(19.99m, spool.Price);
            Assert.Equal(new DateTime(2026, 10, 1), spool.BuyDate);
            Assert.Equal("L-1", spool.LotNr);
            Assert.Equal("Dry box", spool.Location);
            Assert.Equal(["Dry box"], manager.ExistingLocations);
        }

        [Fact(DisplayName = "Two spools of the same filament added by hand stay two spools, next to scanned ones")]
        public async Task NoBackend_ManualSpoolsDontDeduplicate()
        {
            var manager = await Manager(storeLocally: true);

            await manager.AddManualSpoolAsync(ManualSpool.FromCatalog(Filament(AbsBlackId)), Card);
            await manager.AddManualSpoolAsync(ManualSpool.FromCatalog(Filament(AbsBlackId)), Card);

            // A scanned ABS Black is keyed by its tray uid and must not replace either.
            await manager.InventorySpool(JsonConvert.DeserializeObject<BambuFilamentInfo>(SampleTags.AbsBlack)!, null, null, null, null);

            Assert.Equal(3, manager.Spools.Count);
            Assert.Equal(3, manager.Spools.Select(x => x.Key).Distinct().Count());
            Assert.Equal(2, manager.Spools.Count(x => x.IsManual));
        }

        [Fact(DisplayName = "A spool added by hand can be edited and removed by its key")]
        public async Task NoBackend_EditAndRemove()
        {
            var manager = await Manager(storeLocally: true);
            await manager.AddManualSpoolAsync(ManualSpool.Unknown("PETG", "00FF00", 1000), Card);

            var key = Assert.Single(manager.Spools).Key!;

            Assert.True(await manager.UpdateSpoolAsync(key, new SpoolEditInput(900m, 250m, null, null, null, "Shelf A")));
            Assert.Equal(900m, manager.FindSpool(key)!.Weight);

            await manager.RemoveSpoolAsync(key);
            Assert.Empty(await Store().LoadAsync());
        }

        [Fact(DisplayName = "A spool added by hand shows as the filament it was picked as, an unknown one as entered")]
        public async Task NoBackend_Describe()
        {
            var manager = await Manager(storeLocally: true);
            await manager.AddManualSpoolAsync(ManualSpool.FromCatalog(Filament(MatteIceBlueId)), Card);
            await manager.AddManualSpoolAsync(ManualSpool.Unknown("PLA", "000000", 1000), Card);

            var picked = await manager.DescribeAsync(manager.Spools.Single(x => x.FilamentId == MatteIceBlueId));
            Assert.True(picked.Matched);
            Assert.Contains(picked.Rows, x => x is { Label: "Name", Value: "Matte Ice Blue" });
            Assert.DoesNotContain(picked.Rows, x => x.Label is "Tag serial" or "Tray uid");

            // Plain black PLA would match some catalog entry by colour alone; an unknown filament must not be guessed.
            var unknown = await manager.DescribeAsync(manager.Spools.Single(x => x.FilamentId == null));
            Assert.False(unknown.Matched);
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

        private static LocalSpool ManualLocalSpool() => new()
        {
            Info = ManualSpool.FromCatalog(Filament(MatteIceBlueId)).Info,
            ManualId = LocalSpool.NewManualId(),
            FilamentId = MatteIceBlueId,
            Manufacturer = "Bambu Lab",
            Name = "Matte Ice Blue",
            Weight = 1100m,
            Location = "Dry box",
            FirstScanned = new DateTime(2026, 9, 1, 10, 0, 0),
            LastScanned = new DateTime(2026, 9, 1, 10, 0, 0)
        };

        [Fact(DisplayName = "A spool added by hand exports without tag fields, keyed by its manual id")]
        public void Csv_ExportsTaglessSpool()
        {
            var spool = ManualLocalSpool();
            var row = ExportRow(spool);

            Assert.Equal("PLA", row["material"]);
            Assert.Equal("Matte Ice Blue", row["color_name"]);
            Assert.Equal("A3D8E1FF", row["rgba"]);
            Assert.Equal("1000", row["label_weight"]);
            Assert.Equal("", row["tag_uid"]);
            Assert.Equal("", row["tray_uuid"]);
            Assert.Equal("", row["data_origin"]);
            Assert.Equal("", row["tag_type"]);
            Assert.Equal(spool.ManualId, row["manual_id"]);
            Assert.Equal(MatteIceBlueId, row["filament_id"]);
        }

        [Fact(DisplayName = "A spool added by hand comes back from its csv with the same key, and an import replaces it")]
        public async Task Csv_RoundTripsTaglessSpool()
        {
            var original = ManualLocalSpool();

            using var stream = new MemoryStream();
            LocalSpoolCsv.Write(stream, [original]);
            stream.Position = 0;

            var result = LocalSpoolCsv.Read(stream);

            Assert.Empty(result.Errors);
            var spool = Assert.Single(result.Spools);

            Assert.Equal(original.Key, spool.Key);
            Assert.True(spool.IsManual);
            Assert.Equal(MatteIceBlueId, spool.FilamentId);
            Assert.Equal("PLA", spool.Info.FilamentType);
            Assert.Equal("A3D8E1FF", spool.Info.Color);
            Assert.Equal(1100m, spool.Weight);
            Assert.Equal("Dry box", spool.Location);

            var manager = await Manager(storeLocally: true);

            Assert.Equal((1, 0), await manager.ImportSpoolsAsync([original]));
            Assert.Equal((0, 1), await manager.ImportSpoolsAsync(result.Spools));
            Assert.Single(manager.Spools);
        }

        [Fact(DisplayName = "A csv of hand-added spools only needs the manual_id column")]
        public void Csv_ManualIdIsEnoughToImport()
        {
            var result = LocalSpoolCsv.Read(new StringReader("material,manual_id,rgba\nPLA,manual-abc,ff0000\n"));

            Assert.Empty(result.Errors);
            Assert.Equal("MANUAL-ABC", Assert.Single(result.Spools).Key);
        }

        #endregion
    }
}
