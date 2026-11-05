using BambuMan.Shared.Enums;
using BambuMan.Shared.Models;
using BambuMan.Shared.Nfc;
using System.Text;

namespace BambuMan.Shared.Test.Tags
{
    [Trait("Category", "SpoolTags")]
    public class SpoolTagWriteTests
    {
        private static readonly byte[] Uid = [0x04, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06];

        /// <summary>User memory of a blank NDEF-formatted NTAG: an empty NDEF TLV, the terminator, then zeros.</summary>
        private static byte[] BlankMemory()
        {
            var memory = new byte[TigerTag.DataLength];
            memory[0] = 0x03;
            memory[2] = 0xFE;
            return memory;
        }

        private static NfcNdefRecord Mime(string json) => new() { TypeFormat = NfcNdefTypeFormat.Mime, MimeType = OpenSpool.MimeType, Payload = Encoding.UTF8.GetBytes(json) };

        [Fact(DisplayName = "Write: a blank NDEF tag whose pages were read may be written")]
        public void CanOverwrite_Blank()
        {
            Assert.True(SpoolTagReader.CanOverwrite(new TagInfo(Uid, true) { UserMemory = BlankMemory() }));
        }

        [Fact(DisplayName = "Write: an OpenSpool tag may be rewritten")]
        public void CanOverwrite_OpenSpool()
        {
            var json = """{"protocol":"openspool","version":"1.0","type":"PLA","color_hex":"FFAABB","brand":"Generic","min_temp":"220","max_temp":"240"}""";

            Assert.True(SpoolTagReader.CanOverwrite(new TagInfo(Uid, true) { Records = [Mime(json)], UserMemory = BlankMemory() }));
        }

        [Fact(DisplayName = "Write: a tag whose raw pages couldn't be read is never written")]
        public void CanOverwrite_RawReadFailed()
        {
            Assert.False(SpoolTagReader.CanOverwrite(new TagInfo(Uid, true)));
        }

        [Fact(DisplayName = "Write: a tag holding other NDEF data, or a Bambu Lab tag, is not overwritten")]
        public void CanOverwrite_OtherData()
        {
            var text = new NfcNdefRecord { TypeFormat = NfcNdefTypeFormat.WellKnown, Payload = Encoding.UTF8.GetBytes("\u0002enhello") };

            Assert.False(SpoolTagReader.CanOverwrite(new TagInfo(Uid, true) { Records = [text], UserMemory = BlankMemory() }));
            Assert.False(SpoolTagReader.CanOverwrite(new TagInfo(Uid, true) { Records = [Mime("""{"protocol":"other"}""")], UserMemory = BlankMemory() }));
            Assert.False(SpoolTagReader.CanOverwrite(new BambuFilamentInfo(Uid)));
        }

        [Theory(DisplayName = "Tag type: known tag types and format names parse")]
        [InlineData("bambu_rfid", SpoolTagFormat.BambuLab)]
        [InlineData("BambuLab", SpoolTagFormat.BambuLab)]
        [InlineData("openspool", SpoolTagFormat.OpenSpool)]
        [InlineData(" TigerTag ", SpoolTagFormat.TigerTag)]
        public void FromTagType_Known(string value, SpoolTagFormat expected)
        {
            Assert.Equal(expected, SpoolTagFormatExtensions.FromTagType(value));
        }

        [Theory(DisplayName = "Tag type: numbers and unknown values are rejected")]
        [InlineData("0")]
        [InlineData("1")]
        [InlineData("7")]
        [InlineData("-1")]
        [InlineData("generic")]
        [InlineData("")]
        [InlineData(null)]
        public void FromTagType_Rejected(string? value)
        {
            Assert.Null(SpoolTagFormatExtensions.FromTagType(value));
        }
    }
}
