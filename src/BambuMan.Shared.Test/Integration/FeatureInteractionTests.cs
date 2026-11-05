using BambuMan.Shared.Enums;
using BambuMan.Shared.Managers;
using BambuMan.Shared.Matcher;
using BambuMan.Shared.Models;
using BambuMan.Shared.Nfc;
using BambuMan.Shared.Services;
using BambuMan.Shared.Test.Bambuddy;
using BambuMan.Shared.Test.PendingScans;
using Newtonsoft.Json;

namespace BambuMan.Shared.Test.Integration
{
    /// <summary>
    /// Where the offline queue, sending stored spools, manual spools and third-party tags meet: what each one does with
    /// the others' spools.
    /// </summary>
    public class FeatureInteractionTests : IDisposable
    {
        private static readonly byte[] Uid = [0x04, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66];

        private readonly string directory = Path.Combine(Path.GetTempPath(), "bambuman-tests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        private static SpoolTag OpenSpoolTag() =>
            OpenSpool.TryParse(Uid, """{"protocol":"openspool","version":"1.0","type":"PETG","color_hex":"FF0000","brand":"Polymaker","min_temp":"230","max_temp":"250","subtype":"Matte"}""")!;

        #region Methods

        [Fact(DisplayName = "A queued third-party scan keeps its format and brand across a restart and is keyed by its tag uid")]
        public async Task PendingScan_ThirdPartyTagSurvivesRestart()
        {
            var tag = OpenSpoolTag();

            await new PendingScanQueue { Directory = directory }.EnqueueAsync(new PendingScan { Backend = InventoryBackend.Spoolman, Tag = tag, Price = 20m });

            Assert.Contains("\"Format\": \"OpenSpool\"", await File.ReadAllTextAsync(Path.Combine(directory, "pending-scans.json"), TestContext.Current.CancellationToken));

            var queue = new PendingScanQueue { Directory = directory };
            await queue.LoadAsync();

            var scan = Assert.Single(queue.Items);
            var read = Assert.IsType<SpoolTag>(scan.Tag);

            Assert.Equal(SpoolTagFormat.OpenSpool, read.Format);
            Assert.Equal("Polymaker", read.Brand);
            Assert.Equal("PETG Matte", read.DetailedFilamentType);
            Assert.Equal(tag.Color, read.Color);
            Assert.Equal(tag.SpoolId, read.SpoolId);
            Assert.Equal(tag.SerialNumber, scan.Key);
        }

        [Fact(DisplayName = "A live third-party scan reaches the backend as read, with every field")]
        public void PendingScan_LiveThirdPartyTagAsRead()
        {
            var tag = OpenSpoolTag();

            Assert.Same(tag, new PendingScan { Tag = tag }.Tag);
        }

        [Fact(DisplayName = "A queued Bambu Lab scan is stored as before: the tag itself, no brand")]
        public void PendingScan_BambuTagUnchanged()
        {
            var info = JsonConvert.DeserializeObject<BambuFilamentInfo>(SampleTags.AbsBlack)!;
            var scan = new PendingScan { Tag = info };

            Assert.Same(info, scan.Info);
            Assert.Same(info, scan.Tag);
            Assert.Equal(SpoolTagFormat.BambuLab, scan.Format);
            Assert.Null(scan.Brand);
        }

        [Fact(DisplayName = "A spool added by hand isn't sent to a server: it has no tag the server could find it by again")]
        public async Task Upload_RefusesManualSpool()
        {
            using var manager = await FakeServerManager.ReadyAsync();
            var spool = new LocalSpool { Info = ManualSpool.Unknown("PLA", "FF0000", 1000).Info, ManualId = LocalSpool.NewManualId(), Weight = 1200 };

            var result = await manager.UploadSpoolAsync(spool);

            Assert.True(spool.IsManual);
            Assert.Equal(SpoolUploadStatus.Failed, result.Status);
            Assert.Contains("by hand", result.Message);
            Assert.Empty(manager.Sent);
        }

        [Fact(DisplayName = "A stored third-party spool is sent as its own tag format, keyed by the tag uid")]
        public async Task Upload_SendsThirdPartyTag()
        {
            using var manager = await FakeServerManager.ReadyAsync();
            var tag = OpenSpoolTag();
            var spool = new LocalSpool { Info = tag.ToFilamentInfo(), Format = tag.Format, Brand = tag.Brand };

            ISpoolTag? sent = null;
            manager.OnSpoolFound += (_, found) => sent = found;

            await manager.UploadSpoolAsync(spool);

            Assert.Equal(tag.SerialNumber, spool.Key);
            Assert.Equal(tag.SerialNumber, Assert.Single(manager.Sent).Info.SerialNumber);
            Assert.Null(sent);
        }

        [Fact(DisplayName = "A queued scan sent while a stored spool is in flight waits, so a rejected scan doesn't read as inventoried")]
        public async Task QueuedScan_WaitsForUpload()
        {
            using var manager = await FakeServerManager.ReadyAsync();
            var gate = new TaskCompletionSource();
            manager.Gate = gate;

            var rejected = JsonConvert.DeserializeObject<BambuFilamentInfo>(SampleTags.AbsBlack)!;
            manager.RejectedTrayUids.Add(rejected.TrayUid!);

            var upload = manager.UploadSpoolAsync(new LocalSpool { Info = JsonConvert.DeserializeObject<BambuFilamentInfo>(SampleTags.PlaWood)! });
            var sendScan = manager.SendScanAsync(new PendingScan { Tag = rejected, QueuedAt = DateTime.Now });

            await Task.Delay(50, TestContext.Current.CancellationToken);

            Assert.False(sendScan.IsCompleted);

            gate.SetResult();

            // Without the shared send lock the upload's found spool would count for the queued scan.
            Assert.NotEqual(SpoolUploadStatus.Failed, (await upload).Status);
            Assert.Equal(ScanSendStatus.Rejected, (await sendScan).Status);
            Assert.Equal([JsonConvert.DeserializeObject<BambuFilamentInfo>(SampleTags.PlaWood)!.TrayUid, rejected.TrayUid], manager.Sent.Select(x => x.Info.TrayUid));
        }

        [Fact(DisplayName = "Spoolman matches a third-party tag of another brand only once its catalog has loaded, as on the reconnect that sends the queued scans")]
        public async Task Spoolman_ThirdPartyTagWaitsForCatalog()
        {
            var manager = new LoadingSpoolmanManager();
            var match = manager.FindExternalFilament(OpenSpoolTag());

            await Task.Delay(50, TestContext.Current.CancellationToken);

            Assert.False(match.IsCompleted);

            var filament = ExternalFilamentMatcher.LoadEmbeddedFilaments().First(x => x.Material == "PETG");
            filament.Manufacturer = "Polymaker";
            filament.ColorHex = "FF0000";
            filament.ColorHexes = null;

            manager.AllExternalFilaments = [filament];
            manager.Load.SetResult();

            Assert.Same(filament, await match);
        }

        #endregion

        /// <summary>A Spoolman manager whose background load of the external catalog is still running until the test ends it.</summary>
        private sealed class LoadingSpoolmanManager : SpoolmanManager
        {
            public TaskCompletionSource Load { get; } = new();

            public LoadingSpoolmanManager() : base(null)
            {
                ExternalFilamentsLoad = Load.Task;
            }
        }
    }
}
