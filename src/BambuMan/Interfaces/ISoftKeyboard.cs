namespace BambuMan.Interfaces
{
    /// <summary>
    /// Shows / hides the on-screen keyboard for whatever is focused in the web view. Focusing an input from script
    /// alone doesn't open the keyboard on Android unless a tap started it, and a spool read is not a tap.
    /// </summary>
    public interface ISoftKeyboard
    {
        void Show();

        void Hide();
    }
}
