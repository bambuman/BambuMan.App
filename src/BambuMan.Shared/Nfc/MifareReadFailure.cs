namespace BambuMan.Shared.Nfc
{
    /// <summary>
    /// A MIFARE Classic tag (e.g. a Bambu Lab spool tag) that was found but could not be read.
    /// </summary>
    public class MifareReadFailure : ITagInfo
    {
        #region Properties

        public byte[] Identifier { get; }

        public string? SerialNumber => null;

        public bool IsWritable { get; set; }

        public bool IsEmpty => false;

        public bool IsSupported => false;

        public int Capacity { get; set; }

        public NfcNdefRecord?[]? Records { get; set; }

        /// <summary>Sectors the read needed.</summary>
        public int SectorCount { get; }

        /// <summary>Sectors that authenticated before the read stopped.</summary>
        public int SectorsAuthenticated { get; }

        /// <summary>Blocks read before the read stopped.</summary>
        public int BlocksRead { get; }

        /// <summary>Whether the phone reports the <c>com.nxp.mifare</c> feature (native MIFARE Classic support).</summary>
        public bool? HasMifareFeature { get; }

        /// <summary>Why the read stopped, e.g. <c>authentication failed</c> or the exception type.</summary>
        public string Reason { get; }

        /// <summary>
        /// Not a single sector authenticated on a phone without native MIFARE support: its NFC controller can't unlock
        /// the tag at all. A tag moved away mid-read fails later, after at least one sector, or with an IO exception.
        /// </summary>
        public bool IsAuthenticationUnsupported => SectorsAuthenticated == 0 && Reason == AuthenticationFailed && HasMifareFeature != true;

        public const string AuthenticationFailed = "authentication failed";

        #endregion

        public MifareReadFailure(byte[] identifier, int sectorCount, int sectorsAuthenticated, int blocksRead, bool? hasMifareFeature, string reason)
        {
            Identifier = identifier;
            SectorCount = sectorCount;
            SectorsAuthenticated = sectorsAuthenticated;
            BlocksRead = blocksRead;
            HasMifareFeature = hasMifareFeature;
            Reason = reason;
        }

        /// <summary>Diagnostic summary for logs. Holds no keys or tag ids.</summary>
        public override string ToString() =>
            $"MIFARE read failed ({Reason}): {SectorsAuthenticated}/{SectorCount} sectors authenticated, {BlocksRead} blocks read, " +
            $"com.nxp.mifare: {(HasMifareFeature?.ToString().ToLowerInvariant() ?? "unknown")}";
    }
}
