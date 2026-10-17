using System;
using System.IO;

namespace RimUnity.Editor
{
    //只排列 DXT 块和行索引，保持原始色端点、Alpha 端点与每级 mip 的像素值。
    internal static class BlockTextureRows
    {
        //非整块高度超过四行时无法只移动块保持像素，交给原有读回路径处理。
        internal static bool Supports(int format, int height)
            => (format == 10 || format == 12) && height > 0 && (height <= 4 || height % 4 == 0);

        //Unity 源纹理自底向上，宿主设备载荷自顶向下；末级 mip 仅翻转有效行。
        internal static byte[] Flip(byte[] source, int width, int height, int format)
        {
            if (width <= 0 || !Supports(format, height)) throw new NotSupportedException("DXT 块无法无损翻转该尺寸或格式。");
            int blockBytes = format == 10 ? 8 : 16;
            int columns = (width + 3) / 4, rows = (height + 3) / 4;
            if (source.Length != checked(columns * rows * blockBytes)) throw new InvalidDataException("DXT mip 的原始块长度不匹配。");
            var result = new byte[source.Length];
            int visibleRows = Math.Min(height, 4);
            for (int row = 0; row < rows; row++)
                for (int column = 0; column < columns; column++)
                {
                    int input = (row * columns + column) * blockBytes;
                    int output = ((rows - 1 - row) * columns + column) * blockBytes;
                    Buffer.BlockCopy(source, input, result, output, blockBytes);
                    int color = format == 10 ? 0 : 8;
                    for (int y = 0; y < visibleRows; y++) result[output + color + 4 + y] = source[input + color + 4 + visibleRows - 1 - y];
                    if (format != 12) continue;
                    ulong alpha = 0;
                    for (int i = 0; i < 6; i++) alpha |= (ulong)source[input + 2 + i] << (i * 8);
                    ulong flipped = alpha;
                    for (int y = 0; y < visibleRows; y++)
                    {
                        int shift = y * 12;
                        flipped = (flipped & ~(0xfffUL << shift)) | (((alpha >> ((visibleRows - 1 - y) * 12)) & 0xfffUL) << shift);
                    }
                    for (int i = 0; i < 6; i++) result[output + 2 + i] = (byte)(flipped >> (i * 8));
                }
            return result;
        }
    }
}
