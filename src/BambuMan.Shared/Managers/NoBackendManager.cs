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
    /// shown — read-only, unless the spools are stored on the device, where they can be edited too. Nothing leaves the device, so the whole flow works offline. Tag upload to bambuman.ee is
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

        // The spool the last scan surfaced, which UpdateCurrentSpoolAsync writes to.
        private string? currentKey;

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

        /// <summary>Spools kept on the device can be edited; this session's scans alone are only shown.</summary>
        public override bool CanEditSpools => IsStoreLoaded;

        /// <summary>The device store keeps every field of the edit card; without it nothing is editable.</summary>
        public override SpoolEditFields EditFields => IsStoreLoaded ? new(BuyDate: true, LotNr: true) : new(BuyDate: false, LotNr: false);

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
        /// Show the tag and keep it in <see cref="Spools"/>, with the defaults passed in. Price and location also go into
        /// the csv export (Bambuddy's cost_per_kg and storage_location); with the store on, all of them can be edited.
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

            var key = await KeepSpoolAsync(info, matched, buyDate, price, lotNr, location);

            currentKey = key;

            // Raised for the on-screen inventory counter (material + tray uid) and, with the store on, to prefill the
            // edit card with what the spool has. An unweighed spool reads as full: its label weight plus the core.
            var kept = key == null ? null : FindSpool(key);
            var emptyWeight = kept?.EmptyWeight ?? BambuddyManager.CoreWeight;

            RaiseSpoolFound(new SpoolFound(
                Material: matched?.Material ?? info.FilamentType,
                TrayUid: info.TrayUid,
                Weight: kept?.Weight ?? BambuddyManager.LabelWeight(info) + emptyWeight,
                EmptyWeight: emptyWeight,
                Price: kept?.Price,
                BuyDate: kept?.BuyDate,
                LotNr: kept?.LotNr,
                Location: kept?.Location), info);

            return matched != null;
        }

        /// <summary>Saves the edit card to the spool the last scan surfaced. Does nothing while only this session's scans are kept.</summary>
        public override Task UpdateCurrentSpoolAsync(SpoolEditInput input) => currentKey == null || !CanEditSpools ? Task.CompletedTask : UpdateSpoolAsync(currentKey, input);

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

        /// <summary>The kept spool with this <see cref="LocalSpool.Key"/>, or null.</summary>
        public LocalSpool? FindSpool(string key)
        {
            lock (spoolLock) return (storedSpools ?? sessionSpools).FirstOrDefault(x => x.Key == key);
        }

        /// <summary>
        /// Writes the edit card's fields to a kept spool and saves the store. Only while the store is loaded — this
        /// session's scans alone are not editable. Returns false when there is no such spool.
        /// </summary>
        public async Task<bool> UpdateSpoolAsync(string key, SpoolEditInput input)
        {
            LocalSpool? spool;

            lock (spoolLock)
            {
                spool = storedSpools?.FirstOrDefault(x => x.Key == key);

                if (spool != null)
                {
                    spool.Weight = input.Weight;
                    spool.EmptyWeight = input.EmptyWeight;
                    spool.Price = input.Price;
                    spool.BuyDate = input.BuyDate;
                    spool.LotNr = string.IsNullOrWhiteSpace(input.LotNr) ? null : input.LotNr.Trim();
                    spool.Location = string.IsNullOrWhiteSpace(input.Location) ? null : input.Location.Trim();
                }
            }

            if (spool == null) return false;

            await SaveAndNotifyAsync();

            var name = string.Join(" ", new[] { spool.Info.DetailedFilamentType ?? spool.Info.FilamentType, spool.Name }.Where(x => !string.IsNullOrEmpty(x)));

            await Log(LogLevel.Success, $"Spool {name} updated");
            ShowMessage(false, $"Spool {name} updated.");

            return true;
        }

        /// <summary>The read-only card for a kept spool, matched against the catalog again so it shows what a scan shows.</summary>
        public async Task<SpoolDisplayInfo> DescribeAsync(LocalSpool spool)
        {
            if (!catalogLoaded)
            {
                bambuLabFilaments = ExternalFilamentMatcher.LoadEmbeddedFilaments();
                catalogLoaded = true;
            }

            var candidates = await ExternalFilamentMatcher.FindExternalFilament(bambuLabFilaments, spool.Info, Overrides);

            return SpoolDisplayInfo.From(spool.Info, candidates.Count == 1 ? candidates[0] : null, Overrides);
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

        /// <summary>Adds or refreshes the scanned spool; returns its key, or null when the tag has nothing to key it by.</summary>
        private async Task<string?> KeepSpoolAsync(BambuFilamentInfo info, ExternalFilament? matched, DateTime? buyDate, decimal? price, string? lotNr, string? location)
        {
            var key = LocalSpool.KeyOf(info);

            if (key == null) return null;

            location = string.IsNullOrWhiteSpace(location) ? null : location;
            lotNr = string.IsNullOrWhiteSpace(lotNr) ? null : lotNr;

            var now = DateTime.Now;

            lock (spoolLock)
            {
                Upsert(sessionSpools);
                if (storedSpools != null) Upsert(storedSpools);
            }

            await SaveAndNotifyAsync();

            return key;

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
                        Location = location,
                        BuyDate = buyDate,
                        LotNr = lotNr,
                        FirstScanned = now,
                        LastScanned = now
                    });

                    return;
                }

                // A re-scan refreshes the tag and catalog data, but keeps the price, location, buy date and lot nr the
                // spool already has — they may have been edited or imported, and the defaults a re-scan passes would
                // overwrite them. The one exception is "override location on read", which asks for exactly that.
                existing.Info = info;
                existing.Manufacturer = matched?.Manufacturer ?? existing.Manufacturer;
                existing.Name = matched?.Name ?? existing.Name;
                existing.FilamentCode = ExternalFilamentMatcher.FindFilamentCode(info, Overrides) ?? existing.FilamentCode;
                existing.Price ??= price;
                existing.BuyDate ??= buyDate;
                existing.LotNr ??= lotNr;

                if (OverrideLocationOnRead && location != null) existing.Location = location;
                else existing.Location ??= location;
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

                if (wasLoaded) NotifySpoolsChanged();

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
            else NotifySpoolsChanged();
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

            NotifySpoolsChanged();
        }

        /// <summary>Publishes the kept spools' locations as <see cref="BaseManager.ExistingLocations"/> and raises <see cref="OnSpoolsChanged"/>.</summary>
        private void NotifySpoolsChanged()
        {
            lock (spoolLock)
            {
                ExistingLocations = (storedSpools ?? sessionSpools)
                    .Select(x => x.Location)
                    .OfType<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            RaiseLocationsLoaded();
            OnSpoolsChanged?.Invoke();
        }

        #endregion
    }
}
