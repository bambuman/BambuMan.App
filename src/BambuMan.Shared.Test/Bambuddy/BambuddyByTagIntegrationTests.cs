using BambuMan.Shared;
using BambuMan.Shared.Enums;
using BambuMan.Shared.Models;
using Newtonsoft.Json;

namespace BambuMan.Shared.Test.Bambuddy
{
    /// <summary>
    /// Live checks for the by-tag lookup against the Bambuddy DEV server, which exposes the new
    /// <c>GET /api/v1/inventory/spools/by-tag</c> endpoint. Uses <c>tmp/test_bambuddy_dev.txt</c>
    /// (or env <c>BAMBUDDY_DEV_TEST_URL</c> / <c>BAMBUDDY_DEV_TEST_KEY</c>); auto-skips when the dev
    /// credentials or server are unavailable. Run with <c>dotnet test --filter Category=Integration</c>.
    /// </summary>
    [Trait("Category", "Integration")]
    public class BambuddyByTagIntegrationTests
    {
        private const string DevFile = "test_bambuddy_dev.txt";
        private const string DevEnvPrefix = "BAMBUDDY_DEV_TEST";

        [Fact(DisplayName = "By-tag endpoint finds a spool, archived included via include_archived=true")]
        public async Task GetSpoolByTag_FindsSpoolIncludingArchived()
        {
            if (!BambuddyTestConfig.TryLoad(out var url, out var key, DevFile, DevEnvPrefix))
            {
                Assert.Skip("Bambuddy DEV credentials not available (set BAMBUDDY_DEV_TEST_URL/KEY or provide tmp/test_bambuddy_dev.txt).");
                return;
            }

            var manager = new BambuddyManager(null) { ApiUrl = url, ApiKey = key, HasNetworkAccess = () => true };
            await manager.Init();

            if (manager.Status != ManagerStatusType.Ready)
            {
                Assert.Skip($"Bambuddy DEV server not reachable / not ready (status: {manager.Status}).");
                return;
            }

            var uid = Guid.NewGuid().ToString("N").ToUpperInvariant();
            var info = JsonConvert.DeserializeObject<BambuFilamentInfo>(SampleTags.AbsWhite)!;
            info.TrayUid = uid;
            info.SerialNumber = uid[..8]; // tag_uid is 8 hex chars; tray_uuid is the full 32

            int? createdId = null;
            try
            {
                Assert.True(await manager.InventorySpool(info, buyDate: null, price: 20m, lotNr: null, location: "ByTagTest"), "create should succeed");

                // Active spool is found directly via the by-tag endpoint (not the session cache).
                var active = await manager.GetSpoolByTagAsync(uid, info.SerialNumber, includeArchived: true);
                Assert.NotNull(active);
                createdId = active!.Id;

                // Archive it, then prove include_archived actually changes the result.
                await manager.ArchiveSpoolAsync(active.Id);

                var archivedIncluded = await manager.GetSpoolByTagAsync(uid, info.SerialNumber, includeArchived: true);
                Assert.NotNull(archivedIncluded);
                Assert.Equal(createdId, archivedIncluded!.Id);

                var archivedExcluded = await manager.GetSpoolByTagAsync(uid, info.SerialNumber, includeArchived: false);
                Assert.Null(archivedExcluded);
            }
            finally
            {
                if (createdId.HasValue) await manager.ArchiveSpoolAsync(createdId.Value);
            }
        }

        [Fact(DisplayName = "Fresh manager dedups an archived tag via the by-tag endpoint (cache can't see it)")]
        public async Task InventorySpool_DedupsArchivedTag_ViaByTag()
        {
            if (!BambuddyTestConfig.TryLoad(out var url, out var key, DevFile, DevEnvPrefix))
            {
                Assert.Skip("Bambuddy DEV credentials not available (set BAMBUDDY_DEV_TEST_URL/KEY or provide tmp/test_bambuddy_dev.txt).");
                return;
            }

            BambuddyManager NewManager() => new(null) { ApiUrl = url, ApiKey = key, HasNetworkAccess = () => true };

            var seeder = NewManager();
            await seeder.Init();

            if (seeder.Status != ManagerStatusType.Ready)
            {
                Assert.Skip($"Bambuddy DEV server not reachable / not ready (status: {seeder.Status}).");
                return;
            }

            var uid = Guid.NewGuid().ToString("N").ToUpperInvariant();
            var info = JsonConvert.DeserializeObject<BambuFilamentInfo>(SampleTags.AbsWhite)!;
            info.TrayUid = uid;
            info.SerialNumber = uid[..8];

            int? createdId = null;
            try
            {
                Assert.True(await seeder.InventorySpool(info, buyDate: null, price: 20m, lotNr: null, location: "ByTagDedup"));
                createdId = (await seeder.GetSpoolByTagAsync(uid, info.SerialNumber))?.Id;
                Assert.NotNull(createdId);

                // Archive it, so a list-scan (include_archived=false) can no longer see it.
                await seeder.ArchiveSpoolAsync(createdId!.Value);

                // Fresh manager: its session cache + init list-load (non-archived) cannot contain the archived spool...
                var reader = NewManager();
                await reader.Init();
                Assert.Null(reader.FindSpoolByTag(uid, null)); // cache fallback genuinely can't find it

                // ...so re-inventorying the same tag must dedup via the by-tag endpoint, not create a duplicate.
                SpoolFound? found = null;
                reader.OnSpoolFound += (f, _) => found = f;

                Assert.True(await reader.InventorySpool(info, buyDate: null, price: 20m, lotNr: null, location: "ByTagDedup"));
                Assert.NotNull(found);

                // Same spool still backs the tag — no duplicate was created.
                var afterId = (await reader.GetSpoolByTagAsync(uid, info.SerialNumber, includeArchived: true))?.Id;
                Assert.Equal(createdId, afterId);
            }
            finally
            {
                if (createdId.HasValue) await seeder.ArchiveSpoolAsync(createdId.Value);
            }
        }
    }
}
