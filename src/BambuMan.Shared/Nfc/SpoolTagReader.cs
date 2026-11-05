using BambuMan.Shared.Enums;
using BambuMan.Shared.Models;

namespace BambuMan.Shared.Nfc
{
    /// <summary>
    /// Recognises a spool tag in whatever a platform's NFC layer read. Bambu Lab tags arrive already decoded (the
    /// platform has to derive the MIFARE keys to read them at all); NTAG-based formats arrive as NDEF records and/or
    /// raw user memory on a <see cref="TagInfo"/> and are decoded here, so every platform shares one parser.
    /// </summary>
    public static class SpoolTagReader
    {
        /// <summary>The spool tag in <paramref name="tagInfo"/>, or null when it holds no format BambuMan knows.</summary>
        public static ISpoolTag? TryRead(ITagInfo? tagInfo)
        {
            switch (tagInfo)
            {
                case null:
                    return null;
                case ISpoolTag spoolTag:
                    return spoolTag;
            }

            if (OpenSpool.TryParse(tagInfo.Identifier, tagInfo.Records) is { } openSpool)
            {
                openSpool.IsWritable = tagInfo.IsWritable;
                openSpool.Capacity = tagInfo.Capacity;
                openSpool.Records = tagInfo.Records;
                return openSpool;
            }

            if (tagInfo is TagInfo { UserMemory: { } memory } && TigerTag.TryParse(tagInfo.Identifier, memory) is { } tigerTag)
            {
                tigerTag.IsWritable = tagInfo.IsWritable;
                tigerTag.Capacity = tagInfo.Capacity;
                return tigerTag;
            }

            return null;
        }

        /// <summary>
        /// Whether an OpenSpool record may replace what <paramref name="tagInfo"/> holds: only when the raw user pages
        /// were read (so a TigerTag, which isn't NDEF, is ruled out) and the tag is NDEF-empty or already OpenSpool.
        /// </summary>
        public static bool CanOverwrite(ITagInfo tagInfo) =>
            tagInfo is TagInfo { UserMemory: not null } && TryRead(tagInfo) switch
            {
                { Format: SpoolTagFormat.OpenSpool } => true,
                null => tagInfo.IsEmpty,
                _ => false
            };
    }
}
