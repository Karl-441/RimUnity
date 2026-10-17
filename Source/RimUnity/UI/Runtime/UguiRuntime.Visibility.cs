using System.Collections.Generic;

namespace RimUnity.UI.Runtime;

//收集阶段排除视口外的有界图形，不影响布局和越过父矩形的子节点。
internal static partial class UguiRuntime
{
    private static readonly List<Shadow> ClipShadows = new();

    //只裁剪矩形内绘制的常规图形；自定义网格、材质和溢出文字保留原路径。
    private static bool IntersectsClip(in UguiProjection projection, RectTransform rect, Graphic graphic, Rect clip)
    {
        if (clip.width <= 0 || clip.height <= 0) return false;
        if (graphic.material != null) return true;
        Rect local;
        if (graphic is Text text)
        {
            if (!text.clipText) return true;
            local = text.ContentRect;
        }
        else if (graphic is Image or RawImage) local = rect.rect;
        else return true;
        graphic.GetComponents(ClipShadows);
        foreach (Shadow shadow in ClipShadows)
        {
            if (!shadow.enabled) continue;
            float x = Mathf.Abs(shadow.effectDistance.x), y = Mathf.Abs(shadow.effectDistance.y);
            local.xMin -= x; local.xMax += x; local.yMin -= y; local.yMax += y;
        }
        ClipShadows.Clear();
        return projection.Bounds(rect, local).Overlaps(clip);
    }

    //求两个 GUI 裁剪矩形的交集。
    private static Rect IntersectClip(Rect a, Rect b)
        => Rect.MinMaxRect(Mathf.Max(a.xMin, b.xMin), Mathf.Max(a.yMin, b.yMin),
            Mathf.Min(a.xMax, b.xMax), Mathf.Min(a.yMax, b.yMax));
}
