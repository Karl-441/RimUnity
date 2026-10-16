using System;
using System.Runtime.CompilerServices;

namespace RimUnity.UI;

//统一提供动态矩形精灵和导出精灵的几何数据。
public static class SpriteGeometry
{
    internal sealed record Geometry(Vector2[] Positions, Vector2[] UV, int[] Triangles);
    private static readonly ConditionalWeakTable<Sprite, Geometry> Data = new();

    //归一化几何先还原为像素，再减去像素枢轴并换算为局部单位。
    internal static Vector2 LocalPosition(Sprite sprite, Vector2 position)
        => new((position.x * sprite.rect.width - sprite.pivot.x) / sprite.pixelsPerUnit,
            (position.y * sprite.rect.height - sprite.pivot.y) / sprite.pixelsPerUnit);

    //加载器传入与 Unity Image.GenerateSprite 相同的归一化顶点。
    public static void Register(Sprite sprite, Vector2[] positions, Vector2[] uv, int[] triangles)
    {
        ArgumentNullException.ThrowIfNull(sprite);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(uv);
        ArgumentNullException.ThrowIfNull(triangles);
        if (positions.Length != uv.Length || triangles.Length % 3 != 0)
            throw new ArgumentException("Sprite 顶点、UV 或三角形数量不匹配。");
        for (int i = 0; i < positions.Length; i++)
            if (!float.IsFinite(positions[i].x) || !float.IsFinite(positions[i].y) || !float.IsFinite(uv[i].x) || !float.IsFinite(uv[i].y))
                throw new ArgumentException("Sprite 几何包含非有限坐标。");
        foreach (int index in triangles)
            if (index < 0 || index >= positions.Length) throw new ArgumentException("Sprite 三角形索引越界。");
        Data.Remove(sprite);
        Data.Add(sprite, new Geometry((Vector2[])positions.Clone(), (Vector2[])uv.Clone(), (int[])triangles.Clone()));
    }

    //已注册的轮廓优先；动态矩形只生成一次，所有精灵绘制与粒子入口共用。
    internal static Geometry Get(Sprite sprite) => Data.GetValue(sprite, CreateRectangle);

    //局部顶点保持归一化，UV 取指定贴图区域；枢轴与像素密度由 LocalPosition 应用。
    private static Geometry CreateRectangle(Sprite sprite)
    {
        if (sprite.meshType != SpriteMeshType.FullRect)
            throw new InvalidOperationException("紧密 Sprite 未注册几何数据：" + sprite.name);
        Rect rect = sprite.rect;
        float left = rect.xMin / sprite.texture.width, right = rect.xMax / sprite.texture.width;
        float bottom = rect.yMin / sprite.texture.height, top = rect.yMax / sprite.texture.height;
        return new Geometry(
            new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right },
            new[] { new Vector2(left, bottom), new Vector2(left, top), new Vector2(right, top), new Vector2(right, bottom) },
            new[] { 0, 1, 2, 0, 2, 3 });
    }
}
