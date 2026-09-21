using System.Buffers.Binary;
using System.IO.Compression;
namespace IntoChat.LocalFiles;

internal static class ImageHeader
{
    public static void ValidatePng(ReadOnlySpan<byte> data)
    {
        if (data.Length < 45 || !data[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) { throw new ArgumentException("Export is not a complete PNG."); }
        var size = Read(data);
        if (data[24] != 8 || data[26] != 0 || data[27] != 0 || data[28] != 0)
        { throw new ArgumentException("Export requires a non-interlaced 8-bit PNG."); }
        var channels = data[25] switch { 0 => 1, 2 => 3, 4 => 2, 6 => 4, _ => throw new ArgumentException("Unsupported PNG color format.") };
        using var compressed = new MemoryStream();
        var offset = 8;
        var hasEnd = false;
        while (offset + 12 <= data.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset, 4));
            if (length < 0 || (long)offset + length + 12 > data.Length) { throw new ArgumentException("Export contains a truncated PNG chunk."); }
            var type = data.Slice(offset + 4, 4);
            if (offset == 8 && (!type.SequenceEqual("IHDR"u8) || length != 13)) { throw new ArgumentException("Export has no valid PNG header."); }
            if (offset != 8 && type.SequenceEqual("IHDR"u8)) { throw new ArgumentException("Export has duplicate headers."); }
            uint crc = 0xffffffff;
            foreach (var value in data.Slice(offset + 4, length + 4))
            {
                crc ^= value;
                for (var bit = 0; bit < 8; bit++) { crc = (crc >> 1) ^ ((crc & 1) == 1 ? 0xedb88320u : 0u); }
            }
            if (~crc != BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset + 8 + length, 4))) { throw new ArgumentException("Export contains a corrupt PNG chunk."); }
            if (type.SequenceEqual("IDAT"u8)) { compressed.Write(data.Slice(offset + 8, length)); }
            offset += length + 12;
            if (type.SequenceEqual("IEND"u8)) { hasEnd = length == 0 && offset == data.Length; break; }
        }
        if (compressed.Length == 0 || !hasEnd) { throw new ArgumentException("Export is not a complete PNG."); }
        compressed.Position = 0;
        try
        {
            using var decoded = new ZLibStream(compressed, CompressionMode.Decompress);
            var row = new byte[checked(size.Width * channels)];
            for (var y = 0; y < size.Height; y++)
            {
                var filter = decoded.ReadByte();
                if (filter is < 0 or > 4) { throw new InvalidDataException("Invalid PNG scanline."); }
                decoded.ReadExactly(row);
            }
            if (decoded.ReadByte() != -1) { throw new InvalidDataException("Unexpected PNG pixel data."); }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { throw new ArgumentException("Export contains invalid PNG image data.", ex); }
    }

    public static (int Width, int Height) Read(ReadOnlySpan<byte> data)
    {
        int width = 0, height = 0;
        if (data.Length >= 24 && data[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            width = BinaryPrimitives.ReadInt32BigEndian(data.Slice(16, 4));
            height = BinaryPrimitives.ReadInt32BigEndian(data.Slice(20, 4));
        }
        else if (data.Length > 4 && data[0] == 255 && data[1] == 216)
        {
            var i = 2;
            var orientation = 1;
            while (i + 4 < data.Length)
            {
                if (data[i++] != 255) { break; }
                while (i < data.Length && data[i] == 255) { i++; }
                if (i >= data.Length) { break; }
                var marker = data[i++];
                if (marker is 0xd9 or 0xda) { break; }
                if (marker is 0x01 or >= 0xd0 and <= 0xd7) { continue; }
                if (i + 2 > data.Length) { break; }
                var length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(i, 2));
                if (length < 2 || i + length > data.Length) { break; }
                if (marker is 0xc0 or 0xc1 or 0xc2 && length >= 7)
                {
                    height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(i + 3, 2));
                    width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(i + 5, 2));
                }
                if (marker == 0xe1) { orientation = ExifOrientation(data.Slice(i + 2, length - 2)); }
                i += length;
            }
            if (orientation is >= 5 and <= 8) { (width, height) = (height, width); }
        }
        if (width <= 0 || height <= 0 || (long)width * height > 40_000_000) { throw new ArgumentException("Choose a valid PNG or JPEG of at most 40 million pixels."); }
        return (width, height);
    }
    private static int ExifOrientation(ReadOnlySpan<byte> segment)
    {
        if (segment.Length < 14 || !segment[..6].SequenceEqual("Exif\0\0"u8)) { return 1; }
        var tiff = segment[6..];
        var little = tiff[..2].SequenceEqual("II"u8);
        if (!little && !tiff[..2].SequenceEqual("MM"u8)) { return 1; }
        var offset = U32(tiff.Slice(4, 4), little);
        if (offset > (uint)tiff.Length - 2) { return 1; }
        var count = U16(tiff.Slice((int)offset, 2), little);
        for (var i = 0; i < count; i++)
        {
            var start = (long)offset + 2 + i * 12;
            if (start + 12 > tiff.Length) { break; }
            var entry = tiff.Slice((int)start, 12);
            if (U16(entry[..2], little) == 274 && U16(entry.Slice(2, 2), little) == 3 && U32(entry.Slice(4, 4), little) == 1)
            { return U16(entry.Slice(8, 2), little); }
        }
        return 1;
    }
    private static ushort U16(ReadOnlySpan<byte> data, bool little) => little ? BinaryPrimitives.ReadUInt16LittleEndian(data) : BinaryPrimitives.ReadUInt16BigEndian(data);
    private static uint U32(ReadOnlySpan<byte> data, bool little) => little ? BinaryPrimitives.ReadUInt32LittleEndian(data) : BinaryPrimitives.ReadUInt32BigEndian(data);

}