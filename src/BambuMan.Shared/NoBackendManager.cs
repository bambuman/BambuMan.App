using BambuMan.Shared.Enums;
using BambuMan.Shared.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ExternalFilament = SpoolMan.Api.Model.ExternalFilament;
using LogLevel = BambuMan.Shared.Enums.LogLevel;

namespace BambuMan.Shared
{
    /// <summary>
    /// The "no inventory server" option: a scanned tag is matched against the embedded Bambu Lab catalog and
    /// shown read-only. Nothing is written anywhere and nothing leaves the device, so the whole flow works
    /// offline. Tag upload to bambuman.ee is unaffected — it is governed by its own consent setting and runs
    /// independently of the inventory backend.
    /// </summary>
    public class NoBackendManager(ILogger<NoBackendManager>? logger) : BaseManager(logger)
    {
        private List<ExternalFilament> bambuLabFilaments = [];
        private bool catalogLoaded;

        #region BaseManager overrides

        public override InventoryBackend Backend => InventoryBackend.NoBackend;

        public override bool IsReadOnly => true;

        /// <summary>Nothing is editable, so neither optional edit field applies.</summary>
        public override SpoolEditFields EditFields => new(BuyDate: false, LotNr: false);

        /// <summary>Unreachable — <see cref="BaseManager.Init"/> short-circuits for a read-only backend.</summary>
        protected override IHost CreateApiHost(string normalizedApiUrl) => throw new NotSupportedException($"{nameof(NoBackendManager)} has no api");

        protected override Task<bool> CheckHealthAsync() => Task.FromResult(IsHealth = true);

        protected override async Task<bool> LoadInitialDataAsync()
        {
            if (catalogLoaded) return true;

            bambuLabFilaments = ExternalFilamentMatcher.LoadEmbeddedFilaments();
            catalogLoaded = true;

            await Log(LogLevel.Information, $"Loaded local filaments: {bambuLabFilaments.Count}");

            return bambuLabFilaments.Count > 0;
        }

        #endregion

        #region Inventory

        /// <summary>
        /// Show the tag. The edit arguments are ignored — there is nowhere to store a price, buy date, lot nr
        /// or location.
        /// </summary>
        public override async Task<bool> InventorySpool(BambuFilamentInfo info, DateTime? buyDate, decimal? price, string? lotNr, string? location)
        {
            var candidates = await ExternalFilamentMatcher.FindExternalFilament(bambuLabFilaments, info, Overrides);
            var matched = candidates.Count == 1 ? candidates[0] : null;

            // Unlike the server-backed managers an unmatched tag is not a failure here: nothing has to be
            // created, and the tag itself carries the material, colour, temperatures and drying times worth
            // showing. So no error tone, and the card renders either way.
            if (matched == null)
            {
                await Log(LogLevel.Information, candidates.Count > 1
                    ? $"Tag matched {candidates.Count} catalog filaments, showing tag data only"
                    : "Tag not found in the filament catalog, showing tag data only");
            }

            RaiseSpoolInfoRead(SpoolDisplayInfo.From(info, matched, Overrides));

            // Also raised so the on-screen inventory counter keeps working — it only needs material + tray uid.
            RaiseSpoolFound(new SpoolFound(
                Material: matched?.Material ?? info.FilamentType,
                TrayUid: info.TrayUid,
                Weight: info.SpoolWeight,
                EmptyWeight: null,
                Price: null,
                BuyDate: null,
                LotNr: null,
                Location: null), info);

            return matched != null;
        }

        /// <summary>No-op — there is no spool record to update.</summary>
        public override Task UpdateCurrentSpoolAsync(SpoolEditInput input) => Task.CompletedTask;

        #endregion
    }
}
