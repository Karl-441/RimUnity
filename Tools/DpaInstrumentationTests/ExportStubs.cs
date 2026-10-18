using System;
using System.Collections.Generic;
using System.Reflection;
namespace UnityEngine
{
    public enum TextAnchor { MiddleCenter }
    public struct Rect { public float width; public Rect TopPartPixels(float h) => this; }
    public struct Vector2 { public float x; }
    public static class GUI { public static object color; }
}
namespace RimWorld { public static class MessageTypeDefOf { public static object TaskCompletion; } }
namespace Verse
{
    public enum GameFont { Small }
    public static class Text { public static GameFont Font; public static UnityEngine.TextAnchor Anchor; public static bool WordWrap; public static float LineHeight => 20; public static UnityEngine.Vector2 CalcSize(string text) => new() { x = text.Length * 10 }; }
    public static class Widgets { public static bool ButtonText(UnityEngine.Rect rect, string text) => false; }
    public static class TooltipHandler { public static void TipRegion(UnityEngine.Rect rect, string text) {} }
    public static class Messages { public static void Message(string text, object type, bool historical) {} }
    public static class Log { public static void Error(string text) => throw new Exception(text); }
}
namespace Analyzer.Profiling
{
    public static class Extensions
    {
        public static string Clipboard;
        public static void AdjustVerticallyBy(this ref UnityEngine.Rect rect, float delta) {}
        public static string Tr(this string s) => s;
        public static void CopyToClipboard(this string s) => Clipboard = s;
    }
    public static class GUIController { public static Category CurrentCategory = Category.RimUnity; public static Entry CurrentEntry = new("test", Category.RimUnity); }
    public static class Panel_TopRow { public static int fps = 144, tps = 60, tpsTarget = 60; public static string TimesFilter = "visible"; }
    public static class ModFilter { public static bool HasBlockedMods = true; public static bool IsAllowed(ProfileLog log) => log.label != "visible-blocked"; }
    public static class Panel_Logs { public static bool Matched(ProfileLog log, string filter) => log.label.Contains(filter); }
    public class ProfileLog
    {
        public string label; public double average, max, calls, total, percent; public int entries; public bool pinned;
    }
}
