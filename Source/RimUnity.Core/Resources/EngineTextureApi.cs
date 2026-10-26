using System;
using System.Reflection;

namespace RimUnity;

//宿主在 Waylith.Rendering 与 Waylith.Graphics.Textures 之间迁移过纹理辅助类型；
//这里按新旧命名空间绑定 PNG 解码，绑定失败时回退宿主通用解码，避免贴图管线在 JIT 期抛异常。
internal static class EngineTextureApi
{
    private delegate byte[] PngDecode(ReadOnlySpan<byte> file, out int width, out int height);

    private static readonly PngDecode Decode = ResolvePngDecoder();

    internal static byte[] TryDecodePng(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        if (Decode != null) return Decode(bytes, out width, out height);
        width = height = 0;
        return null;
    }

    //与宿主 TexturePixelData.IsOpaqueRgba32 语义一致，但不绑定宿主类型。
    internal static bool IsOpaqueRgba32(byte[] rgba)
    {
        for (int i = 3; i < rgba.Length; i += 4)
            if (rgba[i] != 255) return false;
        return true;
    }

    private static PngDecode ResolvePngDecoder()
    {
        try
        {
            var assembly = typeof(Waylith.Rendering.Texture2D).Assembly;
            foreach (var name in new[] { "Waylith.Graphics.Textures.PngDecoder", "Waylith.Rendering.PngDecoder" })
            {
                var method = assembly.GetType(name)?.GetMethod("TryDecodeRgba32", BindingFlags.Public | BindingFlags.Static);
                if (method == null || method.ReturnType != typeof(byte[])) continue;
                return method.CreateDelegate<PngDecode>();
            }
        }
        catch (Exception) { }
        return null;
    }
}
