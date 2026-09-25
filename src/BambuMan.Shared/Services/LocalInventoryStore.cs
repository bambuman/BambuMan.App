using BambuMan.Shared.Models;
using Newtonsoft.Json;

namespace BambuMan.Shared.Services
{
    /// <summary>
    /// Persists the no-backend mode's spools to a json file on the device, for when "Store spools locally" is on.
    /// Nothing here ever leaves the device.
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

        private readonly SemaphoreSlim fileLock = new(1, 1);

        public string? Directory { get; set; }

        private string FilePath => string.IsNullOrEmpty(Directory)
            ? throw new InvalidOperationException($"{nameof(LocalInventoryStore)}.{nameof(Directory)} is not set")
            : Path.Combine(Directory, FileName);

        public async Task<List<LocalSpool>> LoadAsync()
        {
            var path = FilePath;

            await fileLock.WaitAsync();

            try
            {
                if (!File.Exists(path)) return [];

                var file = JsonConvert.DeserializeObject<LocalInventoryFile>(await File.ReadAllTextAsync(path))
                    ?? throw new InvalidDataException($"{path} is empty");

                return file.Spools;
            }
            finally
            {
                fileLock.Release();
            }
        }

        public async Task SaveAsync(IReadOnlyCollection<LocalSpool> spools)
        {
            var path = FilePath;
            var tempPath = path + ".tmp";
            var json = JsonConvert.SerializeObject(new LocalInventoryFile { Version = CurrentVersion, Spools = spools.ToList() }, Formatting.Indented);

            await fileLock.WaitAsync();

            try
            {
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                await File.WriteAllTextAsync(tempPath, json);
                File.Move(tempPath, path, overwrite: true);
            }
            finally
            {
                fileLock.Release();
            }
        }

        /// <summary>On-disk shape. Versioned so a later format change can migrate rather than guess.</summary>
        private class LocalInventoryFile
        {
            public int Version { get; set; }

            public List<LocalSpool> Spools { get; set; } = [];
        }
    }
}
