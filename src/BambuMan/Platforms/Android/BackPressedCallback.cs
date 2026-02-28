using AndroidX.Activity;

namespace BambuMan;

/// <summary>
/// Modern back-press handler using OnBackPressedDispatcher (replaces deprecated OnBackPressed).
/// When the user is on MainPage, minimizes the app instead of finishing the Activity.
/// This prevents Activity destruction and avoids the Glide/FontImageSource crash (dotnet/maui#12513).
/// </summary>
public class BackPressedCallback(ComponentActivity activity) : OnBackPressedCallback(true)
{
    public override void HandleOnBackPressed()
    {
        var location = Shell.Current?.CurrentState?.Location?.OriginalString;

        if (location == "//MainPage")
        {
            // Minimize instead of closing to keep the Activity alive
            activity.MoveTaskToBack(true);
            return;
        }

        // For other pages, let MAUI handle normal back navigation
        Enabled = false;
        activity.OnBackPressedDispatcher.OnBackPressed();
        Enabled = true;
    }
}
