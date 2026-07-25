namespace BambuMan
{
    public partial class App
    {
        private Window? mainWindow;
        private readonly Dictionary<ShellContent, ImageSource?> shellIcons = new();

        public App()
        {
            InitializeComponent();

            HorusStudio.Maui.MaterialDesignControls.MaterialDesignControls.InitializeComponents();

            Preferences.Default.Set("default_buy_date", $"{DateTime.Today:yyyy-MM-dd}");
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

        private void OnWindowStopped(object? sender, EventArgs e)
        {
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
