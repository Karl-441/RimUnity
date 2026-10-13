using HarmonyLib;
using Verse;

namespace RimUnity;

//初始化明文资源运行时与地图渲染接线，资源按需加载。
public sealed class RimUnityMod : Mod
{
    public static ModContentPack OwnContent { get; private set; }
    public static Rendering.BloomSettings Bloom { get; private set; }
    internal static string ShaderDirectory { get; private set; }

    //Harmony 由依赖模组提供，不复制其程序集。
    public RimUnityMod(ModContentPack content) : base(content)
    {
        OwnContent = content;
        ShaderDirectory = System.IO.Path.Combine(content.RootDir, "1.7", "Shaders", "RimUnity");
        Bloom = GetSettings<Rendering.BloomSettings>();
        new Harmony("Chezhou.RimUnity").PatchAll(typeof(RimUnityMod).Assembly);
        Log.Message("[RimUnity] 运行时已加载；功能及验收状态见 Docs/Status.md。");
    }

    //渲染设置属于桥接框架，内容模组无需各自提供后处理窗口。
    public override string SettingsCategory() => "RimUnity";
    public override void DoSettingsWindowContents(UnityEngine.Rect rect) => Bloom.Draw(rect);
}
