using System;
using UnityEngine;

namespace RimUnity.Editor
{
    //Standard Unlit 保留不透明、透明与加法模式的混合和剔除，不导入屏幕采样功能。
    internal static class StandardParticleMaterial
    {
        internal static string Target(Material material, string path)
        {
            int mode = (int)material.GetFloat("_Mode");
            if (mode < 0 || mode > 6) throw new NotSupportedException(path + "：未知 Standard Unlit 混合模式：" + mode);
            foreach (string property in new[] { "_DistortionEnabled", "_SoftParticlesEnabled" })
                if (material.GetFloat(property) != 0) throw new NotSupportedException(path + "：Standard Unlit 尚未接通 " + property);
            int cull = (int)material.GetFloat("_Cull");
            if (cull != 0 && cull != 2) throw new NotSupportedException(path + "：Standard Unlit 尚未接通前面剔除。");
            if (material.GetFloat("_SrcBlend") != (mode == 6 ? 2 : mode <= 1 || mode == 3 ? 1 : 5) || material.GetFloat("_DstBlend") != (mode <= 1 || mode == 6 ? 0 : mode == 4 || mode == 5 ? 1 : 10)
                || material.GetFloat("_BlendOp") != (mode == 5 ? 2 : 0) || material.GetFloat("_ZWrite") != (mode <= 1 ? 1 : 0))
                throw new NotSupportedException(path + "：Standard Unlit 的实际混合／深度状态与选择的模式不一致。");
            bool advanced = mode > 4 || material.GetFloat("_ColorMode") != 0 || material.GetFloat("_FlipbookMode") != 0
                || material.GetFloat("_CameraFadingEnabled") != 0 || material.GetFloat("_EmissionEnabled") != 0;
            if (advanced) return "RimUnity_StandardParticle" + mode + (cull == 2 ? "Back" : "");
            return (mode == 0 ? "RimUnity_ParticleOpaque" : mode == 1 ? "RimUnity_ParticleCutout" : mode == 3 ? "RimUnity_ParticlePremultiply" : mode == 4 ? "RimUnity_ParticleAdditive" : "RimUnity_ParticleAlpha") + (cull == 2 ? "Back" : "");
        }
    }
}
