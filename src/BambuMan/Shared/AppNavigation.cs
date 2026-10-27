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

        /// <summary>
        /// Goes one level up — a stored spool back to the spool list, any other page to the scan page. False when
        /// already on the scan page.
        /// </summary>
        public bool TryGoBack()
        {
            if (IsAtHome) return false;

            var path = CurrentPath(navigationManager!);
            var parent = path.Contains('/') ? path[..path.LastIndexOf('/')] : string.Empty;

            navigationManager!.NavigateTo(parent.Length == 0 ? "/" : parent);
            return true;
        }

        public static string CurrentPath(NavigationManager manager) => manager.ToBaseRelativePath(manager.Uri).Split('?', '#')[0].TrimEnd('/');
    }
}
