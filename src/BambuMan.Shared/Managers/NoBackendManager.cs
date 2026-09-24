using BambuMan.Shared.Enums;
using BambuMan.Shared.Matcher;
using BambuMan.Shared.Models;
using BambuMan.Shared.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ExternalFilament = SpoolMan.Api.Model.ExternalFilament;
using LogLevel = BambuMan.Shared.Enums.LogLevel;

namespace BambuMan.Shared.Managers
{
    /// <summary>
    /// The "no inventory server" option: a scanned tag is matched against the embedded Bambu Lab catalog and
    /// shown read-only. Nothing leaves the device, so the whole flow works offline. Tag upload to bambuman.ee is
    /// unaffected — it is governed by its own consent setting and runs independently of the inventory backend.
    /// <para>
    /// Every scanned spool is kept in <see cref="Spools"/> for a csv export. By default that is only this session's
    /// scans, held in memory; with <see cref="StoreSpoolsLocally"/> it is the inventory kept on the device by
    /// <see cref="ILocalInventoryStore"/>, which survives a restart.
    /// </para>
    /// </summary>
    public class NoBackendManager(ILogger<NoBackendManager>? logger, ILocalInventoryStore? localStore = null) : BaseManager(logger)
    {
        public delegate void SpoolsChangedEventHandler();

        /// <summary>Raised whenever <see cref="Spools"/> changes — a scan, an import, a delete, or the store being switched on or off.</summary>
        public event SpoolsChangedEventHandler? OnSpoolsChanged;

        private readonly Lock spoolLock = new();

        // This session's scans — always kept, so turning "store locally" off falls back to them.
        private readonly List<LocalSpool> sessionSpools = [];

        // The device inventory; null while "store locally" is off or it couldn't be loaded.
        private List<LocalSpool>? storedSpools;

        private List<ExternalFilament> bambuLabFilaments = [];
        private bool catalogLoaded;

        /// <summary>
        /// Keep scanned spools on the device (see <see cref="ILocalInventoryStore"/>). Takes effect on the next
        /// <see cref="BaseManager.Init"/>: switching it on loads the stored inventory and adds this session's scans to
        /// it; switching it off goes back to this session's scans, leaving the stored inventory untouched.
        /// </summary>
        public bool StoreSpoolsLocally { get; set; }

        /// <summary>True when <see cref="Spools"/> is the stored device inventory rather than this session's scans.</summary>
        public bool IsStoreLoaded
        {
            get { lock (spoolLock) return storedSpools != null; }
        }

        /// <summary>The spools a csv export writes: the stored inventory when <see cref="IsStoreLoaded"/>, else this session's scans.</summary>
        public IReadOnlyList<LocalSpool> Spools
        {
            get { lock (spoolLock) return (storedSpools ?? sessionSpools).ToList(); }
        }

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
            if (!catalogLoaded)
            {
                bambuLabFilaments = ExternalFilamentMatcher.LoadEmbeddedFilaments();
                catalogLoaded = true;

                await Log(LogLevel.Information, $"Loaded local filaments: {bambuLabFilaments.Count}");
            }

            await SyncLocalStoreAsync();

            return bambuLabFilaments.Count > 0;
        }

        #endregion

        #region Inventory

        /// <summary>
        /// Show the tag and keep it in <see cref="Spools"/>. Buy date and lot nr are ignored — there is nowhere to show
        /// them; price and location are kept for the csv export (Bambuddy's cost_per_kg and storage_location).
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

            await KeepSpoolAsync(info, matched, price, location);

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

        /// <summary>No-op — the spool is shown read-only, so there are no edits to save.</summary>
        public override Task UpdateCurrentSpoolAsync(SpoolEditInput input) => Task.CompletedTask;

        #endregion

        #region Local spools

        /// <summary>
        /// Add the spools of a csv import to the stored inventory: a spool not there yet is added, one already there
        /// (same <see cref="LocalSpool.Key"/>) is replaced. Only possible while the stored inventory is loaded.
        /// </summary>
        public async Task<(int Added, int Updated)> ImportSpoolsAsync(IEnumerable<LocalSpool> spools)
        {
            int added = 0, updated = 0;

            lock (spoolLock)
            {
                if (storedSpools == null) throw new InvalidOperationException("Spools can only be imported while they are stored locally");

                foreach (var spool in spools)
                {
                    if (spool.Key == null) continue;

                    var index = storedSpools.FindIndex(x => x.Key == spool.Key);

                    if (index >= 0)
                    {
                        storedSpools[index] = spool;
                        updated++;
                    }
                    else
                    {
                        storedSpools.Add(spool);
                        added++;
                    }
                }
            }

            await SaveAndNotifyAsync();

            await Log(LogLevel.Information, $"Imported spools: {added} added, {updated} updated");

            return (added, updated);
        }

        /// <summary>Remove one spool from <see cref="Spools"/> (and from the device, when stored locally).</summary>
        public async Task RemoveSpoolAsync(string key)
        {
            lock (spoolLock)
            {
                sessionSpools.RemoveAll(x => x.Key == key);
                storedSpools?.RemoveAll(x => x.Key == key);
            }

            await SaveAndNotifyAsync();
        }

        /// <summary>Empty <see cref="Spools"/> (and the device inventory, when stored locally).</summary>
        public async Task ClearSpoolsAsync()
        {
            lock (spoolLock)
            {
                sessionSpools.Clear();
                storedSpools?.Clear();
            }

            await SaveAndNotifyAsync();
        }

        private async Task KeepSpoolAsync(BambuFilamentInfo info, ExternalFilament? matched, decimal? price, string? location)
        {
            var key = LocalSpool.KeyOf(info);

            if (key == null) return;

            var now = DateTime.Now;

            lock (spoolLock)
            {
                Upsert(sessionSpools);
                if (storedSpools != null) Upsert(storedSpools);
            }

            await SaveAndNotifyAsync();

            void Upsert(List<LocalSpool> spools)
            {
                var existing = spools.FirstOrDefault(x => x.Key == key);

                if (existing == null)
                {
                    spools.Add(new LocalSpool
                    {
                        Info = info,
                        Manufacturer = matched?.Manufacturer,
                        Name = matched?.Name,
                        FilamentCode = ExternalFilamentMatcher.FindFilamentCode(info, Overrides),
                        ColorHexes = matched?.ColorHexes?.Select(x => x.TrimStart('#').ToUpperInvariant()).ToList() ?? [],
                        Price = price,
                        Location = string.IsNullOrWhiteSpace(location) ? null : location,
                        FirstScanned = now,
                        LastScanned = now
                    });

                    return;
                }

                // A re-scan refreshes the tag and catalog data, but keeps the price and location the spool already
                // has — they may have come from an import, and the defaults a re-scan passes would overwrite them.
                existing.Info = info;
                existing.Manufacturer = matched?.Manufacturer ?? existing.Manufacturer;
                existing.Name = matched?.Name ?? existing.Name;
                existing.FilamentCode = ExternalFilamentMatcher.FindFilamentCode(info, Overrides) ?? existing.FilamentCode;
                existing.Price ??= price;
                existing.Location ??= string.IsNullOrWhiteSpace(location) ? null : location;
                existing.LastScanned = now;
            }
        }

        /// <summary>Bring the loaded inventory in line with <see cref="StoreSpoolsLocally"/>.</summary>
        private async Task SyncLocalStoreAsync()
        {
            if (!StoreSpoolsLocally)
            {
                bool wasLoaded;

                lock (spoolLock)
                {
                    wasLoaded = storedSpools != null;
                    storedSpools = null;
                }

                if (wasLoaded) OnSpoolsChanged?.Invoke();

                return;
            }

            if (IsStoreLoaded) return;

            if (localStore == null)
            {
                await Log(LogLevel.Error, "Can't store spools locally: no local inventory store is configured");
                return;
            }

            List<LocalSpool> stored;

            try
            {
                stored = await localStore.LoadAsync();
            }
            catch (Exception e)
            {
                // Leave the store unloaded: saving now would overwrite whatever is in the damaged file.
                await Log(LogLevel.Error, $"Could not read the locally stored spools: {e.Message}", e);
                ShowMessage(true, "Could not read the locally stored spools. See logs.");
                return;
            }

            int merged;

            lock (spoolLock)
            {
                // Spools scanned before "store locally" was switched on are kept too.
                var missing = sessionSpools.Where(x => stored.All(s => s.Key != x.Key)).ToList();

                stored.AddRange(missing);
                storedSpools = stored;
                merged = missing.Count;
            }

            await Log(LogLevel.Information, $"Loaded locally stored spools: {stored.Count}");

            if (merged > 0) await SaveAndNotifyAsync();
            else OnSpoolsChanged?.Invoke();
        }

        private async Task SaveAndNotifyAsync()
        {
            List<LocalSpool>? toSave;

            lock (spoolLock) toSave = storedSpools?.ToList();

            if (toSave != null && localStore != null)
            {
                try
                {
                    await localStore.SaveAsync(toSave);
                }
                catch (Exception e)
                {
                    await Log(LogLevel.Error, $"Could not save the locally stored spools: {e.Message}", e);
                    ShowMessage(true, "Could not save the locally stored spools. See logs.");
                }
            }

            OnSpoolsChanged?.Invoke();
        }

        #endregion
    }
}
