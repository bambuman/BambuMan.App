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
            info.SerialNumber = uid;

            BambuddySpoolFound? found = null;
            manager.OnSpoolFound += f => found = f;

            int? createdId = null;
            try
            {
                var ok = await manager.InventorySpool(info, buyDate: null, price: 25m, lotNr: null, location: "IntegrationTest");

                Assert.True(ok, "InventorySpool should succeed");
                Assert.NotNull(found);
                createdId = found!.Id;

                // Re-scan -> cache hit by tray_uuid.
                var cached = manager.FindSpoolByTag(uid, null);
                Assert.NotNull(cached);
                Assert.Equal(createdId, cached!.Id);

                // Reduced update persists.
                await manager.UpdateSpoolReduced(createdId.Value, weightUsed: 123, location: "IntegrationTest2", note: "integration");

                var afterUpdate = manager.FindSpoolByTag(uid, null);
                Assert.NotNull(afterUpdate);
                Assert.Equal("IntegrationTest2", afterUpdate!.StorageLocation);
                Assert.Equal("integration", afterUpdate.Note);
            }
            finally
            {
                if (createdId.HasValue) await manager.ArchiveSpoolAsync(createdId.Value);
            }
        }
    }
}
