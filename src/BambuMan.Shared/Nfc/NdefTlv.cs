using System.Text;

namespace BambuMan.Shared.Nfc
{
    /// <summary>
    /// Decodes the NDEF message from raw NFC Forum Type 2 tag memory (NTAG21x user pages from 0x04 on), for readers
    /// that only hand out raw pages (PC/SC). Android and iOS decode NDEF themselves.
    /// </summary>
    public static class NdefTlv
    {
        private const byte NullTlv = 0x00;
        private const byte NdefMessageTlv = 0x03;
        private const byte TerminatorTlv = 0xFE;

        /// <summary>
        /// The NDEF message TLV's value: its offset in <paramref name="memory"/> and length, or null when there is no
        /// NDEF message (yet — a caller reading page by page can read on until offset + length is covered).
        /// </summary>
        public static (int Offset, int Length)? FindMessage(byte[] memory)
        {
            var index = 0;

            while (index < memory.Length)
            {
                var type = memory[index++];

                if (type == NullTlv) continue;
                if (type == TerminatorTlv || index >= memory.Length) return null;

                int length = memory[index++];

                if (length == 0xFF)
                {
                    if (index + 2 > memory.Length) return null;

                    length = memory[index] << 8 | memory[index + 1];
                    index += 2;
                }

                if (type == NdefMessageTlv) return (index, length);

                index += length;
            }

            return null;
        }

        /// <summary>The records of the NDEF message in <paramref name="memory"/>; empty when there is none or it is cut short.</summary>
        public static NfcNdefRecord[] ParseRecords(byte[] memory)
        {
            if (FindMessage(memory) is not { } message || message.Offset + message.Length > memory.Length) return [];

            var data = memory.AsSpan(message.Offset, message.Length);
            var records = new List<NfcNdefRecord>();
            var index = 0;

            while (index < data.Length)
            {
                var header = data[index++];
                var shortRecord = (header & 0x10) != 0;
                var hasId = (header & 0x08) != 0;
                var chunked = (header & 0x20) != 0;
                var tnf = (NfcNdefTypeFormat)(header & 0x07);

                if (index >= data.Length) break;
                int typeLength = data[index++];

                int payloadLength;

                if (shortRecord)
                {
                    if (index >= data.Length) break;
                    payloadLength = data[index++];
                }
                else
                {
                    if (index + 4 > data.Length) break;
                    payloadLength = data[index] << 24 | data[index + 1] << 16 | data[index + 2] << 8 | data[index + 3];
                    index += 4;
                }

                var idLength = 0;

                if (hasId)
                {
                    if (index >= data.Length) break;
                    idLength = data[index++];
                }

                if (payloadLength < 0 || index + typeLength + idLength + payloadLength > data.Length) break;

                var type = data.Slice(index, typeLength);
                index += typeLength + idLength;

                var payload = data.Slice(index, payloadLength).ToArray();
                index += payloadLength;

                // Chunked records only appear in messages too large for these tags; they are not reassembled.
                if (chunked) continue;

                records.Add(new NfcNdefRecord
                {
                    TypeFormat = tnf,
                    MimeType = tnf == NfcNdefTypeFormat.Mime ? Encoding.ASCII.GetString(type) : "text/plain",
                    Payload = payload
                });

                if ((header & 0x40) != 0) break; // message end
            }

            return records.ToArray();
        }
    }
}
