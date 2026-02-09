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
            return mainWindow;
        }

        private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (mainWindow != null)
                    mainWindow.Page = new AppShell();
            });
        }
    }
}
