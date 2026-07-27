namespace AnalyseTool.Fragments
{
    /// <summary>
    /// The 22-character base64 identifier IFC uses (IfcGloballyUniqueId).
    /// <para>
    /// This is the key that makes a Revit model and an IFC file comparable. Both sides can be written
    /// to .frag — the Revit model by our writer, the IFC file by That Open's own importer — but only
    /// if our items carry the same identifier the IFC side will carry. Revit's own IFC exporter
    /// derives it from <c>ExportUtils.GetExportId</c>, and this is the encoding it applies to the
    /// result, so the two agree element for element.
    /// </para>
    /// <para>
    /// Encoding per the IFC specification: the 128-bit GUID is read big-endian and split into six
    /// groups — one of 8 bits and five of 24 — each written base-64 into 2 and 4 characters
    /// respectively, most significant character first.
    /// </para>
    /// </summary>
    public static class IfcGuid
    {
        // Not RFC 4648 base64: IFC has its own alphabet and ordering.
        private const string Alphabet =
            "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

        private static readonly int[] GroupLengths = { 2, 4, 4, 4, 4, 4 };

        /// <summary>Encodes a GUID as its 22-character IFC form.</summary>
        public static string From(Guid guid)
        {
            byte[] b = guid.ToByteArray();

            // Guid.ToByteArray() is little-endian for the first three fields, so the indices below
            // put the bytes back in the order the specification reads them.
            uint[] groups =
            {
                b[3],
                (uint)(b[2] << 16 | b[1] << 8 | b[0]),
                (uint)(b[5] << 16 | b[4] << 8 | b[7]),
                (uint)(b[6] << 16 | b[8] << 8 | b[9]),
                (uint)(b[10] << 16 | b[11] << 8 | b[12]),
                (uint)(b[13] << 16 | b[14] << 8 | b[15]),
            };

            char[] buffer = new char[22];
            int offset = 0;
            for (int g = 0; g < 6; g++)
            {
                uint value = groups[g];
                int length = GroupLengths[g];
                for (int c = 0; c < length; c++)
                {
                    buffer[offset + length - c - 1] = Alphabet[(int)(value % 64)];
                    value /= 64;
                }

                offset += length;
            }

            return new string(buffer);
        }

        /// <summary>
        /// Decodes the 22-character IFC form back into a GUID — the direction needed when matching an
        /// item that came from an IFC file against a Revit element.
        /// </summary>
        public static Guid Parse(string ifcGuid)
        {
            if (ifcGuid is null) throw new ArgumentNullException(nameof(ifcGuid));
            if (ifcGuid.Length != 22)
                throw new FormatException($"An IFC guid is 22 characters, got {ifcGuid.Length}.");

            uint[] groups = new uint[6];
            int offset = 0;
            for (int g = 0; g < 6; g++)
            {
                uint value = 0;
                for (int c = 0; c < GroupLengths[g]; c++)
                {
                    int digit = Alphabet.IndexOf(ifcGuid[offset + c]);
                    if (digit < 0)
                        throw new FormatException(
                            $"'{ifcGuid[offset + c]}' is not part of the IFC guid alphabet.");
                    value = value * 64 + (uint)digit;
                }

                groups[g] = value;
                offset += GroupLengths[g];
            }

            byte[] b = new byte[16];
            b[3] = (byte)groups[0];
            b[2] = (byte)(groups[1] >> 16); b[1] = (byte)(groups[1] >> 8); b[0] = (byte)groups[1];
            b[5] = (byte)(groups[2] >> 16); b[4] = (byte)(groups[2] >> 8); b[7] = (byte)groups[2];
            b[6] = (byte)(groups[3] >> 16); b[8] = (byte)(groups[3] >> 8); b[9] = (byte)groups[3];
            b[10] = (byte)(groups[4] >> 16); b[11] = (byte)(groups[4] >> 8); b[12] = (byte)groups[4];
            b[13] = (byte)(groups[5] >> 16); b[14] = (byte)(groups[5] >> 8); b[15] = (byte)groups[5];

            return new Guid(b);
        }

        /// <summary>
        /// Extracts the GUID part of a Revit <c>UniqueId</c> ("&lt;guid&gt;-&lt;8 hex&gt;").
        /// <para>
        /// A fallback only. Revit's IFC exporter uses <c>ExportUtils.GetExportId</c>, which folds the
        /// episode suffix in, and that is what a matching IFC file will contain — so prefer it and
        /// reach for this only where the Revit API is out of reach.
        /// </para>
        /// </summary>
        public static bool TryGuidFromUniqueId(string uniqueId, out Guid guid)
        {
            guid = default;
            if (string.IsNullOrEmpty(uniqueId) || uniqueId.Length < 36) return false;
            return Guid.TryParse(uniqueId.Substring(0, 36), out guid);
        }
    }
}
