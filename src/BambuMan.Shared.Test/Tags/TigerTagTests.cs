using BambuMan.Shared.Enums;
using BambuMan.Shared.Nfc;
using System.Text;

namespace BambuMan.Shared.Test.Tags
{
    [Trait("Category", "SpoolTags")]
    public class TigerTagTests
    {
        private static readonly byte[] Uid = [0x04, 0xA1, 0xB2, 0xC3, 0xD4, 0xE5, 0xF6];

        /// <summary>
        /// User memory (page 0x04 on, 80 bytes) of the guide's example 4, "TigerTag-encoded Rosa3D red PLA", assembled
        /// from its field table at the offsets of §2.
        /// </summary>
        private static byte[] Rosa3DRedPla(byte aspect2 = 0x00, byte type = TigerTag.TypeFilament, uint id = TigerTag.IdTigerTag)
        {
            var memory = new List<byte>();

            memory.AddRange([(byte)(id >> 24), (byte)(id >> 16), (byte)(id >> 8), (byte)id]); // 0x04 ID TigerTag
            memory.AddRange([0xFF, 0xFF, 0xFF, 0xFF]);                                         // 0x05 ID Product
            memory.AddRange([0x95, 0x4B, 0x68, aspect2]);                                      // 0x06 material PLA, aspect 1 Basic, aspect 2
            // The guide prints Rosa3D's brand id as "0x4E19 = 19961"; the hex is a typo (0x4E19 is 19993), the table id is 19961 = 0x4DF9.
            memory.AddRange([type, 0x38, 0x4D, 0xF9]);                                         // 0x07 type, 1.75 mm, brand Rosa3D
            memory.AddRange([0xFF, 0x00, 0x00, 0xFF]);                                         // 0x08 colour 1 RGBA
            memory.AddRange([0x00, 0x03, 0xE8, 0x15]);                                         // 0x09 1000, unit g
            memory.AddRange([0x00, 0xC3, 0x00, 0xE6]);                                         // 0x0A nozzle 195–230
            memory.AddRange([0x32, 0x05, 0x32, 0x3C]);                                         // 0x0B dry 50 °C 5 h, bed 50–60
            memory.AddRange([0x2F, 0x7A, 0x0A, 0x5C]);                                         // 0x0C timestamp
            memory.AddRange([0x00, 0x80, 0xFF, 0x12]);                                         // 0x0D colour 2 RGB, tag 1 of 2
            memory.AddRange([0x11, 0x22, 0x33, 0x00]);                                         // 0x0E colour 3 RGB
            memory.AddRange([0x00, 0xE6, 0x00, 0x00]);                                         // 0x0F TD 23.0

            var message = new byte[28];
            Encoding.UTF8.GetBytes("Starter Red").CopyTo(message, 0);
            memory.AddRange(message);                                                          // 0x10–0x16 message

            memory.AddRange([0x00, 0x02, 0xEE, 0x00]);                                         // 0x17 750 available

            return memory.ToArray();
        }

        [Fact(DisplayName = "TigerTag: a TigerTag is never overwritten with an OpenSpool record")]
        public void Write_RefusesTigerTag()
        {
            Assert.False(SpoolTagReader.CanOverwrite(new TagInfo(Uid, false) { UserMemory = Rosa3DRedPla() }));
            Assert.False(SpoolTagReader.CanOverwrite(new TagInfo(Uid, true) { UserMemory = Rosa3DRedPla() }));
        }

        [Fact(DisplayName = "TigerTag: the guide's Rosa3D red PLA example decodes with its ids resolved")]
        public void Parse_Rosa3DExample()
        {
            var tag = TigerTag.TryParse(Uid, Rosa3DRedPla());

            Assert.NotNull(tag);
            Assert.Equal(SpoolTagFormat.TigerTag, tag.Format);
            Assert.Equal("Rosa3D", tag.Brand);
            Assert.Equal("PLA", tag.FilamentType);
            Assert.Equal("Basic", tag.Subtype);
            Assert.Equal("PLA Basic", tag.DetailedFilamentType);
            Assert.Equal("FF0000FF", tag.Color);
            Assert.Empty(tag.AdditionalColors);
            Assert.Equal((ushort)1000, tag.SpoolWeight);
            Assert.Equal((ushort)750, tag.RemainingWeight);
            Assert.Equal(1.75f, tag.FilamentDiameter);
            Assert.Equal((ushort)195, tag.MinTemperatureForHotend);
            Assert.Equal((ushort)230, tag.MaxTemperatureForHotend);
            Assert.Equal((ushort)50, tag.DryingTemperature);
            Assert.Equal((ushort)5, tag.DryingTime);
            Assert.Equal((ushort)50, tag.BedTemperature);
            Assert.Equal(new DateTime(2025, 3, 29, 1, 33, 16, DateTimeKind.Utc), tag.ProductionDateTime);
            Assert.Equal("Starter Red", tag.Message);
            Assert.Null(tag.ProductId);
            Assert.Equal("04A1B2C3D4E5F6", tag.SpoolId);
        }

        [Fact(DisplayName = "TigerTag: a TigerTag+ id is read the same way and keeps its product id")]
        public void Parse_TigerTagPlus()
        {
            var memory = Rosa3DRedPla(id: TigerTag.IdTigerTagPlus);
            memory[4] = 0x00; memory[5] = 0x00; memory[6] = 0x00; memory[7] = 0x0A;

            var tag = TigerTag.TryParse(Uid, memory);

            Assert.NotNull(tag);
            Assert.Equal(10, tag.ProductId);
        }

        [Fact(DisplayName = "TigerTag: a bicolor aspect adds the second colour")]
        public void Parse_Bicolor()
        {
            var tag = TigerTag.TryParse(Uid, Rosa3DRedPla(aspect2: 252));

            Assert.NotNull(tag);
            Assert.Equal(["0080FFFF"], tag.AdditionalColors);
            Assert.Equal("Basic", tag.Subtype); // Bicolor is a colour composition, not a finish
        }

        [Fact(DisplayName = "TigerTag: a tricolor aspect adds the second and third colour")]
        public void Parse_Tricolor()
        {
            var tag = TigerTag.TryParse(Uid, Rosa3DRedPla(aspect2: 24));

            Assert.NotNull(tag);
            Assert.Equal(["0080FFFF", "112233FF"], tag.AdditionalColors);
        }

        [Fact(DisplayName = "TigerTag: only the fields before the message are required")]
        public void Parse_ShortRead()
        {
            var tag = TigerTag.TryParse(Uid, Rosa3DRedPla()[..48]);

            Assert.NotNull(tag);
            Assert.Equal("PLA", tag.FilamentType);
            Assert.Null(tag.Message);
            Assert.Null(tag.RemainingWeight);
        }

        [Fact(DisplayName = "TigerTag: an initialised blank, a resin bottle, a short read or other data is not a spool")]
        public void Parse_RejectsNonSpools()
        {
            Assert.Null(TigerTag.TryParse(Uid, Rosa3DRedPla(id: TigerTag.IdTigerTagInit)));
            Assert.Null(TigerTag.TryParse(Uid, Rosa3DRedPla(type: 0xAD)));
            Assert.Null(TigerTag.TryParse(Uid, Rosa3DRedPla()[..40]));
            Assert.Null(TigerTag.TryParse(Uid, new byte[80]));
            Assert.Null(TigerTag.TryParse(Uid, null));
        }

        [Fact(DisplayName = "TigerTag: an id missing from the reference tables leaves that field empty")]
        public void Parse_UnknownIds()
        {
            var memory = Rosa3DRedPla();
            memory[8] = 0x00; memory[9] = 0x01;   // material 1 — not in the table
            memory[14] = 0xFF; memory[15] = 0xFE; // brand 65534 — not in the table

            var tag = TigerTag.TryParse(Uid, memory);

            Assert.NotNull(tag);
            Assert.Null(tag.FilamentType);
            Assert.Null(tag.Brand);
            Assert.Equal("FF0000FF", tag.Color);
        }

        [Fact(DisplayName = "SpoolTagReader: raw NTAG user memory holding a TigerTag reads as one")]
        public void Reader_ReadsTigerTag()
        {
            var tag = SpoolTagReader.TryRead(new TagInfo(Uid, isNdef: false) { UserMemory = Rosa3DRedPla() });

            Assert.NotNull(tag);
            Assert.Equal(SpoolTagFormat.TigerTag, tag.Format);
            Assert.Equal("Rosa3D", tag.Brand);
        }
    }
}
