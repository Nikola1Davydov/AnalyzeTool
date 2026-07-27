using Xunit;

namespace AnalyseTool.Fragments.Tests
{
    /// <summary>
    /// The expected strings come from an independent implementation of the same specification
    /// (tools/ifc_guid_reference.py), so a mistake in the C# does not quietly validate itself.
    /// </summary>
    public class IfcGuidTests
    {
        [Theory]
        [InlineData("00000000-0000-0000-0000-000000000000", "0000000000000000000000")]
        [InlineData("ffffffff-ffff-ffff-ffff-ffffffffffff", "3$$$$$$$$$$$$$$$$$$$$$")]
        [InlineData("12345678-1234-5678-1234-567812345678", "0ID5Pu4ZHMU18qLdWID5Pu")]
        [InlineData("d8e2c1a4-5b6f-4c3d-9e8a-7f6b5c4d3e2f", "3Oui6aMszCFPwAVsjSJJul")]
        [InlineData("0a1b2c3d-4e5f-6071-8293-a4b5c6d7e8f9", "0A6omzJbzWSOAJfBN6r_Zv")]
        public void Encodes_the_way_the_ifc_specification_says(string guid, string expected)
        {
            Assert.Equal(expected, IfcGuid.From(Guid.Parse(guid)));
        }

        [Fact]
        public void Encoding_is_always_22_characters_from_the_ifc_alphabet()
        {
            const string alphabet =
                "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

            for (int i = 0; i < 500; i++)
            {
                string encoded = IfcGuid.From(Guid.NewGuid());
                Assert.Equal(22, encoded.Length);
                Assert.All(encoded, c => Assert.Contains(c, alphabet));
            }
        }

        [Fact]
        public void Decoding_recovers_the_original_guid()
        {
            for (int i = 0; i < 500; i++)
            {
                Guid original = Guid.NewGuid();
                Assert.Equal(original, IfcGuid.Parse(IfcGuid.From(original)));
            }
        }

        [Theory]
        [InlineData("too-short")]
        [InlineData("0ID5Pu4ZHMU18qLdWID5Pu!")]
        public void Malformed_input_is_rejected(string bad)
        {
            Assert.ThrowsAny<FormatException>(() => IfcGuid.Parse(bad));
        }

        [Fact]
        public void Guid_part_of_a_revit_unique_id_can_be_read_as_a_fallback()
        {
            // Revit UniqueId = <guid>-<8 hex episode>.
            Assert.True(IfcGuid.TryGuidFromUniqueId(
                "d8e2c1a4-5b6f-4c3d-9e8a-7f6b5c4d3e2f-000a1b2c", out Guid guid));
            Assert.Equal(Guid.Parse("d8e2c1a4-5b6f-4c3d-9e8a-7f6b5c4d3e2f"), guid);

            Assert.False(IfcGuid.TryGuidFromUniqueId("not-a-unique-id", out _));
        }
    }
}
