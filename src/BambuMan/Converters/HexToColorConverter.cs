using System.Globalization;
using CommunityToolkit.Maui.Converters;

namespace BambuMan.Converters
{
    /// <summary>
    /// Turns an <c>#AARRGGBB</c> string into a <see cref="Color"/>. Done explicitly rather than leaning on the
    /// binding engine's implicit string conversion, which compiled bindings don't apply consistently.
    /// </summary>
    [AcceptEmptyServiceProvider]
    public class HexToColorConverter : BaseConverter<string?, Color?>
    {
        /// <inheritdoc/>
        public override Color? DefaultConvertReturnValue { get; set; } = null;

        /// <inheritdoc />
        public override string? DefaultConvertBackReturnValue { get; set; } = null;

        public override Color? ConvertFrom(string? value, CultureInfo? culture = null) =>
            string.IsNullOrWhiteSpace(value) ? null : Color.FromArgb(value);

        public override string? ConvertBackTo(Color? value, CultureInfo? culture = null) => value?.ToArgbHex(true);
    }
}
