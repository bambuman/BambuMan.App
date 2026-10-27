using Android.Content;
using Android.Views.InputMethods;
using BambuMan.Interfaces;

namespace BambuMan.Implementations
{
    public class AndroidSoftKeyboard : ISoftKeyboard
    {
        public void Show()
        {
            var activity = Platform.CurrentActivity;
            var view = activity?.CurrentFocus;

            if (view == null || activity?.GetSystemService(Context.InputMethodService) is not InputMethodManager manager) return;

            manager.ShowSoftInput(view, ShowFlags.Implicit);
        }

        public void Hide()
        {
            var activity = Platform.CurrentActivity;
            var view = activity?.CurrentFocus;

            if (view == null || activity?.GetSystemService(Context.InputMethodService) is not InputMethodManager manager) return;

            manager.HideSoftInputFromWindow(view.WindowToken, HideSoftInputFlags.None);
        }
    }
}
