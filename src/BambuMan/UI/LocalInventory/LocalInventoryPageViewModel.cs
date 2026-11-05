using BambuMan.Shared.Enums;
using BambuMan.Shared.Models;
using BambuMan.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace BambuMan.UI.LocalInventory
{
    /// <summary>Which stored spools the list shows, by whether they were sent to the selected server backend.</summary>
    public enum LocalSpoolSentFilter
    {
        All,
        NotSent,
        Sent
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

        [ObservableProperty] private LocalSpoolSentFilter sent;

        /// <summary>The server backend spools can be sent to; null with "No backend" selected, which hides the sent filter.</summary>
        [ObservableProperty] private InventoryBackend? sendBackend;

        /// <summary>The keys of the spools the filters show — what "Send" sends.</summary>
        public IReadOnlyList<string> FilteredKeys { get; private set; } = [];

        public int TotalCount => allSpools.Count;

        public bool IsFiltered => LocalSpoolFilter.IsFiltered(Search, Location) || (SendBackend != null && Sent != LocalSpoolSentFilter.All);

        /// <summary>Whether a spool was sent to the selected server backend, with the values it has now.</summary>
        public bool IsSent(LocalSpool spool) => SendBackend != null && spool.SentTo == SendBackend;

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
            Sent = LocalSpoolSentFilter.All;
        }

        partial void OnSearchChanged(string? value) => ApplyFilter();

        partial void OnLocationChanged(string? value) => ApplyFilter();

        partial void OnSentChanged(LocalSpoolSentFilter value) => ApplyFilter();

        partial void OnSendBackendChanged(InventoryBackend? value) => ApplyFilter();

        private void ApplyFilter()
        {
            var filtered = LocalSpoolFilter.Filter(allSpools, Search, Location)
                .Where(x => SendBackend == null || Sent == LocalSpoolSentFilter.All || IsSent(x.Spool) == (Sent == LocalSpoolSentFilter.Sent))
                .ToList();

            FilteredKeys = filtered.Select(x => x.Key).ToList();

            // Groups keep the order of their most recently scanned spool, since allSpools is sorted that way.
            Groups = new ObservableCollection<LocalSpoolGroup>(LocalSpoolFilter.Group(filtered));

            CountText = LocalSpoolFilter.CountText(filtered.Count, allSpools.Count, Groups.Count, IsFiltered);

            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(IsFiltered));
        }
    }
}
