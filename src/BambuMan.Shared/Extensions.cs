using System.ComponentModel;
using System.Drawing;
using System.Security.Cryptography;

namespace BambuMan.Shared
{
    public static class Extensions
    {
        public static string GetDescriptionAttr(this Enum @enum)
        {
            var type = @enum.GetType();
            var memInfo = type.GetMember(@enum.ToString());
            var attributes = memInfo[0].GetCustomAttributes(typeof(DescriptionAttribute), false);
            return attributes.Any() ? ((DescriptionAttribute)attributes[0]).Description : "";
        }

        public static byte[] StringToByteArray(this string hex)
        {
            return Enumerable.Range(0, hex.Length)
                .Where(x => x % 2 == 0)
                .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                .ToArray();
        }

        public static string ToHex(this Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        public static string DarkHex(this string seed) => DarkHex(seed.DeterministicHashCode());

        public static string DarkHex(this int hash)
        {
            var hue = (int)(((hash & 0xFFFF00) >> 8) / 65535.0 * 360.0);
            var light = (int)((hash & 0x0000FF) / 255.0 * 30.0) + 15;

            return HslToHex(hue, 100, light);
        }

        public static string HslToHex(int preciseHue, int preciseSaturation, int preciseLight)
        {
            double red, green, blue;

            var h = preciseHue / 360.0;
            var s = preciseSaturation / 100.0;
            var l = preciseLight / 100.0;

            if (Math.Abs(s - 0.0) < 0.00001)
            {
                red = l;
                green = l;
                blue = l;
            }
            else
            {
                double var2;

                if (l < 0.5)
                {
                    var2 = l * (1.0 + s);
                }
                else
                {
                    var2 = l + s - s * l;
                }

                var var1 = 2.0 * l - var2;

                red = Hue2Rgb(var1, var2, h + 1.0 / 3.0);
                green = Hue2Rgb(var1, var2, h);
                blue = Hue2Rgb(var1, var2, h - 1.0 / 3.0);
            }

            // --

            var nRed = Convert.ToInt32(red * 255.0);
            var nGreen = Convert.ToInt32(green * 255.0);
            var nBlue = Convert.ToInt32(blue * 255.0);

            return Color.FromArgb(nRed, nGreen, nBlue).ToHex();
        }

        private static double Hue2Rgb(double v1, double v2, double vH)
        {
            if (vH < 0.0) vH += 1.0;

            if (vH > 1.0) vH -= 1.0;

            if (6.0 * vH < 1.0) return v1 + (v2 - v1) * 6.0 * vH;

            if (2.0 * vH < 1.0) return v2;

            if (3.0 * vH < 2.0) return v1 + (v2 - v1) * (2.0 / 3.0 - vH) * 6.0;

            return v1;
        }

        public static int DeterministicHashCode(this string str)
        {
            // Turns out string.GetHashCode() is not deterministic between runs.
            // https://andrewlock.net/why-is-string-gethashcode-different-each-time-i-run-my-program-in-net-core/

            unchecked
            {
                var hash1 = (5381 << 16) + 5381;
                var hash2 = hash1;

                for (var i = 0; i < str.Length; i += 2)
                {
                    hash1 = ((hash1 << 5) + hash1) ^ str[i];
                    if (i == str.Length - 1) break;
                    hash2 = ((hash2 << 5) + hash2) ^ str[i + 1];
                }

                return hash1 + hash2 * 1566083941;
            }
        }

        public static bool IsNullOrEmpty(this string s)
        {
            return string.IsNullOrEmpty(s);
        }

        public static bool IsNullOrWhiteSpace(this string s)
        {
            return s == null || s.All(char.IsWhiteSpace);
        }
        public static bool IsNotNullOrWhiteSpace(this string @string)
        {
            return !IsNullOrWhiteSpace(@string);
        }

        public static bool IsNotNullOrEmpty(this string s)
        {
            return !string.IsNullOrEmpty(s);
        }

        public static string TrimTo(this string s, int length)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length > length ? s.Substring(0, length) : s;
        }

        public static string TrimTo(this string s, int length, string ext)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (ext.IsNotNullOrEmpty()) length -= ext.Length;

            return s.Length > length ? s.Substring(0, length) + ext : s;
        }

        // ReSharper disable once InconsistentNaming
        public static bool EqualsCI(this string? source, string? value)
        {
            if (source == null || value == null) return false;

            return source.Equals(value, StringComparison.OrdinalIgnoreCase);
        }

        // ReSharper disable once InconsistentNaming
        public static bool ContainsCI(this string? source, string? value)
        {
            if (source == null || value == null) return false;

            return source.Contains(value, StringComparison.OrdinalIgnoreCase);
        }

        // ReSharper disable once InconsistentNaming
        public static bool StartsWithCI(this string? source, string? value)
        {
            if (source == null || value == null) return false;

            return source.StartsWith(value, StringComparison.OrdinalIgnoreCase);
        }

        public static byte[][] GetBambuKeys(this byte[] uid)
        {
            var master = new byte[] { 0x9a, 0x75, 0x9c, 0xf2, 0xc4, 0xf7, 0xca, 0xff, 0x22, 0x2c, 0xb9, 0x76, 0x9b, 0x41, 0xbc, 0x96 };

            var primary = HKDF.Extract(HashAlgorithmName.SHA256, uid, salt: master);
            var dest = HKDF.Expand(HashAlgorithmName.SHA256, primary, 6 * 16, "RFID-A\0"u8.ToArray());

            return Enumerable.Range(0, 16).Select(x => dest[new Range(x * 6, x * 6 + 6)]).ToArray();
        }
    }
}