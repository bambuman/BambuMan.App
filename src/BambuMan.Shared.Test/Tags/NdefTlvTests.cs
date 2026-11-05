using BambuMan.Shared.Enums;
using BambuMan.Shared.Nfc;
using System.Text;

namespace BambuMan.Shared.Test.Tags
{
    [Trait("Category", "SpoolTags")]
    public class NdefTlvTests
    {
        private const string Json = """{"protocol":"openspool","version":"1.0","type":"PLA","color_hex":"FFAABB","brand":"Generic","min_temp":"220","max_temp":"240"}""";

        /// <summary>NTAG user memory from page 0x04: a lock control TLV, then one short mime record, then the terminator.</summary>
        private static byte[] Memory(string mimeType, string payload, bool longLength = false)
        {
            var type = Encoding.ASCII.GetBytes(mimeType);
            var body = Encoding.UTF8.GetBytes(payload);

            var record = new List<byte> { 0xD2, (byte)type.Length, (byte)body.Length }; // MB | ME | SR, TNF mime
            record.AddRange(type);
            record.AddRange(body);

            var memory = new List<byte> { 0x01, 0x03, 0xA0, 0x0C, 0x34 }; // lock control TLV
            memory.Add(0x03);

            if (longLength) memory.AddRange([0xFF, (byte)(record.Count >> 8), (byte)record.Count]);
            else memory.Add((byte)record.Count);

            memory.AddRange(record);
            memory.Add(0xFE);
            memory.AddRange(new byte[16 - memory.Count % 16]); // rest of the last 4-page read

            return memory.ToArray();
        }

        [Fact(DisplayName = "NDEF TLV: a mime record is decoded from raw NTAG memory")]
        public void Parse_MimeRecord()
        {
            var record = Assert.Single(NdefTlv.ParseRecords(Memory("application/json", Json)));

            Assert.Equal(NfcNdefTypeFormat.Mime, record.TypeFormat);
            Assert.Equal("application/json", record.MimeType);
            Assert.Equal(Json, Encoding.UTF8.GetString(record.Payload!));
        }

        [Fact(DisplayName = "NDEF TLV: the three-byte length form is read")]
        public void Parse_LongLength()
        {
            Assert.Single(NdefTlv.ParseRecords(Memory("application/json", Json, longLength: true)));
        }

        [Fact(DisplayName = "NDEF TLV: a message cut short by a partial read yields nothing yet")]
        public void Parse_Truncated()
        {
            var memory = Memory("application/json", Json);
            var partial = memory[..32];

            var message = NdefTlv.FindMessage(partial);

            Assert.NotNull(message);
            Assert.True(message.Value.Offset + message.Value.Length > partial.Length);
            Assert.Empty(NdefTlv.ParseRecords(partial));
        }

        [Fact(DisplayName = "NDEF TLV: empty or non-NDEF memory has no records")]
        public void Parse_NoMessage()
        {
            Assert.Empty(NdefTlv.ParseRecords([0x03, 0x00, 0xFE, 0x00]));
            Assert.Empty(NdefTlv.ParseRecords(new byte[16]));
            Assert.Empty(NdefTlv.ParseRecords([0xFE]));
            Assert.Null(NdefTlv.FindMessage([]));
        }

        [Fact(DisplayName = "NDEF TLV: raw memory with an OpenSpool record reads as an OpenSpool tag")]
        public void Reader_ReadsRawOpenSpool()
        {
            var memory = Memory("application/json", Json);
            var tagInfo = new TagInfo([0x04, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06]) { Records = NdefTlv.ParseRecords(memory), UserMemory = memory };

            var tag = SpoolTagReader.TryRead(tagInfo);

            Assert.NotNull(tag);
            Assert.Equal(SpoolTagFormat.OpenSpool, tag.Format);
            Assert.Equal("Generic", tag.Brand);
        }
    }
}
