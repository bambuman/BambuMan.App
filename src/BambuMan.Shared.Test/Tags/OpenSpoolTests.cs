using BambuMan.Shared.Enums;
using BambuMan.Shared.Models;
using BambuMan.Shared.Nfc;
using BambuMan.Shared.Test.Bambuddy;
using Newtonsoft.Json;
using System.Text;

namespace BambuMan.Shared.Test.Tags
{
    [Trait("Category", "SpoolTags")]
    public class OpenSpoolTests
    {
        private static readonly byte[] Uid = [0x04, 0xA1, 0xB2, 0xC3, 0xD4, 0xE5, 0xF6];

        /// <summary>The example record from the OpenSpool readme — numbers written as strings.</summary>
        private const string ReadmeExample = """{"protocol":"openspool","version":"1.0","type":"PLA","color_hex":"FFAABB","brand":"Generic","min_temp":"220","max_temp":"240"}""";

        private static NfcNdefRecord MimeRecord(string mimeType, string payload) => new()
        {
            TypeFormat = NfcNdefTypeFormat.Mime,
            MimeType = mimeType,
            Payload = Encoding.UTF8.GetBytes(payload)
        };

        #region Read

        [Fact(DisplayName = "OpenSpool: the readme example parses into the format-neutral tag")]
        public void Parse_ReadmeExample()
        {
            var tag = OpenSpool.TryParse(Uid, ReadmeExample);

            Assert.NotNull(tag);
            Assert.Equal(SpoolTagFormat.OpenSpool, tag.Format);
            Assert.Equal("Generic", tag.Brand);
            Assert.Equal("PLA", tag.FilamentType);
            Assert.Equal("PLA", tag.DetailedFilamentType);
            Assert.Equal("FFAABBFF", tag.Color);
            Assert.Equal((ushort)220, tag.MinTemperatureForHotend);
            Assert.Equal((ushort)240, tag.MaxTemperatureForHotend);
            Assert.Null(tag.SpoolWeight);
            Assert.Equal(ReadmeExample, tag.RawPayload);
        }

        [Fact(DisplayName = "OpenSpool: the tag uid is the spool id, as there is no tray uid")]
        public void Parse_UsesTagUidAsSpoolId()
        {
            var tag = OpenSpool.TryParse(Uid, ReadmeExample)!;

            Assert.Equal("04A1B2C3D4E5F6", tag.SerialNumber);
            Assert.Equal("04A1B2C3D4E5F6", tag.SpoolId);
        }

        [Fact(DisplayName = "OpenSpool: json numbers, a # colour and the optional fields are read too")]
        public void Parse_NumbersAndOptionalFields()
        {
            const string json = """{"protocol":"OpenSpool","version":"1.0","type":"PETG","subtype":"Matte","color_hex":"#00ff00","alpha":"80","brand":"Polymaker","min_temp":230,"max_temp":250.0,"weight":"750","diameter":1.75,"additional_color_hexes":["FF0000","0000FF"]}""";

            var tag = OpenSpool.TryParse(Uid, json);

            Assert.NotNull(tag);
            Assert.Equal("PETG Matte", tag.DetailedFilamentType);
            Assert.Equal("00FF0080", tag.Color);
            Assert.Equal((ushort)230, tag.MinTemperatureForHotend);
            Assert.Equal((ushort)250, tag.MaxTemperatureForHotend);
            Assert.Equal((ushort)750, tag.SpoolWeight);
            Assert.Equal(1.75f, tag.FilamentDiameter);
            Assert.Equal(["FF0000FF", "0000FFFF"], tag.AdditionalColors);
        }

        [Theory(DisplayName = "OpenSpool: anything that isn't an OpenSpool json payload is not a tag")]
        [InlineData("""{"protocol":"opentag3d","type":"PLA"}""")]
        [InlineData("""{"type":"PLA","color_hex":"FFFFFF"}""")]
        [InlineData("""{ not json""")]
        [InlineData("""["openspool"]""")]
        [InlineData("")]
        public void Parse_RejectsOtherPayloads(string json)
        {
            Assert.Null(OpenSpool.TryParse(Uid, json));
        }

        [Fact(DisplayName = "OpenSpool: a bad colour or temperature is dropped, the rest of the tag still reads")]
        public void Parse_ToleratesBadValues()
        {
            var tag = OpenSpool.TryParse(Uid, """{"protocol":"openspool","type":"ABS","color_hex":"nothex","min_temp":"hot","max_temp":""}""");

            Assert.NotNull(tag);
            Assert.Equal("ABS", tag.FilamentType);
            Assert.Null(tag.Color);
            Assert.Null(tag.MinTemperatureForHotend);
            Assert.Null(tag.MaxTemperatureForHotend);
        }

        [Fact(DisplayName = "OpenSpool: the application/json record is picked out of the tag's records")]
        public void Parse_FindsRecordAmongOthers()
        {
            var records = new[]
            {
                null,
                new NfcNdefRecord { TypeFormat = NfcNdefTypeFormat.WellKnown, Payload = Encoding.UTF8.GetBytes("hello") },
                MimeRecord("text/plain", ReadmeExample),
                MimeRecord("Application/JSON", ReadmeExample)
            };

            var tag = OpenSpool.TryParse(Uid, records);

            Assert.NotNull(tag);
            Assert.Equal("Generic", tag.Brand);
        }

        [Fact(DisplayName = "OpenSpool: a tag without an OpenSpool record is not one")]
        public void Parse_NoRecord()
        {
            Assert.Null(OpenSpool.TryParse(Uid, (IEnumerable<NfcNdefRecord?>?)null));
            Assert.Null(OpenSpool.TryParse(Uid, [MimeRecord("text/plain", ReadmeExample)]));
        }

        #endregion

        #region Write

        [Fact(DisplayName = "OpenSpool: a Bambu Lab spool writes the documented 1.0 fields, numbers as strings")]
        public void Write_FromBambuTag()
        {
            var info = JsonConvert.DeserializeObject<BambuFilamentInfo>(SampleTags.AbsBlack)!;

            var json = OpenSpool.CreateJson(info);

            Assert.Equal("""{"protocol":"openspool","version":"1.0","type":"ABS","color_hex":"000000","brand":"Bambu Lab","min_temp":"240","max_temp":"270"}""", json);
        }

        [Fact(DisplayName = "OpenSpool: a written record reads back as the same spool")]
        public void Write_RoundTrips()
        {
            var source = new SpoolTag(SpoolTagFormat.OpenSpool, Uid) { Brand = "Sunlu", FilamentType = "PETG", Color = "1A2B3CFF", MinTemperatureForHotend = 230, MaxTemperatureForHotend = 250 };

            var record = OpenSpool.CreateRecord(source);

            Assert.Equal(NfcNdefTypeFormat.Mime, record.TypeFormat);
            Assert.Equal("application/json", record.MimeType);

            var tag = OpenSpool.TryParse(Uid, [record]);

            Assert.NotNull(tag);
            Assert.Equal("Sunlu", tag.Brand);
            Assert.Equal("PETG", tag.FilamentType);
            Assert.Equal("1A2B3CFF", tag.Color);
            Assert.Equal((ushort)230, tag.MinTemperatureForHotend);
            Assert.Equal((ushort)250, tag.MaxTemperatureForHotend);
        }

        [Fact(DisplayName = "OpenSpool: a spool without a brand is written as Generic")]
        public void Write_DefaultsBrand()
        {
            var json = OpenSpool.CreateJson(new SpoolTag(SpoolTagFormat.OpenSpool, Uid) { FilamentType = "PLA", Color = "FFFFFFFF" });

            Assert.Contains("\"brand\":\"Generic\"", json);
        }

        #endregion

        #region SpoolTagReader

        [Fact(DisplayName = "SpoolTagReader: a Bambu Lab tag is passed through as read")]
        public void Reader_PassesBambuTagThrough()
        {
            var info = JsonConvert.DeserializeObject<BambuFilamentInfo>(SampleTags.AbsBlack)!;

            Assert.Same(info, SpoolTagReader.TryRead(info));
        }

        [Fact(DisplayName = "SpoolTagReader: an NDEF tag with an OpenSpool record reads as an OpenSpool tag")]
        public void Reader_ReadsOpenSpool()
        {
            var tagInfo = new TagInfo(Uid) { IsWritable = true, Capacity = 496, Records = [MimeRecord(OpenSpool.MimeType, ReadmeExample)] };

            var tag = Assert.IsType<SpoolTag>(SpoolTagReader.TryRead(tagInfo));

            Assert.Equal(SpoolTagFormat.OpenSpool, tag.Format);
            Assert.True(tag.IsWritable);
            Assert.Equal(496, tag.Capacity);
        }

        [Fact(DisplayName = "SpoolTagReader: any other tag is not a spool tag")]
        public void Reader_IgnoresOtherTags()
        {
            Assert.Null(SpoolTagReader.TryRead(null));
            Assert.Null(SpoolTagReader.TryRead(new TagInfo(Uid) { Records = [MimeRecord("text/plain", "hello")] }));
            Assert.Null(SpoolTagReader.TryRead(new TagInfo(Uid, isNdef: false) { UserMemory = new byte[80] }));
        }

        #endregion
    }
}
