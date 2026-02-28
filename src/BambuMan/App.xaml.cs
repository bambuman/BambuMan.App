namespace BambuMan
{
    public partial class App
    {
        private Window? mainWindow;

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
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (mainWindow == null) return;
                if (!IsActivityAlive()) return;

                mainWindow.Page = new AppShell();
            });
        }

        private void OnWindowStopped(object? sender, EventArgs e)
        {
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
            // Recreate AppShell to restore font image sources with a valid Activity context
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (mainWindow == null) return;
                if (!IsActivityAlive()) return;

                mainWindow.Page = new AppShell();
            });
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
