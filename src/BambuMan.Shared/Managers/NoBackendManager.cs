using BambuMan.Shared.Enums;
using BambuMan.Shared.Interfaces;
using BambuMan.Shared.Matcher;
using BambuMan.Shared.Models;
using BambuMan.Shared.Resources.Strings;
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
    /// <para>
    /// The stored spools stay on the device when a server backend is selected later, and can be sent to it with
    /// <see cref="SendSpoolsAsync"/> — besides a csv export, the only way they leave the device, and only when the user asks.
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
        public override async Task<bool> InventorySpool(ISpoolTag tag, DateTime? buyDate, decimal? price, string? lotNr, string? location)
        {
            var candidates = await ExternalFilamentMatcher.FindExternalFilament(bambuLabFilaments, tag, Overrides);
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

            RaiseSpoolInfoRead(SpoolDisplayInfo.From(tag, matched, Overrides));

            var key = await KeepSpoolAsync(tag, matched, buyDate, price, lotNr, location);

            currentKey = key;

            // Raised for the on-screen inventory counter (material + tray uid) and, with the store on, to prefill the
            // edit card with what the spool has. An unweighed spool reads as full: its label weight plus the core.
            var kept = key == null ? null : FindSpool(key);
            var emptyWeight = kept?.EmptyWeight ?? BambuddyManager.CoreWeight;

            RaiseSpoolFound(new SpoolFound(
                Material: matched?.Material ?? tag.FilamentType,
                TrayUid: tag.SpoolId,
                Weight: kept?.Weight ?? BambuddyManager.LabelWeight(tag) + emptyWeight,
                EmptyWeight: emptyWeight,
                Price: kept?.Price,
                BuyDate: kept?.BuyDate,
                LotNr: kept?.LotNr,
                Location: kept?.Location), tag);

            return matched != null;
        }

        /// <summary>Saves the edit card to the spool the last scan surfaced. Does nothing while only this session's scans are kept.</summary>
        public override Task UpdateCurrentSpoolAsync(SpoolEditInput input) => currentKey == null || !CanEditSpools ? Task.CompletedTask : UpdateSpoolAsync(currentKey, input);

        public override IReadOnlyList<ExternalFilament> KnownFilaments => bambuLabFilaments;

        /// <summary>
        /// Keeps a spool added by hand under a new <see cref="LocalSpool.ManualId"/>, with the edit card's fields. Only
        /// while the store is loaded — this session's scans alone are shown, not edited, and the same goes for these.
        /// </summary>
        protected override async Task<bool> AddManualSpoolCoreAsync(ManualSpool spool, SpoolEditInput input)
        {
            if (!CanEditSpools)
            {
                await Log(LogLevel.Warning, "Spools can only be added by hand while they are stored locally");
                return false;
            }

            var now = DateTime.Now;
            var filament = spool.Filament;

            var localSpool = new LocalSpool
            {
                Info = spool.Info,
                ManualId = LocalSpool.NewManualId(),
                FilamentId = filament?.Id,
                Manufacturer = filament?.Manufacturer,
                Name = filament?.Name,
                ColorHexes = filament?.ColorHexes?.Select(x => x.TrimStart('#').ToUpperInvariant()).ToList() ?? [],
                Weight = input.Weight,
                EmptyWeight = input.EmptyWeight,
                Price = input.Price,
                BuyDate = input.BuyDate,
                LotNr = string.IsNullOrWhiteSpace(input.LotNr) ? null : input.LotNr.Trim(),
                Location = string.IsNullOrWhiteSpace(input.Location) ? null : input.Location.Trim(),
                FirstScanned = now,
                LastScanned = now
            };

            lock (spoolLock)
            {
                sessionSpools.Add(localSpool);
                storedSpools?.Add(localSpool);
            }

            await SaveAndNotifyAsync();

            currentKey = localSpool.Key;

            await Log(LogLevel.Success, $"Spool {spool.Label} added manually");
            ShowMessage(false, string.Format(SharedResources.Manager_ManualSpoolAdded, spool.Label));

            RaiseSpoolFound(new SpoolFound(
                Material: filament?.Material ?? spool.Info.FilamentType,
                TrayUid: null,
                Weight: localSpool.Weight,
                EmptyWeight: localSpool.EmptyWeight,
                Price: localSpool.Price,
                BuyDate: localSpool.BuyDate,
                LotNr: localSpool.LotNr,
                Location: localSpool.Location), spool.Info);

            return true;
        }

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
                    var before = spool.EditInput;

                    spool.Weight = input.Weight;
                    spool.EmptyWeight = input.EmptyWeight;
                    spool.Price = input.Price;
                    spool.BuyDate = input.BuyDate;
                    spool.LotNr = string.IsNullOrWhiteSpace(input.LotNr) ? null : input.LotNr.Trim();
                    spool.Location = string.IsNullOrWhiteSpace(input.Location) ? null : input.Location.Trim();

                    // The server's copy no longer has these values, so the spool reads as not sent until it is sent again.
                    if (spool.EditInput != before)
                    {
                        spool.SentTo = null;
                        spool.SentAt = null;
                    }
                }
            }

            if (spool == null) return false;

            await SaveAndNotifyAsync();

            var name = string.Join(" ", new[] { spool.Info.DetailedFilamentType ?? spool.Info.FilamentType, spool.Name }.Where(x => !string.IsNullOrEmpty(x)));

            await Log(LogLevel.Success, $"Spool {name} updated");
            ShowMessage(false, string.Format(SharedResources.Manager_LocalSpoolUpdated, name));

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

            var tag = spool.Tag;

            // A spool added by hand has no tag data to match by — it is the filament it was picked as, or unknown.
            if (spool.IsManual)
                return SpoolDisplayInfo.From(tag, spool.FilamentId == null ? null : bambuLabFilaments.FirstOrDefault(x => x.Id == spool.FilamentId), Overrides);

            var candidates = await ExternalFilamentMatcher.FindExternalFilament(bambuLabFilaments, tag, Overrides);

            return SpoolDisplayInfo.From(tag, candidates.Count == 1 ? candidates[0] : null, Overrides);
        }

        /// <summary>
        /// Load (or drop) the stored inventory to match <see cref="StoreSpoolsLocally"/> without a full <see cref="BaseManager.Init"/>,
        /// for when an inventory server is the selected backend: its spools stay listed, editable and sendable, while this manager
        /// doesn't report itself ready to read tags.
        /// </summary>
        public Task LoadStoreAsync() => SyncLocalStoreAsync();

        /// <summary>
        /// Send stored spools to an inventory server, one after the other (see <see cref="ISpoolUploadTarget.UploadSpoolAsync"/>). A spool
        /// the server took is marked with <see cref="LocalSpool.SentTo"/> and saved right away, so an interrupted batch keeps what was
        /// sent; it stays on the device — sending it again updates the server's spool rather than adding another. A failed spool is
        /// left as it was. Cancelling stops before the next spool.
        /// </summary>
        /// <param name="keys">The <see cref="LocalSpool.Key"/>s to send; keys no longer stored are skipped.</param>
        public async Task<SpoolUploadSummary> SendSpoolsAsync(ISpoolUploadTarget target, IEnumerable<string> keys, IProgress<SpoolUploadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            List<LocalSpool> spools;

            lock (spoolLock)
            {
                if (storedSpools == null) throw new InvalidOperationException("Spools can only be sent while they are stored locally");

                var stored = storedSpools;
                spools = keys.Distinct().Select(key => stored.FirstOrDefault(x => x.Key == key)).OfType<LocalSpool>().ToList();
            }

            var backendName = target.Backend.DisplayName();
            var results = new List<SpoolUploadResult>();

            await Log(LogLevel.Information, $"Sending {spools.Count} stored spools to {backendName}");

            foreach (var spool in spools)
            {
                if (cancellationToken.IsCancellationRequested) break;

                progress?.Report(new SpoolUploadProgress(results.Count, spools.Count, spool));

                SpoolEditInput sentValues;

                lock (spoolLock) sentValues = spool.EditInput;

                var result = await target.UploadSpoolAsync(spool);
                results.Add(result);

                if (result.Status == SpoolUploadStatus.Failed)
                {
                    // Information, not Warning: the cause (no filament match, a rejected request) was already logged by the scan path.
                    await Log(LogLevel.Information, $"Spool {spool.Title} ({spool.Key}) not sent: {result.Message}");
                    continue;
                }

                lock (spoolLock)
                {
                    // Edited while it was being sent: the server may not have the new values, so it stays unsent.
                    if (spool.EditInput == sentValues)
                    {
                        spool.SentTo = target.Backend;
                        spool.SentAt = DateTime.Now;
                    }
                }

                await SaveAndNotifyAsync();

                await Log(LogLevel.Information, $"Spool {spool.Title} ({spool.Key}) {(result.Status == SpoolUploadStatus.Created ? "added to" : "updated in")} {backendName}{(result.KeptServerWeight == null ? "" : $", kept server weight {result.KeptServerWeight:0.#} g")}{(result.Message == null ? "" : $": {result.Message}")}");
            }

            progress?.Report(new SpoolUploadProgress(results.Count, spools.Count, null));

            var summary = new SpoolUploadSummary(target.Backend, results, spools.Count, results.Count < spools.Count);

            await Log(summary.Failed > 0 || summary.Cancelled ? LogLevel.Information : LogLevel.Success, summary.Text);

            return summary;
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
        private async Task<string?> KeepSpoolAsync(ISpoolTag tag, ExternalFilament? matched, DateTime? buyDate, decimal? price, string? lotNr, string? location)
        {
            // The store keeps every format in the Bambu tag's shape; a third-party tag keys by its tag uid (no tray uid).
            var info = tag switch
            {
                BambuFilamentInfo bambu => bambu,
                SpoolTag thirdParty => thirdParty.ToFilamentInfo(),
                _ => throw new NotSupportedException($"Unsupported spool tag {tag.GetType().Name}")
            };

            var brand = tag.Format == SpoolTagFormat.BambuLab ? null : tag.Brand;
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
                        Format = tag.Format,
                        Brand = brand,
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
                existing.Format = tag.Format;
                existing.Brand = brand;
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
                ShowMessage(true, SharedResources.Manager_LocalStoreReadFailed);
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
                    ShowMessage(true, SharedResources.Manager_LocalStoreSaveFailed);
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
