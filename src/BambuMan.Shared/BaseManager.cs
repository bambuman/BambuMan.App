using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using BambuMan.Shared.Enums;
using BambuMan.Shared.Models;
using LogLevel = BambuMan.Shared.Enums.LogLevel;

namespace BambuMan.Shared
{
    /// <summary>
    /// Shared infrastructure for inventory backend managers (SpoolMan, Bambuddy):
    /// connection lifecycle, health-check timer, status/logging, network-error handling.
    /// Backend-specific behaviour is supplied through the abstract/virtual hooks.
    /// </summary>
    public abstract class BaseManager(ILogger? logger)
    {
        public delegate void StatusChangedEventHandler();
        public delegate void ShowMessageEventHandler(bool isError, string message);
        public delegate void LogMessageEventHandler(LogLevel level, string message);
        public delegate void PlayErrorToneEventHandler();
        public delegate void SpoolFoundEventHandler(SpoolFound found, BambuFilamentInfo info);
        public delegate void LocationsLoadedEventHandler();

        public event StatusChangedEventHandler? OnStatusChanged;
        public event ShowMessageEventHandler? OnShowMessage;
        public event LogMessageEventHandler? OnLogMessage;
        public event PlayErrorToneEventHandler? OnPlayErrorTone;
        public event SpoolFoundEventHandler? OnSpoolFound;
        public event LocationsLoadedEventHandler? OnLocationsLoaded;

        /// <summary>Raise <see cref="OnShowMessage"/> from a subclass (base events can only be invoked here).</summary>
        protected void ShowMessage(bool isError, string message) => OnShowMessage?.Invoke(isError, message);

        /// <summary>Raise <see cref="OnPlayErrorTone"/> from a subclass.</summary>
        protected void PlayErrorTone() => OnPlayErrorTone?.Invoke();

        /// <summary>Raise <see cref="OnSpoolFound"/> from a subclass. Pass the scanned <paramref name="info"/> so consumers can read raw tag fields without extending <see cref="SpoolFound"/>.</summary>
        protected void RaiseSpoolFound(SpoolFound found, BambuFilamentInfo info) => OnSpoolFound?.Invoke(found, info);

        /// <summary>Raise <see cref="OnLocationsLoaded"/> from a subclass.</summary>
        protected void RaiseLocationsLoaded() => OnLocationsLoaded?.Invoke();

        /// <summary>Known storage locations for the location autocomplete (backend-specific; empty when unsupported).</summary>
        public string[] ExistingLocations { get; protected set; } = [];

        private string? initializedApiUrl;
        private bool isInitialized;
        private Timer? healthCheckTimer;
        private bool healthCheckInProgress;

        /// <summary>The DI host wrapping the generated API client, built by <see cref="CreateApiHost"/>.</summary>
        protected IHost? ApiHost { get; set; }

        public string? AppVersion { get; set; }

        public bool ShowLogs { get; set; }

        public string? ApiUrl { get; set; }

        public bool UnknownFilamentEnabled { get; set; } = false;

        public bool OverrideLocationOnRead { get; set; }

        public Func<bool>? HasNetworkAccess { get; set; }

        public bool IsHealth { get; set; }

        public bool IsInitialized => isInitialized;

        public ManagerStatusType Status { get; private set; } = ManagerStatusType.Initializing;

        /// <summary>Force the next <see cref="Init"/> to fully re-initialize (rebuild the API host), e.g. after credentials change.</summary>
        public void ResetInitialization()
        {
            isInitialized = false;
            initializedApiUrl = null;
            ResetInitState();
        }

        public async Task Init()
        {
            if (AppVersion != null) await Log(LogLevel.Information, $"App version {AppVersion}");

            if (string.IsNullOrEmpty(ApiUrl))
            {
                await LogAndSetStatus(ManagerStatusType.ApiUrlMissing, LogLevel.Information, "Api url not set");
                return;
            }

            // Fast path: already initialized with the same URL — just verify health
            if (isInitialized && initializedApiUrl == ApiUrl)
            {
                if (HasNetworkAccess?.Invoke() == false)
                {
                    await LogAndSetStatus(ManagerStatusType.CantConnectToApi, LogLevel.Warning, "No network connection. Please check your connection.");
                    StartHealthCheckTimer(healthy: false);
                    return;
                }

                try
                {
                    if (await CheckHealthAsync())
                        await LogAndSetStatus(ManagerStatusType.Ready, LogLevel.Success, "Ready to inventory filament");
                    else
                    {
                        await LogAndSetStatus(ManagerStatusType.CantConnectToApi, LogLevel.Warning, $"Can't reach {BackendName} server. Will retry automatically.");
                        StartHealthCheckTimer(healthy: false);
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    await LogAndSetStatus(ManagerStatusType.CantConnectToApi, LogLevel.Warning, $"Can't reach {BackendName} server. Will retry automatically.");
                    await Log(LogLevel.Warning, $"Health check failed: {ex.Message}");
                    StartHealthCheckTimer(healthy: false);
                }
                return;
            }

            // URL changed — reset init flags
            if (initializedApiUrl != null && initializedApiUrl != ApiUrl) ResetInitState();

            // Full initialization path
            await LogAndSetStatus(ManagerStatusType.Initializing, LogLevel.Information, "Initializing ...");

            var apiUrl = NormalizeApiUrl(ApiUrl);

            ApiHost = CreateApiHost(apiUrl);

            if (HasNetworkAccess?.Invoke() == false)
            {
                await LogAndSetStatus(ManagerStatusType.CantConnectToApi, LogLevel.Warning, "No network connection. Please check your connection.");
                StartHealthCheckTimer(healthy: false);
                return;
            }

            try
            {
                // Health check with retry (up to 3 attempts, 3s delay between)
                var healthOk = false;
                for (var attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        healthOk = await CheckHealthAsync();
                        if (healthOk) break;
                    }
                    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                    {
                        await Log(LogLevel.Warning, $"Connecting to {BackendName} (attempt {attempt}/3) ... {ex.Message}");
                    }

                    if (attempt < 3) await Task.Delay(3000);
                }

                if (!healthOk)
                {
                    await LogAndSetStatus(ManagerStatusType.CantConnectToApi, LogLevel.Warning, $"Can't connect to {BackendName} api, check if url is correct!");
                    StartHealthCheckTimer(healthy: false);
                    return;
                }

                await LogAndSetStatus(ManagerStatusType.ApiConnected, LogLevel.Success, $"Api connected, {BackendName} healthy");

                var ready = await LoadInitialDataAsync();

                if (ready)
                {
                    initializedApiUrl = ApiUrl;
                    isInitialized = true;

                    await LogAndSetStatus(ManagerStatusType.Ready, LogLevel.Success, "Ready to inventory filament");

                    StartHealthCheckTimer();

                    await OnReady();
                }
                else
                {
                    StartHealthCheckTimer(healthy: false);
                }
            }
            catch (UriFormatException)
            {
                await LogAndSetStatus(ManagerStatusType.CantConnectToApi, LogLevel.Warning, $"Invalid {BackendName} URL. Please check the URL in Settings.");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                await LogAndSetStatus(ManagerStatusType.CantConnectToApi, LogLevel.Warning, $"Can't reach {BackendName} server. Will retry automatically.");
                StartHealthCheckTimer(healthy: false);
            }
            catch (Exception e)
            {
                await LogAndSetStatus(ManagerStatusType.Error, LogLevel.Error, e.ToString());
                logger?.LogError(e, "Error connecting to api");
            }
        }

        #region Backend-specific hooks

        /// <summary>Which backend this manager talks to. Also the source of the name used in status/log messages.</summary>
        public abstract InventoryBackend Backend { get; }

        /// <summary>Which optional edit-panel fields this backend supports (drives the single adaptive edit panel).</summary>
        public abstract SpoolEditFields EditFields { get; }

        /// <summary>Human-readable backend name used in status/log messages.</summary>
        protected string BackendName => Backend.ToString();

        /// <summary>Build the DI host wrapping the generated API client (base address, auth, retry).</summary>
        protected abstract IHost CreateApiHost(string normalizedApiUrl);

        /// <summary>
        /// Quiet health probe: set <see cref="IsHealth"/> and return it. MUST NOT change <see cref="Status"/>
        /// (it runs on every health-check tick); the base class owns all status transitions. May log diagnostics.
        /// </summary>
        protected abstract Task<bool> CheckHealthAsync();

        /// <summary>Post-health one-time data load (defaults, catalog, spool cache). Returns true when ready to inventory.</summary>
        protected abstract Task<bool> LoadInitialDataAsync();

        /// <summary>Normalize the configured URL before building the host. Base trims a trailing slash.</summary>
        protected virtual string NormalizeApiUrl(string apiUrl) => apiUrl.EndsWith("/") ? apiUrl[..^1] : apiUrl;

        /// <summary>Reset subclass init flags when the URL changes. Base no-op.</summary>
        protected virtual void ResetInitState() { }

        /// <summary>Fire-and-forget background work after the manager reaches Ready. Base no-op.</summary>
        protected virtual Task OnReady() => Task.CompletedTask;

        #endregion

        #region Inventory operations

        /// <summary>Inventory a scanned spool: match, create-or-find, link, then fire <see cref="OnSpoolFound"/>. Returns false on no/ambiguous filament match.</summary>
        public abstract Task<bool> InventorySpool(BambuFilamentInfo info, DateTime? buyDate, decimal? price, string? lotNr, string? location);

        /// <summary>Persist edits to the spool most recently surfaced via <see cref="OnSpoolFound"/>. Fields the backend doesn't model are ignored.</summary>
        public abstract Task UpdateCurrentSpoolAsync(SpoolEditInput input);

        /// <summary>Refresh known storage locations (backend-specific; base no-op).</summary>
        public virtual Task RefreshLocationsAsync() => Task.CompletedTask;

        #endregion

        #region Health check timer

        private void StartHealthCheckTimer(bool healthy = true)
        {
            var interval = healthy ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(1);

            if (healthCheckTimer != null)
            {
                healthCheckTimer.Change(interval, interval);
                return;
            }

            healthCheckTimer = new Timer(async void (_) =>
            {
                try
                {
                    await PerformHealthCheck();
                }
                catch (Exception)
                {
                    // ignored
                }
            }, null, interval, interval);
        }

        private async Task PerformHealthCheck()
        {
            if (healthCheckInProgress || ApiHost == null) return;

            if (HasNetworkAccess?.Invoke() == false)
            {
                if (IsHealth)
                {
                    IsHealth = false;
                    await LogAndSetStatus(ManagerStatusType.CantConnectToApi, LogLevel.Warning, "No network connection. Please check your connection.");
                    StartHealthCheckTimer(healthy: false);
                }
                return;
            }
            healthCheckInProgress = true;

            try
            {
                var wasHealthy = IsHealth;
                var nowHealthy = await CheckHealthAsync();

                if (!wasHealthy && nowHealthy)
                {
                    if (!isInitialized)
                        await Init();
                    else
                    {
                        await LogAndSetStatus(ManagerStatusType.Ready, LogLevel.Success, $"Reconnected to {BackendName}");
                        StartHealthCheckTimer();
                    }
                }
                else if (wasHealthy != nowHealthy)
                {
                    await LogAndSetStatus(ManagerStatusType.CantConnectToApi, LogLevel.Warning, $"Lost connection to {BackendName}. Retrying ...");
                    StartHealthCheckTimer(healthy: false);
                }
            }
            catch
            {
                if (IsHealth)
                {
                    IsHealth = false;
                    await LogAndSetStatus(ManagerStatusType.CantConnectToApi, LogLevel.Warning, $"Lost connection to {BackendName}. Retrying ...");
                    StartHealthCheckTimer(healthy: false);
                }
            }
            finally
            {
                healthCheckInProgress = false;
            }
        }

        #endregion

        #region Error handling

        protected async Task HandleNetworkError(Exception ex, string operation)
        {
            var message = ex is TaskCanceledException
                ? $"{operation} timed out. Will retry automatically."
                : $"{operation} failed. Will retry automatically.";

            OnShowMessage?.Invoke(true, message);
            await Log(LogLevel.Information, $"{operation}: {ex.Message}");
        }

        #endregion

        #region Logging and Status

        protected async Task LogAndSetStatus(ManagerStatusType status, LogLevel level, string message, Exception? exception = null)
        {
            await Log(level, message, exception);
            await SetStatus(status);
        }

        private Task SetStatus(ManagerStatusType status)
        {
            Status = status;
            OnStatusChanged?.Invoke();

            return Task.CompletedTask;
        }

        protected Task Log(LogLevel level, string message, Exception? ex = null)
        {
            if (ShowLogs) OnLogMessage?.Invoke(level, message);

            switch (level)
            {
                case LogLevel.Trace:
                    logger?.LogTrace(message);
                    break;
                case LogLevel.Debug:
                    logger?.LogDebug(message);
                    break;
                case LogLevel.Information:
                case LogLevel.Success:
                    logger?.LogInformation(message);
                    break;
                case LogLevel.Warning:
                    logger?.LogWarning(ex, message);
                    break;
                case LogLevel.Error:
                    logger?.LogError(ex, message);
                    break;
                case LogLevel.Critical:
                    logger?.LogCritical(ex, message);
                    break;
                case LogLevel.None:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(level), level, null);
            }

            return Task.CompletedTask;
        }

        #endregion
    }
}
