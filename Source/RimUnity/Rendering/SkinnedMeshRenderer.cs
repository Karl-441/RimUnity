using System;
using System.Collections.Generic;

namespace RimUnity;

//CPU 只计算每根骨骼的矩阵，顶点权重混合在 Waylith 顶点 Shader 中执行。
public sealed partial class SkinnedMeshRenderer : MonoBehaviour
{
    internal static readonly HashSet<SkinnedMeshRenderer> Active = new();
    public Mesh sharedMesh;
    public Material[] sharedMaterials = Array.Empty<Material>();
    public Transform[] bones = Array.Empty<Transform>();
    public Transform rootBone;
    public Bounds localBounds;
    public bool updateWhenOffscreen;
    public Material sharedMaterial { get => sharedMaterials.Length == 0 ? null : sharedMaterials[0]; set => sharedMaterials = new[] { value }; }
    private Mesh source, gpuMesh;
    private Matrix4x4[] bindposes;
    //宿主编译的骨骼 SSBO 是 RowMajor，不能直接上传列顺序存储的 RimUnity.Matrix4x4。
    private System.Numerics.Matrix4x4[] matrices = Array.Empty<System.Numerics.Matrix4x4>();
    private ComputeBuffer buffer, weightsBuffer;
    private bool bonesUploaded;
    private readonly MaterialPropertyBlock properties = new();
    private readonly Dictionary<Material, (Material adapted, int version, Waylith.Rendering.Shader[] passes, Shader shadowCaster)> materials = new();

    private void OnEnable() => Active.Add(this);
    private void OnDisable() => Active.Remove(this);
    private void OnDestroy()
    {
        Active.Remove(this); buffer?.Dispose(); weightsBuffer?.Dispose();
        if (gpuMesh != null) Destroy(gpuMesh);
        foreach (var material in materials.Values) Destroy(material.adapted);
        materials.Clear();
    }
    //原网格共享缓存，不写回实例专用权重通道或骨骼矩阵。
    private void Prepare()
    {
        if (source == sharedMesh) return;
        Bounds wantedBounds = sharedMesh != null ? sharedMesh.bounds : default;
        if (sharedMesh != null && localBounds.size.sqrMagnitude > 0)
        {
            Matrix4x4 space = rootBone != null ? transform.worldToLocalMatrix * rootBone.localToWorldMatrix : Matrix4x4.identity;
            wantedBounds = Rendering.AnimationVisibility.WorldBounds(localBounds, space);
        }
        //GPU 顶点不随姿势改变，视锥判断在 Draw 使用当前根骨骼边界。
        //宿主不读取 DrawItem 的网格边界进行剔除，边界变化不需要重建或重传顶点。
        wantedBounds.extents *= 1.25f;
        if (gpuMesh != null) Destroy(gpuMesh);
        source = sharedMesh;
        if (source == null) { gpuMesh = null; return; }
        bindposes = source.bindposes;
        var weights = Rendering.SkinWeights.Get(source);
        if (bindposes.Length == 0 || bones.Length != bindposes.Length) throw new InvalidOperationException("骨骼数量、Bind Pose 与顶点权重不匹配：" + name);
        gpuMesh = new Mesh { name = source.name + " GPU Skin", indexFormat = IndexFormat.UInt32, vertices = source.vertices, normals = source.normals, tangents = source.tangents, colors32 = source.colors32, subMeshCount = source.subMeshCount };
        for (int i = 0; i < source.subMeshCount; i++) gpuMesh.SetTriangles(source.GetIndices(i), i, false);
        foreach (var influence in weights.influences) if (influence.x >= bones.Length) throw new InvalidOperationException("骨骼索引越界：" + source.name);
        var uv = new List<Vector4>();
        for (int channel = 0; channel < 8; channel++)
        {
            source.GetUVs(channel, uv);
            if (channel == 6 && uv.Count != 0) throw new InvalidOperationException("源网格已使用蒙皮权重专用 UV6：" + source.name);
            gpuMesh.SetUVs(channel, uv);
        }
        gpuMesh.SetUVs(6, weights.ranges);
        //仅源网格改变时替换权重和矩阵缓冲，动画继续复用已上传的顶点。
        weightsBuffer?.Dispose();
        weightsBuffer = ComputeBuffer.CreateImmutable(weights.influences.Length == 0 ? new Vector4[1] : weights.influences);
        properties.SetBuffer("_SkinWeights", weightsBuffer);
        matrices = new System.Numerics.Matrix4x4[bones.Length];
        bonesUploaded = false;
        buffer?.Dispose(); buffer = new ComputeBuffer(bones.Length, 64); properties.SetBuffer("_Bones", buffer);
        gpuMesh.bounds = wantedBounds;
    }
    internal void Draw(Camera camera)
    {
        if (!isActiveAndEnabled || sharedMesh == null || sharedMesh.subMeshCount == 0 || sharedMaterials.Length == 0) return;
        //离屏舞台和地图共用对象注册表，视锥外的蒙皮不占用当前相机的绘制缓冲。
        bool hasBounds = localBounds.size.sqrMagnitude > 0;
        Transform space = hasBounds && rootBone != null ? rootBone : transform;
        Bounds worldBounds = Rendering.AnimationVisibility.WorldBounds(hasBounds ? localBounds : sharedMesh.bounds, space.localToWorldMatrix);
        if (!Rendering.AnimationVisibility.Intersects(camera.projectionMatrix * camera.worldToCameraMatrix, worldBounds)) return;
        Prepare();
        //保持 Renderer 局部空间，自定义顶点程序的对象坐标语义不变。
        Matrix4x4 inverse = transform.worldToLocalMatrix;
        bool poseChanged = !bonesUploaded;
        for (int i = 0; i < matrices.Length; i++)
        {
            var matrix = Matrix4x4.ToNumerics(inverse * bones[i].localToWorldMatrix * bindposes[i]);
            poseChanged |= !matrices[i].Equals(matrix);
            matrices[i] = matrix;
        }
        //主体、遮罩和多相机共享同一姿势快照，姿势未变时不再复制矩阵缓冲。
        if (poseChanged) { buffer.SetData(matrices); bonesUploaded = true; }
        for (int i = 0; i < sharedMaterials.Length; i++)
        {
            var material = sharedMaterials[i]; if (material == null) continue;
            if (!materials.TryGetValue(material, out var cached)
                || cached.shadowCaster != material.shadowCaster || !SamePasses(material, cached.passes))
            {
                if (cached.adapted != null) Destroy(cached.adapted);
                var passes = new Waylith.Rendering.Shader[material.passCount];
                for (int pass = 0; pass < passes.Length; pass++) passes[pass] = material.GetNativePass(pass).Shader;
                cached = (Rendering.SkinnedShaderVariants.Adapt(material), -1, passes, material.shadowCaster);
            }
            if (cached.version != material.engineMaterial.Version)
            {
                cached.adapted.CopyPropertiesFromMaterial(material);
                cached.version = material.engineMaterial.Version;
                materials[material] = cached;
            }
            //附加描边等材质沿用 Unity 规则，重复绘制最后一个子网格。
            RimUnity.Graphics.DrawMesh(gpuMesh, transform.localToWorldMatrix, cached.adapted, gameObject.layer,
                Math.Min(i, gpuMesh.subMeshCount - 1), properties);
        }
    }

    //参数版本不包含完整 Pass 组合，切换程序时必须同步蒙皮变体。
    private static bool SamePasses(Material source, Waylith.Rendering.Shader[] passes)
    {
        if (source.passCount != passes.Length) return false;
        for (int i = 0; i < passes.Length; i++)
            if (!ReferenceEquals(source.GetNativePass(i).Shader, passes[i])) return false;
        return true;
    }
}
