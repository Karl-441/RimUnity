using System;
using System.Collections.Generic;
using System.Numerics;
using HarmonyLib;
using Waylith.Rendering;

namespace RimUnity.Rendering;

//在主线程复制本帧选中层，保持材质、实例化、骨骼数据与原地图提交一致。
internal static class BloomLayerCapture
{
    internal static readonly byte MapPass = (byte)AccessTools.Field(
        AccessTools.TypeByName("RimWorldWaylith.Rendering.RenderPassIds"), "MapMain").GetValue(null);
    private delegate FinalDrawItem RentDraw(Matrix4x4 matrix, byte passId, MeshDrawGeometry mesh, MaterialBinding binding,
        bool hasOverride, in MaterialUniformBytes uniforms, in MaterialTextureResourceSet<Texture> resources,
        int order, int queue, InstancedDrawInfo instancing, int layer, ushort hostOrder, InstanceStorageBinding[] storage);
    private static readonly RentDraw Rent = AccessTools.Method(typeof(FinalDrawItem), "Rent").CreateDelegate<RentDraw>();
    private static readonly AccessTools.FieldRef<FinalDrawItem, DrawSortKey> SortKey =
        AccessTools.FieldRefAccess<FinalDrawItem, DrawSortKey>("<SortKey>k__BackingField");

    //复制 GPU 状态缓存，不能将原绘制项交给第二个 MeshBatchRenderer 共用。
    internal static MeshDrawFrame Capture(MeshDrawFrame source, int mask)
    {
        var draws = MeshDrawFrame.RentList();
        var bindings = new Dictionary<MaterialBinding, MaterialBinding>();
        try
        {
            Copy(source.Draws); Copy(source.PersistentDraws);
            return new MeshDrawFrame(source.FrameNumber, source.View, draws, MeshDrawFrame.RentList(), source.SunDirection)
            {
                CameraYaw = source.CameraYaw, CameraPitch = source.CameraPitch, SwayHead = source.SwayHead,
                WindSpeed = source.WindSpeed, BillboardPitchDelta = source.BillboardPitchDelta
            };
        }
        catch
        {
            new MeshDrawFrame(source.FrameNumber, source.View, draws, MeshDrawFrame.RentList(), source.SunDirection).Dispose();
            throw;
        }

        //共享主场景深度用于遮挡，禁止二次绘制改写它。
        void Copy(List<FinalDrawItem> items)
        {
            foreach (var item in items)
            {
                if ((mask & (1 << item.Layer)) == 0 || (item.PassId != 0 && item.PassId != MapPass)) continue;
                var original = item.Binding;
                if (!bindings.TryGetValue(original, out var binding))
                {
                    var state = original.PipelineState;
                    state = state with { Depth = state.Depth with { Write = false },
                        Stencil = state.Stencil with { WriteMask = 0, PassOp = StencilOp.Keep, FailOp = StencilOp.Keep, DepthFailOp = StencilOp.Keep } };
                    binding = new MaterialBinding(original.Id, original.MaterialId, original.MaterialVersion, original.DebugName,
                        original.Shader, original.MaterialLayout, original.Uniforms, original.Resources, original.RenderQueue,
                        original.ShaderVariant, state, original.WrapModeOverride, original.UsesGlobals);
                    binding.UpdateStorageArrays(original.StorageArrays);
                    bindings.Add(original, binding);
                }
                var copy = Rent(item.Matrix, item.PassId, item.Mesh, binding, true, in item.Uniforms, in item.Resources,
                    draws.Count, item.RenderQueue, item.Instancing, item.Layer, 0, item.Storage);
                SortKey(copy) = item.SortKey;
                draws.Add(copy);
            }
        }
    }
}
