using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using RimUnity;

internal static class Program
{
    static string runtime, game, harmony;
    static long integrations;
    static bool verify;
    static void Main(string[] args)
    {
        runtime = Path.GetFullPath(args[0]); game = Path.GetFullPath(args[1]); harmony = Path.GetFullPath(args[2]);
        verify = args.Skip(3).Contains("--verify");
        AssemblyLoadContext.Default.Resolving += (_, name) => {
            foreach (var dir in new[] { runtime, game }) {
                string path = Path.Combine(dir, name.Name + ".dll");
                if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            }
            return name.Name == "0Harmony" ? AssemblyLoadContext.Default.LoadFromAssemblyPath(harmony) : null;
        };
        Run();
    }
    static void Count() => integrations++;
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Run()
    {
        RimUnity.Runtime.NativeRuntime.InitializeOnCurrentThread();
        var patch = new Harmony("RimUnity.Tests.ParticleFeedback");
        patch.Patch(AccessTools.Method(typeof(ParticleSystem), "Integrate"), prefix: new HarmonyMethod(typeof(Program), nameof(Count)));
        try {
            foreach (int population in new[] { 1, 10 })
            foreach (float dt in new[] { 1f / 60, .05f, .235f, 1f / 3 })
            {
                var roots = new List<GameObject>();
                var systems = new List<ParticleSystem>();
                try {
                    for (int i = 0; i < population; i++) {
                        var root = new GameObject("feedback regression"); roots.Add(root);
                        var s = root.AddComponent<ParticleSystem>(); systems.Add(s);
                        s.useAutoRandomSeed = false; s.randomSeed = 7;
                        s.main.maxParticles = 1000; s.main.startLifetime = 5; s.main.startSpeed = 0;
                        s.shape.enabled = false; s.emission.rateOverTime = 200;
                        s.noise.enabled = true; s.noise.quality = ParticleSystemNoiseQuality.Medium;
                        s.noise.strength = .05f; s.noise.scrollSpeed = 1;
                        s.Simulate(0, false, true, false); s.Emit(1000);
                        var particles = new ParticleSystem.Particle[1000]; s.GetParticles(particles);
                        for (int j = 0; j < 1000; j++) particles[j].remainingLifetime = (j + 1) / 200f;
                        s.SetParticles(particles, 1000);
                    }
                    foreach (var s in systems) s.Simulate(dt, false, false, true);
                    integrations = 0;
                    long allocated = GC.GetAllocatedBytesForCurrentThread();
                    var watch = Stopwatch.StartNew();
                    for (int frame = 0; frame < 12; frame++)
                        foreach (var s in systems) s.Simulate(dt, false, false, true);
                    watch.Stop();
                    if (verify) {
                        long bound = population * 1000L * (long)Math.Ceiling(dt / (double)(1f / 60f)) * 12;
                        if (integrations > bound) throw new Exception($"Capacity turnover reintegrated survivors: {integrations} > {bound}");
                        if (systems.Any(s => s.particleCount < 998 || s.particleCount > 1000))
                            throw new Exception("Steady-state particle population changed unexpectedly");
                    }
                    Console.WriteLine($"systems={population}, dt={dt:F6}: {watch.Elapsed.TotalMilliseconds / 12:F3} ms/frame, {integrations / 12} integrations/frame, {(GC.GetAllocatedBytesForCurrentThread() - allocated) / 12} B/frame; particles={systems.Sum(s => s.particleCount)}");
                } finally { foreach (var root in roots) RimUnity.Object.DestroyImmediate(root); }
            }
        } finally { patch.UnpatchAll(patch.Id); }
        if (verify) Console.WriteLine("PASS: at most one integration per particle per 60Hz substep; bounded steady-state population, 1/10 systems and 16.7/50/235/333ms frames.");
    }
}
