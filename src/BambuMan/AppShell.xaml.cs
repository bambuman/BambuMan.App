using BambuMan.UI.Scan;

namespace BambuMan
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            Routing.RegisterRoute(nameof(ScanPage), typeof(ScanPage));

            InitializeComponent();

            if (string.IsNullOrWhiteSpace(Preferences.Default.Get("spoolman_url", string.Empty)))
            {
                CurrentItem = Items.First(x => x.Title == "Settings");
                //Dispatcher.DispatchAsync(async () => await GoToAsync("//SettingsPage"));
            }
        }
        protected override bool OnBackButtonPressed()
        {
            if (Current.CurrentState.Location.OriginalString == "//MainPage") return false;

            Current.GoToAsync("//MainPage", true);
            return true;
        }

    }
}
