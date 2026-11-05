namespace BambuMan.Shared.Enums
{
    /// <summary>What became of a scan sent to an inventory backend — see <see cref="Managers.BaseManager.SendScanAsync"/>.</summary>
    public enum ScanSendStatus
    {
        /// <summary>The backend created or found the spool.</summary>
        Inventoried,

        /// <summary>
        /// The backend was reached but the spool wasn't inventoried (no or ambiguous filament match, the server refused
        /// the request). Sending it again unchanged gives the same answer.
        /// </summary>
        Rejected,

        /// <summary>The backend couldn't be reached (no network, server down, health check failing). Worth sending again once it's back.</summary>
        Unreachable
    }
}
