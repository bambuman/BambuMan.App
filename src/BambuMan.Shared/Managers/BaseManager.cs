using BambuMan.Shared.Enums;
using BambuMan.Shared.Matcher;
using BambuMan.Shared.Models;
using BambuMan.Shared.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using LogLevel = BambuMan.Shared.Enums.LogLevel;

namespace BambuMan.Shared.Managers
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
        public delegate void SpoolInfoReadEventHandler(SpoolDisplayInfo info);
        public delegate void LocationsLoadedEventHandler();

        public event StatusChangedEventHandler? OnStatusChanged;
        public event ShowMessageEventHandler? OnShowMessage;
        public event LogMessageEventHandler? OnLogMessage;
        public event PlayErrorToneEventHandler? OnPlayErrorTone;
        public event SpoolFoundEventHandler? OnSpoolFound;

        /// <summary>Raised instead of an edit form by read-only backends — see <see cref="IsReadOnly"/>.</summary>
        public event SpoolInfoReadEventHandler? OnSpoolInfoRead;

        public event LocationsLoadedEventHandler? OnLocationsLoaded;

        /// <summary>Raise <see cref="OnShowMessage"/> from a subclass (base events can only be invoked here). While a stored spool is sent, an error becomes that spool's result instead.</summary>
        protected void ShowMessage(bool isError, string message)
        {
            if (upload.Value is { } capture)
            {
                if (isError) capture.Error ??= message;
                return;
            }

            OnShowMessage?.Invoke(isError, message);
        }

        /// <summary>Raise <see cref="OnPlayErrorTone"/> from a subclass. Silent while stored spools are sent — nobody is scanning.</summary>
        protected void PlayErrorTone()
        {
            if (upload.Value == null) OnPlayErrorTone?.Invoke();
        }

        /// <summary>
        /// Raise <see cref="OnSpoolFound"/> from a subclass. Pass the scanned <paramref name="info"/> so consumers can read raw tag fields
        /// without extending <see cref="SpoolFound"/>, and whether the server's spool was just <paramref name="created"/> or already there.
        /// While a stored spool is sent, the spool is kept for <see cref="UploadSpoolAsync"/> instead, so the scan page doesn't open an
        /// edit card for every one.
        /// </summary>
        protected void RaiseSpoolFound(SpoolFound found, BambuFilamentInfo info, bool created = false)
        {
            if (upload.Value is { } capture)
            {
                capture.Found = found;
                capture.Created = created;
                return;
            }

            OnSpoolFound?.Invoke(found, info);
        }

        /// <summary>Raise <see cref="OnSpoolInfoRead"/> from a subclass.</summary>
        protected void RaiseSpoolInfoRead(SpoolDisplayInfo info)
        {
            if (upload.Value == null) OnSpoolInfoRead?.Invoke(info);
        }

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

        /// <summary>
        /// Per-SKU filament match overrides, supplied by the host. Held as the service rather than a copied
        /// set so a refresh that lands after startup is picked up on the next scan.
        /// </summary>
        public IFilamentOverrideService? FilamentOverrides { get; set; }

        /// <summary>The override set to match against right now — the compiled-in set when the host supplied nothing.</summary>
        protected FilamentOverrideSet Overrides => FilamentOverrides?.Current ?? FilamentMatchOverrides.Internal;

        public bool IsHealth { get; set; }

        /// <summary>
        /// A backend that neither talks to a server nor persists edits. <see cref="Init"/> skips the url,
        /// network and health gates for these, and the ui shows a scanned tag read-only instead of the
        /// spool edit form.
        /// </summary>
        public virtual bool IsReadOnly => false;

        /// <summary>
        /// Whether a read spool can be edited and saved (the ui shows the edit form rather than the read-only card).
        /// Every server backend can; a backend without a server can only when it keeps spools somewhere.
        /// </summary>
        public virtual bool CanEditSpools => !IsReadOnly;

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

            // Nothing to connect to: load whatever local data the backend needs and go straight to ready.
            // No health timer either — there is nothing to poll.
            if (IsReadOnly)
            {
                if (await LoadInitialDataAsync())
                {
                    isInitialized = true;
                    IsHealth = true;
                    await LogAndSetStatus(ManagerStatusType.Ready, LogLevel.Success, "Ready to read tags");
                    await OnReady();
                }
                else await LogAndSetStatus(ManagerStatusType.Error, LogLevel.Error, "Could not load the filament catalog");

                return;
            }

            if (string.IsNullOrEmpty(ApiUrl))
            {
                await LogAndSetStatus(ManagerStatusType.ApiUrlMissing, LogLevel.Information, "Api url not set");
                return;
            }

            // Urls stored before settings validation required a scheme (e.g. "nas.example.com:7912") can't be called.
            if (!ApiUrl.IsHttpUrl())
            {
                await LogAndSetStatus(ManagerStatusType.CantConnectToApi, LogLevel.Warning, $"Invalid {BackendName} URL, it must start with http:// or https://. Please check the URL in Settings.");
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

        #region Sending stored spools

        /// <summary>
        /// What the scan path reported for the spool <see cref="UploadSpoolAsync"/> is sending. An <see cref="AsyncLocal{T}"/>, so only
        /// that call's own flow is captured: a health check running meanwhile still reaches the ui as usual.
        /// </summary>
        private readonly AsyncLocal<UploadCapture?> upload = new();

        private sealed class UploadCapture
        {
            public SpoolFound? Found { get; set; }

            public bool Created { get; set; }

            public string? Error { get; set; }

            public string? Warning { get; set; }
        }

        /// <summary>
        /// Send a spool kept on the device (see <see cref="NoBackendManager"/>) to this backend, through the same path a scan of its tag
        /// takes: the filament is matched, and the server's spool is looked up by tray uid / tag and only created when it isn't there.
        /// A new spool is created with the stored spool's price, location, buy date and lot nr; then every field the stored spool has a
        /// value for — its weight too — is written to the server's spool the way saving the edit card would (see
        /// <see cref="LocalSpool.ToEditInput"/>). Fields the backend doesn't model are ignored, as they are for the edit card. The one
        /// exception is the weight of a spool the server already had: it is only written when it doesn't leave more filament on the
        /// spool than the server has, since the server may have tracked filament used since the spool was stored.
        /// <para>
        /// Nothing reaches the scan page while this runs — no edit card, message, error tone or status change; the result says what
        /// would have. Afterwards the sent spool is the one <see cref="UpdateCurrentSpoolAsync"/> writes to, so the caller closes any
        /// open edit card first and doesn't run a scan alongside.
        /// </para>
        /// </summary>
        public async Task<SpoolUploadResult> UploadSpoolAsync(LocalSpool spool)
        {
            if (IsReadOnly) return new(spool, SpoolUploadStatus.Failed, $"Spools can't be sent to {Backend.DisplayName()}");
            if (!IsInitialized) return new(spool, SpoolUploadStatus.Failed, $"Not connected to {BackendName}");

            var capture = new UploadCapture();
            upload.Value = capture;

            try
            {
                await InventorySpool(spool.Info, spool.BuyDate, spool.Price, spool.LotNr, spool.Location);

                if (capture.Found == null) return new(spool, SpoolUploadStatus.Failed, capture.Error ?? capture.Warning ?? $"Not added to {BackendName}, see logs");

                var found = capture.Found;
                var created = capture.Created;
                var input = spool.ToEditInput(found);
                string? weightKept = null;

                // The server may have tracked filament used since this spool was stored (prints, a later scan and weigh), so a
                // stale stored weight must not hand that filament back: a spool the server already had keeps its own weight
                // when it has less filament left than stored here.
                if (!created && RemainingWeight(input) > RemainingWeight(found.ToEditInput()))
                {
                    input = input with { Weight = found.Weight, EmptyWeight = found.EmptyWeight };
                    weightKept = $"Kept the weight {BackendName} has, it has less filament left than stored here";

                    await Log(LogLevel.Information, $"Spool '{spool.Key}': {weightKept} ({found.Weight:0.#}g, stored {spool.Weight:0.#}g)");
                }

                // A spool just created from the stored values, or one the server already has the same values for, needs no update.
                if (input != found.ToEditInput())
                {
                    capture.Error = null;

                    await UpdateCurrentSpoolAsync(input);

                    if (capture.Error != null) return new(spool, SpoolUploadStatus.Failed, created ? $"Added, but its weight and details weren't saved: {capture.Error}" : capture.Error);
                }

                var message = string.Join(". ", new[] { capture.Warning, weightKept }.OfType<string>());

                return new(spool, created ? SpoolUploadStatus.Created : SpoolUploadStatus.Updated, message.Length == 0 ? null : message);
            }
            catch (Exception e)
            {
                await Log(LogLevel.Error, $"Sending spool '{spool.Key}' to {BackendName} failed: {e.Message}", e);
                return new(spool, SpoolUploadStatus.Failed, e.Message);
            }
            finally
            {
                upload.Value = null;
            }

            // Filament left on the spool; an unknown empty weight counts as 0, as the managers' updates do.
            static decimal? RemainingWeight(SpoolEditInput input) => input.Weight - (input.EmptyWeight ?? 0);
        }

        #endregion

        #region Health check timer

        /// <summary>
        /// Stop polling the backend. Called on managers that are not the selected backend so an
        /// inactive backend never keeps hitting its server. The next <see cref="Init"/> restarts it.
        /// </summary>
        public void StopHealthChecks() => healthCheckTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

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
            // A stored spool being sent isn't retried: its result says why it failed, and it can simply be sent again.
            if (upload.Value is { } capture) capture.Error ??= ex is TaskCanceledException ? $"{operation} timed out" : $"{operation} failed: {ex.Message}";
            else
            {
                var message = ex is TaskCanceledException
                    ? $"{operation} timed out. Will retry automatically."
                    : $"{operation} failed. Will retry automatically.";

                OnShowMessage?.Invoke(true, message);
            }

            await Log(LogLevel.Information, $"{operation}: {ex.Message}");
        }

        #endregion

        #region Logging and Status

        protected async Task LogAndSetStatus(ManagerStatusType status, LogLevel level, string message, Exception? exception = null)
        {
            await Log(level, message, exception);

            // A stored spool the backend can't take is that spool's result, not the backend's status: the next scan still reads Ready.
            if (upload.Value is { } capture)
            {
                if (level is LogLevel.Error or LogLevel.Critical) capture.Error ??= message;
                else if (level == LogLevel.Warning) capture.Warning ??= message;

                return;
            }

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
