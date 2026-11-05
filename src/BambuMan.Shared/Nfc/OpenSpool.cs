using BambuMan.Shared.Enums;
using BambuMan.Shared.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Text;

namespace BambuMan.Shared.Nfc
{
    /// <summary>
    /// The OpenSpool tag format (openspool.io): an NTAG215/216 holding one NDEF record of mime type
    /// <c>application/json</c>, e.g.
    /// <code>{"protocol":"openspool","version":"1.0","type":"PLA","color_hex":"FFAABB","brand":"Generic","min_temp":"220","max_temp":"240"}</code>
    /// Numbers are written as strings by the reference implementation, but plain json numbers are read too.
    /// </summary>
    public static class OpenSpool
    {
        public const string MimeType = "application/json";
        public const string Protocol = "openspool";
        public const string Version = "1.0";

        #region Read

        /// <summary>The first OpenSpool record among <paramref name="records"/>, parsed; null when there is none.</summary>
        public static SpoolTag? TryParse(byte[] identifier, IEnumerable<NfcNdefRecord?>? records)
        {
            if (records == null) return null;

            foreach (var record in records)
            {
                if (record is not { TypeFormat: NfcNdefTypeFormat.Mime, Payload: { Length: > 0 } payload }) continue;
                if (!record.MimeType.Equals(MimeType, StringComparison.OrdinalIgnoreCase)) continue;

                var tag = TryParse(identifier, Encoding.UTF8.GetString(payload));
                if (tag != null) return tag;
            }

            return null;
        }

        /// <summary>Parse an OpenSpool json payload; null when it isn't valid json or names another protocol.</summary>
        public static SpoolTag? TryParse(byte[] identifier, string json)
        {
            JObject obj;

            try
            {
                obj = JObject.Parse(json);
            }
            catch (JsonReaderException)
            {
                return null;
            }

            if (!Text(obj, "protocol").EqualsCI(Protocol)) return null;

            var color = NormalizeColor(Text(obj, "color_hex"), Text(obj, "alpha"));

            return new SpoolTag(SpoolTagFormat.OpenSpool, identifier)
            {
                Brand = Text(obj, "brand"),
                FilamentType = Text(obj, "type"),
                // Not in the documented 1.0 example — read when a writer adds it, never required.
                Subtype = Text(obj, "subtype"),
                Color = color,
                AdditionalColors = AdditionalColors(obj),
                MinTemperatureForHotend = UShort(obj, "min_temp"),
                MaxTemperatureForHotend = UShort(obj, "max_temp"),
                BedTemperature = UShort(obj, "bed_min_temp") ?? UShort(obj, "bed_max_temp"),
                SpoolWeight = UShort(obj, "weight"),
                FilamentDiameter = Float(obj, "diameter"),
                RawPayload = json
            };
        }

        #endregion

        #region Write

        /// <summary>
        /// The OpenSpool json for a spool: only the fields of the documented 1.0 example, numbers as strings like the
        /// reference implementation writes them.
        /// </summary>
        public static string CreateJson(ISpoolTag tag)
        {
            var obj = new JObject
            {
                ["protocol"] = Protocol,
                ["version"] = Version,
                ["type"] = tag.FilamentType ?? string.Empty,
                ["color_hex"] = tag.Color is { Length: >= 6 } color ? color[..6].ToUpperInvariant() : "FFFFFF",
                ["brand"] = string.IsNullOrWhiteSpace(tag.Brand) ? "Generic" : tag.Brand,
                ["min_temp"] = tag.MinTemperatureForHotend?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                ["max_temp"] = tag.MaxTemperatureForHotend?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
            };

            return obj.ToString(Formatting.None);
        }

        /// <summary>The NDEF record to write for <paramref name="tag"/>.</summary>
        public static NfcNdefRecord CreateRecord(ISpoolTag tag) => new()
        {
            TypeFormat = NfcNdefTypeFormat.Mime,
            MimeType = MimeType,
            Payload = Encoding.UTF8.GetBytes(CreateJson(tag))
        };

        #endregion

        #region Helpers

        private static string? Text(JObject obj, string name)
        {
            var token = obj[name];

            if (token == null || token.Type is JTokenType.Null or JTokenType.Object or JTokenType.Array) return null;

            var text = token.Type == JTokenType.Float ? token.Value<double>().ToString(CultureInfo.InvariantCulture) : token.ToString();

            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        private static ushort? UShort(JObject obj, string name) =>
            decimal.TryParse(Text(obj, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value is > 0 and <= ushort.MaxValue ? (ushort)Math.Round(value) : null;

        private static float? Float(JObject obj, string name) =>
            float.TryParse(Text(obj, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : null;

        /// <summary><c>RRGGBB</c> (+ optional alpha) or <c>RRGGBBAA</c>, with or without <c>#</c>, as <c>RRGGBBAA</c>; null when it isn't hex.</summary>
        internal static string? NormalizeColor(string? hex, string? alpha = null)
        {
            hex = hex?.Trim().TrimStart('#').ToUpperInvariant();

            if (hex == null || !hex.All(Uri.IsHexDigit)) return null;

            var a = alpha?.Trim().TrimStart('#').ToUpperInvariant();
            if (a is not { Length: 2 } || !a.All(Uri.IsHexDigit)) a = "FF";

            return hex.Length switch
            {
                6 => hex + a,
                8 => hex,
                _ => null
            };
        }

        private static List<string> AdditionalColors(JObject obj)
        {
            var token = obj["additional_color_hexes"];

            IEnumerable<string?> values = token switch
            {
                JArray array => array.Select(x => x.Type == JTokenType.String ? x.Value<string>() : null),
                JValue { Type: JTokenType.String } value => (value.Value<string>() ?? string.Empty).Split(','),
                _ => []
            };

            return values.Select(x => NormalizeColor(x)).OfType<string>().ToList();
        }

        #endregion
    }
}
