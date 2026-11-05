using BambuMan.Shared.Enums;

namespace BambuMan.Shared.Models
{
    /// <summary>The outcome of sending <paramref name="Total"/> stored spools to <paramref name="Backend"/>.</summary>
    /// <param name="Cancelled">Stopped before every spool was sent; <paramref name="Results"/> holds the ones that were tried.</param>
    public record SpoolUploadSummary(InventoryBackend Backend, IReadOnlyList<SpoolUploadResult> Results, int Total, bool Cancelled)
    {
        public int Created => Results.Count(x => x.Status == SpoolUploadStatus.Created);

        public int Updated => Results.Count(x => x.Status == SpoolUploadStatus.Updated);

        public int Failed => Results.Count(x => x.Status == SpoolUploadStatus.Failed);

        /// <summary>Spools never tried because the batch was cancelled.</summary>
        public int Skipped => Total - Results.Count;

        public IEnumerable<SpoolUploadResult> Failures => Results.Where(x => x.Status == SpoolUploadStatus.Failed);

        /// <summary>Sent spools whose server spool kept its own, lower weight — see <see cref="SpoolUploadResult.KeptServerWeight"/>.</summary>
        public IEnumerable<SpoolUploadResult> WeightsKept => Results.Where(x => x.Status != SpoolUploadStatus.Failed && x.KeptServerWeight != null);

        /// <summary>One line for a toast or the log, e.g. <c>Sent to Spoolman: 3 created, 2 updated (1 kept server weight), 1 failed.</c></summary>
        public string Text
        {
            get
            {
                var weightsKept = WeightsKept.Count();
                var parts = new List<string> { $"{Created} created", weightsKept > 0 ? $"{Updated} updated ({weightsKept} kept server weight)" : $"{Updated} updated" };

                if (Failed > 0) parts.Add($"{Failed} failed");
                if (Skipped > 0) parts.Add($"{Skipped} not sent");

                return $"{(Cancelled ? "Sending cancelled. " : "")}Sent to {Backend.DisplayName()}: {string.Join(", ", parts)}.";
            }
        }
    }
}
