using System.IO.Compression;
using System.Text;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 生成用于图片识别能力探测的 PNG：白底黑字的随机数字。
/// 不依赖任何图像库，手写 5×7 位图字体 + 最小 PNG 编码（zlib + CRC32）。
/// <para>
/// 为什么不用纯色图片：颜色可以被纯文本模型猜中（「红色」是高频猜测），
/// 随机数字几乎不可能猜对，只有真正读图的模型才能答对，判定因此可靠得多。
/// </para>
/// </summary>
internal static class ProbeImageRenderer
{
    private const int GlyphWidth = 5;
    private const int GlyphHeight = 7;

    /// <summary>5×7 点阵数字，每行 5 位，最高位在左。</summary>
    private static readonly byte[][] DigitFont =
    {
        new byte[] { 0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E }, // 0
        new byte[] { 0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E }, // 1
        new byte[] { 0x0E, 0x11, 0x01, 0x02, 0x04, 0x08, 0x1F }, // 2
        new byte[] { 0x1F, 0x02, 0x04, 0x02, 0x01, 0x11, 0x0E }, // 3
        new byte[] { 0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02 }, // 4
        new byte[] { 0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x0E }, // 5
        new byte[] { 0x06, 0x08, 0x10, 0x1E, 0x11, 0x11, 0x0E }, // 6
        new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08 }, // 7
        new byte[] { 0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E }, // 8
        new byte[] { 0x0E, 0x11, 0x11, 0x0F, 0x01, 0x02, 0x0C }  // 9
    };

    private static readonly uint[] Crc32Table = BuildCrc32Table();

    /// <summary>把一串数字渲染成白底黑字的 PNG（RGB，无压缩伪影，适合 OCR）。</summary>
    /// <param name="digits">要渲染的数字字符，非数字字符会被跳过。</param>
    /// <param name="scale">每个点阵像素放大的倍数，越大越清晰。</param>
    public static byte[] RenderDigits(string digits, int scale = 14)
    {
        digits ??= "";
        var count = Math.Max(1, digits.Length);
        const int padding = 24;
        var gap = scale;

        var width = padding * 2 + count * GlyphWidth * scale + (count - 1) * gap;
        var height = padding * 2 + GlyphHeight * scale;
        var rgb = new byte[width * height * 3];
        Array.Fill(rgb, (byte)0xFF); // 白底

        var x = padding;
        foreach (var ch in digits)
        {
            if (ch is >= '0' and <= '9')
            {
                var glyph = DigitFont[ch - '0'];
                for (var row = 0; row < GlyphHeight; row++)
                {
                    var bits = glyph[row];
                    for (var col = 0; col < GlyphWidth; col++)
                    {
                        if ((bits & (1 << (GlyphWidth - 1 - col))) == 0) continue;
                        FillBlock(rgb, width, x + col * scale, padding + row * scale, scale);
                    }
                }
            }

            x += GlyphWidth * scale + gap;
        }

        return EncodePng(width, height, rgb);
    }

    private static void FillBlock(byte[] rgb, int strideWidth, int left, int top, int size)
    {
        for (var y = top; y < top + size; y++)
        {
            var rowStart = (y * strideWidth + left) * 3;
            for (var x = 0; x < size; x++)
            {
                rgb[rowStart + x * 3] = 0;
                rgb[rowStart + x * 3 + 1] = 0;
                rgb[rowStart + x * 3 + 2] = 0;
            }
        }
    }

    private static byte[] EncodePng(int width, int height, byte[] rgb)
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        var header = new byte[13];
        WriteInt32(header, 0, width);
        WriteInt32(header, 4, height);
        header[8] = 8;   // 位深
        header[9] = 2;   // 颜色类型：真彩色 RGB
        header[10] = 0;  // 压缩方法
        header[11] = 0;  // 过滤方法
        header[12] = 0;  // 不隔行
        WriteChunk(output, "IHDR", header);

        // 每行前面加一个 0（None）过滤字节
        using var raw = new MemoryStream();
        var stride = width * 3;
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            raw.Write(rgb, y * stride, stride);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(zlib);
        }

        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", Array.Empty<byte>());
        return output.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        WriteInt32(length, 0, data.Length);
        stream.Write(length);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);

        var crcInput = new byte[typeBytes.Length + data.Length];
        Buffer.BlockCopy(typeBytes, 0, crcInput, 0, typeBytes.Length);
        Buffer.BlockCopy(data, 0, crcInput, typeBytes.Length, data.Length);
        var crc = new byte[4];
        WriteUInt32(crc, 0, ComputeCrc32(crcInput));
        stream.Write(crc);
    }

    private static void WriteInt32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static void WriteUInt32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint[] BuildCrc32Table()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++)
                value = (value & 1) != 0 ? 0xEDB88320 ^ (value >> 1) : value >> 1;
            table[i] = value;
        }

        return table;
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
            crc = Crc32Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
