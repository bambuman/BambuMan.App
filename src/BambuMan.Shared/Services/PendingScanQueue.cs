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
        /// Whether <see cref="SendAsync"/> has something to do for <paramref name="manager"/> right now: it is ready and
        /// reachable, nothing is being sent, and scans not rejected before wait for its backend. Checked on every status
        /// change, so a reconnect sends the queue.
        /// </summary>
        bool HasScansToSend(BaseManager manager);

        /// <summary>
        /// Read the queue from the device. Done once; later calls return straight away. A file that can't be read is
        /// moved aside (kept, never saved over) and the queue starts empty; that throws an <see cref="InvalidDataException"/>
        /// naming the kept file, so the caller can report it.
        /// </summary>
        Task LoadAsync();

        /// <summary>
        /// Queue a scan. A tag already waiting for the same backend is replaced by the newer read (keeping its turn)
        /// rather than queued twice. Returns the queued entry.
        /// </summary>
        Task<PendingScan> EnqueueAsync(PendingScan scan);

        /// <summary>
        /// Send a scan just read the way <see cref="BaseManager.SendScanAsync"/> does, and queue it when the backend
        /// can't be reached. The result is the send's: <see cref="ScanSendStatus.Unreachable"/> means it was queued.
        /// </summary>
        Task<ScanSendResult> SendOrQueueAsync(BaseManager manager, PendingScan scan);

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

        // The list is replaced, never changed in place, so Items can hand it out without a lock.
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

        public bool HasScansToSend(BaseManager manager) =>
            manager is { Status: ManagerStatusType.Ready, CanSendScans: true } && !IsSending && Items.Any(x => x.Backend == manager.Backend && !x.IsRejected);

        public async Task LoadAsync()
        {
            if (items != null) return;

            string? keptAs = null;
            Exception? error = null;

            await gate.WaitAsync();

            try
            {
                if (items == null)
                {
                    try
                    {
                        items = (await file.ReadAsync())?.Scans ?? [];
                    }
                    catch (Exception e) when (e is Newtonsoft.Json.JsonException or InvalidDataException)
                    {
                        // Kept for a look by hand rather than lost; the queue must keep working for the next scan.
                        keptAs = file.FilePath.Replace(".json", $".damaged-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                        File.Move(file.FilePath, keptAs);
                        items = [];
                        error = e;
                    }
                }
            }
            finally
            {
                gate.Release();
            }

            OnChanged?.Invoke();

            if (error != null) throw new InvalidDataException($"The queued scans could not be read and were kept as {Path.GetFileName(keptAs)}", error);
        }

        public Task<PendingScan> EnqueueAsync(PendingScan scan) =>
            ChangeAsync(list =>
            {
                var key = scan.Key;
                var existing = key == null ? null : list.FirstOrDefault(x => x.Backend == scan.Backend && x.Key == key);

                // The newest read and defaults win; the turn in the queue stays. Replaced rather than updated, so a send
                // of the older entry that is in flight right now removes only that one, and the newer read still goes.
                scan.QueuedAt = existing?.QueuedAt ?? scan.QueuedAt ?? DateTime.Now;
                scan.LastError = null;

                if (existing == null) list.Add(scan);
                else list[list.IndexOf(existing)] = scan;

                return scan;
            });

        public async Task<ScanSendResult> SendOrQueueAsync(BaseManager manager, PendingScan scan)
        {
            var result = await manager.SendScanAsync(scan);

            if (result.Status == ScanSendStatus.Unreachable) await EnqueueAsync(scan);

            return result;
        }

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

                // Each scan is picked from the queue as it is now, not from a copy taken up front: one discarded while
                // the send runs is not sent any more, and one queued or read again meanwhile is sent in its turn.
                var tried = new HashSet<Guid>();

                while (Items.FirstOrDefault(x => x.Backend == manager.Backend && !tried.Contains(x.Id) && filter(x)) is { } scan)
                {
                    tried.Add(scan.Id);

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
