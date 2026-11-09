using System.Text;

namespace Guanacaste3D.Core.Import;

/// <summary>
/// Minimal reader for OLE structured storage (compound file binary format), the container used by Revit
/// (.rvt, .rfa, .rte), older Office files and others. Only the pieces needed to look up and read named
/// streams are implemented, with random access so a 400 MB project is never loaded into memory.
/// </summary>
internal sealed class CompoundFile : IDisposable
{
    private const uint EndOfChain = 0xFFFFFFFE;
    private const uint FreeSector = 0xFFFFFFFF;
    private static readonly byte[] Signature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private readonly FileStream _stream;
    private readonly int _sectorSize;
    private readonly int _miniSectorSize;
    private readonly int _miniCutoff;
    private readonly uint[] _fat;
    private readonly uint[] _miniFat;
    private readonly List<Entry> _entries = [];
    private byte[]? _miniStream;

    private sealed record Entry(string Name, byte Type, uint StartSector, long Size);

    private CompoundFile(FileStream stream, int sectorSize, int miniSectorSize, int miniCutoff, uint[] fat,
        uint[] miniFat)
    {
        _stream = stream;
        _sectorSize = sectorSize;
        _miniSectorSize = miniSectorSize;
        _miniCutoff = miniCutoff;
        _fat = fat;
        _miniFat = miniFat;
    }

    /// <summary>Opens a compound file, or throws <see cref="InvalidDataException"/> when the container is damaged.</summary>
    public static CompoundFile Open(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, FileOptions.RandomAccess);
        try
        {
            var header = new byte[512];
            if (stream.Read(header, 0, 512) < 512 || !header.AsSpan(0, 8).SequenceEqual(Signature))
                throw new InvalidDataException("The file is not a Revit file.");
            var sectorSize = 1 << BitConverter.ToUInt16(header, 30);
            var miniSectorSize = 1 << BitConverter.ToUInt16(header, 32);
            if (sectorSize is not (512 or 4096) || miniSectorSize is < 16 or > 4096)
                throw new InvalidDataException("The file is not a Revit file.");
            var fatSectors = BitConverter.ToInt32(header, 44);
            var directoryStart = BitConverter.ToUInt32(header, 48);
            var miniCutoff = BitConverter.ToInt32(header, 56);
            var miniFatStart = BitConverter.ToUInt32(header, 60);
            var difatStart = BitConverter.ToUInt32(header, 68);
            var difatCount = BitConverter.ToInt32(header, 72);
            if (fatSectors < 0 || fatSectors > 1 << 20) throw new InvalidDataException("The file is not a Revit file.");

            // The first 109 FAT sector numbers live in the header; larger files chain extra DIFAT sectors.
            var fatSectorList = new List<uint>();
            for (var i = 0; i < 109 && fatSectorList.Count < fatSectors; i++)
                fatSectorList.Add(BitConverter.ToUInt32(header, 76 + 4 * i));
            var difat = difatStart;
            var perDifat = sectorSize / 4 - 1;
            for (var i = 0; i < difatCount && difat != EndOfChain && difat != FreeSector; i++)
            {
                var sector = ReadSector(stream, sectorSize, difat);
                for (var k = 0; k < perDifat && fatSectorList.Count < fatSectors; k++)
                    fatSectorList.Add(BitConverter.ToUInt32(sector, 4 * k));
                difat = BitConverter.ToUInt32(sector, 4 * perDifat);
            }

            var fat = new uint[fatSectorList.Count * (sectorSize / 4)];
            for (var i = 0; i < fatSectorList.Count; i++)
            {
                var sector = ReadSector(stream, sectorSize, fatSectorList[i]);
                Buffer.BlockCopy(sector, 0, fat, i * sectorSize, sectorSize);
            }

            var file = new CompoundFile(stream, sectorSize, miniSectorSize, miniCutoff, fat, []);
            var miniFatBytes = file.ReadChain(miniFatStart, long.MaxValue);
            var miniFat = new uint[miniFatBytes.Length / 4];
            Buffer.BlockCopy(miniFatBytes, 0, miniFat, 0, miniFat.Length * 4);
            var opened = new CompoundFile(stream, sectorSize, miniSectorSize, miniCutoff, fat, miniFat);
            opened.ReadDirectory(directoryStart);
            return opened;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static byte[] ReadSector(FileStream stream, int sectorSize, uint sector)
    {
        var buffer = new byte[sectorSize];
        stream.Position = (sector + 1L) * sectorSize;
        if (stream.Read(buffer, 0, sectorSize) < sectorSize) throw new InvalidDataException("The file is not a Revit file.");
        return buffer;
    }

    private byte[] ReadChain(uint start, long size)
    {
        using var output = new MemoryStream();
        var sector = start;
        var guard = 0;
        while (sector != EndOfChain && sector != FreeSector && output.Length < size && guard++ < _fat.Length + 1)
        {
            if (sector >= _fat.Length) throw new InvalidDataException("The file is not a Revit file.");
            output.Write(ReadSector(_stream, _sectorSize, sector));
            sector = _fat[sector];
        }
        var bytes = output.ToArray();
        return size < bytes.Length ? bytes[..(int)size] : bytes;
    }

    private void ReadDirectory(uint start)
    {
        var data = ReadChain(start, long.MaxValue);
        for (var offset = 0; offset + 128 <= data.Length; offset += 128)
        {
            var nameLength = BitConverter.ToUInt16(data, offset + 64);
            var type = data[offset + 66];
            if (type == 0 || nameLength < 2 || nameLength > 64)
            {
                _entries.Add(new Entry("", 0, 0, 0));
                continue;
            }
            var name = Encoding.Unicode.GetString(data, offset, nameLength - 2);
            var startSector = BitConverter.ToUInt32(data, offset + 116);
            var size = (long)BitConverter.ToUInt64(data, offset + 120);
            _entries.Add(new Entry(name, type, startSector, size));
        }
        if (_entries.Count == 0 || _entries[0].Type != 5) throw new InvalidDataException("The file is not a Revit file.");
    }

    /// <summary>Names of all streams in the file, at any depth.</summary>
    public IEnumerable<string> StreamNames => _entries.Where(entry => entry.Type == 2).Select(entry => entry.Name);

    /// <summary>Size of a named stream, or −1 when it is absent.</summary>
    public long StreamSize(string name) => _entries.FirstOrDefault(entry => entry.Type == 2 && entry.Name == name)?.Size ?? -1;

    /// <summary>Reads a whole stream by name (small streams only: it is returned in memory), or null when absent.</summary>
    public byte[]? ReadStream(string name, long maximumBytes = 16 * 1024 * 1024)
    {
        var entry = _entries.FirstOrDefault(candidate => candidate.Type == 2 && candidate.Name == name);
        if (entry is null || entry.Size > maximumBytes) return null;
        return entry.Size < _miniCutoff ? ReadMini(entry) : ReadChain(entry.StartSector, entry.Size);
    }

    private byte[] ReadMini(Entry entry)
    {
        _miniStream ??= ReadChain(_entries[0].StartSector, _entries[0].Size);
        var output = new byte[entry.Size];
        var sector = entry.StartSector;
        var written = 0;
        var guard = 0;
        while (sector != EndOfChain && sector != FreeSector && written < output.Length && guard++ < _miniFat.Length + 1)
        {
            var offset = (long)sector * _miniSectorSize;
            if (sector >= _miniFat.Length || offset >= _miniStream.Length) break;
            var count = (int)Math.Min(_miniSectorSize, Math.Min(output.Length - written, _miniStream.Length - offset));
            Buffer.BlockCopy(_miniStream, (int)offset, output, written, count);
            written += count;
            sector = _miniFat[sector];
        }
        return output;
    }

    public void Dispose() => _stream.Dispose();
}
