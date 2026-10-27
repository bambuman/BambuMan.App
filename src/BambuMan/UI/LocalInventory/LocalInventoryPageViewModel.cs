using BambuMan.Shared.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace BambuMan.UI.LocalInventory
{
    /// <summary>One stored spool, as a row of the list.</summary>
    public record LocalSpoolItem(string Key, string Title, string Subtitle, string Color, string? Location, LocalSpool Spool);

    /// <summary>Spools of the same filament (material, colour name and colour), shown as one expandable row.</summary>
    public record LocalSpoolGroup(string Key, string Title, string Color, IReadOnlyList<LocalSpoolItem> Spools)
    {
        public int Count => Spools.Count;

        /// <summary>Where the spools of this group are, e.g. <c>Shelf A, Dry box</c>.</summary>
        public string Locations => string.Join(", ", Spools.Select(x => x.Location).Where(x => !string.IsNullOrEmpty(x)).Distinct());
    }

    public partial class LocalInventoryPageViewModel : ObservableObject
    {
        private List<LocalSpoolItem> allSpools = [];

        [ObservableProperty] private ObservableCollection<LocalSpoolGroup> groups = [];

        [ObservableProperty] private string? countText;

        /// <summary>Every location a stored spool has, for the location filter.</summary>
        [ObservableProperty] private IReadOnlyList<string> locations = [];

        /// <summary>Matches the filament name (material, colour, manufacturer, filament code) or the tag (tray uid / tag serial).</summary>
        [ObservableProperty] private string? search;

        [ObservableProperty] private string? location;

        public int TotalCount => allSpools.Count;

        public bool IsFiltered => !string.IsNullOrWhiteSpace(Search) || !string.IsNullOrEmpty(Location);

        public void Load(IEnumerable<LocalSpool> localSpools)
        {
            allSpools = localSpools
                .OrderByDescending(x => x.LastScanned)
                .Where(x => x.Key != null)
                .Select(ToItem)
                .ToList();

            Locations = allSpools.Select(x => x.Location).OfType<string>().Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();

            // A location filter whose last spool was deleted would otherwise leave an empty list with no way to see why.
            if (!string.IsNullOrEmpty(Location) && !Locations.Contains(Location, StringComparer.OrdinalIgnoreCase)) Location = null;

            ApplyFilter();
        }

        public void ClearFilters()
        {
            Search = null;
            Location = null;
        }

        partial void OnSearchChanged(string? value) => ApplyFilter();

        partial void OnLocationChanged(string? value) => ApplyFilter();

        private void ApplyFilter()
        {
            var search = Search?.Trim();

            var filtered = allSpools
                .Where(x => string.IsNullOrEmpty(Location) || string.Equals(x.Location, Location, StringComparison.OrdinalIgnoreCase))
                .Where(x => string.IsNullOrEmpty(search) || Matches(x.Spool, search))
                .ToList();

            // Groups keep the order of their most recently scanned spool, since allSpools is sorted that way.
            Groups = new ObservableCollection<LocalSpoolGroup>(filtered
                .GroupBy(GroupKey)
                .Select(g => new LocalSpoolGroup(g.Key, g.First().Title, g.First().Color, g.ToList())));

            var groupText = Groups.Count == 1 ? "1 filament" : $"{Groups.Count} filaments";

            CountText = IsFiltered
                ? $"{filtered.Count} of {allSpools.Count} spools · {groupText}"
                : $"{allSpools.Count} {(allSpools.Count == 1 ? "spool" : "spools")} · {groupText}";

            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(IsFiltered));
        }

        private static bool Matches(LocalSpool spool, string search)
        {
            var info = spool.Info;

            return new[] { info.DetailedFilamentType, info.FilamentType, spool.Name, spool.Manufacturer, spool.FilamentCode, info.TrayUid, info.SerialNumber }
                .Any(x => x?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);
        }

        /// <summary>Same filament: material, catalog colour name and the swatch colour all equal.</summary>
        private static string GroupKey(LocalSpoolItem item) => $"{item.Title}|{item.Color}".ToUpperInvariant();

        private static LocalSpoolItem ToItem(LocalSpool spool)
        {
            var info = spool.Info;
            var material = info.DetailedFilamentType ?? info.FilamentType;
            var title = spool.Name == null ? material ?? "Unknown filament" : $"{material} {spool.Name}";
            var subtitle = string.Join(" · ", new[] { spool.Location, info.TrayUid ?? info.SerialNumber, $"{spool.LastScanned:yyyy-MM-dd}" }.Where(x => !string.IsNullOrEmpty(x)));

            // Same colour the read-only card shows for this tag.
            var color = SpoolDisplayInfo.From(info, null).ColorHexes.FirstOrDefault() ?? "#00000000";

            return new LocalSpoolItem(spool.Key!, title, subtitle, color, spool.Location, spool);
        }
    }
}
