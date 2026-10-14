using HarmonyLib;
using UnityEngine;
using Waylith.Rendering;

namespace RimUnity.Rendering;

//替换贴图时同步采样编码标记；数值贴图保持原值，颜色贴图由 Linear Shader 解码。
[HarmonyPatch(typeof(UnityEngine.Material), nameof(UnityEngine.Material.SetTexture), new[] { typeof(int), typeof(UnityEngine.Texture) })]
internal static class MaterialColorSpace
{
    private static void Postfix(UnityEngine.Material __instance, int nameID, UnityEngine.Texture value)
    {
        if (!ShaderPropertyId.TryGetName(nameID, out string name)) return;
        string flag = ShaderColorSpace.TextureFlag + name;
        if (!__instance.HasProperty(flag)) return;
        bool srgb = value is CubemapTexture cube ? !cube.Linear : value is UnityEngine.Texture2D texture && !texture.CoreTexture.CpuData.Linear;
        __instance.SetFloat(flag, srgb ? 1 : 0);
    }
}

