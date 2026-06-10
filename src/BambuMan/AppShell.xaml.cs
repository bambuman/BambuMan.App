using BambuMan.Shared.Enums;
using BambuMan.UI.Scan;
using BambuMan.UI.Settings;

namespace BambuMan
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            Routing.RegisterRoute(nameof(ScanPage), typeof(ScanPage));

            InitializeComponent();

            var activeUrl = SettingsPage.GetInventoryBackend() == InventoryBackend.Bambuddy
                ? Preferences.Default.Get(SettingsPage.KeyBambuddyUrl, string.Empty)
                : Preferences.Default.Get(SettingsPage.KeySpoolmanUrl, string.Empty);

            if (string.IsNullOrWhiteSpace(activeUrl) && Items.FirstOrDefault() is { } tabBar)
            {
                // Fresh install / no URL configured: open the Settings tab so the user can set the
                // backend URL. With the M3 bottom TabBar the tabs live one level below the TabBar.
                var settingsTab = tabBar.Items.FirstOrDefault(t => t.Title == "Settings");
                if (settingsTab != null) tabBar.CurrentItem = settingsTab;
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
