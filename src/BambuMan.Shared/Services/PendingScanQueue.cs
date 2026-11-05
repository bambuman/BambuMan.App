using BambuMan.Shared.Enums;
using BambuMan.Shared.Managers;
using BambuMan.Shared.Models;

namespace BambuMan.Shared.Services
{
    /// <summary>The outcome of sending queued scans: how many went through, how many the backend rejected, and how many still wait.</summary>
    public record PendingScanSendResult(int Sent, int Rejected, int Remaining, bool Unreachable);

    /// <summary>
    /// Scans an inventory backend couldn't be reached for, kept on the device until it can. Persisted to a json file in
    /// the app data directory, in scan order; nothing here ever leaves the device except through the backend the scan
    /// was made for. <see cref="SendAsync"/> sends them through <see cref="BaseManager.SendScanAsync"/> — the same path
    /// a live scan takes, so a queued re-scan of a known spool updates it instead of creating a duplicate.
    /// </summary>
    public interface IPendingScanQueue
    {
        /// <summary>Raised whenever the queue changes — a scan queued, sent, rejected or discarded.</summary>
        event Action? OnChanged;

        /// <summary>Where the file lives. Set by the host — the shared library can't know the platform's app data path.</summary>
        string? Directory { get; set; }

        /// <summary>The queued scans, oldest first.</summary>
        IReadOnlyList<PendingScan> Items { get; }

        /// <summary>True while <see cref="SendAsync"/> or <see cref="SendOneAsync"/> is running.</summary>
        bool IsSending { get; }

        /// <summary>How many scans wait for <paramref name="backend"/>.</summary>
        int CountFor(InventoryBackend backend);

        /// <summary>
        /// Read the queue from the device. Done once; later calls return straight away. Throws when the file exists but
        /// can't be read, and leaves the queue unloaded, so a damaged file is never saved over.
        /// </summary>
        Task LoadAsync();

        /// <summary>
        /// Queue a scan. A tag already waiting for the same backend is refreshed in place (keeping its turn) rather than
        /// queued twice. Returns the queued entry.
        /// </summary>
        Task<PendingScan> EnqueueAsync(PendingScan scan);

        Task RemoveAsync(Guid id);

        /// <summary>Discard every scan waiting for <paramref name="backend"/>, or every queued scan when null.</summary>
        Task ClearAsync(InventoryBackend? backend = null);

        /// <summary>
        /// Send the scans waiting for <paramref name="manager"/>'s backend, oldest first. Each one inventoried is removed;
        /// a rejected one is kept with its error. Stops at the first unreachable answer, leaving it and the rest queued.
        /// Scans rejected before are only sent again with <paramref name="retryRejected"/>. Does nothing while another
        /// send is running.
        /// </summary>
        Task<PendingScanSendResult> SendAsync(BaseManager manager, bool retryRejected = false);

        /// <summary>Send one queued scan, rejected before or not. Its backend must be <paramref name="manager"/>'s.</summary>
        Task<PendingScanSendResult> SendOneAsync(BaseManager manager, Guid id);
    }

    public class PendingScanQueue : IPendingScanQueue
    {
        private const string FileName = "pending-scans.json";
        private const int CurrentVersion = 1;

        private readonly JsonFileStore<PendingScanFile> file = new(FileName);

        // Every change and the save that follows it run under this, so the file is always written in change order.
        private readonly SemaphoreSlim gate = new(1, 1);

        // Replaced, never mutated, so Items can hand it out without a lock.
        private List<PendingScan>? items;

        private int sending;

        public event Action? OnChanged;

        public string? Directory
        {
            get => file.Directory;
            set => file.Directory = value;
        }

        public IReadOnlyList<PendingScan> Items => items ?? [];

        public bool IsSending => sending != 0;

        public int CountFor(InventoryBackend backend) => Items.Count(x => x.Backend == backend);

        public async Task LoadAsync()
        {
            if (items != null) return;

            await gate.WaitAsync();

            try
            {
                items ??= (await file.ReadAsync())?.Scans ?? [];
            }
            finally
            {
                gate.Release();
            }

            OnChanged?.Invoke();
        }

        public Task<PendingScan> EnqueueAsync(PendingScan scan) =>
            ChangeAsync(list =>
            {
                var key = scan.Key;
                var existing = key == null ? null : list.FirstOrDefault(x => x.Backend == scan.Backend && x.Key == key);

                if (existing == null)
                {
                    scan.QueuedAt ??= DateTime.Now;
                    list.Add(scan);
                    return scan;
                }

                // The newest read and defaults win; the turn in the queue stays.
                existing.Info = scan.Info;
                existing.BuyDate = scan.BuyDate;
                existing.Price = scan.Price;
                existing.LotNr = scan.LotNr;
                existing.Location = scan.Location;
                existing.LastError = null;
                return existing;
            });

        public Task RemoveAsync(Guid id) => ChangeAsync(list => list.RemoveAll(x => x.Id == id));

        public Task ClearAsync(InventoryBackend? backend = null) => ChangeAsync(list => list.RemoveAll(x => backend == null || x.Backend == backend));

        public Task<PendingScanSendResult> SendAsync(BaseManager manager, bool retryRejected = false) =>
            SendCoreAsync(manager, x => retryRejected || !x.IsRejected);

        public Task<PendingScanSendResult> SendOneAsync(BaseManager manager, Guid id) =>
            SendCoreAsync(manager, x => x.Id == id);

        private async Task<PendingScanSendResult> SendCoreAsync(BaseManager manager, Func<PendingScan, bool> filter)
        {
            if (Interlocked.CompareExchange(ref sending, 1, 0) != 0) return new PendingScanSendResult(0, 0, CountFor(manager.Backend), false);

            try
            {
                await LoadAsync();

                int sent = 0, rejected = 0;
                var unreachable = false;

                foreach (var scan in Items.Where(x => x.Backend == manager.Backend).Where(filter).ToList())
                {
                    var result = await manager.SendScanAsync(scan);

                    if (result.Status == ScanSendStatus.Unreachable)
                    {
                        unreachable = true;
                        break;
                    }

                    if (result.Status == ScanSendStatus.Inventoried)
                    {
                        await RemoveAsync(scan.Id);
                        sent++;
                        continue;
                    }

                    await ChangeAsync(list =>
                    {
                        var queued = list.FirstOrDefault(x => x.Id == scan.Id);
                        if (queued != null) queued.LastError = result.Error;
                        return queued;
                    });

                    rejected++;
                }

                return new PendingScanSendResult(sent, rejected, CountFor(manager.Backend), unreachable);
            }
            finally
            {
                Interlocked.Exchange(ref sending, 0);
            }
        }

        /// <summary>Apply a change to a copy of the queue, save it, then publish it and raise <see cref="OnChanged"/>.</summary>
        private async Task<TResult> ChangeAsync<TResult>(Func<List<PendingScan>, TResult> change)
        {
            await LoadAsync();

            TResult result;

            await gate.WaitAsync();

            try
            {
                var list = Items.ToList();

                result = change(list);

                await file.WriteAsync(new PendingScanFile { Version = CurrentVersion, Scans = list });

                items = list;
            }
            finally
            {
                gate.Release();
            }

            OnChanged?.Invoke();

            return result;
        }

        /// <summary>On-disk shape. Versioned so a later format change can migrate rather than guess.</summary>
        private class PendingScanFile
        {
            public int Version { get; set; }

            public List<PendingScan> Scans { get; set; } = [];
        }
    }
}
