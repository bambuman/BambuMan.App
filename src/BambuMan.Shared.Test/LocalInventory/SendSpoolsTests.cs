using BambuMan.Shared.Enums;
using BambuMan.Shared.Interfaces;
using BambuMan.Shared.Managers;
using BambuMan.Shared.Models;
using BambuMan.Shared.Services;
using BambuMan.Shared.Test.Bambuddy;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using LogLevel = BambuMan.Shared.Enums.LogLevel;

namespace BambuMan.Shared.Test.LocalInventory
{
    /// <summary>Sending the spools kept on the device to an inventory server ("Send to Spoolman / Bambuddy").</summary>
    [Trait("Category", "LocalInventory")]
    public class SendSpoolsTests : IDisposable
    {
        private const string AbsBlackKey = "5E755D3AE0FD409F913D5A89F817A248";
        private const string PlaWoodKey = "4663E9ADF9CC454380EB58CE627BFE72";

        private readonly string directory = Path.Combine(Path.GetTempPath(), "bambuman-tests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        private static BambuFilamentInfo Tag(string json) => JsonConvert.DeserializeObject<BambuFilamentInfo>(json)!;

        private LocalInventoryStore Store() => new() { Directory = directory };

        private static SpoolFound ServerSpool(decimal? weight = 1250, decimal? emptyWeight = 250, decimal? price = null, DateTime? buyDate = null, string? lotNr = null, string? location = null) =>
            new("ABS", AbsBlackKey, weight, emptyWeight, price, buyDate, lotNr, location);

        #region LocalSpool mapping

        [Fact(DisplayName = "The stored spool's values win, the server keeps a value the stored spool has none for")]
        public void ToEditInput_MergesWithServerSpool()
        {
            var spool = new LocalSpool { Info = Tag(SampleTags.AbsBlack), Weight = 900, Price = 20m, Location = "Shelf A" };

            var input = spool.ToEditInput(ServerSpool(weight: 1250, emptyWeight: 250, price: 15m, buyDate: new DateTime(2025, 1, 2), lotNr: "LOT1", location: "Dry box"));

            Assert.Equal(900, input.Weight);
            Assert.Equal(250, input.EmptyWeight);
            Assert.Equal(20m, input.Price);
            Assert.Equal(new DateTime(2025, 1, 2), input.BuyDate);
            Assert.Equal("LOT1", input.LotNr);
            Assert.Equal("Shelf A", input.Location);
        }

        [Fact(DisplayName = "An unweighed stored spool leaves the server's remaining weight alone")]
        public void ToEditInput_UnweighedKeepsServerWeight()
        {
            var spool = new LocalSpool { Info = Tag(SampleTags.AbsBlack) };
            var server = ServerSpool(weight: 640);

            Assert.Equal(server.ToEditInput(), spool.ToEditInput(server));
        }

        [Fact(DisplayName = "The sent marker is stored by name and read back")]
        public async Task SentTo_PersistsByName()
        {
            await Store().SaveAsync([new LocalSpool { Info = Tag(SampleTags.AbsBlack), SentTo = InventoryBackend.Spoolman, SentAt = new DateTime(2026, 1, 2) }]);

            Assert.Contains("\"SentTo\": \"Spoolman\"", await File.ReadAllTextAsync(Path.Combine(directory, "local-inventory.json")));

            var spool = Assert.Single(await Store().LoadAsync());
            Assert.Equal(InventoryBackend.Spoolman, spool.SentTo);
            Assert.Equal(new DateTime(2026, 1, 2), spool.SentAt);
        }

        [Fact(DisplayName = "The summary counts each outcome and the spools never tried")]
        public void Summary_CountsOutcomes()
        {
            var spool = new LocalSpool { Info = Tag(SampleTags.AbsBlack) };

            var summary = new SpoolUploadSummary(InventoryBackend.Spoolman,
            [
                new(spool, SpoolUploadStatus.Created),
                new(spool, SpoolUploadStatus.Created),
                new(spool, SpoolUploadStatus.Updated),
                new(spool, SpoolUploadStatus.Failed, "No matching filament found")
            ], Total: 5, Cancelled: true);

            Assert.Equal(2, summary.Created);
            Assert.Equal(1, summary.Updated);
            Assert.Equal(1, summary.Failed);
            Assert.Equal(1, summary.Skipped);
            Assert.Equal("No matching filament found", Assert.Single(summary.Failures).Message);
            Assert.Equal("Sending cancelled. Sent to Spoolman: 2 created, 1 updated, 1 failed, 1 not sent.", summary.Text);
        }

        [Fact(DisplayName = "The summary lists and counts the spools whose server weight was kept")]
        public void Summary_CountsWeightsKept()
        {
            var spool = new LocalSpool { Info = Tag(SampleTags.AbsBlack) };

            var summary = new SpoolUploadSummary(InventoryBackend.Bambuddy,
            [
                new(spool, SpoolUploadStatus.Updated, KeptServerWeight: 600),
                new(spool, SpoolUploadStatus.Updated)
            ], Total: 2, Cancelled: false);

            Assert.Equal("Updated (kept server weight: 600 g)", Assert.Single(summary.WeightsKept).StatusText);
            Assert.Equal("Sent to Bambuddy: 0 created, 2 updated (1 kept server weight).", summary.Text);
        }

        #endregion

        #region BaseManager.UploadSpoolAsync

        [Fact(DisplayName = "A spool the server doesn't have is created with the stored values, then weighed")]
        public async Task Upload_CreatesAndAppliesWeight()
        {
            var manager = await FakeServerManager.ConnectedAsync();
            var spool = new LocalSpool { Info = Tag(SampleTags.AbsBlack), Weight = 900, Price = 20m, Location = "Shelf A", LotNr = "L1", BuyDate = new DateTime(2025, 3, 4) };

            var result = await manager.UploadSpoolAsync(spool);

            Assert.Equal(SpoolUploadStatus.Created, result.Status);
            Assert.Null(result.Message);
            Assert.Same(spool, result.Spool);

            // The scan path got the stored values as the new spool's defaults ...
            var inventoried = Assert.Single(manager.Inventoried);
            Assert.Same(spool.Info, inventoried.Info);
            Assert.Equal((20m, "Shelf A", "L1", new DateTime(2025, 3, 4)), (inventoried.Price, inventoried.Location, inventoried.LotNr, inventoried.BuyDate));

            // ... and the edit-card update carried the weight.
            var update = Assert.Single(manager.Updates);
            Assert.Equal(900, update.Weight);
            Assert.Equal(250, update.EmptyWeight);
        }

        [Fact(DisplayName = "A spool the server already has is updated, not created again")]
        public async Task Upload_UpdatesExisting()
        {
            var manager = await FakeServerManager.ConnectedAsync();
            manager.ServerSpools[AbsBlackKey] = ServerSpool(weight: 1250, price: 15m, location: "Dry box");

            var result = await manager.UploadSpoolAsync(new LocalSpool { Info = Tag(SampleTags.AbsBlack), Weight = 700 });

            Assert.Equal(SpoolUploadStatus.Updated, result.Status);

            var update = Assert.Single(manager.Updates);
            Assert.Equal(700, update.Weight);
            Assert.Equal(15m, update.Price);
            Assert.Equal("Dry box", update.Location);
        }

        [Fact(DisplayName = "A spool the server already has keeps its weight when it has less filament left than stored")]
        public async Task Upload_ExistingKeepsLowerServerWeight()
        {
            var manager = await FakeServerManager.ConnectedAsync();
            manager.ServerSpools[AbsBlackKey] = ServerSpool(weight: 600, emptyWeight: 250, location: "Dry box");

            // Stored full, before the server tracked 650g printed from it.
            var result = await manager.UploadSpoolAsync(new LocalSpool { Info = Tag(SampleTags.AbsBlack), Weight = 1250, EmptyWeight = 200, Location = "Shelf A" });

            Assert.Equal(SpoolUploadStatus.Updated, result.Status);
            Assert.Null(result.Message);
            Assert.Equal(600, result.KeptServerWeight);
            Assert.Equal("Updated (kept server weight: 600 g)", result.StatusText);

            var update = Assert.Single(manager.Updates);
            Assert.Equal((600, 250), (update.Weight, update.EmptyWeight));
            Assert.Equal("Shelf A", update.Location);
        }

        [Fact(DisplayName = "A spool just created takes the stored weight, even above the label weight")]
        public async Task Upload_CreatedTakesHigherStoredWeight()
        {
            var manager = await FakeServerManager.ConnectedAsync();

            var result = await manager.UploadSpoolAsync(new LocalSpool { Info = Tag(SampleTags.AbsBlack), Weight = 1400 });

            Assert.Equal(SpoolUploadStatus.Created, result.Status);
            Assert.Null(result.Message);
            Assert.Null(result.KeptServerWeight);
            Assert.Equal(1400, Assert.Single(manager.Updates).Weight);
        }

        [Fact(DisplayName = "No update is sent when the server's spool already has the stored values")]
        public async Task Upload_SkipsUnchangedUpdate()
        {
            var manager = await FakeServerManager.ConnectedAsync();
            manager.ServerSpools[AbsBlackKey] = ServerSpool(weight: 800, location: "Shelf A");

            var result = await manager.UploadSpoolAsync(new LocalSpool { Info = Tag(SampleTags.AbsBlack), Weight = 800, Location = "Shelf A" });

            Assert.Equal(SpoolUploadStatus.Updated, result.Status);
            Assert.Empty(manager.Updates);
        }

        [Fact(DisplayName = "An unmatched filament fails with the reason, without an error tone, message or status change")]
        public async Task Upload_NoMatchFailsQuietly()
        {
            var manager = await FakeServerManager.ConnectedAsync();
            manager.Matches = false;

            var events = new List<string>();
            manager.OnPlayErrorTone += () => events.Add("tone");
            manager.OnShowMessage += (_, message) => events.Add(message);
            manager.OnStatusChanged += () => events.Add("status");
            manager.OnSpoolFound += (_, _) => events.Add("found");

            var result = await manager.UploadSpoolAsync(new LocalSpool { Info = Tag(SampleTags.AbsBlack) });

            Assert.Equal(SpoolUploadStatus.Failed, result.Status);
            Assert.Equal("No matching filament found", result.Message);
            Assert.Empty(events);
            Assert.Equal(ManagerStatusType.Ready, manager.Status);
        }

        [Fact(DisplayName = "A send doesn't open the edit card or show messages, while a scan afterwards still does")]
        public async Task Upload_SuppressesScanEventsOnlyWhileSending()
        {
            var manager = await FakeServerManager.ConnectedAsync();

            var found = new List<SpoolFound>();
            var messages = new List<string>();
            manager.OnSpoolFound += (spool, _) => found.Add(spool);
            manager.OnShowMessage += (_, message) => messages.Add(message);

            await manager.UploadSpoolAsync(new LocalSpool { Info = Tag(SampleTags.AbsBlack), Weight = 900 });

            Assert.Empty(found);
            Assert.Empty(messages);

            await manager.InventorySpool(Tag(SampleTags.PlaWood), null, null, null, null);

            Assert.Single(found);
            Assert.NotEmpty(messages);
        }

        [Fact(DisplayName = "A created spool whose update is rejected is reported as failed")]
        public async Task Upload_UpdateFailure()
        {
            var manager = await FakeServerManager.ConnectedAsync();
            manager.UpdateError = "Can't update spool. Api response: 422";

            var result = await manager.UploadSpoolAsync(new LocalSpool { Info = Tag(SampleTags.AbsBlack), Weight = 900 });

            Assert.Equal(SpoolUploadStatus.Failed, result.Status);
            Assert.Equal("Added, but its weight and details weren't saved: Can't update spool. Api response: 422", result.Message);
            Assert.Equal(ManagerStatusType.Ready, manager.Status);
        }

        [Fact(DisplayName = "A network error is the spool's result")]
        public async Task Upload_NetworkError()
        {
            var manager = await FakeServerManager.ConnectedAsync();
            manager.NetworkError = new HttpRequestException("Connection refused");

            var result = await manager.UploadSpoolAsync(new LocalSpool { Info = Tag(SampleTags.AbsBlack) });

            Assert.Equal(SpoolUploadStatus.Failed, result.Status);
            Assert.Equal("Inventory spool failed: Connection refused", result.Message);
        }

        [Fact(DisplayName = "An unknown filament that is still added carries the warning")]
        public async Task Upload_UnknownFilamentWarning()
        {
            var manager = await FakeServerManager.ConnectedAsync();
            manager.Matches = false;
            manager.UnknownFilamentEnabled = true;

            var result = await manager.UploadSpoolAsync(new LocalSpool { Info = Tag(SampleTags.AbsBlack) });

            Assert.Equal(SpoolUploadStatus.Created, result.Status);
            Assert.Equal("No matching filament found", result.Message);
        }

        [Fact(DisplayName = "Nothing is sent before the backend is connected, or to the no-backend mode")]
        public async Task Upload_RequiresConnectedServer()
        {
            var spool = new LocalSpool { Info = Tag(SampleTags.AbsBlack) };

            var notConnected = await new FakeServerManager().UploadSpoolAsync(spool);
            Assert.Equal((SpoolUploadStatus.Failed, "Not connected to Spoolman"), (notConnected.Status, notConnected.Message));

            var noBackend = new NoBackendManager(null);
            await noBackend.Init();
            Assert.Equal(SpoolUploadStatus.Failed, (await noBackend.UploadSpoolAsync(spool)).Status);
        }

        #endregion

        #region NoBackendManager.SendSpoolsAsync

        private async Task<NoBackendManager> StoredAsync(params string[] tags)
        {
            var manager = new NoBackendManager(null, Store()) { StoreSpoolsLocally = true };
            await manager.Init();

            foreach (var tag in tags) await manager.InventorySpool(Tag(tag), null, null, null, null);

            return manager;
        }

        [Fact(DisplayName = "Sent spools are marked as sent and kept, failed ones are left as they were")]
        public async Task Send_MarksSentAndKeepsSpools()
        {
            var manager = await StoredAsync(SampleTags.AbsBlack, SampleTags.PlaWood, SampleTags.AbsWhite);
            var whiteKey = manager.Spools.Single(x => x.Key is not AbsBlackKey and not PlaWoodKey).Key!;

            var target = new FakeTarget
            {
                Results = { [AbsBlackKey] = SpoolUploadStatus.Created, [PlaWoodKey] = SpoolUploadStatus.Updated, [whiteKey] = SpoolUploadStatus.Failed }
            };

            var summary = await manager.SendSpoolsAsync(target, [AbsBlackKey, PlaWoodKey, whiteKey]);

            Assert.Equal((1, 1, 1, 0, false), (summary.Created, summary.Updated, summary.Failed, summary.Skipped, summary.Cancelled));
            Assert.Equal(3, manager.Spools.Count);
            Assert.Equal(InventoryBackend.Bambuddy, manager.FindSpool(AbsBlackKey)!.SentTo);
            Assert.Equal(InventoryBackend.Bambuddy, manager.FindSpool(PlaWoodKey)!.SentTo);
            Assert.NotNull(manager.FindSpool(PlaWoodKey)!.SentAt);
            Assert.Null(manager.FindSpool(whiteKey)!.SentTo);

            // The marks are on the device, not only in memory.
            var stored = await Store().LoadAsync();
            Assert.Equal(InventoryBackend.Bambuddy, stored.Single(x => x.Key == AbsBlackKey).SentTo);
            Assert.Null(stored.Single(x => x.Key == whiteKey).SentTo);
        }

        [Fact(DisplayName = "Only the picked spools are sent, in order, and unknown keys are skipped")]
        public async Task Send_OnlyPickedSpools()
        {
            var manager = await StoredAsync(SampleTags.AbsBlack, SampleTags.PlaWood);
            var target = new FakeTarget();

            var summary = await manager.SendSpoolsAsync(target, [PlaWoodKey, "NOT-STORED"]);

            Assert.Equal([PlaWoodKey], target.Sent.Select(x => x.Key));
            Assert.Equal(1, summary.Total);
            Assert.Null(manager.FindSpool(AbsBlackKey)!.SentTo);
        }

        [Fact(DisplayName = "Progress is reported before each spool and once done")]
        public async Task Send_ReportsProgress()
        {
            var manager = await StoredAsync(SampleTags.AbsBlack, SampleTags.PlaWood);
            var reports = new List<SpoolUploadProgress>();

            await manager.SendSpoolsAsync(new FakeTarget(), [AbsBlackKey, PlaWoodKey], new SyncProgress(reports.Add));

            Assert.Equal([(0, AbsBlackKey), (1, PlaWoodKey), (2, null)], reports.Select(x => (x.Done, x.Current?.Key)));
            Assert.All(reports, x => Assert.Equal(2, x.Total));
        }

        [Fact(DisplayName = "Cancelling stops before the next spool and keeps what was sent")]
        public async Task Send_Cancel()
        {
            var manager = await StoredAsync(SampleTags.AbsBlack, SampleTags.PlaWood);
            using var cancel = new CancellationTokenSource();
            var target = new FakeTarget { AfterSend = cancel.Cancel };

            var summary = await manager.SendSpoolsAsync(target, [AbsBlackKey, PlaWoodKey], cancellationToken: cancel.Token);

            Assert.True(summary.Cancelled);
            Assert.Equal(1, summary.Skipped);
            Assert.Single(target.Sent);
            Assert.Equal(InventoryBackend.Bambuddy, manager.FindSpool(AbsBlackKey)!.SentTo);
            Assert.Null(manager.FindSpool(PlaWoodKey)!.SentTo);
        }

        [Fact(DisplayName = "Editing a sent spool makes it unsent again; saving it unchanged doesn't")]
        public async Task Send_EditClearsSentMark()
        {
            var manager = await StoredAsync(SampleTags.AbsBlack);
            await manager.SendSpoolsAsync(new FakeTarget(), [AbsBlackKey]);

            var spool = manager.FindSpool(AbsBlackKey)!;

            await manager.UpdateSpoolAsync(AbsBlackKey, spool.EditInput);
            Assert.Equal(InventoryBackend.Bambuddy, spool.SentTo);

            await manager.UpdateSpoolAsync(AbsBlackKey, spool.EditInput with { Weight = 500 });
            Assert.Null(spool.SentTo);
            Assert.Null(spool.SentAt);
        }

        [Fact(DisplayName = "A spool edited while it is being sent isn't marked as sent")]
        public async Task Send_EditDuringSendStaysUnsent()
        {
            var manager = await StoredAsync(SampleTags.AbsBlack);
            var spool = manager.FindSpool(AbsBlackKey)!;
            var target = new FakeTarget { AfterSend = () => _ = manager.UpdateSpoolAsync(AbsBlackKey, spool.EditInput with { Weight = 500 }) };

            var summary = await manager.SendSpoolsAsync(target, [AbsBlackKey]);

            Assert.Equal(1, summary.Created);
            Assert.Equal(500, spool.Weight);
            Assert.Null(spool.SentTo);
            Assert.Null((await Store().LoadAsync()).Single().SentTo);
        }

        [Fact(DisplayName = "Spools can only be sent while they are stored locally")]
        public async Task Send_RequiresStore()
        {
            var manager = new NoBackendManager(null, Store());
            await manager.Init();

            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.SendSpoolsAsync(new FakeTarget(), [AbsBlackKey]));
        }

        [Fact(DisplayName = "The store loads without a full init while a server backend is selected")]
        public async Task LoadStore_WithoutInit()
        {
            await Store().SaveAsync([new LocalSpool { Info = Tag(SampleTags.AbsBlack) }]);

            var manager = new NoBackendManager(null, Store()) { StoreSpoolsLocally = true };
            await manager.LoadStoreAsync();

            Assert.False(manager.IsInitialized);
            Assert.True(manager.IsStoreLoaded);
            Assert.Equal(AbsBlackKey, Assert.Single(manager.Spools).Key);
        }

        #endregion

        #region Fakes

        /// <summary>A server-backed manager without a server: its scan path behaves like Spoolman's / Bambuddy's against an in-memory spool list.</summary>
        private class FakeServerManager() : BaseManager(null)
        {
            public Dictionary<string, SpoolFound> ServerSpools { get; } = [];

            public List<(BambuFilamentInfo Info, DateTime? BuyDate, decimal? Price, string? LotNr, string? Location)> Inventoried { get; } = [];

            public List<SpoolEditInput> Updates { get; } = [];

            public bool Matches { get; set; } = true;

            public string? UpdateError { get; set; }

            public Exception? NetworkError { get; set; }

            private string? currentKey;

            public static async Task<FakeServerManager> ConnectedAsync()
            {
                var manager = new FakeServerManager { ApiUrl = "http://localhost:7912" };
                await manager.Init();
                manager.StopHealthChecks();

                Assert.Equal(ManagerStatusType.Ready, manager.Status);

                return manager;
            }

            public override InventoryBackend Backend => InventoryBackend.Spoolman;

            public override SpoolEditFields EditFields => new(BuyDate: true, LotNr: true);

            protected override IHost CreateApiHost(string normalizedApiUrl) => new HostBuilder().Build();

            protected override Task<bool> CheckHealthAsync() => Task.FromResult(IsHealth = true);

            protected override Task<bool> LoadInitialDataAsync() => Task.FromResult(true);

            public override async Task<bool> InventorySpool(BambuFilamentInfo info, DateTime? buyDate, decimal? price, string? lotNr, string? location)
            {
                Inventoried.Add((info, buyDate, price, lotNr, location));

                if (NetworkError != null)
                {
                    await HandleNetworkError(NetworkError, "Inventory spool");
                    return false;
                }

                if (!Matches)
                {
                    await LogAndSetStatus(UnknownFilamentEnabled ? ManagerStatusType.Ready : ManagerStatusType.Error, UnknownFilamentEnabled ? LogLevel.Warning : LogLevel.Error, "No matching filament found");
                    PlayErrorTone();

                    if (!UnknownFilamentEnabled) return false;
                }

                var key = LocalSpool.KeyOf(info)!;
                currentKey = key;

                if (ServerSpools.TryGetValue(key, out var existing))
                {
                    ShowMessage(false, "Existing spool found");
                    RaiseSpoolFound(existing, info);
                    return true;
                }

                var created = new SpoolFound(info.FilamentType, info.TrayUid, (info.SpoolWeight ?? 1000) + 250, 250, price, buyDate, lotNr, location);
                ServerSpools[key] = created;

                ShowMessage(false, "Spool added");
                RaiseSpoolFound(created, info, created: true);

                return Matches;
            }

            public override async Task UpdateCurrentSpoolAsync(SpoolEditInput input)
            {
                if (UpdateError != null)
                {
                    await LogAndSetStatus(ManagerStatusType.Error, LogLevel.Error, UpdateError);
                    return;
                }

                Updates.Add(input);
                ShowMessage(false, "Spool updated");
            }
        }

        private class FakeTarget : ISpoolUploadTarget
        {
            public InventoryBackend Backend => InventoryBackend.Bambuddy;

            public Dictionary<string, SpoolUploadStatus> Results { get; } = [];

            public List<LocalSpool> Sent { get; } = [];

            public Action? AfterSend { get; set; }

            public Task<SpoolUploadResult> UploadSpoolAsync(LocalSpool spool)
            {
                Sent.Add(spool);
                AfterSend?.Invoke();

                var status = Results.GetValueOrDefault(spool.Key!, SpoolUploadStatus.Created);

                return Task.FromResult(new SpoolUploadResult(spool, status, status == SpoolUploadStatus.Failed ? "Rejected" : null));
            }
        }

        /// <summary><see cref="Progress{T}"/> posts to the thread pool; this reports inline so the order can be asserted.</summary>
        private class SyncProgress(Action<SpoolUploadProgress> report) : IProgress<SpoolUploadProgress>
        {
            public void Report(SpoolUploadProgress value) => report(value);
        }

        #endregion
    }
}
