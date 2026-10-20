using System.Diagnostics;
using System.Reflection;
using RimUnity;
using RimUnity.Diagnostics;
using RimUnity.Assets;

class Program
{
    static int assertions;
    static void Check(bool b, string why) { if (!b) throw new Exception(why); assertions++; }
    static void Update(PerformanceLab lab) => typeof(PerformanceLab).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(lab, null);
    static PerformanceLab Lab(float warmup = 0) => new() { WarmupSeconds = warmup, Rate = 1e9f, Lifetime = -1 };
    static void Fill(PerformanceLab lab) { for (int i = 0; i < 30 && lab.LiveCount < lab.Concurrency; i++) Update(lab); }
    static void Main()
    {
        var lab = Lab(); lab.StartPlayback();
        Check(lab.Sampler.Starts == 0, "fixed sampling excludes construction");
        Update(lab); Check(lab.Spawned <= 16, "fixed setup keeps per-frame spawn cap"); Fill(lab);
        Check(lab.LiveCount == 20 && lab.Sampler.Starts == 1 && lab.Sampler.StartCount == 20, "sampling starts once after target is full");
        var clones = RimUnity.Object.Clones.Where(r => r.activeInHierarchy).ToArray();
        var positions = clones.Select(r => r.transform.position).ToArray();
        var seeds = clones.SelectMany(r => r.Systems.Select(s => s.randomSeed)).ToArray();
        Check(seeds.Distinct().Count() == seeds.Length && seeds.All(s => s != 0), "independent stable nonzero particle seeds");
        Check(clones.All(r => r.Systems[0].main.loop && !r.Systems[1].main.loop), "only ordinary roots loop; event subemitters preserved");
        Check(clones.All(r => r.Systems.All(s => !s.useAutoRandomSeed && s.main.useUnscaledTime)), "clones use explicit particle seeds and real time");
        Check(!PrefabLoader.Template.Systems[0].main.loop && PrefabLoader.Template.Systems[0].useAutoRandomSeed, "source template parameters unchanged");
        for (int i = 0; i < 1000; i++) Update(lab);
        Check(lab.Spawned == 20 && lab.LiveCount == 20 && lab.Sampler.Starts == 1, "steady frames neither recreate nor expire instances nor restart sampler");
        lab.StartPlayback(); Fill(lab);
        var second = RimUnity.Object.Clones.Where(r => r.activeInHierarchy).ToArray();
        Check(second.Select(r => r.transform.position).SequenceEqual(positions), "restart reconstructs exact initial positions");
        Check(second.SelectMany(r => r.Systems.Select(s => s.randomSeed)).SequenceEqual(seeds), "restart reconstructs particle seeds");
        Check(clones.All(r => !r.activeInHierarchy), "restart retires previous session instances");
        second[0].SetActive(false); Update(lab);
        Check(!lab.Playing && lab.LiveCount == 0 && lab.Status.Contains("自行禁用"), "self-disabled root stops invalid fixed workload and clears it");
        int spawned = lab.Spawned; Update(lab); Check(lab.Spawned == spawned, "invalid fixed workload does not retry or silently refill");
        var warming = Lab(10); warming.StartPlayback(); Fill(warming);
        Check(warming.LiveCount == 20 && warming.Sampler.Starts == 0 && !warming.StableReady, "warmup begins after fixed population, excludes samples");
        typeof(PerformanceLab).GetField("warmupUntil", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(warming, Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency - 1);
        Update(warming); Check(warming.Sampler.Starts == 1 && warming.StableReady, "warmup completion starts sampling");
        warming.Sampler.Stop("test", false); Update(warming); Check(warming.Sampler.Starts == 1, "manual sampling stop does not auto-restart");
        warming.IsCurrent = false; Update(warming); Check(warming.LiveCount == 0 && !warming.Playing, "leaving scene cleans owned instances");
        var failing = Lab(); PrefabLoader.Fail = true; failing.StartPlayback(); Update(failing); PrefabLoader.Fail = false;
        Check(!failing.Playing && failing.LiveCount == 0 && failing.Sampler.Starts == 0, "construction exception stops setup without starting samples");
        var legacy = Lab(); legacy.FixedInstances = false; legacy.StartPlayback(); Update(legacy); int first = legacy.Spawned; Update(legacy);
        Check(legacy.Sampler.Starts == 1 && legacy.Spawned > first, "legacy create/destroy workload still replaces expired instances"); legacy.StopPlayback();
        var selection = Lab(); selection.Effects.Clear(); selection.StartPlayback(); Check(!selection.Playing, "empty selection rejected");
        selection.Effects.Add(new()); selection.Effects.Add(new()); selection.Concurrency = 1; selection.StartPlayback(); Check(!selection.Playing, "all selected effects must fit fixed scene");
        Console.WriteLine($"PASS: {assertions} fixed-scene lifecycle assertions (production playback, fake prefab/lifecycle APIs; no game/GPU).");
    }
}

namespace RimUnity.Diagnostics
{
    internal sealed partial class PerformanceLab
    {
        internal bool IsCurrent = true;
        internal readonly List<LabEffect> Effects = new() { new() };
        internal LabSampler Sampler;
        internal string Status;
        internal readonly FakeMap Map = new();
        internal PerformanceLab() { Sampler = new(this); }
        internal void Close() { Sampler.Stop("close", false); StopPlayback(); }
    }
    internal class FakeMap { internal Vector3 Center = new(48, 0, 48); }
    internal class LabEffect { internal bool Selected = true; internal object Mod; internal string Path = "fixture", Key = "fixture", Label = "fixture"; }
    internal class LabSampler(PerformanceLab lab)
    {
        internal bool Sampling; internal int Starts, StartCount;
        internal void Stop(string why, bool export) { Sampling = false; }
        internal void Start(int seconds) { Sampling = true; Starts++; StartCount = lab.LiveCount; }
        internal void RecordSpawn(string key, double ms, long bytes) { }
    }
    internal class SpikeCaptureData { }
    internal static class ParticleSpikeCapture { internal static SpikeCaptureData BeginLabCapture() => null; }
}
namespace Verse { internal static class Log { internal static void Error(string value) { } } }
namespace RimUnity.Runtime
{
    internal static class NativeRuntime
    {
        private sealed class Scope : IDisposable { public void Dispose() { } }
        internal static IDisposable DeferLifecycle() => new Scope();
    }
}
namespace RimUnity.Assets
{
    internal static class PrefabLoader
    {
        internal static bool Fail;
        internal static readonly GameObject Template = new();
        internal static GameObject Load(object mod, string path) => Fail ? throw new Exception("injected failure") : Template;
    }
}
namespace RimUnity
{
    internal static class Object
    {
        internal static readonly List<GameObject> Clones = new();
        internal static GameObject Instantiate(GameObject source) { var clone = new GameObject(); Clones.Add(clone); return clone; }
        internal static void DestroyImmediate(GameObject root) => root.SetActive(false);
    }
    internal class GameObject
    {
        internal bool activeInHierarchy = true;
        internal Transform transform = new();
        internal ParticleSystem[] Systems = { new(), new() };
        internal GameObject() { Systems[0].subEmitters.enabled = true; Systems[0].subEmitters.entries = new[] { new SubEmitter { system = Systems[1] } }; }
        internal void SetActive(bool value) => activeInHierarchy = value;
        internal T[] GetComponentsInChildren<T>(bool inactive) => (T[])(object)Systems;
    }
    internal class ParticleSystem
    {
        internal bool isActiveAndEnabled = true, useAutoRandomSeed = true;
        internal uint randomSeed = 1; internal int particleCount;
        internal MainModule main = new(); internal SubModule subEmitters = new();
    }
    internal class MainModule { internal bool loop, useUnscaledTime, playOnAwake; }
    internal class SubModule { internal bool enabled; internal SubEmitter[] entries = Array.Empty<SubEmitter>(); }
    internal class SubEmitter { internal ParticleSystem system; }
    internal class Transform { internal Vector3 position, localScale = new(1, 1, 1); internal Quaternion rotation; }
    internal readonly record struct Vector3(float x, float y, float z) { public static Vector3 operator *(Vector3 v, float k) => new(v.x*k, v.y*k, v.z*k); }
    internal struct Quaternion { internal static Quaternion Euler(float x, float y, float z) => default; public static Quaternion operator *(Quaternion a, Quaternion b) => default; }
}
