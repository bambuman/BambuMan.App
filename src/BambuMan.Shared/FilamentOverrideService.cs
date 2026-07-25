using BambuMan.Shared.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using LogLevel = BambuMan.Shared.Enums.LogLevel;

namespace BambuMan.Shared
{
    /// <summary>
    /// Keeps the active <see cref="FilamentOverrideSet"/> up to date: the set compiled into this build, or a
    /// newer one fetched from the BambuMan api and cached on disk so it survives an offline launch.
    /// </summary>
    public interface IFilamentOverrideService
    {
        /// <summary>Where the cache file lives. Set by the host — the shared library can't know the platform's app data path.</summary>
        string? CacheDirectory { get; set; }

        /// <summary>Optional hook so the host can surface updates in its own log view.</summary>
        Action<LogLevel, string>? LogAction { get; set; }

        /// <summary>The set to match against. Never null; falls back to the compiled-in set.</summary>
        FilamentOverrideSet Current { get; }

        /// <summary>Read the cached set, if any. Safe to call before there is any network.</summary>
        void LoadCache();

        /// <summary>Ask the api whether it has a newer set, and adopt + cache it if so.</summary>
        Task RefreshAsync();
    }

    public class FilamentOverrideService(TagApiService tagApiService, ILogger<FilamentOverrideService>? logger = null) : IFilamentOverrideService
    {
        private const string CacheFileName = "filament-overrides.json";

        /// <summary>Web defaults, so the cache file is byte-comparable with what the api sends.</summary>
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public Action<LogLevel, string>? LogAction { get; set; }

        public string? CacheDirectory { get; set; }

        public FilamentOverrideSet Current { get; private set; } = FilamentMatchOverrides.Internal;

        private string? CacheFilePath => string.IsNullOrEmpty(CacheDirectory) ? null : Path.Combine(CacheDirectory, CacheFileName);

        public void LoadCache()
        {
            try
            {
                var path = CacheFilePath;

                if (path == null || !File.Exists(path)) return;

                var cached = JsonSerializer.Deserialize<FilamentOverrideSet>(File.ReadAllText(path), JsonOptions);

                // A cache older than the compiled-in set is simply stale — the normal state right after an
                // app update, since a released build already contains everything the api had served.
                if (cached == null || cached.Version <= FilamentMatchOverrides.CurrentVersion) return;

                Current = cached;
            }
            catch (Exception e)
            {
                logger?.LogWarning(e, "Error reading cached filament overrides");
            }
        }

        public async Task RefreshAsync()
        {
            var remote = await tagApiService.GetFilamentOverridesAsync(Current.Version);

            if (remote == null || remote.Version <= Current.Version) return;

            // Single reference swap — readers never observe a half-built set.
            Current = remote;

            Log(LogLevel.Information, $"Filament overrides updated: internal v{FilamentMatchOverrides.CurrentVersion}, active v{Current.Version} ({Current.Count} entries)");

            WriteCache(remote);
        }

        private void WriteCache(FilamentOverrideSet set)
        {
            try
            {
                var path = CacheFilePath;

                if (path == null) return;

                if (CacheDirectory != null) Directory.CreateDirectory(CacheDirectory);
                File.WriteAllText(path, JsonSerializer.Serialize(set, JsonOptions));
            }
            catch (Exception e)
            {
                // Non-fatal: the set is already active for this session, we just won't have it offline next launch.
                logger?.LogWarning(e, "Error writing cached filament overrides");
            }
        }

        private void Log(LogLevel level, string message)
        {
            LogAction?.Invoke(level, message);
            logger?.LogInformation(message);
        }
    }
}
