namespace BambuMan.Shared.Enums
{
    /// <summary>
    /// How large the app's text is drawn. Declaration order drives the settings segmented-button order. Persisted by
    /// name, so this order is safe to change.
    /// </summary>
    public enum TextScale
    {
        Normal,
        Large,
        Larger,
        Largest
    }

    public static class TextScaleExtensions
    {
        /// <summary>The factor every font size (and the icons beside the text) is multiplied by.</summary>
        public static double Factor(this TextScale scale) => scale switch
        {
            TextScale.Large => 1.15,
            TextScale.Larger => 1.3,
            TextScale.Largest => 1.5,
            _ => 1
        };

        /// <summary>How the scale is labelled in the ui — short, so four of them fit side by side on a phone.</summary>
        public static string DisplayName(this TextScale scale) => $"{scale.Factor() * 100:0}%";
    }
}
