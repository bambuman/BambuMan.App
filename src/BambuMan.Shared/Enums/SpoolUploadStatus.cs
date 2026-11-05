namespace BambuMan.Shared.Enums
{
    /// <summary>What sending one stored spool to an inventory server did.</summary>
    public enum SpoolUploadStatus
    {
        /// <summary>The server had no spool for the tag, so one was created.</summary>
        Created,

        /// <summary>The server already had the spool (same tray uid / tag), and it was updated rather than duplicated.</summary>
        Updated,

        /// <summary>Nothing was sent, or the server rejected it — see <see cref="Models.SpoolUploadResult.Message"/>.</summary>
        Failed
    }
}
