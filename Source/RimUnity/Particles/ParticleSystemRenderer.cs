using System;
using System.Collections.Generic;
using RimUnity.Diagnostics;

namespace RimUnity;

//粒子按排序生成动态网格；数值流按导出的列表顺序紧密打包。
public sealed partial class ParticleSystemRenderer : MonoBehaviour
{
    public Material sharedMaterial, trailMaterial;
    public ParticleSystemRenderMode renderMode;
    public ParticleSystemRenderSpace alignment;
    public ParticleSystemSortMode sortMode;
    public float lengthScale = 2, velocityScale, normalDirection = 1;
    public Vector3 pivot, flip;
    public bool allowRoll = true;
    //最近一次世界网格提交，区分模拟和绘制问题。
    private Mesh batch, trailBatch;
    private readonly List<Vector3> vertices = new();
    private readonly List<Vector2> uv = new();
    private readonly List<Vector4> packedUV = new();
    private readonly List<Color32> colors = new();
    private readonly List<Vector4> custom1 = new(), custom2 = new();
    private readonly List<int> indices = new();
    private readonly List<int> order = new();
    private Comparison<int> particleComparison;
    private ParticleSystem sortingSystem;
    // Numeric scratch owned by this renderer, valid only during synchronous Sort.
    // Refilled for each camera/draw; never a cross-frame transform cache.
    private readonly List<Vector3> sortingWorldPositions = new();
    private Vector3 sortingPosition, sortingForward;
    private readonly List<ParticleSystemVertexStream> streams = new();
    private float particleRandom;
    private Mesh sourceMesh;
    private Vector3[] sourcePositions;
    private Vector2[] sourceUV;
    private int[] sourceTriangles;
    private readonly List<Material> ownedMaterials = new();
    //实例参数不能污染其他粒子系统共用的模板材质。
    public Material material
    {
        get
        {
            Material source = sharedMaterial;
            if (source == null || ownedMaterials.Contains(source)) return source;
            var clone = new Material(source);
            sharedMaterial = clone;
            ownedMaterials.Add(clone);
            return clone;
        }
        set => sharedMaterial = value;
    }
    public void GetActiveVertexStreams(List<ParticleSystemVertexStream> target) { target.Clear(); target.AddRange(streams); }
    public void SetActiveVertexStreams(List<ParticleSystemVertexStream> value) => vertexStreams = value.ToArray();

    //依照 Unity 的列表顺序将数值流紧密排列到 TEXCOORD。
    public ParticleSystemVertexStream[] vertexStreams
    {
        get => streams.ToArray();
        set
        {
            int width = 0;
            foreach (var stream in value) width += StreamWidth(stream);
            if (width > 12) throw new NotSupportedException("粒子数值流超过 TEXCOORD0～2 的容量。");
            var layout = BuildStreamLayout(value);
            streams.Clear(); streams.AddRange(value);
            particleStreamPlan = layout.Particle; vertexStreamPlan = layout.Vertex;
            streamPacking = SelectStreamPacking(value.Length, particleStreamPlan, vertexStreamPlan);
            preparedStreamValues = default; streamValuesPrepared = false;
        }
    }

    //网格缓存随组件销毁，不持有已卸载实例。
    internal void ReleaseGeometry() { if (batch != null) Destroy(batch); if (trailBatch != null) Destroy(trailBatch); batch = trailBatch = null; ReleaseSpriteBatches(); meshGeometry.Clear(); foreach (var material in uiMaterials.Values) Destroy(material); uiMaterials.Clear(); }
    private void OnDestroy()
    {
        ReleaseGeometry();
        foreach (Material owned in ownedMaterials) Destroy(owned);
        ownedMaterials.Clear();
    }
    internal void Draw(ParticleSystem system, Camera camera)
    {
        if (!enabled || !gameObject.activeInHierarchy) return;
        using var cpu = LabCpuMetrics.MeasureSystem(LabCpuStage.ParticleRenderer, system.GetInstanceID());
        preparedSpriteTextures.Clear();
        //零上限只隐藏粒子面片，模拟和下方独立绘制的拖尾继续运行。
        if (sharedMaterial != null && renderMode != ParticleSystemRenderMode.None && system.count != 0
            && (renderMode == ParticleSystemRenderMode.Mesh || maxParticleSize > 0))
        {
            var cameraState = new ParticleCameraState(camera, uiEntry?.Canvas, allowRoll);
            var drawSpace = new ParticleSystem.DrawSpace(system);
            ClearMesh(); order.Clear();
            for (int i = 0; i < system.count; i++) order.Add(i);
            if (sortMode != ParticleSystemSortMode.None)
            {
                using var sortCpu = LabCpuMetrics.Measure(LabCpuStage.ParticleSort);
                try
                {
                    PrepareSorting(system, in cameraState, in drawSpace);
                    // Keep this call in Draw: optional DPA instruments this exact site.
                    order.Sort(particleComparison ??= CompareParticles);
                }
                finally { ClearSorting(); }
            }
            DrawParticleBatches(system, camera, in cameraState, in drawSpace);
        }
        if (system.trails.enabled && trailMaterial != null) DrawTrails(system, camera);
        TrimSpriteBatches();
    }
    private void PrepareSorting(ParticleSystem system, in ParticleCameraState cameraState, in ParticleSystem.DrawSpace drawSpace)
    {
        sortingSystem = system;
        sortingPosition = cameraState.Position;
        sortingForward = cameraState.Forward;
        if (sortMode != ParticleSystemSortMode.OldestInFront && sortMode != ParticleSystemSortMode.YoungestInFront && system.count > 1)
            for (int i = 0; i < system.count; i++) sortingWorldPositions.Add(drawSpace.WorldPosition(system.particles[i]));
    }
    private void ClearSorting() { sortingSystem = null; sortingWorldPositions.Clear(); }
    //排序期间相机姿态不变，比较器不重复读层级变换或创建闭包。
    private int CompareParticles(int a, int b)
    {
        var s = sortingSystem;
        return sortMode switch
        {
            ParticleSystemSortMode.OldestInFront => s.particles[a].elapsedAge.CompareTo(s.particles[b].elapsedAge),
            ParticleSystemSortMode.YoungestInFront => s.particles[b].elapsedAge.CompareTo(s.particles[a].elapsedAge),
            ParticleSystemSortMode.Depth => Vector3.Dot(sortingWorldPositions[b] - sortingWorldPositions[a], sortingForward).CompareTo(0),
            _ => (sortingWorldPositions[b] - sortingPosition).sqrMagnitude.CompareTo((sortingWorldPositions[a] - sortingPosition).sqrMagnitude)
        };
    }
    private void AppendParticle(ParticleSystem s, in ParticleSystem.Particle p, in ParticleCameraState cameraState, in ParticleSystem.DrawSpace drawSpace)
    {
        Vector3 center, size; Color32 color; Vector4 c1, c2; Rect rect;
        using (LabCpuMetrics.Measure(LabCpuStage.ParticleAttributes))
        {
            particleRandom = ParticleSystem.Random(p.randomSeed, 97);
            center = drawSpace.WorldPosition(p); size = Vector3.Scale(s.SizeAt(p), drawSpace.ParticleScale(p));
            color = s.ColorAt(p); c1 = s.CustomAt(p, 0); c2 = s.CustomAt(p, 1);
            streamParticle = p; streamSize = size; streamVelocity = drawSpace.WorldVelocity(p); streamCenter = center;
            rect = UV(s, p);
            if (ParticleSystem.Random(p.randomSeed, 80) < flip.x) { rect.x += rect.width; rect.width = -rect.width; }
            if (ParticleSystem.Random(p.randomSeed, 81) < flip.y) { rect.y += rect.height; rect.height = -rect.height; }
        }
        if (renderMode == ParticleSystemRenderMode.Mesh)
        {
            SelectMesh(p);
            int offset = vertices.Count;
            // Shape-aligned particles already carry their emission orientation; other meshes obey Renderer alignment.
            Quaternion orientation = p.alignedToDirection ? s.ParticleRotation(p) : BillboardAlignment(s, in cameraState, center, streamVelocity);
            Matrix4x4 matrix = Matrix4x4.TRS(center, orientation * Quaternion.Euler(p.rotation3D), size);
            Matrix4x4 normalMatrix = sourcePositions.Length != 0 && sourceNormals.Length != 0 ? matrix.inverse.transpose : default;
            // Reuse identical source directions only within this particle's current pose.
            Vector3 sharedNormal = sourceUniformNormals ? normalMatrix.MultiplyVector(sourceNormals[0]).normalized : default;
            Vector4 sharedTangent = sourceUniformTangents ? (Vector4)matrix.MultiplyVector((Vector3)sourceTangents[0]).normalized : default;
            bool flipMesh = ParticleSystem.Random(p.randomSeed, 82) < flip.z;
            if (sourcePositions.Length != 0 && streamPacking == StreamPacking.General) PrepareParticleStreams(c1, c2);
            for (int i = 0; i < sourcePositions.Length; i++)
            {
                streamNormal = i < sourceNormals.Length ? (sourceUniformNormals ? sharedNormal : normalMatrix.MultiplyVector(sourceNormals[i]).normalized) : Vector3.forward;
                streamTangent = i < sourceTangents.Length ? (sourceUniformTangents ? sharedTangent : (Vector4)matrix.MultiplyVector((Vector3)sourceTangents[i]).normalized) : new Vector4(1, 0, 0, 1);
                if (i < sourceTangents.Length) streamTangent.w = sourceTangents[i].w;
                Vector2 source = i < sourceUV.Length ? sourceUV[i] : Vector2.zero;
                streamVertexId = i; streamUV3 = i < sourceUV3.Length ? sourceUV3[i] : Vector2.zero; streamUV4 = i < sourceUV4.Length ? sourceUV4[i] : Vector2.zero;
                Vector3 local = sourcePositions[i] + pivot;
                if (flipMesh) local.z = -local.z;
                Vertex(matrix.MultiplyPoint3x4(local), new Vector2(rect.x + source.x * rect.width, rect.y + source.y * rect.height), color, c1, c2);
            }
            foreach (int index in sourceTriangles) indices.Add(offset + index);
            return;
        }
        BillboardAxesPrepared(s, p, in cameraState, center, size, streamVelocity, out Vector3 x, out Vector3 y);
        streamNormal = -Vector3.Cross(x, y).normalized;
        Vector3 tangent = x.normalized;
        streamTangent = new Vector4(tangent.x, tangent.y, tangent.z, 1);
        center += x * (pivot.x * 2) + y * (pivot.y * 2);
        int start = GeometryVertexCount;
        streamVertexId = 0; streamUV3 = streamUV4 = Vector2.zero;
        if (streamPacking != StreamPacking.General)
        {
            AppendSimpleBillboard(center, x, y, rect, color, c1, c2);
            streamVertexId = 3;
            Quad(start);
            return;
        }
        if (streamPacking == StreamPacking.General) PrepareParticleStreams(c1, c2);
        Vertex(center - x - y, new Vector2(rect.xMin, rect.yMin), color, c1, c2);
        streamVertexId = 1;
        Vertex(center - x + y, new Vector2(rect.xMin, rect.yMax), color, c1, c2);
        streamVertexId = 2;
        Vertex(center + x + y, new Vector2(rect.xMax, rect.yMax), color, c1, c2);
        streamVertexId = 3;
        Vertex(center + x - y, new Vector2(rect.xMax, rect.yMin), color, c1, c2);
        Quad(start);
    }
    private void ClearMesh() { packedBillboards = false; billboardVertices.Clear(); vertices.Clear(); uv.Clear(); packedUV.Clear(); colors.Clear(); custom1.Clear(); custom2.Clear(); indices.Clear(); normals.Clear(); tangents.Clear(); particleRandom = 0; streamValuesPrepared = false; streamNormal = Vector3.forward; streamTangent = new Vector4(1, 0, 0, 1); }
    private void Vertex(Vector3 p, Vector2 t, Color32 c, Vector4 c1 = default, Vector4 c2 = default)
    {
        vertices.Add(p); uv.Add(t);
        PackStreams(t, c1, c2);
        colors.Add(c); normals.Add(streamNormal); tangents.Add(streamTangent);
    }
    private void Quad(int start) { indices.Add(start); indices.Add(start + 1); indices.Add(start + 2); indices.Add(start); indices.Add(start + 2); indices.Add(start + 3); }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void PrepareSubmitUI(Material mat)
    {
        for (int i = 0; i < colors.Count; i++) { Color color = colors[i]; color.a *= cameraUiEntry.Alpha; colors[i] = color; }
        UI.Runtime.UiMaskData.Apply(mat);
    }
    private void Submit(ref Mesh target, Material mat, Camera camera)
    {
        using var cpu = LabCpuMetrics.Measure(LabCpuStage.ParticleSubmit);
        LabCpuMetrics.Work(LabCpuStage.ParticleSubmit, GeometryVertexCount, indices.Count);
        var dpaToken = Diagnostics.DpaDiagnostics.BeginSubmit();
        int dpaRoute = 0;
        try
        {
            if (indices.Count == 0) { LabCpuMetrics.Batch(LabBatchReason.Empty); dpaRoute = 1; return; }
            if (uiEntry != null)
            {
                ParticleDrawBatch.Flush();
                LabCpuMetrics.Batch(LabBatchReason.UI);
                CaptureDiagnostics(mat, null);
                using (LabCpuMetrics.Measure(LabCpuStage.SubmitUI)) SubmitUI(mat);
                dpaRoute = 2; return;
            }
            if (cameraUiEntry != null)
            {
                ParticleDrawBatch.Flush();
                LabCpuMetrics.Batch(LabBatchReason.CameraUI);
                using (LabCpuMetrics.Measure(LabCpuStage.SubmitUI)) PrepareSubmitUI(mat);
            }
            if (cameraUiEntry == null && ParticleDrawBatch.TryAppend(mat, gameObject.layer, propertyBlock,
                vertices, normals, tangents, colors, packedUV, custom1, custom2, indices, packedBillboards ? billboardVertices : null)) { dpaRoute = 3; return; }
            using (LabCpuMetrics.Measure(LabCpuStage.MeshFill))
            {
                if (target == null)
                {
                    target = new Mesh { name = "RimUnity Particle Batch", indexFormat = IndexFormat.UInt32 };
                    target.MarkDynamic();
                }
                if (packedBillboards)
                {
                    target.Clear();
                    target.AppendParticleGeometry(vertices, normals, tangents, colors, packedUV, custom1, custom2, indices, 0, billboardVertices);
                }
                else
                {
                    target.SetVertices(vertices); target.SetColors(colors); target.SetUVs(0, packedUV); target.SetUVs(1, custom1); target.SetUVs(2, custom2); target.SetTriangles(indices, 0);
                    target.SetNormals(normals); target.SetTangents(tangents);
                }
            }
            LabCpuMetrics.Work(LabCpuStage.MeshFill, GeometryVertexCount, indices.Count);
            CaptureDiagnostics(mat, target);
            using (LabCpuMetrics.Measure(LabCpuStage.DrawMesh))
                RimUnity.Graphics.DrawMesh(target, Matrix4x4.identity, mat, cameraUiEntry?.RenderLayer ?? gameObject.layer, 0, propertyBlock);
            dpaRoute = cameraUiEntry == null ? 4 : 5;
        }
        finally
        {
            if (dpaToken.Start != 0) Diagnostics.DpaDiagnostics.EndSubmit(dpaToken, UnityEngine.Time.frameCount, GeometryVertexCount, indices.Count, dpaRoute);
        }
    }
}
