using System.Text;

namespace AnalyseTool.Tools.Doom
{
    /// <summary>
    /// Reads one map out of a classic Doom WAD file (IWAD or PWAD, vanilla format — Hexen-format
    /// maps with a BEHAVIOR lump are rejected). Only the four lumps needed for geometry are parsed:
    /// VERTEXES, LINEDEFS, SIDEDEFS, SECTORS. Pure file parsing — no Revit types, safe to run off
    /// the Revit thread.
    /// </summary>
    internal static class WadReader
    {
        private const int HeaderSize = 12;
        private const int DirEntrySize = 16;
        private const int LinedefSize = 14;
        private const int SidedefSize = 30;
        private const int VertexSize = 4;
        private const int SectorSize = 26;

        /// <summary>Lumps that may follow a map marker; the first lump NOT in this set ends the map.</summary>
        private static readonly HashSet<string> MapLumps = new(StringComparer.OrdinalIgnoreCase)
        {
            "THINGS", "LINEDEFS", "SIDEDEFS", "VERTEXES", "SEGS",
            "SSECTORS", "NODES", "SECTORS", "REJECT", "BLOCKMAP", "BEHAVIOR", "SCRIPTS"
        };

        public static DoomMap ReadMap(string wadPath, string mapName)
        {
            byte[] wad = File.ReadAllBytes(wadPath);
            if (wad.Length < HeaderSize)
                throw new InvalidDataException("File is too small to be a WAD.");

            string magic = Encoding.ASCII.GetString(wad, 0, 4);
            if (magic != "IWAD" && magic != "PWAD")
                throw new InvalidDataException($"Not a WAD file (magic '{magic}', expected IWAD/PWAD).");

            int lumpCount = BitConverter.ToInt32(wad, 4);
            int dirOffset = BitConverter.ToInt32(wad, 8);
            if (lumpCount < 0 || dirOffset < 0 || dirOffset + (long)lumpCount * DirEntrySize > wad.Length)
                throw new InvalidDataException("WAD directory is out of bounds — corrupt file.");

            var lumps = new (string Name, int Offset, int Size)[lumpCount];
            for (int i = 0; i < lumpCount; i++)
            {
                int e = dirOffset + i * DirEntrySize;
                int offset = BitConverter.ToInt32(wad, e);
                int size = BitConverter.ToInt32(wad, e + 4);
                string name = Encoding.ASCII.GetString(wad, e + 8, 8).TrimEnd('\0');
                lumps[i] = (name, offset, size);
            }

            int marker = Array.FindIndex(lumps, l => l.Name.Equals(mapName, StringComparison.OrdinalIgnoreCase));
            if (marker < 0)
            {
                string available = string.Join(", ", lumps.Where(l => IsMapMarker(l.Name)).Select(l => l.Name));
                throw new InvalidDataException($"Map '{mapName}' not found in WAD. Available maps: {available}");
            }

            byte[]? linedefs = null, sidedefs = null, vertexes = null, sectors = null;
            for (int i = marker + 1; i < lumpCount && MapLumps.Contains(lumps[i].Name); i++)
            {
                var (name, offset, size) = lumps[i];
                if (offset < 0 || size < 0 || offset + (long)size > wad.Length)
                    throw new InvalidDataException($"Lump '{name}' is out of bounds — corrupt WAD.");
                if (name.Equals("BEHAVIOR", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Hexen-format map (BEHAVIOR lump) — only vanilla Doom maps are supported.");

                byte[] data = new byte[size];
                Array.Copy(wad, offset, data, 0, size);
                switch (name.ToUpperInvariant())
                {
                    case "LINEDEFS": linedefs = data; break;
                    case "SIDEDEFS": sidedefs = data; break;
                    case "VERTEXES": vertexes = data; break;
                    case "SECTORS": sectors = data; break;
                }
            }

            if (linedefs == null || sidedefs == null || vertexes == null || sectors == null)
                throw new InvalidDataException($"Map '{mapName}' is missing one of LINEDEFS/SIDEDEFS/VERTEXES/SECTORS.");

            return new DoomMap(
                mapName.ToUpperInvariant(),
                ParseVertexes(vertexes),
                ParseLinedefs(linedefs),
                ParseSidedefs(sidedefs),
                ParseSectors(sectors));
        }

        private static bool IsMapMarker(string name) =>
            (name.Length == 4 && name[0] == 'E' && char.IsDigit(name[1]) && name[2] == 'M' && char.IsDigit(name[3])) ||
            (name.Length == 5 && name.StartsWith("MAP", StringComparison.OrdinalIgnoreCase) &&
             char.IsDigit(name[3]) && char.IsDigit(name[4]));

        private static List<DoomVertex> ParseVertexes(byte[] data)
        {
            var list = new List<DoomVertex>(data.Length / VertexSize);
            for (int o = 0; o + VertexSize <= data.Length; o += VertexSize)
                list.Add(new DoomVertex(BitConverter.ToInt16(data, o), BitConverter.ToInt16(data, o + 2)));
            return list;
        }

        private static List<DoomLinedef> ParseLinedefs(byte[] data)
        {
            if (data.Length % LinedefSize != 0)
                throw new InvalidDataException("LINEDEFS lump has unexpected size — not a vanilla Doom map.");
            var list = new List<DoomLinedef>(data.Length / LinedefSize);
            for (int o = 0; o < data.Length; o += LinedefSize)
            {
                int v1 = BitConverter.ToUInt16(data, o);
                int v2 = BitConverter.ToUInt16(data, o + 2);
                int right = BitConverter.ToUInt16(data, o + 10);
                int left = BitConverter.ToUInt16(data, o + 12);
                list.Add(new DoomLinedef(v1, v2, right == 0xFFFF ? -1 : right, left == 0xFFFF ? -1 : left));
            }
            return list;
        }

        private static List<DoomSidedef> ParseSidedefs(byte[] data)
        {
            var list = new List<DoomSidedef>(data.Length / SidedefSize);
            for (int o = 0; o + SidedefSize <= data.Length; o += SidedefSize)
                list.Add(new DoomSidedef(BitConverter.ToUInt16(data, o + 28)));
            return list;
        }

        private static List<DoomSector> ParseSectors(byte[] data)
        {
            var list = new List<DoomSector>(data.Length / SectorSize);
            for (int o = 0; o + SectorSize <= data.Length; o += SectorSize)
                list.Add(new DoomSector(BitConverter.ToInt16(data, o), BitConverter.ToInt16(data, o + 2)));
            return list;
        }
    }
}
