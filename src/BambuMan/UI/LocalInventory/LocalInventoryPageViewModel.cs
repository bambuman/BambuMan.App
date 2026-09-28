using BambuMan.Shared.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace BambuMan.UI.LocalInventory
{
    /// <summary>One row of the local spool list.</summary>
    public record LocalSpoolItem(string Key, string Title, string Subtitle, string Color);

    public partial class LocalInventoryPageViewModel : ObservableObject
    {
        [ObservableProperty] private ObservableCollection<LocalSpoolItem> spools = [];

        [ObservableProperty] private string? countText;

        public void Load(IEnumerable<LocalSpool> localSpools)
        {
            Spools = new ObservableCollection<LocalSpoolItem>(localSpools
                .OrderByDescending(x => x.LastScanned)
                .Where(x => x.Key != null)
                .Select(ToItem));

            CountText = $"Stored spools: {Spools.Count}";
        }

        private static LocalSpoolItem ToItem(LocalSpool spool)
        {
            var info = spool.Info;
            var material = info.DetailedFilamentType ?? info.FilamentType;
            var title = spool.Name == null ? material ?? "Unknown filament" : $"{material} {spool.Name}";
            var subtitle = string.Join(" · ", new[] { spool.Location, info.TrayUid ?? info.SerialNumber, $"{spool.LastScanned:yyyy-MM-dd}" }.Where(x => !string.IsNullOrEmpty(x)));

            // Same colour the read-only card shows for this tag.
            var color = SpoolDisplayInfo.From(info, null).ColorHexes.FirstOrDefault() ?? "#00000000";

            return new LocalSpoolItem(spool.Key!, title, subtitle, color);
        }
    }
}
