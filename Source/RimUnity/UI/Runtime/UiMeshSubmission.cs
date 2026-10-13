using System;
using System.Collections;
using System.Reflection;
using System.Linq;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using UnityEngine;
using UnityStubs.Gui;
using Waylith.Rendering;
using Material = UnityEngine.Material;
using Texture = UnityEngine.Texture;

namespace RimUnity.UI.Runtime;

//为 1.7 的 GUI 材质提交补上任意三角网格；直接沿用引擎纹理、裁切与绘制队列。
internal static class UiMeshSubmission
{
    private delegate float2 Position(in GuiVertex vertex, bool screen);
    private delegate bool ResolveTexture(Texture texture, out ImTextureRef reference);
    private delegate void ResolveMaterial(Waylith.Rendering.Material material, out MaterialUniformBytes uniforms, out MaterialTextureResourceSet<Waylith.Rendering.Texture> resources, out bool globals);
    private static IGuiBackend backend;
    private static FieldInfo drawListField, drawsField, activeField, drawingField;
    private static ConstructorInfo drawConstructor;
    private static Position position;
    private static ResolveTexture resolveTexture;
    private static ResolveMaterial resolveMaterial;

    //私有边界集中在这里，版本不匹配会明确失败；其余桥接不改写宿主 GUI。
    private static void Bind()
    {
        if (ReferenceEquals(backend, GuiBackend.Current)) return;
        backend = GuiBackend.Current;
        Type type = backend.GetType(); const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        if (type.FullName != "RimWorldWaylith.Imgui.ImGuiGuiBackend") throw new NotSupportedException("未适配 GUI 后端：" + type.FullName);
        drawListField = type.GetField("drawList", flags); drawsField = type.GetField("materialDraws", flags);
        activeField = type.GetField("frameActive", flags); drawingField = type.GetField("drawingEnabled", flags);
        position = type.GetMethod("PrimPosition", flags).CreateDelegate<Position>(backend);
        resolveTexture = type.GetMethod("TryResolveTexture", flags).CreateDelegate<ResolveTexture>(backend);
        var drawType = type.Assembly.GetType("RimWorldWaylith.Imgui.ImGuiMaterialDraw", true);
        drawConstructor = drawType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single(c => c.GetParameters().Length == 6);
        var resolver = typeof(ShaderRegistry).Assembly.GetType("Waylith.Rendering.MaterialBindingResolver", true);
        resolveMaterial = resolver.GetMethod("ResolveMaterialBindingData", BindingFlags.Static | BindingFlags.Public).CreateDelegate<ResolveMaterial>();
    }
    //无材质的普通图片保持原生快速路径；有材质时按整个网格登记一次。
    internal static void Draw(ReadOnlySpan<GuiVertex> vertices, Texture texture, Material material)
    {
        if (vertices.Length == 0) return;
        material = UiMaskData.Apply(material);
        if (WorldUiSubmission.Current != null) { WorldUiSubmission.Draw(vertices, texture, material); return; }
        if (material == null) { GuiBackend.Current.DrawPrimitives(4, vertices, texture, false); return; }
        UiShaderStages.Prepare(material);
        Bind();
        if (!(bool)activeField.GetValue(backend) || !(bool)drawingField.GetValue(backend)) return;
        texture ??= UnityEngine.Texture2D.whiteTexture;
        if (!resolveTexture(texture, out var tex)) throw new InvalidOperationException("UI 纹理尚未驻留 GPU：" + texture.name);
        for (int i = 0; i < (UiMaskData.Current?.Masks.Length ?? 0); i++)
            if (!resolveTexture(material.GetTexture("_UiMask" + i), out _)) throw new InvalidOperationException("Mask 纹理尚未驻留 GPU。");
        material.SetTexture("_MainTex", texture);
        var engine = material.engineMaterial;
        resolveMaterial(engine, out var uniforms, out var resources, out _);
        var draws = (IList)drawsField.GetValue(backend); int index = draws.Count;
        draws.Add(drawConstructor.Invoke(new object[] { engine, texture.CoreTexture, tex._TexID.Value, uniforms, resources, engine.StorageArrays }));
        var list = (ImDrawListPtr)drawListField.GetValue(backend);
        list.PushTexture(new ImTextureRef { _TexID = (ImTextureID)(-index - 1) });
        try
        {
            list.PrimReserve(vertices.Length, vertices.Length);
            foreach (ref readonly GuiVertex vertex in vertices)
            {
                float2 xy = position(in vertex, false), uv = new(vertex.uv.x, vertex.uv.y);
                list.PrimVtx(in xy, in uv, new ImColor8(vertex.color.r, vertex.color.g, vertex.color.b, vertex.color.a));
            }
        }
        finally { list.PopTexture(); }
    }
}
