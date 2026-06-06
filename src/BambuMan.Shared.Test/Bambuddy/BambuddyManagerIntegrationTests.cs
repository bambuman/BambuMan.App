using BambuMan.Shared;
using BambuMan.Shared.Enums;
using BambuMan.Shared.Models;
using Newtonsoft.Json;

namespace BambuMan.Shared.Test.Bambuddy
{
    /// <summary>
    /// End-to-end checks against a live Bambuddy server. Skipped automatically when credentials are
    /// absent or the server is unreachable. Run locally with <c>dotnet test --filter Category=Integration</c>;
    /// exclude in CI with <c>--filter Category!=Integration</c>.
    /// </summary>
    [Trait("Category", "Integration")]
    public class BambuddyManagerIntegrationTests
    {
        [Fact(DisplayName = "InventorySpool create -> link -> find -> update against live server")]
        public async Task InventorySpool_CreateLinkFindUpdate_AgainstLiveServer()
        {
            if (!BambuddyTestConfig.TryLoad(out var url, out var key))
            {
                Assert.Skip("Bambuddy test credentials not available (set BAMBUDDY_TEST_URL/BAMBUDDY_TEST_KEY or provide tmp/test_bambuddy.txt).");
                return;
            }

            var manager = new BambuddyManager(null) { ApiUrl = url, ApiKey = key, HasNetworkAccess = () => true };
            await manager.Init();

            if (manager.Status != ManagerStatusType.Ready)
            {
                Assert.Skip($"Bambuddy server not reachable / not ready (status: {manager.Status}).");
                return;
            }

            // Unique tag so we always exercise the create path (no by-tag endpoint upstream yet).
            var uid = Guid.NewGuid().ToString("N").ToUpperInvariant();
            var info = JsonConvert.DeserializeObject<BambuFilamentInfo>(SampleTags.AbsWhite)!;
            info.TrayUid = uid;
            info.SerialNumber = uid[..8]; // tag_uid is 8 hex chars (e.g. 5B1449F6); tray_uuid stays the full 32

            SpoolFound? found = null;
            manager.OnSpoolFound += (f, _) => found = f;

            int? createdId = null;
            try
            {
                var ok = await manager.InventorySpool(info, buyDate: null, price: 25m, lotNr: null, location: "IntegrationTest");

                Assert.True(ok, "InventorySpool should succeed");
                Assert.NotNull(found);

                // Re-scan -> cache hit by tray_uuid; use it for the id (and later cleanup).
                var cached = manager.FindSpoolByTag(uid, null);
                Assert.NotNull(cached);
                createdId = cached!.Id;

                // Edit submitted as the unified SpoolEditInput (Bambuddy ignores BuyDate/LotNr, derives weight_used).
                await manager.UpdateCurrentSpoolAsync(new SpoolEditInput(
                    Weight: 800m, EmptyWeight: 250m, Price: 30m, BuyDate: null, LotNr: null, Location: "IntegrationTest2"));

                var afterUpdate = manager.FindSpoolByTag(uid, null);
                Assert.NotNull(afterUpdate);
                Assert.Equal("IntegrationTest2", afterUpdate!.StorageLocation);
            }
            finally
            {
                if (createdId.HasValue) await manager.ArchiveSpoolAsync(createdId.Value);
            }
        }

        [Fact(DisplayName = "Find existing spool by tag via server list (read path, fresh manager)")]
        public async Task FindExistingSpoolByTag_ViaServerList_FreshManager()
        {
            if (!BambuddyTestConfig.TryLoad(out var url, out var key))
            {
                Assert.Skip("Bambuddy test credentials not available (set BAMBUDDY_TEST_URL/BAMBUDDY_TEST_KEY or provide tmp/test_bambuddy.txt).");
                return;
            }

            BambuddyManager NewManager() => new(null) { ApiUrl = url, ApiKey = key, HasNetworkAccess = () => true };

            // Seed a spool so a known tag exists on the server.
            var seeder = NewManager();
            await seeder.Init();

            if (seeder.Status != ManagerStatusType.Ready)
            {
                Assert.Skip($"Bambuddy server not reachable / not ready (status: {seeder.Status}).");
                return;
            }

            var uid = Guid.NewGuid().ToString("N").ToUpperInvariant();
            var info = JsonConvert.DeserializeObject<BambuFilamentInfo>(SampleTags.AbsWhite)!;
            info.TrayUid = uid;
            info.SerialNumber = uid[..8]; // tag_uid is 8 hex chars (e.g. 5B1449F6); tray_uuid stays the full 32

            int? createdId = null;
            try
            {
                Assert.True(await seeder.InventorySpool(info, buyDate: null, price: 20m, lotNr: null, location: "ReadPathTest"));
                createdId = seeder.FindSpoolByTag(uid, null)?.Id;
                Assert.NotNull(createdId);

                // Fresh manager: its session cache is empty, so it must LIST spools from the server
                // (requires the "Read Status" scope) to find the existing tag — the read path the
                // create-only test never exercises.
                var reader = NewManager();
                await reader.Init();

                if (reader.CachedSpools.Count == 0)
                {
                    Assert.Skip("API key lacks inventory read (Read Status): server spool list came back empty, so the read path can't be exercised.");
                    return;
                }

                // Found via the server-loaded cache (not the session cache — this is a different instance).
                var existing = reader.FindSpoolByTag(uid, null);
                Assert.NotNull(existing);
                Assert.Equal(createdId, existing!.Id);

                // Re-inventory the same tag on the fresh manager -> existing path, must NOT create a duplicate.
                SpoolFound? found = null;
                reader.OnSpoolFound += (f, _) => found = f;

                Assert.True(await reader.InventorySpool(info, buyDate: null, price: 20m, lotNr: null, location: "ReadPathTest"));
                Assert.NotNull(found);
                Assert.Equal(createdId, reader.FindSpoolByTag(uid, null)!.Id);
            }
            finally
            {
                if (createdId.HasValue) await seeder.ArchiveSpoolAsync(createdId.Value);
            }
        }
    }
}
