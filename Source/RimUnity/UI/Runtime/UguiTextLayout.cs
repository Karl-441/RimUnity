using System;
using System.Collections;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Brutal.ImGuiApi;
using HarmonyLib;
using UnityEngine;
using UnityStubs.Gui;

namespace RimUnity.UI.Runtime;

//只在明确核对过的 Waylith 后端读取排版结果，反射访问在绑定时编译为委托。
internal static class UguiTextLayout
{
    internal readonly record struct Run(int Start, int Length, Color Color, FontStyle Style, float Size, float X, float Width, bool IsSpace);
    internal readonly record struct Line(int PieceStart, int PieceCount, float Width, float Top, float Ascent, float FirstLine, float Pitch);
    internal sealed record Layout(string Source, int FontIndex, Vector2 Size, Line[] Lines, Run[] Runs);
    private static IGuiBackend backend;
    private static Func<IList> getLines, getRuns;
    private static Func<object, Line> readLine;
    private static Func<object, Run> readRun;
    private static Func<int, FontStyle, ImFontPtr> styledFont;
    [ThreadStatic] private static bool literal;

    //仅在桥接 Text 的同步调用中改变标签解析，嵌套调用退出后恢复之前状态。
    internal readonly struct ParseScope : IDisposable
    {
        private readonly bool previous;
        internal ParseScope(bool richText) { previous = literal; literal = !richText; }
        public void Dispose() => literal = previous;
    }

    //宿主没有禁用富文本的参数；None 枚举为零，跳过原方法即可按普通字符排版。
    [HarmonyPatch]
    private static class LiteralTags
    {
        private static MethodBase TargetMethod() => AccessTools.Method("RimWorldWaylith.Imgui.RichText:Parse");
        private static bool Prefix(int i, ref int end, ref Color color, ref float size)
        {
            if (!literal) return true;
            end = i; color = default; size = 0;
            return false;
        }
    }

    //绑定内部排版与字体接口，后端结构变化时明确报错。
    private static void Bind()
    {
        if (ReferenceEquals(backend, GuiBackend.Current)) return;
        IGuiBackend current = GuiBackend.Current;
        Type type = current.GetType();
        if (type.FullName != "RimWorldWaylith.Imgui.ImGuiGuiBackend")
            throw new NotSupportedException("文字排版尚未适配 GUI 后端：" + type.FullName);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object layout = type.GetField("textLayout", flags).GetValue(current);
        object host = type.GetField("host", flags).GetValue(current);
        getLines = ListGetter(layout, "Lines");
        getRuns = ListGetter(layout, "Pieces");
        readLine = RecordGetter<Line>(layout.GetType().GetNestedType("Line", BindingFlags.NonPublic));
        readRun = RecordGetter<Run>(layout.GetType().GetNestedType("Piece", BindingFlags.NonPublic));
        styledFont = host.GetType().GetMethod("StyledFont").CreateDelegate<Func<int, FontStyle, ImFontPtr>>(host);
        backend = current;
    }

    //拷贝排版结果后，后续绘制可安全调用会重用后端缓冲的文字接口。
    internal static Layout Build(UnityEngine.UI.Text text, GUIStyle style, float width)
    {
        using var parse = new ParseScope(text.supportRichText);
        Bind();
        int font = text.font != null ? text.font.GameFontIndex : 1;
        string source = text.text ?? string.Empty;
        Vector2 size = backend.MeasureText(font, style.fontSize, style.fontStyle, source, style.wordWrap ? width : -1f);
        IList nativeLines = getLines(), nativeRuns = getRuns();
        Line[] lines = new Line[nativeLines.Count];
        Run[] runs = new Run[nativeRuns.Count];
        for (int i = 0; i < lines.Length; i++) lines[i] = readLine(nativeLines[i]);
        for (int i = 0; i < runs.Length; i++) runs[i] = readRun(nativeRuns[i]);
        if (!float.IsFinite(text.lineSpacing) || text.lineSpacing < 0f)
            throw new InvalidOperationException("Text 行距必须是有限非负数。");
        if (lines.Length > 0) size.y += lines[^1].Top * (text.lineSpacing - 1f);
        return new Layout(source, font, size, lines, runs);
    }

    //取得与宿主排版相同的粗体、斜体和字号字形。
    internal static ImFontBakedPtr Font(int fontIndex, FontStyle style, float size)
    {
        Bind();
        return styledFont(fontIndex, style).GetFontBaked(size);
    }

    //缓存列表访问，不在逐字绘制中查找成员。
    private static Func<IList> ListGetter(object owner, string property)
    {
        Expression access = Expression.Property(Expression.Constant(owner), property);
        return Expression.Lambda<Func<IList>>(Expression.Convert(access, typeof(IList))).Compile();
    }

    //把宿主只读结构转换为本地排版结构。
    private static Func<object, T> RecordGetter<T>(Type native)
    {
        ConstructorInfo constructor = typeof(T).GetConstructors()[0];
        ParameterExpression input = Expression.Parameter(typeof(object));
        Expression[] args = constructor.GetParameters().Select(p =>
            (Expression)Expression.Field(Expression.Convert(input, native), p.Name)).ToArray();
        return Expression.Lambda<Func<object, T>>(Expression.New(constructor, args), input).Compile();
    }
}
