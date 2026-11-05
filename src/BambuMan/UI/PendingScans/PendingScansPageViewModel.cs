using BambuMan.Resources.Strings;
using BambuMan.Shared.Enums;
using BambuMan.Shared.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace BambuMan.UI.PendingScans
{
    /// <summary>One queued scan, as a row of the list. <see cref="CanSend"/> is false for a scan queued for another backend than the selected one.</summary>
    public record PendingScanItem(Guid Id, string Title, string Subtitle, string Color, string? Error, bool CanSend, PendingScan Scan);

    public partial class PendingScansPageViewModel : ObservableObject
    {
        [ObservableProperty] private ObservableCollection<PendingScanItem> items = [];

        [ObservableProperty] private string? countText;

        public int TotalCount => Items.Count;

        /// <summary>Whether any scan is queued for the selected backend — the others can only be discarded.</summary>
        public bool HasSendable => Items.Any(x => x.CanSend);

        public void Load(IEnumerable<PendingScan> scans, InventoryBackend activeBackend)
        {
            Items = new ObservableCollection<PendingScanItem>(scans.Select(x => ToItem(x, activeBackend)));

            var rejected = Items.Count(x => x.Error != null);

            CountText = string.Format(Items.Count == 1 ? AppResources.Pending_CountOne : AppResources.Pending_CountMany, Items.Count);
            if (rejected > 0) CountText += " · " + string.Format(AppResources.Pending_CountRejected, rejected);

            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(HasSendable));
        }

        private static PendingScanItem ToItem(PendingScan scan, InventoryBackend activeBackend)
        {
            var info = scan.Info;
            var title = info.DetailedFilamentType ?? info.FilamentType ?? AppResources.Spools_UnknownFilament;
            var canSend = scan.Backend == activeBackend;

            var subtitle = string.Join(" · ", new[]
            {
                canSend ? null : string.Format(AppResources.Pending_ForBackend, scan.Backend.DisplayName()),
                scan.Location,
                info.TrayUid ?? info.SerialNumber,
                scan.QueuedAt?.ToString("yyyy-MM-dd HH:mm")
            }.Where(x => !string.IsNullOrEmpty(x)));

            // Same colour the read-only card shows for this tag.
            var color = SpoolDisplayInfo.From(info, null).ColorHexes.FirstOrDefault() ?? "#00000000";

            return new PendingScanItem(scan.Id, title, subtitle, color, scan.LastError, canSend, scan);
        }
    }
}
