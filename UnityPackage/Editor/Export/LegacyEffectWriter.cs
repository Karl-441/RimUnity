using System;
using System.Collections.Generic;
using System.Linq;
using RimUnity.Serialization;
using UnityEditor;
using UnityEngine;

namespace RimUnity.Editor
{
    //Legacy 动画与拖尾使用明确的组件数据，不把宿主同名空接口当成已完成转换。
    internal static class LegacyEffectWriter
    {
        internal static void Animation(UnityEngine.Animation source, ComponentData data, ValueWriter writer, string path)
        {
            if (source.animatePhysics) throw new NotSupportedException(path + "：Legacy Animation 的物理时钟模式尚未接通。");
            if (source.cullingType != AnimationCullingType.AlwaysAnimate && source.cullingType != AnimationCullingType.BasedOnRenderers)
                throw new NotSupportedException(path + "：不支持的 Legacy Animation 裁剪模式：" + source.cullingType);
            if (source.cullingType == AnimationCullingType.BasedOnRenderers)
                foreach (Renderer renderer in source.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer is UnityEngine.ParticleSystemRenderer particles)
                    {
                        if (particles.renderMode == ParticleSystemRenderMode.Mesh || particles.GetComponent<ParticleSystem>().trails.enabled
                            || particles.GetComponentInParent<Canvas>() != null)
                            throw new NotSupportedException(path + "：Legacy Animation 的粒子可见性裁剪尚未接通网格、拖尾或Canvas。");
                    }
                    else if (!(renderer is UnityEngine.MeshRenderer) && !(renderer is UnityEngine.SkinnedMeshRenderer))
                        throw new NotSupportedException(path + "：Legacy Animation 可见性裁剪尚未接通此渲染器：" + renderer.GetType().Name);
                }
            var entries = new List<ValueData>();
            foreach (UnityEngine.AnimationState state in source)
            {
                UnityEngine.AnimationClip clip = state.clip;
                if (clip == null) throw new InvalidOperationException(path + "：Legacy 动画状态缺少 Clip：" + state.name);
                if (state.layer != 0 || state.blendMode != AnimationBlendMode.Blend)
                    throw new NotSupportedException(path + "：Legacy Animation 分层或加法混合尚未接通。");
                if (AnimationUtility.GetAnimationEvents(clip).Length != 0)
                    throw new NotSupportedException(path + "：Legacy Animation 事件尚未接通。");
                entries.Add(new ValueData { kind = "data", fields = new List<MemberData>
                {
                    Field("name", writer.Write(state.name, path)), Field("clip", writer.Write(clip, path)),
                    Field("wrapMode", writer.Write(state.wrapMode, path)), Field("speed", writer.Write(state.speed, path))
                } });
            }
            data.type = "RimUnity.Animation";
            data.members.Add(writer.Property(source, "clip", path));
            data.members.Add(writer.Property(source, "playAutomatically", path));
            data.members.Add(writer.Property(source, "wrapMode", path));
            data.members.Add(writer.Property(source, "cullingType", path));
            data.members.Add(Field("states", new ValueData { kind = "array", items = entries }));
        }

        //保留纹理模式、对齐、端帽及颜色与 Alpha 的全部关键点。
        internal static void Trail(UnityEngine.TrailRenderer source, ComponentData data, ValueWriter writer, string path)
        {
            if (source.textureMode != LineTextureMode.Stretch && source.textureMode != LineTextureMode.Tile)
                throw new NotSupportedException(path + "：此批 TrailRenderer 仅接通 Stretch／Tile 贴图模式。");
            if (source.maskInteraction != SpriteMaskInteraction.None)
                throw new NotSupportedException(path + "：TrailRenderer 的 SpriteMask 交互尚未接通。");
            if (source.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off
                || source.receiveShadows && source.sharedMaterials.Any(material => !ProvenUnlitSurface(material)))
                throw new NotSupportedException(path + "：TrailRenderer 阴影尚未接通。");
            if (source.lightProbeUsage != UnityEngine.Rendering.LightProbeUsage.Off || source.reflectionProbeUsage != UnityEngine.Rendering.ReflectionProbeUsage.Off)
                throw new NotSupportedException(path + "：TrailRenderer 的探针采样尚未接通。");
            if (source.sortingLayerID != 0) throw new NotSupportedException(path + "：TrailRenderer 自定义排序层尚未接通。");
            if (source.GetComponentInParent<Canvas>() != null) throw new NotSupportedException(path + "：TrailRenderer 的 Canvas 绘制尚未接通。");
            data.type = "RimUnity.TrailRenderer";
            foreach (string name in new[] { "sharedMaterials", "time", "minVertexDistance", "widthMultiplier", "widthCurve", "colorGradient",
                "emitting", "autodestruct", "numCornerVertices", "numCapVertices", "alignment", "textureMode", "textureScale", "generateLightingData", "sortingOrder", "enabled" })
                data.members.Add(writer.Property(source, name, path));
        }

        //LineLaser 的两个本地点、逐段 UV 和渐变原样保存，不转换为有寿命的轨迹。
        internal static void Line(UnityEngine.LineRenderer source, ComponentData data, ValueWriter writer, string path)
        {
            if (source.loop) throw new NotSupportedException(path + "：LineRenderer 闭合接缝尚未接通。");
            if (source.textureMode != LineTextureMode.Stretch && source.textureMode != LineTextureMode.Tile
                && source.textureMode != LineTextureMode.RepeatPerSegment)
                throw new NotSupportedException(path + "：LineRenderer 仅接通 Stretch、Tile 和单段 RepeatPerSegment。");
            if (source.textureMode == LineTextureMode.RepeatPerSegment && source.positionCount != 0 && source.positionCount != 2)
                throw new NotSupportedException(path + "：RepeatPerSegment 当前只接通两点直线。");
            if (source.maskInteraction != SpriteMaskInteraction.None || source.sortingLayerID != 0)
                throw new NotSupportedException(path + "：LineRenderer 的 SpriteMask 和自定义排序层尚未接通。");
            if (source.receiveShadows || source.lightProbeUsage != UnityEngine.Rendering.LightProbeUsage.Off
                || source.reflectionProbeUsage != UnityEngine.Rendering.ReflectionProbeUsage.Off)
                throw new NotSupportedException(path + "：LineRenderer 光照与探针路径尚未接通。");
            //该资源勾选了投影，但 AdvancedBeam 没有 ShadowCaster Pass，实际不产生阴影。
            if (source.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly)
                throw new NotSupportedException(path + "：LineRenderer 的仅阴影模式尚未接通。");
            if (source.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off
                && source.sharedMaterials.Any(material => material != null && material.FindPass("ShadowCaster") >= 0))
                throw new NotSupportedException(path + "：LineRenderer 材质存在实际 ShadowCaster Pass，尚未接通。");
            if (source.GetComponentInParent<Canvas>() != null) throw new NotSupportedException(path + "：LineRenderer 的 Canvas 绘制尚未接通。");
            var positions = new Vector3[source.positionCount]; source.GetPositions(positions);
            data.type = "RimUnity.LineRenderer";
            data.members.Add(Field("positions", writer.Write(positions, path + ".positions")));
            foreach (string name in new[] { "sharedMaterials", "useWorldSpace", "loop", "widthMultiplier", "widthCurve", "colorGradient",
                "numCornerVertices", "numCapVertices", "alignment", "textureMode", "textureScale", "generateLightingData", "sortingOrder", "enabled" })
                data.members.Add(writer.Property(source, name, path));
        }

        private static MemberData Field(string name, ValueData value) => new MemberData { name = name, value = value };

        //接收阴影标记对纯 Emission 的 Surface 无影响，离线编译器还会完整检查该函数。
        private static bool ProvenUnlitSurface(Material material)
        {
            if (material == null) return true;
            string file = AssetDatabase.GetAssetPath(material.shader);
            if (!file.EndsWith(".shader", StringComparison.OrdinalIgnoreCase)) return false;
            string source = System.IO.File.ReadAllText(file);
            return System.Text.RegularExpressions.Regex.IsMatch(source,
                @"LightingUnlit\s*\(\s*SurfaceOutput\s+(\w+)\s*,\s*half3\s+\w+\s*,\s*half\s+\w+\s*\)\s*\{\s*return\s+half4\s*\(\s*0\s*,\s*0\s*,\s*0\s*,\s*\1\.Alpha\s*\)\s*;\s*\}");
        }
    }
}
