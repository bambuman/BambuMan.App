using BambuMan.Shared.Enums;
using BambuMan.Shared.Managers;
using BambuMan.Shared.Models;
using BambuMan.Shared.Services;
using BambuMan.Shared.Test.Bambuddy;
using Newtonsoft.Json;

namespace BambuMan.Shared.Test.PendingScans
{
    [Trait("Category", "PendingScans")]
    public class PendingScanQueueTests : IDisposable
    {
        private const string AbsBlackTray = "5E755D3AE0FD409F913D5A89F817A248";
        private const string AbsWhiteTray = "658AF4C881A64A0781B32CFB5A7CB675";
        private const string PlaWoodTray = "4663E9ADF9CC454380EB58CE627BFE72";

        private readonly string directory = Path.Combine(Path.GetTempPath(), "bambuman-tests", Guid.NewGuid().ToString("N"));
        private readonly List<FakeServerManager> managers = [];

        public void Dispose()
        {
            foreach (var manager in managers) manager.Dispose();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        private string FilePath => Path.Combine(directory, "pending-scans.json");

        private static BambuFilamentInfo Tag(string json) => JsonConvert.DeserializeObject<BambuFilamentInfo>(json)!;

        private static PendingScan Scan(string json, InventoryBackend backend = InventoryBackend.Spoolman, string? location = null) =>
            new() { Backend = backend, Info = Tag(json), Price = 19.99m, Location = location };

        private PendingScanQueue Queue() => new() { Directory = directory };

        private async Task<FakeServerManager> Manager(InventoryBackend backend = InventoryBackend.Spoolman)
        {
            var manager = await FakeServerManager.ReadyAsync(backend);
            managers.Add(manager);
            return manager;
        }

        #region Queue

        [Fact(DisplayName = "Nothing queued yet reads as an empty queue")]
        public async Task Load_MissingFileIsEmpty()
        {
            var queue = Queue();
            await queue.LoadAsync();

            Assert.Empty(queue.Items);
            Assert.False(File.Exists(FilePath));
        }

        [Fact(DisplayName = "Queued scans survive a restart, with the whole tag and the import defaults, oldest first")]
        public async Task Enqueue_SurvivesRestart()
        {
            var buyDate = new DateTime(2026, 11, 1);
            var first = Scan(SampleTags.AbsBlack, location: "Shelf A");
            first.BuyDate = buyDate;
            first.LotNr = "L-1";

            await Queue().EnqueueAsync(first);
            await Queue().EnqueueAsync(Scan(SampleTags.PlaWood, InventoryBackend.Bambuddy));

            var restarted = Queue();
            await restarted.LoadAsync();

            Assert.Equal([AbsBlackTray, PlaWoodTray], restarted.Items.Select(x => x.Key));

            var scan = restarted.Items[0];
            Assert.Equal(InventoryBackend.Spoolman, scan.Backend);
            Assert.Equal("94B0C6D5", scan.Info.SerialNumber);
            Assert.Equal("B00-K0-1.75-1000", scan.Info.SkuStart);
            Assert.Equal(buyDate, scan.BuyDate);
            Assert.Equal(19.99m, scan.Price);
            Assert.Equal("L-1", scan.LotNr);
            Assert.Equal("Shelf A", scan.Location);
            Assert.True(scan.IsQueued);
            Assert.False(scan.IsRejected);

            Assert.Equal(InventoryBackend.Bambuddy, restarted.Items[1].Backend);

            // Persisted by name, like every other enum setting.
            Assert.Contains("\"Backend\": \"Bambuddy\"", await File.ReadAllTextAsync(FilePath));
        }

        [Fact(DisplayName = "The tag keys are never written to the queue file")]
        public async Task Enqueue_DoesNotStoreTagKeys()
        {
            var scan = Scan(SampleTags.AbsBlack);
            scan.Info.Keys = [0x01, 0x02];

            await Queue().EnqueueAsync(scan);

            Assert.DoesNotContain("Keys", await File.ReadAllTextAsync(FilePath));
        }

        [Fact(DisplayName = "A tag scanned again while queued replaces the older read, keeping its turn, not queued twice")]
        public async Task Enqueue_SameTagReplacesInPlace()
        {
            var queue = Queue();

            var first = await queue.EnqueueAsync(Scan(SampleTags.AbsBlack, location: "Shelf A"));
            await queue.EnqueueAsync(Scan(SampleTags.AbsWhite));

            // The other tag on the same spool: different tag uid, same tray uid.
            var otherSide = Scan(SampleTags.AbsBlack, location: "Shelf B");
            otherSide.Info.SerialNumber = "11223344";
            var again = await queue.EnqueueAsync(otherSide);

            Assert.Same(otherSide, again);
            Assert.Equal(first.QueuedAt, again.QueuedAt);
            Assert.Equal([AbsBlackTray, AbsWhiteTray], queue.Items.Select(x => x.Key));
            Assert.Equal("Shelf B", queue.Items[0].Location);
            Assert.Equal("11223344", queue.Items[0].Info.SerialNumber);
        }

        [Fact(DisplayName = "The same tag queued for two backends is kept once for each")]
        public async Task Enqueue_SameTagOtherBackendIsSeparate()
        {
            var queue = Queue();

            await queue.EnqueueAsync(Scan(SampleTags.AbsBlack));
            await queue.EnqueueAsync(Scan(SampleTags.AbsBlack, InventoryBackend.Bambuddy));

            Assert.Equal(2, queue.Items.Count);
            Assert.Equal(1, queue.CountFor(InventoryBackend.Spoolman));
            Assert.Equal(1, queue.CountFor(InventoryBackend.Bambuddy));
            Assert.Equal(0, queue.CountFor(InventoryBackend.NoBackend));
        }

        [Fact(DisplayName = "A damaged queue file is reported and kept aside, never saved over, and queueing goes on")]
        public async Task Load_DamagedFileIsKeptAside()
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(FilePath, "{ not json");

            var queue = Queue();

            var error = await Assert.ThrowsAsync<InvalidDataException>(queue.LoadAsync);
            Assert.IsAssignableFrom<JsonException>(error.InnerException);

            var kept = Assert.Single(Directory.GetFiles(directory, "pending-scans.damaged-*.json"));
            Assert.Contains(Path.GetFileName(kept), error.Message);
            Assert.Equal("{ not json", await File.ReadAllTextAsync(kept));

            await queue.EnqueueAsync(Scan(SampleTags.AbsBlack));

            var restarted = Queue();
            await restarted.LoadAsync();
            Assert.Equal(AbsBlackTray, Assert.Single(restarted.Items).Key);
        }

        [Fact(DisplayName = "Discarding removes one scan, every scan of one backend, or all")]
        public async Task RemoveAndClear()
        {
            var queue = Queue();

            var black = await queue.EnqueueAsync(Scan(SampleTags.AbsBlack));
            await queue.EnqueueAsync(Scan(SampleTags.AbsWhite));
            await queue.EnqueueAsync(Scan(SampleTags.PlaWood, InventoryBackend.Bambuddy));

            await queue.RemoveAsync(black.Id);
            Assert.Equal([AbsWhiteTray, PlaWoodTray], queue.Items.Select(x => x.Key));

            await queue.ClearAsync(InventoryBackend.Spoolman);
            Assert.Equal(PlaWoodTray, Assert.Single(queue.Items).Key);

            var restarted = Queue();
            await restarted.LoadAsync();
            Assert.Equal(PlaWoodTray, Assert.Single(restarted.Items).Key);

            await restarted.ClearAsync();
            Assert.Empty(restarted.Items);
        }

        [Fact(DisplayName = "Every change raises OnChanged")]
        public async Task Changes_RaiseOnChanged()
        {
            var queue = Queue();
            var changes = 0;
            queue.OnChanged += () => changes++;

            var scan = await queue.EnqueueAsync(Scan(SampleTags.AbsBlack));
            await queue.RemoveAsync(scan.Id);

            // The first change loads the (missing) file, then saves.
            Assert.Equal(3, changes);
        }

        #endregion

        #region Sending

        [Fact(DisplayName = "Back online, queued scans are sent in scan order with their defaults and removed")]
        public async Task Send_SendsInOrderAndRemoves()
        {
            var manager = await Manager();
            var queue = Queue();

            await queue.EnqueueAsync(Scan(SampleTags.AbsBlack, location: "Shelf A"));
            await queue.EnqueueAsync(Scan(SampleTags.AbsWhite));
            await queue.EnqueueAsync(Scan(SampleTags.PlaWood));

            var result = await queue.SendAsync(manager);

            Assert.Equal(new PendingScanSendResult(3, 0, 0, false), result);
            Assert.Equal([AbsBlackTray, AbsWhiteTray, PlaWoodTray], manager.Sent.Select(x => x.Info.TrayUid));
            Assert.Equal("Shelf A", manager.Sent[0].Location);
            Assert.Equal(19.99m, manager.Sent[0].Price);
            Assert.Empty(queue.Items);
        }

        [Fact(DisplayName = "Only the scans made for the manager's backend are sent")]
        public async Task Send_OnlyTheManagersBackend()
        {
            var manager = await Manager(InventoryBackend.Bambuddy);
            var queue = Queue();

            await queue.EnqueueAsync(Scan(SampleTags.AbsBlack));
            await queue.EnqueueAsync(Scan(SampleTags.PlaWood, InventoryBackend.Bambuddy));

            var result = await queue.SendAsync(manager);

            Assert.Equal(new PendingScanSendResult(1, 0, 0, false), result);
            Assert.Equal(PlaWoodTray, Assert.Single(manager.Sent).Info.TrayUid);
            Assert.Equal(AbsBlackTray, Assert.Single(queue.Items).Key);
        }

        [Fact(DisplayName = "A spool added as the unknown filament counts as sent, though the manager reports it unmatched")]
        public async Task Send_UnknownFilamentCountsAsSent()
        {
            var manager = await Manager();
            manager.UnknownFilamentTrayUids.Add(AbsBlackTray);

            var queue = Queue();
            await queue.EnqueueAsync(Scan(SampleTags.AbsBlack));

            Assert.Equal(new PendingScanSendResult(1, 0, 0, false), await queue.SendAsync(manager));
            Assert.Empty(queue.Items);
        }

        [Fact(DisplayName = "A rejected scan is kept with its error and the rest are still sent")]
        public async Task Send_RejectedIsKept()
        {
            var manager = await Manager();
            manager.RejectedTrayUids.Add(AbsWhiteTray);

            var queue = Queue();
            await queue.EnqueueAsync(Scan(SampleTags.AbsBlack));
            await queue.EnqueueAsync(Scan(SampleTags.AbsWhite));
            await queue.EnqueueAsync(Scan(SampleTags.PlaWood));

            var result = await queue.SendAsync(manager);

            Assert.Equal(new PendingScanSendResult(2, 1, 1, false), result);

            var rejected = Assert.Single(queue.Items);
            Assert.Equal(AbsWhiteTray, rejected.Key);
            Assert.Equal("No matching filament found", rejected.LastError);
            Assert.True(rejected.IsRejected);

            // A rejection is not the server being away.
            Assert.True(manager.CanSendScans);
        }

        [Fact(DisplayName = "A rejected scan is only sent again when asked to")]
        public async Task Send_RejectedIsNotRetriedAutomatically()
        {
            var manager = await Manager();
            manager.RejectedTrayUids.Add(AbsBlackTray);

            var queue = Queue();
            var scan = await queue.EnqueueAsync(Scan(SampleTags.AbsBlack));

            await queue.SendAsync(manager);
            manager.RejectedTrayUids.Clear(); // e.g. a newer filament match override arrived

            Assert.Equal(new PendingScanSendResult(0, 0, 1, false), await queue.SendAsync(manager));
            Assert.Single(manager.Sent);

            Assert.Equal(new PendingScanSendResult(1, 0, 0, false), await queue.SendOneAsync(manager, scan.Id));
            Assert.Equal(2, manager.Sent.Count);
            Assert.Empty(queue.Items);
        }

        [Fact(DisplayName = "Retry all sends rejected scans again too")]
        public async Task Send_RetryRejected()
        {
            var manager = await Manager();
            manager.RejectedTrayUids.Add(AbsBlackTray);

            var queue = Queue();
            await queue.EnqueueAsync(Scan(SampleTags.AbsBlack));
            await queue.SendAsync(manager);

            manager.RejectedTrayUids.Clear();

            Assert.Equal(new PendingScanSendResult(1, 0, 0, false), await queue.SendAsync(manager, retryRejected: true));
        }

        [Fact(DisplayName = "Scanning the rejected tag again clears its error, so it's sent automatically again")]
        public async Task Enqueue_RescanClearsRejection()
        {
            var manager = await Manager();
            manager.RejectedTrayUids.Add(AbsBlackTray);

            var queue = Queue();
            await queue.EnqueueAsync(Scan(SampleTags.AbsBlack));
            await queue.SendAsync(manager);

            var again = await queue.EnqueueAsync(Scan(SampleTags.AbsBlack));

            Assert.False(again.IsRejected);
        }

        [Fact(DisplayName = "When the server drops mid-way, sending stops and the unsent scans stay queued in order")]
        public async Task Send_StopsWhenUnreachable()
        {
            var manager = await Manager();
            var queue = Queue();

            await queue.EnqueueAsync(Scan(SampleTags.AbsBlack));
            await queue.EnqueueAsync(Scan(SampleTags.AbsWhite));
            await queue.EnqueueAsync(Scan(SampleTags.PlaWood));

            manager.OnSpoolFound += (_, _) => manager.Reachable = false;

            var result = await queue.SendAsync(manager);

            Assert.Equal(new PendingScanSendResult(1, 0, 2, true), result);
            Assert.Equal([AbsWhiteTray, PlaWoodTray], queue.Items.Select(x => x.Key));
            Assert.All(queue.Items, x => Assert.False(x.IsRejected));

            // The failed request marks the backend unreachable straight away, so the health check reports the reconnect.
            Assert.False(manager.IsHealth);
            Assert.False(manager.CanSendScans);
            Assert.Equal(ManagerStatusType.CantConnectToApi, manager.Status);
        }

        [Fact(DisplayName = "Nothing is tried while the backend is unreachable; everything is sent once it's back")]
        public async Task Send_WaitsForReconnect()
        {
            var manager = await Manager();
            var queue = Queue();

            await queue.EnqueueAsync(Scan(SampleTags.AbsBlack));
            await queue.EnqueueAsync(Scan(SampleTags.AbsWhite));

            manager.Reachable = false;
            manager.IsHealth = false;

            Assert.Equal(new PendingScanSendResult(0, 0, 2, true), await queue.SendAsync(manager));
            Assert.Empty(manager.Sent);

            manager.Reconnect();

            Assert.Equal(new PendingScanSendResult(2, 0, 0, false), await queue.SendAsync(manager));
            Assert.Empty(queue.Items);
        }

        [Fact(DisplayName = "A second send while one is running does nothing, so no scan is sent twice")]
        public async Task Send_IsNotReentrant()
        {
            var manager = await Manager();
            var queue = Queue();
            await queue.EnqueueAsync(Scan(SampleTags.AbsBlack));

            manager.Gate = new TaskCompletionSource();

            var first = queue.SendAsync(manager);
            Assert.True(queue.IsSending);

            Assert.Equal(new PendingScanSendResult(0, 0, 1, false), await queue.SendAsync(manager));

            manager.Gate.SetResult();

            Assert.Equal(new PendingScanSendResult(1, 0, 0, false), await first);
            Assert.False(queue.IsSending);
            Assert.Single(manager.Sent);
        }

        [Fact(DisplayName = "A tag read again while its older read is being sent stays queued, so the newer read is sent too")]
        public async Task Enqueue_WhileSendingTheSameTag()
        {
            var manager = await Manager();
            var queue = Queue();
            await queue.EnqueueAsync(Scan(SampleTags.AbsBlack, location: "Shelf A"));

            manager.Gate = new TaskCompletionSource();
            var sending = queue.SendAsync(manager);

            await queue.EnqueueAsync(Scan(SampleTags.AbsBlack, location: "Shelf B"));

            manager.Gate.SetResult();
            await sending;

            var newer = Assert.Single(queue.Items);
            Assert.Equal("Shelf B", newer.Location);
            Assert.Equal("Shelf A", Assert.Single(manager.Sent).Location);
        }

        [Fact(DisplayName = "A server failing behind a reverse proxy counts as unreachable, not as a rejection")]
        public async Task Send_ServerErrorWithFailingHealthIsUnreachable()
        {
            var manager = await Manager();
            var queue = Queue();
            await queue.EnqueueAsync(Scan(SampleTags.AbsBlack));

            manager.ServerUp = false;

            Assert.Equal(new PendingScanSendResult(0, 0, 1, true), await queue.SendAsync(manager));
            Assert.False(Assert.Single(queue.Items).IsRejected);
            Assert.Equal(ManagerStatusType.CantConnectToApi, manager.Status);
            Assert.False(manager.CanSendScans);
        }

        [Fact(DisplayName = "A rejected queued scan leaves the backend status as it was")]
        public async Task Send_RejectedQueuedScanKeepsStatus()
        {
            var manager = await Manager();
            manager.RejectedTrayUids.Add(AbsBlackTray);

            var queue = Queue();
            await queue.EnqueueAsync(Scan(SampleTags.AbsBlack));
            await queue.SendAsync(manager);

            Assert.Equal(ManagerStatusType.Ready, manager.Status);

            // A live scan's rejection still shows, as before.
            await manager.SendScanAsync(Scan(SampleTags.AbsBlack));
            Assert.Equal(ManagerStatusType.Error, manager.Status);
        }

        [Fact(DisplayName = "A reconnect has something to send only while unrejected scans wait for that backend")]
        public async Task HasScansToSend()
        {
            var manager = await Manager();
            manager.RejectedTrayUids.Add(AbsWhiteTray);

            var queue = Queue();
            Assert.False(queue.HasScansToSend(manager));

            await queue.EnqueueAsync(Scan(SampleTags.PlaWood, InventoryBackend.Bambuddy));
            Assert.False(queue.HasScansToSend(manager));

            await queue.EnqueueAsync(Scan(SampleTags.AbsWhite));
            Assert.True(queue.HasScansToSend(manager));

            manager.Reachable = false;
            manager.IsHealth = false;
            Assert.False(queue.HasScansToSend(manager));

            manager.Reconnect();
            await queue.SendAsync(manager);

            // Only the rejected one is left.
            Assert.False(queue.HasScansToSend(manager));
        }

        [Fact(DisplayName = "A scan just read is queued only when the backend can't be reached")]
        public async Task SendOrQueue()
        {
            var manager = await Manager();
            manager.RejectedTrayUids.Add(AbsWhiteTray);

            var queue = Queue();

            Assert.Equal(ScanSendStatus.Inventoried, (await queue.SendOrQueueAsync(manager, Scan(SampleTags.AbsBlack))).Status);
            Assert.Equal(ScanSendStatus.Rejected, (await queue.SendOrQueueAsync(manager, Scan(SampleTags.AbsWhite))).Status);
            Assert.Empty(queue.Items);

            manager.Reachable = false;

            Assert.Equal(ScanSendStatus.Unreachable, (await queue.SendOrQueueAsync(manager, Scan(SampleTags.PlaWood))).Status);

            var queued = Assert.Single(queue.Items);
            Assert.Equal(PlaWoodTray, queued.Key);
            Assert.True(queued.IsQueued);
        }

        #endregion

        #region BaseManager.SendScanAsync

        [Fact(DisplayName = "A live scan tells inventoried, rejected and unreachable apart")]
        public async Task SendScan_Outcomes()
        {
            var manager = await Manager();
            manager.RejectedTrayUids.Add(AbsWhiteTray);

            Assert.Equal(new ScanSendResult(ScanSendStatus.Inventoried), await manager.SendScanAsync(Scan(SampleTags.AbsBlack)));
            Assert.Equal(new ScanSendResult(ScanSendStatus.Rejected, "No matching filament found"), await manager.SendScanAsync(Scan(SampleTags.AbsWhite)));

            manager.Reachable = false;

            Assert.Equal(new ScanSendResult(ScanSendStatus.Unreachable), await manager.SendScanAsync(Scan(SampleTags.PlaWood)));
            Assert.Equal(2, manager.Sent.Count);
        }

        [Fact(DisplayName = "While a scan is sent its events can tell it apart from a live scan")]
        public async Task SendScan_ExposesTheScanBeingSent()
        {
            var manager = await Manager();
            var queued = await Queue().EnqueueAsync(Scan(SampleTags.AbsBlack));

            PendingScan? sending = null;
            manager.OnSpoolFound += (_, _) => sending = manager.SendingScan;

            await manager.SendScanAsync(queued);

            Assert.NotNull(sending);
            Assert.Same(queued, sending);
            Assert.True(sending.IsQueued);
            Assert.Null(manager.SendingScan);
        }

        [Fact(DisplayName = "Without a server url a scan is rejected, not queued")]
        public async Task SendScan_UrlMissingIsRejected()
        {
            var manager = new FakeServerManager();
            managers.Add(manager);
            await manager.Init();

            var result = await manager.SendScanAsync(Scan(SampleTags.AbsBlack));

            Assert.Equal(ScanSendStatus.Rejected, result.Status);
            Assert.Empty(manager.Sent);
        }

        [Fact(DisplayName = "The no-backend mode is never unreachable")]
        public async Task SendScan_NoBackendIsInventoried()
        {
            var manager = new NoBackendManager(null);
            await manager.Init();

            var result = await manager.SendScanAsync(Scan(SampleTags.AbsBlack, InventoryBackend.NoBackend));

            Assert.Equal(ScanSendStatus.Inventoried, result.Status);
        }

        #endregion
    }
}
