using BambuMan.Shared.Models;
using BambuMan.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace BambuMan.UI.LocalInventory
{
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

        public bool IsFiltered => LocalSpoolFilter.IsFiltered(Search, Location);

        public void Load(IEnumerable<LocalSpool> localSpools)
        {
            allSpools = LocalSpoolFilter.ToItems(localSpools);

            Locations = LocalSpoolFilter.Locations(allSpools);

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
            var filtered = LocalSpoolFilter.Filter(allSpools, Search, Location);

            // Groups keep the order of their most recently scanned spool, since allSpools is sorted that way.
            Groups = new ObservableCollection<LocalSpoolGroup>(LocalSpoolFilter.Group(filtered));

            CountText = LocalSpoolFilter.CountText(filtered.Count, allSpools.Count, Groups.Count, IsFiltered);

            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(IsFiltered));
        }
    }
}
