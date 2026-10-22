using System;

namespace RimUnity.Diagnostics;

// Explicit diagnostic configurations, never asset-name special cases. Resources
// belong to the lab session and are released after all its particle roots.
internal sealed class LabStressScenario : IDisposable
{
    internal const string Version = "mixed-particles-v2";
    internal const int WorldLayer = 30;
    internal const int Families = 12, Copies = 4, ParticleLimit = 64;
    internal static readonly string[] Names = { "普通面片", "拉伸面片", "水平面片", "垂直面片", "网格粒子", "粒子拖尾",
        "多层噪声", "寿命曲线", "图集动画", "复杂顶点流", "自定义空间", "出生子发射器" };
    private Material material, additive, animated;
    private Texture2D atlas;
    private Mesh mesh;
    private bool disposed;

    internal LabStressScenario()
    {
        try
        {
            material = MakeMaterial("RimUnity_WorldParticleAlpha");
            additive = MakeMaterial("RimUnity_WorldParticleAdditive");
            additive.SetColor("_Color", new Color(2.5f, 1.5f, .7f, 1));
            atlas = new Texture2D(16, 16);
            var pixels = new Color32[256];
            for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
            {
                int tile = x / 8 + y / 8 * 2;
                pixels[y * 16 + x] = new Color32((byte)(100 + tile * 40), (byte)(230 - tile * 30), 220, 210);
            }
            atlas.SetPixels32(pixels); atlas.Apply();
            animated = new Material(material); animated.mainTexture = atlas;
            mesh = new Mesh();
            mesh.vertices = new[] { new Vector3(-.4f, 0, 0), new Vector3(.4f, 0, 0), new Vector3(0, .7f, 0), new Vector3(0, 0, .5f) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.uv = new[] { Vector2.zero, Vector2.one, new Vector2(.5f, 1), new Vector2(.5f, .5f) };
            mesh.triangles = new[] { 0, 2, 1, 0, 1, 3, 1, 2, 3, 2, 0, 3 };
        }
        catch { Dispose(); throw; }
    }

    internal GameObject Create(int kind)
    {
        if (disposed) throw new ObjectDisposedException(nameof(LabStressScenario));
        if ((uint)kind >= Families) throw new ArgumentOutOfRangeException(nameof(kind));
        var root = new GameObject("Stress " + Names[kind]);
        root.SetActive(false);
        try { Configure(root, kind, kind == 8 ? animated : kind % 2 == 0 ? material : additive, mesh); return root; }
        catch { RimUnity.Object.DestroyImmediate(root); throw; }
    }

    internal static Rendering.BloomFrame BloomSettings(Rendering.BloomFrame source, bool enabled)
        => source with { Enabled = enabled, LayerMask = enabled ? 1 << WorldLayer : 0,
            Threshold = .8f, Intensity = .6f, SoftKnee = .5f, Scatter = .65f };

    private static Material MakeMaterial(string key)
    {
        if (!Waylith.Rendering.ShaderRegistry.TryResolve(key, out var shader)) throw new InvalidOperationException("测试场缺少内置 Shader：" + key);
        // Explicit resource references; the existing converter validates batching and linear output.
        var result = new Material(Rendering.StandardParticleShader.Resolve(shader, linear: true));
        try { result.mainTexture = Texture2D.whiteTexture; return result; }
        catch { RimUnity.Object.DestroyImmediate(result); throw; }
    }

    // Also exercised with the real runtime and CPU-only fixture resources.
    internal static void Configure(GameObject root, int kind, Material shared, Mesh sourceMesh)
    {
        if ((uint)kind >= Families) throw new ArgumentOutOfRangeException(nameof(kind));
        var ps = root.AddComponent<ParticleSystem>();
        var renderer = root.AddComponent<ParticleSystemRenderer>();
        root.layer = WorldLayer;
        Base(ps, renderer, shared, kind);
        switch (kind)
        {
            case 1: renderer.renderMode = ParticleSystemRenderMode.Stretch; renderer.velocityScale = .25f; renderer.lengthScale = 1.5f; break;
            case 2: renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard; break;
            case 3: renderer.renderMode = ParticleSystemRenderMode.VerticalBillboard; renderer.alignment = ParticleSystemRenderSpace.Facing; break;
            case 4:
                renderer.renderMode = ParticleSystemRenderMode.Mesh; renderer.mesh = sourceMesh;
                ps.main.startRotation3D = true; ps.main.startRotationX = new ParticleSystem.MinMaxCurve(-1, 1);
                ps.rotationOverLifetime.enabled = true; ps.rotationOverLifetime.separateAxes = true;
                ps.rotationOverLifetime.y = .8f; break;
            case 5:
                ps.trails.enabled = true; ps.trails.lifetime = .8f; ps.trails.minVertexDistance = .12f;
                ps.trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .15f, 1, 0));
                renderer.trailMaterial = shared; break;
            case 6:
                ps.noise.enabled = true; ps.noise.octaveCount = 3; ps.noise.strength = .4f; ps.noise.scrollSpeed = .3f; break;
            case 7:
                ps.sizeOverLifetime.enabled = true; ps.sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .2f, 1, 1));
                ps.velocityOverLifetime.enabled = true; ps.velocityOverLifetime.y = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 0, 1, .6f));
                ps.forceOverLifetime.enabled = true; ps.forceOverLifetime.x = .12f;
                ps.rotationOverLifetime.enabled = true; ps.rotationOverLifetime.z = .5f; break;
            case 8:
                ps.textureSheetAnimation.enabled = true; ps.textureSheetAnimation.numTilesX = ps.textureSheetAnimation.numTilesY = 2;
                ps.textureSheetAnimation.timeMode = ParticleSystemAnimationTimeMode.FPS; ps.textureSheetAnimation.fps = 7;
                renderer.vertexStreams = new[] { ParticleSystemVertexStream.UV, ParticleSystemVertexStream.UV2, ParticleSystemVertexStream.AnimBlend };
                break;
            case 9:
                ps.customData.enabled = true; ps.customData.SetMode(ParticleSystemCustomData.Custom1, ParticleSystemCustomDataMode.Vector);
                ps.customData.SetVector(ParticleSystemCustomData.Custom1, 0, new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 0, 1, 1)));
                renderer.vertexStreams = new[] { ParticleSystemVertexStream.UV, ParticleSystemVertexStream.StableRandomXYZW,
                    ParticleSystemVertexStream.VaryingRandomXYZ, ParticleSystemVertexStream.Custom1XY, ParticleSystemVertexStream.VertexID };
                renderer.sortMode = ParticleSystemSortMode.Depth; break;
            case 10:
                ps.main.simulationSpace = ParticleSystemSimulationSpace.Custom; ps.main.customSimulationSpace = root.transform;
                ps.main.scalingMode = ParticleSystemScalingMode.Local; root.transform.localScale = new Vector3(1.2f, .8f, 1);
                renderer.alignment = ParticleSystemRenderSpace.Velocity; break;
            case 11:
                ps.emission.rateOverTime = 4; ps.main.maxParticles = 16;
                var child = new GameObject("Stress event child") { layer = WorldLayer }; child.transform.SetParent(root.transform, false);
                var target = child.AddComponent<ParticleSystem>(); var childRenderer = child.AddComponent<ParticleSystemRenderer>();
                Base(target, childRenderer, shared, kind); target.main.loop = false; target.main.startLifetime = 1;
                target.emission.rateOverTime = 0; target.emission.bursts = new[] { new ParticleSystem.Burst(0, (short)4) };
                ps.subEmitters.enabled = true; ps.subEmitters.AddSubEmitter(target, ParticleSystemSubEmitterType.Birth, ParticleSystemSubEmitterProperties.InheritColor);
                break;
        }
    }

    private static void Base(ParticleSystem ps, ParticleSystemRenderer renderer, Material shared, int kind)
    {
        ps.main.duration = 4; ps.main.loop = true; ps.main.startLifetime = 4; ps.main.startSpeed = .65f;
        ps.main.startSize = .3f; ps.main.maxParticles = ParticleLimit;
        ps.main.simulationSpace = kind % 2 == 0 ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
        ps.main.startColor = new Color(.3f + kind % 3 * .3f, .35f + kind % 4 * .15f, 1 - kind % 3 * .25f, .8f);
        ps.emission.rateOverTime = 16; ps.shape.shapeType = kind % 2 == 0 ? ParticleSystemShapeType.Sphere : ParticleSystemShapeType.Cone;
        ps.shape.radius = .5f; ps.shape.angle = 20;
        renderer.sharedMaterial = shared;
        renderer.vertexStreams = new[] { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV };
    }

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        if (material != null) RimUnity.Object.DestroyImmediate(material);
        if (additive != null) RimUnity.Object.DestroyImmediate(additive);
        if (animated != null) RimUnity.Object.DestroyImmediate(animated);
        if (atlas != null) RimUnity.Object.DestroyImmediate(atlas);
        if (mesh != null) RimUnity.Object.DestroyImmediate(mesh);
        material = additive = animated = null; atlas = null; mesh = null;
    }
}
