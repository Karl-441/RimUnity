//源自 ChezhouLib UGUI；迁移来源与第三方许可见 Docs/ThirdParty.md。
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Waylith.UnityCompat;

namespace RimUnity.UI.Runtime;

//仅对 CL 创建的矩形对象补齐组件名称和引擎已有的 UGUI 接口。
internal static class UguiApiPatches
{
    //组件名称与所属对象共享，确保 Transform.Find 和直接读取子节点名称使用同一套路径。
    [HarmonyPatch(typeof(UnityEngine.Object), nameof(UnityEngine.Object.name), MethodType.Getter)]
    private static class ComponentNameGetter
    {
        //GameObject 自身仍使用引擎存储的名称，避免递归读取组件。
        private static bool Prefix(UnityEngine.Object __instance, ref string __result)
        {
            GameObject owner = NameOwner(__instance);
            if (owner == null) return true;
            __result = owner.name; return false;
        }
    }

    //通过 Transform 或其他组件重命名时，同步修改所属对象。
    [HarmonyPatch(typeof(UnityEngine.Object), nameof(UnityEngine.Object.name), MethodType.Setter)]
    private static class ComponentNameSetter
    {
        //实例重命名后不需要重新同步每个组件的独立名称。
        private static bool Prefix(UnityEngine.Object __instance, string value)
        {
            GameObject owner = NameOwner(__instance);
            if (owner == null) return true;
            owner.name = value; return false;
        }
    }

    //构造期间尚未登记到引擎的组件不能查询 transform；普通场景组件保留原行为。
    private static GameObject NameOwner(UnityEngine.Object instance)
    {
        if (instance is not Component component || UnityRuntime.Current is not { } runtime ||
            runtime.World.GetInstanceId(component) == 0) return null;
        GameObject owner = (GameObject)runtime.World.GetGameObject(component);
        return OwnedObjects.Contains(owner) ? owner : null;
    }

    //旧 Image/Text 调用仍能取得正确的基类 RectTransform。
    [HarmonyPatch(typeof(Graphic), nameof(Graphic.rectTransform), MethodType.Getter)]
    private static class GraphicRect
    {
        //其他来源的对象保留引擎行为。
        private static bool Prefix(Graphic __instance, ref UnityEngine.RectTransform __result)
        {
            if (__instance.transform is not RectTransform rect) return true;
            __result = rect; return false;
        }
    }

    //通过基类引用读取尺寸时也返回兼容层计算结果。
    [HarmonyPatch(typeof(UnityEngine.RectTransform), nameof(UnityEngine.RectTransform.rect), MethodType.Getter)]
    private static class RectGetter
    {
        //新增的矩形属性不依赖此补丁，避免递归调用。
        private static bool Prefix(UnityEngine.RectTransform __instance, ref Rect __result)
        {
            if (__instance is not RectTransform rect) return true;
            __result = rect.rect; return false;
        }
    }

    //基类 pivot 与兼容层保存值保持一致。
    [HarmonyPatch(typeof(UnityEngine.RectTransform), nameof(UnityEngine.RectTransform.pivot), MethodType.Getter)]
    private static class PivotGetter
    {
        //不接管未注册的 RectTransform。
        private static bool Prefix(UnityEngine.RectTransform __instance, ref Vector2 __result)
        {
            if (__instance is not RectTransform rect) return true;
            __result = rect.pivot; return false;
        }
    }

    //旧 API 取得的 Image 也可调用 SetNativeSize。
    [HarmonyPatch(typeof(UnityEngine.UI.Image), nameof(UnityEngine.UI.Image.SetNativeSize))]
    private static class NativeSize
    {
        //类型限定保证只修改 CL 导入的组件。
        private static bool Prefix(UnityEngine.UI.Image __instance)
        {
            if (__instance is not Image image) return true;
            image.SetNativeSize(); return false;
        }
    }

}
