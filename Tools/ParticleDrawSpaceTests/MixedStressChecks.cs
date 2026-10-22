using System.Reflection;
using RimUnity;
using RimUnity.Runtime;
using Object = RimUnity.Object;

internal static class MixedStressChecks
{
    internal static void Run()
    {
        NativeRuntime.InitializeOnCurrentThread();
        var type = typeof(ParticleSystem).Assembly.GetType("RimUnity.Diagnostics.LabStressScenario", true);
        var configure = type.GetMethod("Configure", BindingFlags.Static | BindingFlags.NonPublic);
        var seed = typeof(ParticleSystem).Assembly.GetType("RimUnity.Diagnostics.PerformanceLab", true)
            .GetMethod("ConfigureFixedSystems", BindingFlags.Static | BindingFlags.NonPublic);
        using var geometry = new Owned(new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } });
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        var bloomType = typeof(ParticleSystem).Assembly.GetType("RimUnity.Rendering.BloomFrame", true);
        var source = Activator.CreateInstance(bloomType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            new object[] { false, .2f, 0f, .1f, .2f }, null);
        bloomType.GetProperty("LayerMask", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(source, 1 << 29);
        var apply = type.GetMethod("BloomSettings", BindingFlags.NonPublic | BindingFlags.Static);
        foreach (bool enabled in new[] { true, false })
        {
            var value = apply.Invoke(null, new object[] { source, enabled });
            Check((bool)bloomType.GetProperty("Enabled").GetValue(value) == enabled, "scoped Bloom toggle");
            Check((int)bloomType.GetProperty("LayerMask", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value) == (enabled ? 1 << 30 : 0), "exact test layer mask");
        }
        Check(!(bool)bloomType.GetProperty("Enabled").GetValue(source)
            && (int)bloomType.GetProperty("LayerMask", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(source) == (1 << 29)
            && (float)bloomType.GetProperty("Intensity").GetValue(source) == 0, "original Bloom settings preserved");
        var previousSeeds = new uint[12][];
        for (int run = 0; run < 2; run++) for (int kind = 0; kind < 12; kind++)
        {
            GameObject root;
            using (NativeRuntime.DeferLifecycle())
            {
                root = new GameObject("mixed fixture"); root.SetActive(false);
                configure.Invoke(null, new object[] { root, kind, null, geometry.Value });
            }
            try
            {
                var systems = root.GetComponentsInChildren<ParticleSystem>(true);
                Check(systems.Length == (kind == 11 ? 2 : 1), "recipe system count");
                seed.Invoke(null, new object[] { systems, kind });
                Check(systems.All(p => !p.useAutoRandomSeed && p.randomSeed != 0 && p.main.maxParticles <= 64), "bounded explicit seeds/particle caps");
                Check(systems.All(p => p.gameObject.GetComponent<ParticleSystemRenderer>() != null), "recipe renderer missing");
                var values = systems.Select(p => p.randomSeed).ToArray();
                if (run == 0) previousSeeds[kind] = values; else Check(values.SequenceEqual(previousSeeds[kind]), "rebuild seed drift");
                var ps = systems[0]; var renderer = root.GetComponent<ParticleSystemRenderer>();
                Check(renderer.vertexStreams.Length > 0, "explicit vertex layout missing");
                if (kind == 4) Check(renderer.renderMode == ParticleSystemRenderMode.Mesh && renderer.mesh != null, "mesh recipe");
                if (kind == 5) Check(ps.trails.enabled, "trail recipe");
                if (kind == 6) Check(ps.noise.enabled && ps.noise.octaveCount == 3, "noise recipe");
                if (kind == 7) Check(ps.velocityOverLifetime.enabled && ps.sizeOverLifetime.enabled && ps.forceOverLifetime.enabled, "curve recipe");
                if (kind == 8) Check(ps.textureSheetAnimation.enabled && renderer.vertexStreams.Contains(ParticleSystemVertexStream.UV2), "animated UV recipe");
                if (kind == 9) Check(ps.customData.enabled && renderer.vertexStreams.Contains(ParticleSystemVertexStream.VaryingRandomXYZ), "rich stream recipe");
                if (kind == 10) Check(ps.main.customSimulationSpace == root.transform, "custom space belongs to clone");
                if (kind == 11) Check(ps.subEmitters.entries[0].system == systems[1] && !systems[1].main.loop, "subemitter graph/loop contract");
                root.SetActive(true);
                // Explicit simulation uses the real runtime, without opening a game/GPU.
                for (int i = 0; i < 120; i++) ps.Simulate(.1f, withChildren: true, restart: i == 0, fixedTimeStep: false);
                if (kind == 11) Check(systems[1].particleCount > 0, "birth subemitter actually emits");
                Check(ps.particleCount > 0 && ps.particleCount <= ps.main.maxParticles, "loop has sustained bounded particles");
                Check(root.GetComponentsInChildren<ParticleSystem>(true).All(p => p.particleCount <= p.main.maxParticles), "event descendants exceed particle cap");
            }
            finally { Object.DestroyImmediate(root); }
        }
        Console.WriteLine($"PASS {checks} real-runtime mixed stress assertions: twelve recipes, bounded sustained simulation, seed repeatability, clone-local references, event graph, geometry/layout configuration. No GPU/pixel validation.");
    }
    private sealed class Owned(Mesh value) : IDisposable
    {
        internal Mesh Value => value;
        public void Dispose() => Object.DestroyImmediate(value);
    }
}
