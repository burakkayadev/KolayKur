namespace KolayKur.Core.Icons;

/// <summary>
/// PNG yapı ve özet (CRC-32) denetimi: önbellekteki dosya yarım kalmış ya da gövdesi bozulmuşsa çözmeye çalışmadan elenir.
/// Her parçanın uzunluğu, türü ve CRC'si doğrulanır; ilk parça IHDR, son parça IEND olmalı, en az bir IDAT bulunmalıdır.
/// </summary>
internal static class PngIntegrity
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] Table = BuildTable();

    /// <summary>Bütünse piksel boyutunu verir; değilse <c>null</c>.</summary>
    internal static (int Width, int Height)? Inspect(ReadOnlySpan<byte> png)
    {
        if (png.Length < Signature.Length + 12 + 13 || !png[..Signature.Length].SequenceEqual(Signature))
        {
            return null;
        }

        var position = Signature.Length;
        var first = true;
        var sawData = false;
        (int, int)? size = null;
        while (position + 12 <= png.Length)
        {
            var length = ReadInt(png, position);
            if (length < 0 || position + 12L + length > png.Length)
            {
                return null;
            }

            var type = png.Slice(position + 4, 4);
            var crcStored = (uint)ReadInt(png, position + 8 + length);
            if (Crc32(png.Slice(position + 4, 4 + length)) != crcStored)
            {
                return null;
            }

            var name = System.Text.Encoding.ASCII.GetString(type);
            if (first)
            {
                if (name != "IHDR" || length != 13)
                {
                    return null;
                }

                size = (ReadInt(png, position + 8), ReadInt(png, position + 12));
                first = false;
            }

            sawData |= name == "IDAT";
            position += 12 + length;
            if (name == "IEND")
            {
                return sawData && position == png.Length ? size : null;
            }
        }

        return null;
    }

    internal static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return ~crc;
    }

    private static int ReadInt(ReadOnlySpan<byte> s, int at) => (s[at] << 24) | (s[at + 1] << 16) | (s[at + 2] << 8) | s[at + 3];

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
