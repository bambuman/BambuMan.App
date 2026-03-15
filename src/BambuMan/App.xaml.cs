namespace BambuMan
{
    public partial class App
    {
        private Window? mainWindow;
        private bool isRecreatingShell;
        private CancellationTokenSource? shellRecreationCts;

        public App()
        {
            InitializeComponent();

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
            // Glide from using a stale Activity context during reconstruction.
            mainWindow.Stopped += OnWindowStopped;
            mainWindow.Resumed += OnWindowResumed;

            return mainWindow;
        }

        private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e)
        {
            // Debounce: theme changes during resume are already handled by OnWindowResumed
            ScheduleShellRecreation();
        }

        private void OnWindowStopped(object? sender, EventArgs e)
        {
            // Cancel any pending shell recreation — the window is stopping, so recreating
            // the shell now would give Glide/Skia a stale Activity context (SIGSEGV).
            shellRecreationCts?.Cancel();

            // Clear font image sources so Glide has nothing to render if Android
            // destroys the Activity while backgrounded (dotnet/maui#12513)
            if (mainWindow?.Page is Shell shell)
            {
                foreach (var content in shell.Items
                             .SelectMany(item => item.Items)
                             .SelectMany(section => section.Items))
                    content.Icon = null;
            }
        }

        private void OnWindowResumed(object? sender, EventArgs e)
        {
            // Recreate AppShell to restore font image sources with a valid Activity context.
            // Debounced to prevent rapid config changes (orientation + density on foldable
            // devices) from causing concurrent shell reconstructions that SIGSEGV.
            ScheduleShellRecreation();
        }

        /// <summary>
        /// Debounces AppShell recreation to prevent SIGSEGV from concurrent native view teardown.
        /// Rapid configuration changes (orientation, density, theme) on Samsung foldable devices
        /// can trigger multiple recreation requests within milliseconds — only the last one wins.
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
                    }
                    finally
                    {
                        isRecreatingShell = false;
                    }
                });
            }
            catch (TaskCanceledException)
            {
                // Expected when a newer recreation request supersedes this one
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
