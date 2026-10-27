using Microsoft.AspNetCore.Components;

namespace BambuMan
{
    /// <summary>
    /// Lets native code (the Android back button) see and drive the Blazor router. The layout attaches its
    /// <see cref="NavigationManager"/> once the web view is up.
    /// </summary>
    public class AppNavigation
    {
        private NavigationManager? navigationManager;

        public void Attach(NavigationManager manager) => navigationManager = manager;

        /// <summary>True on the scan page, or before the web view has rendered anything.</summary>
        public bool IsAtHome => navigationManager == null || CurrentPath(navigationManager) == string.Empty;

        /// <summary>Navigates to the scan page; false when already there.</summary>
        public bool TryGoHome()
        {
            if (IsAtHome) return false;

            navigationManager!.NavigateTo("/");
            return true;
        }

        public static string CurrentPath(NavigationManager manager) => manager.ToBaseRelativePath(manager.Uri).Split('?', '#')[0].TrimEnd('/');
    }
}
