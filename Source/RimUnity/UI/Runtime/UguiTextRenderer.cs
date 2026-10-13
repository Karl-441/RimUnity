//源自 ChezhouLib UGUI；迁移来源与第三方许可见 Docs/ThirdParty.md。
using UnityEngine;
using UnityStubs.Gui;

namespace RimUnity.UI.Runtime;

//文字测量、字号选择、行距和绘制使用同一份宿主排版。
internal static partial class UguiTextRenderer
{
    //布局组件取得的尺寸包括自定义行距，但不受自动字号缩放影响。
    internal static Vector2 Measure(UnityEngine.UI.Text text, float width, bool wrap)
    {
        using var parse = new UguiTextLayout.ParseScope(text.supportRichText);
        GUIStyle style = UguiRuntime.TextStyle(text);
        style.wordWrap = wrap;
        if (Mathf.Approximately(text.lineSpacing, 1f))
            return new Vector2(style.CalcSize(new GUIContent(text.text)).x, style.CalcHeight(new GUIContent(text.text), width));
        return UguiTextLayout.Build(text, style, width).Size;
    }

    //普通默认行距沿用引擎快速路径，其余情况按已排好的行提交。
    internal static void Draw(UguiRuntime.Entry entry, UnityEngine.UI.Text text, GUIStyle style, Rect bounds, Color tint, float scale)
    {
        using var parse = new UguiTextLayout.ParseScope(text.supportRichText);
        if (text is Text { resizeTextForBestFit: true } fit) Fit(text, fit, style, bounds, scale);
        UguiTextLayout.Layout layout = text.material != null || !Mathf.Approximately(text.lineSpacing, 1f)
            ? UguiTextLayout.Build(text, style, bounds.width) : null;
        bool clip = text is not Text { clipText: false } && WorldUiSubmission.Current == null && localEntry == null;
        if (clip) GuiBackend.Current.PushClipRect(bounds);
        try
        {
            foreach (Shadow shadow in text.GetComponents<Shadow>())
            {
                if (!shadow.enabled) continue;
                Color color = shadow.effectColor;
                color.a *= shadow.useGraphicAlpha ? tint.a : entry.Alpha;
                Vector2 delta = new(shadow.effectDistance.x * scale, -shadow.effectDistance.y * scale);
                Surface(text, style, new Rect(bounds.position + delta, bounds.size), color, layout, true);
                if (shadow is Outline)
                {
                    Surface(text, style, new Rect(bounds.position - delta, bounds.size), color, layout, true);
                    Surface(text, style, new Rect(bounds.position + new Vector2(delta.x, -delta.y), bounds.size), color, layout, true);
                    Surface(text, style, new Rect(bounds.position + new Vector2(-delta.x, delta.y), bounds.size), color, layout, true);
                }
            }
            Surface(text, style, bounds, tint, layout, false);
        }
        finally { if (clip) GuiBackend.Current.PopClipRect(); }
    }

    //寻找考虑换行和行距后能够容纳的最大字号。
    private static void Fit(UnityEngine.UI.Text text, Text fit, GUIStyle style, Rect bounds, float scale)
    {
        int low = Mathf.Max(1, Mathf.RoundToInt(fit.resizeTextMinSize * scale));
        int high = Mathf.Max(low, Mathf.RoundToInt(fit.resizeTextMaxSize * scale));
        int best = low;
        while (low <= high)
        {
            int size = low + (high - low) / 2;
            style.fontSize = size;
            Vector2 measured = UguiTextLayout.Build(text, style, bounds.width).Size;
            if (measured.x <= bounds.width && measured.y <= bounds.height) { best = size; low = size + 1; }
            else high = size - 1;
        }
        style.fontSize = best;
    }

    //同一排版同时服务阴影、普通文字和材质字形。
    private static void Surface(UnityEngine.UI.Text text, GUIStyle style, Rect bounds, Color tint, UguiTextLayout.Layout layout, bool shadow)
    {
        if (layout == null)
        {
            GUI.color = tint;
            GUI.Label(bounds, text.text, style);
            return;
        }
        if (text.material != null) { MaterialText(text, bounds, tint, layout, shadow); return; }
        float top = bounds.y + (bounds.height - layout.Size.y) * ((int)text.alignment / 3) * 0.5f;
        GuiBackend.Current.FontMetrics(layout.FontIndex, out float importSize, out float ascent, out _);
        foreach (UguiTextLayout.Line line in layout.Lines)
        {
            float x = bounds.x + (bounds.width - line.Width) * ((int)text.alignment % 3) * 0.5f;
            float y = top + line.Top * text.lineSpacing;
            for (int i = line.PieceStart; i < line.PieceStart + line.PieceCount; i++)
            {
                UguiTextLayout.Run run = layout.Runs[i];
                if (run.IsSpace) continue;
                float offset = line.Ascent - ascent * run.Size / importSize;
                Color color = RunColor(run.Color, tint, shadow);
                GuiBackend.Current.DrawText(new Rect(x + run.X, y + offset, run.Width, line.FirstLine),
                    layout.Source.Substring(run.Start, run.Length), layout.FontIndex, run.Size, run.Style,
                    TextAnchor.UpperLeft, false, color);
            }
        }
    }

    //阴影使用统一颜色，正文保留富文本片段的颜色和透明度。
    private static Color RunColor(Color run, Color tint, bool shadow)
        => shadow ? new Color(tint.r, tint.g, tint.b, tint.a * run.a) : run * tint;
}
