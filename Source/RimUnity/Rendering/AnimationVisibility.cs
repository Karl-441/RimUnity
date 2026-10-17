using System.Collections.Generic;

namespace RimUnity.Rendering;

//Legacy 动画以自身及子节点的网格可见性决定是否推进状态。
internal static class AnimationVisibility
{
    private static readonly List<(Camera camera, int frame)> ManualCameras = new();

    //显式 Render 允许禁用相机；保留上一帧的视图供下一次动画更新查询。
    internal static void RecordRender(Camera camera)
    {
        RemoveExpiredCameras();
        for (int i = 0; i < ManualCameras.Count; i++)
            if (ManualCameras[i].camera == camera) { ManualCameras[i] = (camera, Time.frameCount); return; }
        ManualCameras.Add((camera, Time.frameCount));
    }

    //只保留最近发生过的手动绘制，释放已销毁或停止使用的相机引用。
    private static void RemoveExpiredCameras()
    {
        for (int i = ManualCameras.Count - 1; i >= 0; i--)
            if (!ManualCameras[i].camera || ManualCameras[i].frame < Time.frameCount - 1) ManualCameras.RemoveAt(i);
    }

    //动画可见性独立于采样结果，摄像机移回时可以恢复暂停的骨骼动画。
    internal static bool IsVisible(Transform root)
    {
        RemoveExpiredCameras();
        foreach (Camera camera in Camera.Active)
            if (camera.isActiveAndEnabled && (camera == Camera.main || camera.targetTexture != null) && VisibleTo(root, camera)) return true;
        foreach (var entry in ManualCameras)
            if (VisibleTo(root, entry.camera)) return true;
        return false;
    }

    //使用当前层级变换与相机图层，动态移动、启停及重挂节点无需重建缓存。
    private static bool VisibleTo(Transform root, Camera camera)
    {
        Matrix4x4 viewProjection = camera.projectionMatrix * camera.worldToCameraMatrix;
        foreach (MeshRenderer renderer in MeshRenderer.Active)
        {
            if (!renderer.isActiveAndEnabled || !renderer.transform.IsChildOf(root)
                || (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0) continue;
            Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null || mesh.subMeshCount == 0 || renderer.sharedMaterials.Length == 0) continue;
            if (renderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly && HasMaterial(renderer.sharedMaterials)
                && Intersects(viewProjection, renderer.bounds)) return true;
            if (VisibleShadow(camera, renderer)) return true;
        }
        foreach (SkinnedMeshRenderer renderer in SkinnedMeshRenderer.Active)
        {
            if (!renderer.isActiveAndEnabled || !renderer.transform.IsChildOf(root)
                || (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0
                || renderer.sharedMesh == null || renderer.sharedMesh.subMeshCount == 0 || !HasMaterial(renderer.sharedMaterials)) continue;
            bool hasLocalBounds = renderer.localBounds.size.sqrMagnitude > 0;
            Bounds local = hasLocalBounds ? renderer.localBounds : renderer.sharedMesh.bounds;
            //Unity 的蒙皮包围盒随根骨骼移动；原始 Mesh 包围盒仍属于网格自身空间。
            Transform space = hasLocalBounds && renderer.rootBone != null ? renderer.rootBone : renderer.transform;
            if (Intersects(viewProjection, WorldBounds(local, space.localToWorldMatrix))) return true;
        }
        foreach (ParticleSystem system in ParticleSystem.Active)
        {
            if (!system.isActiveAndEnabled || !system.transform.IsChildOf(root)
                || (camera.cullingMask & (1 << system.gameObject.layer)) == 0) continue;
            ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
            if (renderer != null && renderer.isActiveAndEnabled && renderer.AnimationVisible(system, camera, viewProjection)) return true;
        }
        return false;
    }

    //只把实际具有绘制材质的网格视为渲染来源。
    private static bool HasMaterial(Material[] materials)
    {
        foreach (Material material in materials) if (material != null) return true;
        return false;
    }

    //方向阴影目前覆盖同视图的所有投影网格，不能因为本体在视锥外就冻结投影动画。
    private static bool VisibleShadow(Camera camera, MeshRenderer renderer)
    {
        if (renderer.shadowCastingMode == ShadowCastingMode.Off) return false;
        bool casts = false;
        foreach (Material material in renderer.sharedMaterials) if (material?.shadowCaster != null) { casts = true; break; }
        if (!casts) return false;
        if (camera.targetTexture != null)
        {
            if (renderer.transform.root != camera.transform.root) return false;
            foreach (Light light in Light.allLights)
                if (light.isActiveAndEnabled && light.type == LightType.Directional && light.transform.root == camera.transform.root)
                    return light.shadows != LightShadows.None && (light.cullingMask & (1 << renderer.gameObject.layer)) != 0;
        }
        else foreach (Camera other in Camera.Active)
            if (other.targetTexture != null && other.transform.root == renderer.transform.root) return false;
        return true;
    }

    //矩阵的线性部分绝对值将本地半尺寸扩展为世界轴对齐包围盒。
    internal static Bounds WorldBounds(Bounds local, Matrix4x4 matrix)
    {
        Vector3 e = local.extents;
        Vector3 extents = new(Mathf.Abs(matrix.m00) * e.x + Mathf.Abs(matrix.m01) * e.y + Mathf.Abs(matrix.m02) * e.z,
            Mathf.Abs(matrix.m10) * e.x + Mathf.Abs(matrix.m11) * e.y + Mathf.Abs(matrix.m12) * e.z,
            Mathf.Abs(matrix.m20) * e.x + Mathf.Abs(matrix.m21) * e.y + Mathf.Abs(matrix.m22) * e.z);
        return new Bounds(matrix.MultiplyPoint3x4(local.center), extents * 2);
    }

    //Waylith 的齐次裁剪范围是 -w≤x,y≤w、0≤z≤w，兼容透视、正交和反向深度。
    internal static bool Intersects(Matrix4x4 viewProjection, Bounds bounds)
    {
        Vector4 x = viewProjection.GetRow(0), y = viewProjection.GetRow(1), z = viewProjection.GetRow(2), w = viewProjection.GetRow(3);
        return Inside(w + x, bounds) && Inside(w - x, bounds) && Inside(w + y, bounds)
            && Inside(w - y, bounds) && Inside(z, bounds) && Inside(w - z, bounds);
    }

    //AABB 支持点在平面内侧时保留相交对象，不需归一化平面。
    private static bool Inside(Vector4 plane, Bounds bounds)
        => plane.x * bounds.center.x + plane.y * bounds.center.y + plane.z * bounds.center.z + plane.w
            + Mathf.Abs(plane.x) * bounds.extents.x + Mathf.Abs(plane.y) * bounds.extents.y + Mathf.Abs(plane.z) * bounds.extents.z >= 0;
}
