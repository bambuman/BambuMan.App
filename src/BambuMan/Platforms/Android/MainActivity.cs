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
            // Clear stale fragment state to prevent ShellItemRenderer crash when Android
            // restores fragments with invalid view references after config changes or
            // extended background periods. MAUI Shell recreates all fragments anyway.
            savedInstanceState?.Remove("android:support:fragments");
            savedInstanceState?.Remove("androidx.lifecycle.BundlableSavedStateRegistry.key");

            // Plugin NFC : Initialisation
            CrossNfc.Init(this);

            base.OnCreate(savedInstanceState);

            // Modern back-press handler: minimizes on MainPage instead of finishing the Activity,
            // preventing Glide/FontImageSource crash on recreation (dotnet/maui#12513)
            OnBackPressedDispatcher.AddCallback(this, new BackPressedCallback(this));
        }

        protected override void OnRestart()
        {
            try
            {
                base.OnRestart();
            }
            catch (Java.Lang.IllegalStateException ex) when (ex.Message?.Contains("onSaveInstanceState") == true)
            {
                // Known MAUI bug: DialogFragment.dismiss() is called after onSaveInstanceState,
                // which is forbidden by the Android fragment manager. The activity recovers
                // on the next onStart/onResume cycle.
            }
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
