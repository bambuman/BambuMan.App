using BambuMan.Components.Common;
using BambuMan.Shared.Matcher;
using BambuMan.Shared.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Globalization;
using ExternalFilament = SpoolMan.Api.Model.ExternalFilament;

namespace BambuMan.UI.Main
{
    /// <summary>One catalog filament in the picker, with what a spool of it would be.</summary>
    public record ManualFilamentItem(ManualSpool Spool, SpoolDisplayInfo Display, string Title, string Subtitle)
    {
        public ExternalFilament Filament => Spool.Filament!;
    }

    /// <summary>
    /// State of the "Add spool" page, for a spool whose tag can't be read: first pick the filament from the catalog (or
    /// enter it as unknown), then fill in the spool's edit card.
    /// </summary>
    public partial class AddSpoolPageViewModel : ObservableObject
    {
        /// <summary>How many search results are listed; the rest only after the search is narrowed down.</summary>
        public const int MaxResults = 50;

        private const int DefaultLabelWeight = 1000;

        private List<ManualFilamentItem> allItems = [];

        [ObservableProperty] private string? search;
        [ObservableProperty] private IReadOnlyList<ManualFilamentItem> results = [];
        [ObservableProperty] private int matchCount;

        /// <summary>The catalog's materials, suggested for an unknown filament.</summary>
        [ObservableProperty] private IReadOnlyList<string> materials = [];

        [ObservableProperty] private ManualFilamentItem? selected;
        [ObservableProperty] private bool isUnknown;

        [ObservableProperty] private string? unknownMaterial;
        [ObservableProperty] private string unknownColor = "#808080";
        [ObservableProperty] private int? unknownLabelWeight = DefaultLabelWeight;
        [ObservableProperty] private string? unknownMaterialError;
        [ObservableProperty] private string? unknownLabelWeightError;

        [ObservableProperty] private SpoolEditModel? edit;

        [ObservableProperty] private bool saving;
        [ObservableProperty] private string? errorMessage;

        /// <summary>True while no filament is chosen yet, so the picker is shown instead of the edit card.</summary>
        public bool IsPicking => Selected == null && !IsUnknown;

        public void Load(IEnumerable<ExternalFilament> filaments)
        {
            var list = filaments.ToList();

            allItems = ExternalFilamentMatcher.Search(list, null).Select(ToItem).ToList();
            Materials = list.Select(x => x.Material).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();

            ApplySearch();
        }

        /// <summary>Pick a catalog filament. An edit card already filled in keeps its price, date, lot and location.</summary>
        public void Select(ManualFilamentItem item, SpoolEditModel defaults)
        {
            Selected = item;
            IsUnknown = false;
            Edit = Merge(defaults);
            ErrorMessage = null;
        }

        /// <summary>Enter the filament as unknown: material, colour and label weight are typed in.</summary>
        public void SelectUnknown(SpoolEditModel defaults)
        {
            Selected = null;
            IsUnknown = true;
            Edit = Merge(defaults);
            ErrorMessage = null;
        }

        /// <summary>Back to the picker, e.g. after picking the wrong filament.</summary>
        public void ClearSelection()
        {
            Selected = null;
            IsUnknown = false;
            ErrorMessage = null;
        }

        /// <summary>
        /// What to save: the picked catalog filament, or the unknown one built from the typed fields. Null (with the
        /// failing fields marked) when the unknown filament is incomplete, or nothing is picked.
        /// </summary>
        public ManualSpool? BuildSpool()
        {
            if (Selected != null) return Selected.Spool;
            if (!IsUnknown) return null;

            UnknownMaterialError = string.IsNullOrWhiteSpace(UnknownMaterial) ? "Enter the material, e.g. PLA." : null;
            UnknownLabelWeightError = UnknownLabelWeight is null or <= 0 ? "Enter the filament weight printed on the label." : null;

            if (UnknownMaterialError != null || UnknownLabelWeightError != null) return null;

            return ManualSpool.Unknown(UnknownMaterial!, UnknownColor, UnknownLabelWeight!.Value);
        }

        partial void OnSearchChanged(string? value) => ApplySearch();

        partial void OnSelectedChanged(ManualFilamentItem? value) => OnPropertyChanged(nameof(IsPicking));

        partial void OnIsUnknownChanged(bool value) => OnPropertyChanged(nameof(IsPicking));

        /// <summary>A full spool's weight follows the label weight, until the weight is typed in by hand.</summary>
        partial void OnUnknownLabelWeightChanged(int? oldValue, int? newValue)
        {
            if (Edit == null || oldValue == null || newValue == null) return;

            if (Edit.Weight == oldValue + Edit.EmptyWeight.GetValueOrDefault()) Edit.Weight = newValue + Edit.EmptyWeight.GetValueOrDefault();
        }

        private void ApplySearch()
        {
            var matches = ExternalFilamentMatcher.Search(allItems.Select(x => x.Filament), Search).Select(x => x.Id).ToHashSet();
            var filtered = allItems.Where(x => matches.Contains(x.Filament.Id)).ToList();

            MatchCount = filtered.Count;
            Results = filtered.Take(MaxResults).ToList();
        }

        /// <summary>
        /// The defaults for the newly picked filament: its full weight always, the rest only while the card is new — a
        /// price or location already entered survives picking another filament.
        /// </summary>
        private SpoolEditModel Merge(SpoolEditModel defaults)
        {
            if (Edit == null) return defaults;

            Edit.Weight = defaults.Weight;
            Edit.EmptyWeight = defaults.EmptyWeight;

            return Edit;
        }

        private static ManualFilamentItem ToItem(ExternalFilament filament)
        {
            var spool = ManualSpool.FromCatalog(filament);
            var details = new[]
            {
                $"{filament.Weight.ToString("0", CultureInfo.InvariantCulture)} g",
                $"{filament.Diameter.ToString("0.00", CultureInfo.InvariantCulture)} mm",
                filament.Translucent == true ? "Translucent" : null,
                filament.ColorHexes is { Count: > 1 } ? $"{filament.ColorHexes.Count} colours" : null
            };

            return new ManualFilamentItem(
                spool,
                SpoolDisplayInfo.From(spool.Info, filament),
                $"{filament.Material} {filament.Name}",
                string.Join(" · ", details.Where(x => x != null)));
        }
    }
}
