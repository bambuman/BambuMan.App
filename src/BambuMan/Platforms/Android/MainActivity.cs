using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace BambuMan
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            // Plugin NFC : Initialisation
            CrossNfc.Init(this);

            base.OnCreate(savedInstanceState);

            // Modern back-press handler: minimizes on MainPage instead of finishing the Activity,
            // preventing Glide/FontImageSource crash on recreation (dotnet/maui#12513)
            OnBackPressedDispatcher.AddCallback(this, new BackPressedCallback(this));
        }

        protected override void OnResume()
        {
            base.OnResume();

            // Plugin NFC: Restart NFC listening on resume (needed for Android 10+) 
            CrossNfc.OnResume();
        }

        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);

            // Plugin NFC: Tag Discovery Interception
            if (intent != null) CrossNfc.OnNewIntent(intent);
        }
    }
}
