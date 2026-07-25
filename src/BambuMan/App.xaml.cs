namespace BambuMan
{
    public partial class App
    {
        private Window? mainWindow;
        private bool isRecreatingShell;
        private CancellationTokenSource? shellRecreationCts;
        private readonly Dictionary<ShellContent, ImageSource?> shellIcons = new();

        public App()
        {
            InitializeComponent();

            HorusStudio.Maui.MaterialDesignControls.MaterialDesignControls.InitializeComponents();

            Preferences.Default.Set("default_buy_date", $"{DateTime.Today:yyyy-MM-dd}");

            // UraniumUI controls cache theme colors at construction time (issue #660).
            // Recreating the AppShell forces all pages and controls to reconstruct
            // with the correct theme colors.
            RequestedThemeChanged += OnRequestedThemeChanged;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            mainWindow = new Window(new AppShell());

            // Workaround for Glide/FontImageSource crash when Android destroys and
            // recreates the Activity (dotnet/maui#12513). Clearing icons on stop prevents
            // Glide from using a stale Activity context during reconstruction; they are
            // put back on resume.
            mainWindow.Stopped += OnWindowStopped;
            mainWindow.Resumed += OnWindowResumed;

            return mainWindow;
        }

        private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e)
        {
            // UraniumUI controls cache theme colors at construction (issue #660), so the
            // shell has to be rebuilt for a theme switch to take effect. Rare and
            // user-initiated, unlike a resume — a rebuild here is affordable.
            ScheduleShellRecreation();
        }

        private void OnWindowStopped(object? sender, EventArgs e)
        {
            // Cancel any pending shell recreation — the window is stopping, so recreating
            // the shell now would give Glide/Skia a stale Activity context (SIGSEGV).
            shellRecreationCts?.Cancel();

            // Already cleared and cached: a second Stopped without an intervening Resume
            // would otherwise overwrite the cache with the nulls set below.
            if (shellIcons.Count > 0) return;

            // Clear font image sources so Glide has nothing to render if Android
            // destroys the Activity while backgrounded (dotnet/maui#12513)
            foreach (var content in EnumerateShellContents())
            {
                shellIcons[content] = content.Icon;
                content.Icon = null;
            }
        }

        private void OnWindowResumed(object? sender, EventArgs e)
        {
            // Restore the icons cleared on stop. FontImageSource carries no native state,
            // so reassigning the cached instance re-renders it against the current Activity.
            // Rebuilding the whole AppShell here instead tore down and reconstructed every
            // fragment on the main thread on every resume — an ANR and SIGSEGV source.
            if (shellIcons.Count == 0) return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (!IsActivityAlive()) return;

                foreach (var (content, icon) in shellIcons)
                    content.Icon = icon;

                shellIcons.Clear();
            });
        }

        /// <summary>
        /// Walks every <see cref="ShellContent"/> of the current shell, or nothing when the
        /// window has no shell attached.
        /// </summary>
        private IEnumerable<ShellContent> EnumerateShellContents()
        {
            if (mainWindow?.Page is not Shell shell) return [];

            return shell.Items
                .SelectMany(item => item.Items)
                .SelectMany(section => section.Items);
        }

        /// <summary>
        /// Debounces AppShell recreation to prevent SIGSEGV from concurrent native view teardown.
        /// Rapid theme changes on Samsung foldable devices can trigger multiple recreation
        /// requests within milliseconds — only the last one wins.
        /// </summary>
        private async void ScheduleShellRecreation()
        {
            try
            {
                // Cancel any previous pending recreation
                shellRecreationCts?.Cancel();
                var cts = new CancellationTokenSource();
                shellRecreationCts = cts;

                // Wait briefly so rapid config changes coalesce into a single recreation
                await Task.Delay(150, cts.Token);

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (cts.IsCancellationRequested) return;
                    if (mainWindow == null) return;
                    if (!IsActivityAlive()) return;
                    if (isRecreatingShell) return;

                    isRecreatingShell = true;
                    try
                    {
                        mainWindow.Page = new AppShell();

                        // Cached icons belong to the shell that was just replaced
                        shellIcons.Clear();
                    }
                    finally
                    {
                        isRecreatingShell = false;
                    }
                });
            }
            catch (OperationCanceledException)
            {
                // Expected when a newer recreation request supersedes this one
            }
            catch (Exception)
            {
                // Suppress to prevent crash in async void during shell recreation
            }
        }

        /// <summary>
        /// Checks whether the current Android Activity is alive and usable.
        /// Returns true on non-Android platforms.
        /// </summary>
        private static bool IsActivityAlive()
        {
#if ANDROID
            var activity = Platform.CurrentActivity;
            return activity is { IsDestroyed: false, IsFinishing: false };
#else
            return true;
#endif
        }
    }
}
