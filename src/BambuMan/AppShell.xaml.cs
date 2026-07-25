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

            var backend = SettingsPage.GetInventoryBackend();

            // Only nag about a missing url when there is meant to be one — "no backend" is a valid configured state.
            if (backend != InventoryBackend.NoBackend)
            {
                var activeUrl = backend == InventoryBackend.Bambuddy
                    ? Preferences.Default.Get(SettingsPage.KeyBambuddyUrl, string.Empty)
                    : Preferences.Default.Get(SettingsPage.KeySpoolmanUrl, string.Empty);

                if (string.IsNullOrWhiteSpace(activeUrl))
                {
                    CurrentItem = Items.First(x => x.Title == "Settings");
                    //Dispatcher.DispatchAsync(async () => await GoToAsync("//SettingsPage"));
                }
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
