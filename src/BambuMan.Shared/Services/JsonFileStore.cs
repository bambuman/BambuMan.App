using Newtonsoft.Json;

namespace BambuMan.Shared.Services
{
    /// <summary>
    /// One json file in an app data directory, read whole and replaced whole. Shared by the stores that keep data on
    /// the device (<see cref="LocalInventoryStore"/>, <see cref="PendingScanQueue"/>). Nothing here ever leaves the device.
    /// </summary>
    public class JsonFileStore<T>(string fileName) where T : class
    {
        private readonly SemaphoreSlim fileLock = new(1, 1);

        /// <summary>Where the file lives. Set by the host — the shared library can't know the platform's app data path.</summary>
        public string? Directory { get; set; }

        public string FilePath => string.IsNullOrEmpty(Directory)
            ? throw new InvalidOperationException($"{nameof(Directory)} for {fileName} is not set")
            : Path.Combine(Directory, fileName);

        /// <summary>
        /// The stored value, or null when nothing was stored yet. Throws when the file exists but can't be read, so a
        /// caller never mistakes damaged data for no data and saves over it.
        /// </summary>
        public async Task<T?> ReadAsync()
        {
            var path = FilePath;

            await fileLock.WaitAsync();

            try
            {
                if (!File.Exists(path)) return null;

                return JsonConvert.DeserializeObject<T>(await File.ReadAllTextAsync(path))
                    ?? throw new InvalidDataException($"{path} is empty");
            }
            finally
            {
                fileLock.Release();
            }
        }

        /// <summary>Replace the stored value. Written to a temp file first, so an interrupted save never truncates the file.</summary>
        public async Task WriteAsync(T value)
        {
            var path = FilePath;
            var tempPath = path + ".tmp";
            var json = JsonConvert.SerializeObject(value, Formatting.Indented);

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
    }
}
