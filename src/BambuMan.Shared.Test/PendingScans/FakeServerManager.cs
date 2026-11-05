using BambuMan.Shared.Enums;
using BambuMan.Shared.Managers;
using BambuMan.Shared.Models;
using Microsoft.Extensions.Hosting;
using LogLevel = BambuMan.Shared.Enums.LogLevel;

namespace BambuMan.Shared.Test.PendingScans
{
    /// <summary>
    /// A server backend without a server: reachable or not as the test says, and inventories, rejects or fails on the
    /// network the way <see cref="SpoolmanManager"/> and <see cref="BambuddyManager"/> report it.
    /// </summary>
    internal class FakeServerManager(InventoryBackend backend = InventoryBackend.Spoolman) : BaseManager(null), IDisposable
    {
        public record SentScan(BambuFilamentInfo Info, DateTime? BuyDate, decimal? Price, string? LotNr, string? Location);

        /// <summary>Whether the server answers — both the health check and the inventory calls.</summary>
        public bool Reachable { get; set; } = true;

        /// <summary>Tray uids the server refuses, like a tag without a filament match.</summary>
        public HashSet<string> RejectedTrayUids { get; } = [];

        /// <summary>Tray uids inventoried as the unknown filament: the spool is added, but the call returns false.</summary>
        public HashSet<string> UnknownFilamentTrayUids { get; } = [];

        /// <summary>When set, inventory calls wait for it — to hold a send open.</summary>
        public TaskCompletionSource? Gate { get; set; }

        public List<SentScan> Sent { get; } = [];

        public override InventoryBackend Backend => backend;

        public override SpoolEditFields EditFields => new(BuyDate: true, LotNr: true);

        public static async Task<FakeServerManager> ReadyAsync(InventoryBackend backend = InventoryBackend.Spoolman)
        {
            var manager = new FakeServerManager(backend) { ApiUrl = "http://localhost:1" };
            await manager.Init();
            return manager;
        }

        /// <summary>The server is back and the health check noticed.</summary>
        public void Reconnect()
        {
            Reachable = true;
            IsHealth = true;
        }

        protected override IHost CreateApiHost(string normalizedApiUrl) => Host.CreateDefaultBuilder([]).Build();

        protected override Task<bool> CheckHealthAsync() => Task.FromResult(IsHealth = Reachable);

        protected override Task<bool> LoadInitialDataAsync() => Task.FromResult(true);

        public override async Task<bool> InventorySpool(BambuFilamentInfo info, DateTime? buyDate, decimal? price, string? lotNr, string? location)
        {
            if (Gate != null) await Gate.Task;

            if (!Reachable)
            {
                await HandleNetworkError(new HttpRequestException("Connection refused"), "Inventory spool");
                return false;
            }

            Sent.Add(new SentScan(info, buyDate, price, lotNr, location));

            if (info.TrayUid != null && RejectedTrayUids.Contains(info.TrayUid))
            {
                await LogAndSetStatus(ManagerStatusType.Error, LogLevel.Error, "No matching filament found");
                return false;
            }

            RaiseSpoolFound(new SpoolFound(info.FilamentType, info.TrayUid, null, null, price, buyDate, lotNr, location), info);

            // Spoolman adds an unmatched tag as the unknown filament, and reports that as not matched.
            return info.TrayUid == null || !UnknownFilamentTrayUids.Contains(info.TrayUid);
        }

        public override Task UpdateCurrentSpoolAsync(SpoolEditInput input) => Task.CompletedTask;

        public void Dispose() => StopHealthChecks();
    }
}
