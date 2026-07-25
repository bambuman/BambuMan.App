using BambuMan.Shared.Models;
using CommunityToolkit.Maui.Converters;
using SpoolMan.Api.Model;
using System.Globalization;

namespace BambuMan.Converters
{
    /// <summary>
    /// Paints a spool's colours the same way the bambuman.ee ColorSwatch component does:
    /// <c>Longitudinal</c> is a smooth left-to-right blend (a gradient filament), <c>Coaxial</c> is hard
    /// stacked bands (two filaments extruded side by side), and a single colour is flat.
    /// </summary>
    [AcceptEmptyServiceProvider]
    public class SpoolColorBrushConverter : BaseConverter<SpoolDisplayInfo?, Brush?>
    {
        /// <inheritdoc/>
        public override Brush? DefaultConvertReturnValue { get; set; } = null;

        /// <inheritdoc />
        public override SpoolDisplayInfo? DefaultConvertBackReturnValue { get; set; } = null;

        public override Brush? ConvertFrom(SpoolDisplayInfo? value, CultureInfo? culture = null)
        {
            var colors = value?.ColorHexes.Select(Color.FromArgb).ToArray() ?? [];

            if (colors.Length == 0) return null;
            if (colors.Length == 1) return new SolidColorBrush(colors[0]);

            if (value!.MultiColorDirection == SpoolmanExternaldbMultiColorDirection.Longitudinal)
            {
                // Blend across the swatch, one evenly spaced stop per colour.
                var blend = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };

                for (var i = 0; i < colors.Length; i++)
                    blend.GradientStops.Add(new GradientStop(colors[i], i / (float)(colors.Length - 1)));

                return blend;
            }

            // Coaxial, and an unmatched tag whose direction the catalog never told us: equal bands top to
            // bottom. Two stops per colour at the band edges is what makes the boundary hard instead of a fade.
            var bands = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };

            for (var i = 0; i < colors.Length; i++)
            {
                bands.GradientStops.Add(new GradientStop(colors[i], i / (float)colors.Length));
                bands.GradientStops.Add(new GradientStop(colors[i], (i + 1) / (float)colors.Length));
            }

            return bands;
        }

        public override SpoolDisplayInfo? ConvertBackTo(Brush? value, CultureInfo? culture = null) => null;
    }
}
