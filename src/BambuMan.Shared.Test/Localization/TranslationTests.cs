using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using BambuMan.Shared.Enums;
using BambuMan.Shared.Models;

namespace BambuMan.Shared.Test.Localization
{
    /// <summary>
    /// Guards the translations in both resx sets: the Android app's (src/BambuMan) and this library's. A translation with a
    /// placeholder the English text doesn't have makes <see cref="string.Format(string, object?[])"/> throw at runtime.
    /// </summary>
    [Trait("Category", "Localization")]
    public class TranslationTests
    {
        public static TheoryData<string> Translations => new(ResxFiles().Where(x => Culture(x) != null));

        [Theory(DisplayName = "A translation uses the same placeholders as the English text, and only its keys")]
        [MemberData(nameof(Translations))]
        public void Translation_MatchesEnglish(string path)
        {
            var english = Read(path.Replace($".{Culture(path)}.resx", ".resx"));
            var translated = Read(path);

            Assert.Empty(translated.Keys.Except(english.Keys));

            foreach (var (key, value) in translated)
                Assert.True(Placeholders(english[key]).SetEquals(Placeholders(value)), $"{Path.GetFileName(path)}: {key} has other placeholders than the English text");
        }

        [Fact(DisplayName = "Every language in the language setting has its translations")]
        public void AppLanguage_HasResources()
        {
            var cultures = ResxFiles().Select(Culture).OfType<string>().ToHashSet();

            Assert.Null(AppLanguage.SystemDefault.CultureName());

            // English is the resx without a culture.
            foreach (var language in Enum.GetValues<AppLanguage>().Where(x => x is not (AppLanguage.SystemDefault or AppLanguage.English)))
                Assert.Contains(language.CultureName() ?? string.Empty, cultures);
        }

        [Fact(DisplayName = "The spool info rows follow the ui language")]
        public void SpoolDisplayInfo_FollowsUiCulture()
        {
            var original = CultureInfo.CurrentUICulture;

            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");

                var display = SpoolDisplayInfo.From(new BambuFilamentInfo([0x01, 0x02, 0x03, 0x04]) { MaterialVariantIdentifier = "A00-A1" }, null);

                Assert.Contains(display.Rows, x => x.Label == "Variante");
                Assert.Equal("Kein Backend", InventoryBackend.NoBackend.DisplayName());
            }
            finally
            {
                CultureInfo.CurrentUICulture = original;
            }
        }

        #region Helpers

        /// <summary>Every resx under the solution's ui string folders (Resources/Strings).</summary>
        private static IEnumerable<string> ResxFiles()
        {
            var src = new DirectoryInfo(AppContext.BaseDirectory);
            while (src != null && !File.Exists(Path.Combine(src.FullName, "BambuMan.sln"))) src = src.Parent;

            Assert.NotNull(src);

            return new[] { "BambuMan", "BambuMan.Shared" }
                .Select(project => Path.Combine(src.FullName, project, "Resources", "Strings"))
                .Where(Directory.Exists)
                .SelectMany(x => Directory.GetFiles(x, "*.resx"))
                .Order();
        }

        /// <summary>The culture of <c>Name.et.resx</c>; null for the English <c>Name.resx</c>.</summary>
        private static string? Culture(string path)
        {
            var parts = Path.GetFileNameWithoutExtension(path).Split('.');

            return parts.Length > 1 ? parts[^1] : null;
        }

        private static Dictionary<string, string> Read(string path) =>
            XDocument.Load(path).Root!.Elements("data").ToDictionary(x => x.Attribute("name")!.Value, x => x.Element("value")?.Value ?? string.Empty);

        private static HashSet<string> Placeholders(string text) => Regex.Matches(text, @"\{\d+(:[^}]*)?\}").Select(x => x.Value).ToHashSet();

        #endregion
    }
}
