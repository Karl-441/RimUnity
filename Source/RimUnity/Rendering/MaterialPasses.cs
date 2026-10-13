using System.Runtime.CompilerServices;
using UnityEngine;

namespace RimUnity.Rendering;

//一个导出材质的各 Pass 共用参数，但保持独立 Shader 和绘制状态。
internal static class MaterialPasses
{
    private static readonly ConditionalWeakTable<Material, Material[]> Passes = new();

    internal static void Register(Material material, Shader[] shaders)
    {
        var passes = new Material[shaders.Length];
        passes[0] = material;
        for (int i = 1; i < passes.Length; i++) passes[i] = new Material(shaders[i]);
        Passes.Add(material, passes);
    }

    //返回额外 Pass 时同步主材质参数，不覆盖额外 Pass 自身的 Shader。
    internal static Material[] Get(Material material)
    {
        if (!Passes.TryGetValue(material, out Material[] passes)) return null;
        for (int i = 1; i < passes.Length; i++) passes[i].CopyPropertiesFromMaterial(material);
        return passes;
    }

    //实例材质也保留源材质的完整 Pass 集合。
    internal static void Copy(Material source, Material clone)
    {
        if (!Passes.TryGetValue(source, out Material[] passes)) return;
        var shaders = new Shader[passes.Length];
        for (int i = 0; i < shaders.Length; i++) shaders[i] = passes[i].shader;
        Register(clone, shaders);
    }

    //调用方释放实例材质时一并销毁额外 Pass。
    internal static void Release(Material material)
    {
        if (!Passes.TryGetValue(material, out Material[] passes)) return;
        for (int i = 1; i < passes.Length; i++) UnityEngine.Object.Destroy(passes[i]);
        Passes.Remove(material);
    }
}
