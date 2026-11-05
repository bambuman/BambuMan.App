namespace BambuMan.Shared.Enums
{
    /// <summary>
    /// The language the app's ui is shown in. Declaration order drives the settings list order. Persisted by name, so
    /// this order is safe to change. Adding a language: add it here, to both switches below, and add its resx files
    /// (see README "Translations").
    /// </summary>
    public enum AppLanguage
    {
        /// <summary>Follow the device language, falling back to English for a language the app has no translation for.</summary>
        SystemDefault,
        English,
        Estonian,
        German
    }

    public static class AppLanguageExtensions
    {
        /// <summary>The culture whose resources are shown, e.g. <c>et</c>. Null for <see cref="AppLanguage.SystemDefault"/>.</summary>
        public static string? CultureName(this AppLanguage language) => language switch
        {
            AppLanguage.English => "en",
            AppLanguage.Estonian => "et",
            AppLanguage.German => "de",
            _ => null
        };

        /// <summary>
        /// The language's name in that language, so it can be found whatever language the ui is in. Null for
        /// <see cref="AppLanguage.SystemDefault"/>, which the app labels in the current language itself.
        /// </summary>
        public static string? NativeName(this AppLanguage language) => language switch
        {
            AppLanguage.English => "English",
            AppLanguage.Estonian => "Eesti",
            AppLanguage.German => "Deutsch",
            _ => null
        };
    }
}
