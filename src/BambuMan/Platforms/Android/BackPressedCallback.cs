using AndroidX.Activity;

namespace BambuMan;

/// <summary>
/// Modern back-press handler using OnBackPressedDispatcher (replaces deprecated OnBackPressed).
/// Closes the QR scanner when it is open, returns to the scan page from any other Blazor page, and on the scan page
/// minimizes the app instead of finishing the Activity, so NFC listening resumes where it left off.
/// </summary>
public class BackPressedCallback(ComponentActivity activity) : OnBackPressedCallback(true)
{
    public override void HandleOnBackPressed()
    {
        var navigation = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;

        if (navigation?.ModalStack.Count > 0)
        {
            _ = navigation.PopModalAsync();
            return;
        }

        if (AppContainer.Services?.GetService<AppNavigation>()?.TryGoHome() == true) return;

        // Minimize instead of closing to keep the Activity alive
        activity.MoveTaskToBack(true);
    }
}
