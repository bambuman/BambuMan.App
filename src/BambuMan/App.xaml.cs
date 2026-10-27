using Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific;

namespace BambuMan
{
    public partial class App
    {
        public App()
        {
            InitializeComponent();

            // Shrink the web view when the keyboard opens, rather than panning the window, so the scan page can scroll
            // the spool card above the keyboard.
            On<Microsoft.Maui.Controls.PlatformConfiguration.Android>().UseWindowSoftInputModeAdjust(WindowSoftInputModeAdjust.Resize);

            Preferences.Default.Set("default_buy_date", $"{DateTime.Today:yyyy-MM-dd}");
        }

        protected override Window CreateWindow(IActivationState? activationState) => new(new HostPage()) { Title = "BambuMan" };
    }
}
