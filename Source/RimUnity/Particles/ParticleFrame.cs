using HarmonyLib;
using UnityEngine;
using Waylith.UnityCompat;

namespace RimUnity;

//原生 LateUpdate 完成后提交，所有相机冻结绘制队列和上传网格之前完成。
[HarmonyPatch(typeof(UnityWorld), nameof(UnityWorld.LateUpdate))]
internal static class ParticleFrame
{
    private static int frame = -1;
    private static void Postfix()
    {
        if (frame == Time.frameCount) return;
        frame = Time.frameCount;
        Rendering.ShaderGlobals.Update();
        Camera camera = Verse.Current.CameraDriver?.MyCamera;
        if (camera == null) return;
        foreach (var renderer in MeshRenderer.Active)
            renderer.Draw(camera);
        foreach (var system in ParticleSystem.Active)
            if (system.GetComponentInParent<Canvas>() == null) system.GetComponent<ParticleSystemRenderer>()?.Draw(system, camera);
        foreach (var renderer in SkinnedMeshRenderer.Active)
            renderer.Draw(camera);
        UI.Runtime.UguiRuntime.DrawWorld();
        foreach (var renderer in TrailRenderer.Active)
            renderer.Draw(camera);
        foreach (var renderer in LineRenderer.Active)
            renderer.Draw(camera);
    }
}
