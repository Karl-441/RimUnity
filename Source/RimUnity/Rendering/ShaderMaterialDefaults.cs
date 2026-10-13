using System.Collections.Generic;
using HarmonyLib;
using RimUnity.Serialization;
using UnityEngine;

namespace RimUnity.Rendering;

//独立 Shader 的默认属性按进程缓存，仅作用于 RimUnity 已注册的程序。
[HarmonyPatch(typeof(Material), MethodType.Constructor, new[] { typeof(Shader) })]
internal static class ShaderMaterialDefaults
{
    private static readonly Dictionary<string, MemberData[]> Defaults = new();
    internal static void Register(string key, MemberData[] defaults) => Defaults[key] = defaults;

    //在构造后补齐 Unity Properties 默认值，后续脚本与导出材质仍可覆盖。
    private static void Postfix(Material __instance, Shader shader)
    {
        if (shader == null || !Defaults.TryGetValue(shader.name, out var defaults)) return;
        foreach (var property in defaults)
        {
            var v = property.value;
            if (v.kind == "vector") Assets.ResourceLoader.SetMaterialVector(__instance, property.name, new Vector4(v.vector[0], v.vector[1], v.vector[2], v.vector[3]));
            else if (v.kind == "integer") __instance.SetInt(property.name, (int)v.number);
            else if (v.kind == "textureDefault")
                __instance.SetTexture(property.name, v.text switch { "black" => Texture2D.blackTexture, "gray" => Texture2D.grayTexture, "bump" => Texture2D.normalTexture, _ => Texture2D.whiteTexture });
            else __instance.SetFloat(property.name, (float)v.number);
        }
    }
}
