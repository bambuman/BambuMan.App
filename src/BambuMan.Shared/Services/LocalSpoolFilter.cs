using BambuMan.Shared.Models;

namespace BambuMan.Shared.Services
{
    /// <summary>One stored spool, as a row of the list.</summary>
    public record LocalSpoolItem(string Key, string Title, string Subtitle, string Color, string? Location, LocalSpool Spool)
    {
        /// <summary>The tag the spool is known by — its tray uid, else tag uid — or that it was added by hand without one.</summary>
        public string Tag => TagOf(Spool);

        public static string TagOf(LocalSpool spool) => spool.IsManual ? "Added manually" : spool.Info.TrayUid ?? spool.Info.SerialNumber;
    }

    /// <summary>Spools of the same filament (material, colour name and colour), shown as one expandable row.</summary>
    public record LocalSpoolGroup(string Key, string Title, string Color, IReadOnlyList<LocalSpoolItem> Spools)
    {
        public int Count => Spools.Count;

        /// <summary>Where the spools of this group are, e.g. <c>Shelf A, Dry box</c>.</summary>
        public string Locations => string.Join(", ", Spools.Select(x => x.Location).Where(x => !string.IsNullOrEmpty(x)).Distinct());
    }

    /// <summary>
    /// The stored spools list: its rows, search, location filter and grouping. Shared by the Android Stored spools page
    /// and the desktop Stored spools window, so both find and group the same spools.
    /// </summary>
    public static class LocalSpoolFilter
    {
        #region Methods

        /// <summary>The spools that can be listed (those with a <see cref="LocalSpool.Key"/>), most recently scanned first.</summary>
        public static List<LocalSpoolItem> ToItems(IEnumerable<LocalSpool> spools) => spools
            .OrderByDescending(x => x.LastScanned)
            .Where(x => x.Key != null)
            .Select(ToItem)
            .ToList();

        public static LocalSpoolItem ToItem(LocalSpool spool)
        {
            var info = spool.Info;
            var material = info.DetailedFilamentType ?? info.FilamentType;
            var title = spool.Name == null ? material ?? "Unknown filament" : $"{material} {spool.Name}";
            var subtitle = string.Join(" · ", new[] { spool.Location, LocalSpoolItem.TagOf(spool), $"{spool.LastScanned:yyyy-MM-dd}" }.Where(x => !string.IsNullOrEmpty(x)));

            // Same colour the read-only card shows for this tag, as #AARRGGBB.
            var color = SpoolDisplayInfo.From(info, null).ColorHexes.FirstOrDefault() ?? "#00000000";

            return new LocalSpoolItem(spool.Key!, title, subtitle, color, spool.Location, spool);
        }

        /// <summary>Every location a listed spool has, for the location filter.</summary>
        public static List<string> Locations(IEnumerable<LocalSpoolItem> items) => items
            .Select(x => x.Location)
            .OfType<string>()
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order()
            .ToList();

        public static bool IsFiltered(string? search, string? location) => !string.IsNullOrWhiteSpace(search) || !string.IsNullOrEmpty(location);

        /// <summary>
        /// The items in <paramref name="location"/> (any, when empty) whose filament name (material, colour, manufacturer,
        /// filament code) or tag (tray uid / tag serial) contains <paramref name="search"/>. Keeps the order of <paramref name="items"/>.
        /// </summary>
        public static List<LocalSpoolItem> Filter(IEnumerable<LocalSpoolItem> items, string? search, string? location)
        {
            search = search?.Trim();

            return items
                .Where(x => string.IsNullOrEmpty(location) || string.Equals(x.Location, location, StringComparison.OrdinalIgnoreCase))
                .Where(x => string.IsNullOrEmpty(search) || Matches(x.Spool, search))
                .ToList();
        }

        public static bool Matches(LocalSpool spool, string search)
        {
            var info = spool.Info;

            return new[] { info.DetailedFilamentType, info.FilamentType, spool.Name, spool.Manufacturer, spool.Brand, spool.FilamentCode, info.TrayUid, info.SerialNumber }
                .Any(x => x?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);
        }

        /// <summary>Groups of the same filament. A group keeps the place of its first item, so newest-first items give newest-first groups.</summary>
        public static List<LocalSpoolGroup> Group(IEnumerable<LocalSpoolItem> items) => items
            .GroupBy(GroupKey)
            .Select(g => new LocalSpoolGroup(g.Key, g.First().Title, g.First().Color, g.ToList()))
            .ToList();

        /// <summary>The list's count line, e.g. <c>3 spools · 2 filaments</c> or, filtered, <c>1 of 3 spools · 1 filament</c>.</summary>
        public static string CountText(int shown, int total, int groups, bool isFiltered)
        {
            var groupText = groups == 1 ? "1 filament" : $"{groups} filaments";

            return isFiltered
                ? $"{shown} of {total} spools · {groupText}"
                : $"{total} {(total == 1 ? "spool" : "spools")} · {groupText}";
        }

        /// <summary>Same filament: material, catalog colour name and the swatch colour all equal.</summary>
        private static string GroupKey(LocalSpoolItem item) => $"{item.Title}|{item.Color}".ToUpperInvariant();

        #endregion
    }
}
