namespace BambuMan
{
    public partial class App
    {
        public App()
        {
            InitializeComponent();

            Preferences.Default.Set("default_buy_date", $"{DateTime.Today:yyyy-MM-dd}");
        }

        protected override Window CreateWindow(IActivationState? activationState) => new (new AppShell());
    }
}
