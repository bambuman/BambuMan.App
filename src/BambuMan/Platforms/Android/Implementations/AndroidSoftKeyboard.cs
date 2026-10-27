using Android.App;
using Android.Content;
using Android.Views;
using Android.Views.InputMethods;
using BambuMan.Interfaces;
using View = Android.Views.View;
using WebView = Android.Webkit.WebView;

namespace BambuMan.Implementations
{
    public class AndroidSoftKeyboard : ISoftKeyboard
    {
        public void Show()
        {
            var activity = Platform.CurrentActivity;
            var webView = FindWebView(activity);

            if (activity == null || webView == null) return;

            // Until the user has touched the page the web view doesn't hold Android focus, and a keyboard request for
            // a view without focus is ignored — which is why the first read after a start opened no keyboard.
            webView.RequestFocus();

            ShowIme(activity, webView);

            // A tag read pauses the activity to deliver its intent, and a request made before the window has its focus
            // back is dropped, so ask again shortly after. A request while the keyboard is already open does nothing.
            webView.PostDelayed(() => ShowIme(activity, webView), 300);
            webView.PostDelayed(() => ShowIme(activity, webView), 800);
        }

        public void Hide()
        {
            var activity = Platform.CurrentActivity;
            var view = (View?)FindWebView(activity) ?? activity?.CurrentFocus;

            if (view?.WindowToken == null || activity?.GetSystemService(Context.InputMethodService) is not InputMethodManager manager) return;

            manager.HideSoftInputFromWindow(view.WindowToken, HideSoftInputFlags.None);
        }

        private static void ShowIme(Activity activity, View view)
        {
            if (activity.IsFinishing || activity.IsDestroyed || !view.IsAttachedToWindow) return;

            // An explicit request: the implicit one may be ignored by the keyboard when the user didn't tap anything.
            if (activity.GetSystemService(Context.InputMethodService) is InputMethodManager manager) manager.ShowSoftInput(view, 0);
        }

        /// <summary>The BlazorWebView's native web view — the app's only one.</summary>
        private static WebView? FindWebView(Activity? activity) => activity?.Window?.DecorView is { } decor ? FindWebView(decor) : null;

        private static WebView? FindWebView(View view)
        {
            if (view is WebView webView) return webView;
            if (view is not ViewGroup group) return null;

            for (var i = 0; i < group.ChildCount; i++)
            {
                if (group.GetChildAt(i) is { } child && FindWebView(child) is { } found) return found;
            }

            return null;
        }
    }
}
