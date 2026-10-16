using HarmonyLib;
using Verse;

namespace RimUnity.Rendering;

//复用原版加载窗口的背景和文本测量，仅在本框架预热期间更新提示。
internal static class LoadingWarmupUI
{
    //中文游戏使用中文，其余语言使用英文。
    internal static string Text(string chinese, string english)
        => LanguageDatabase.activeLanguage?.folderName?.StartsWith("Chinese") == true ? chinese : english;

    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.LongEventsOnGUI))]
    private static class Progress
    {
        //界面只读取原子进度数值，不访问渲染线程正在构建的任务列表。
        private static void Prefix()
        {
            var feature = LoadingWarmup.Current;
            if (feature?.Work == null) return;
            int total = feature.TotalCount;
            string progress = total == 0 ? Text("准备中", "Preparing") : $"{feature.ProcessedCount} / {total}";
            LongEventHandler.SetCurrentEventText(Text("RimUnity：正在预热渲染管线", "RimUnity: warming up rendering pipelines") + " · " + progress);
        }
    }

    [HarmonyPatch(typeof(ScreenFader), nameof(ScreenFader.OverlayOnGUI))]
    private static class LoadingFade
    {
        //原版淡入可能先于追加的预热事件执行；等待时保留加载画面，不让黑色遮罩盖住提示。
        private static bool Prefix() => LoadingWarmup.Current?.Work == null || !LongEventHandler.AnyEventNowOrWaiting;
    }
}
