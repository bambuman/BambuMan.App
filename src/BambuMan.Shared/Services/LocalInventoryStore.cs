using BambuMan.Shared.Models;

namespace BambuMan.Shared.Services
{
    /// <summary>
    /// Persists the no-backend mode's spools to a json file on the device, for when "Store spools locally" is on.
    /// Nothing here leaves the device unless the user exports it or sends stored spools to their own Spoolman / Bambuddy server
    /// (<see cref="Managers.NoBackendManager.SendSpoolsAsync"/>).
    /// </summary>
    public interface ILocalInventoryStore
    {
        /// <summary>Where the file lives. Set by the host — the shared library can't know the platform's app data path.</summary>
        string? Directory { get; set; }

        /// <summary>
        /// The stored spools, or an empty list when nothing was stored yet. Throws when the file exists but can't be
        /// read, so a caller never mistakes a damaged inventory for an empty one and saves over it.
        /// </summary>
        Task<List<LocalSpool>> LoadAsync();

        /// <summary>Replace the stored spools. Written to a temp file first, so an interrupted save never truncates the inventory.</summary>
        Task SaveAsync(IReadOnlyCollection<LocalSpool> spools);
    }

    public class LocalInventoryStore : ILocalInventoryStore
    {
        private const string FileName = "local-inventory.json";
        private const int CurrentVersion = 1;

        private readonly JsonFileStore<LocalInventoryFile> file = new(FileName);

        public string? Directory
        {
            get => file.Directory;
            set => file.Directory = value;
        }

        public async Task<List<LocalSpool>> LoadAsync() => (await file.ReadAsync())?.Spools ?? [];

        public Task SaveAsync(IReadOnlyCollection<LocalSpool> spools) => file.WriteAsync(new LocalInventoryFile { Version = CurrentVersion, Spools = spools.ToList() });

        /// <summary>On-disk shape. Versioned so a later format change can migrate rather than guess.</summary>
        private class LocalInventoryFile
        {
            public int Version { get; set; }

            public List<LocalSpool> Spools { get; set; } = [];
        }
    }
}
