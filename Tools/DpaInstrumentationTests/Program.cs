using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using HarmonyLib;
using Analyzer.Profiling;

namespace Verse { public static class UnityData { public static int Main; public static bool IsInMainThread => Environment.CurrentManagedThreadId == Main; } }
namespace UnityEngine { public static class Time { public static int frameCount; } }
namespace Analyzer.Profiling
{
    public enum Category { RimUnity }
    public class Entry : Attribute { public string name; public Entry(string name, Category category) { this.name = name; } }
    public static class Analyzer { public static bool CurrentlyProfiling = true; public static bool CurrentlyPaused, CurrentlyCleaningUp; public static object LogicLock = new(); public static List<ProfileLog> Logs = new(); public static int GetCurrentLogCount = 25; }
    public static class Modbase { public static Harmony Harmony = new Harmony("dpa.instrumentation.test"); }
    public static class ThreadSafeLogger
    {
        public static void ReportException(Exception e, string msg) => Console.WriteLine(msg + e);
        public static void Warning(string msg) => Console.WriteLine(msg);
        public static void Message(string msg) => Console.WriteLine(msg);
    }
    public class Profiler
    {
        public const int RECORDS_HELD = 2000; public double Total; public int Calls;
        public Profiler(string key, string label, Type type, MethodBase method) {}
        internal void AddSample(double ms) { Total += ms; Calls++; }
    }
    public static class ProfileController { public static double updateAverage = 1.25; public static ConcurrentDictionary<string, Profiler> Profiles = new(); }
}
class Probe
{
    [MethodImpl(MethodImplOptions.NoInlining)] public static void Sort() { var list = new List<int> { 3, 1, 2 }; list.Sort((a, b) => a.CompareTo(b)); if (list[0] != 1 || list[2] != 3) throw new Exception("Sort changed result"); }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void Child() { GC.KeepAlive(new byte[4096]); Thread.SpinWait(50000); }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void Parent() { Thread.SpinWait(50000); Child(); Thread.SpinWait(50000); }
    [MethodImpl(MethodImplOptions.NoInlining)] public static int Recursive(int n) { Thread.SpinWait(1000); return n == 0 ? 0 : Recursive(n - 1) + 1; }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void Fail() { throw new InvalidOperationException("expected"); }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void Scoped() { Thread.SpinWait(1000); }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void WithScoped() { Scoped(); }
}
class Built
{
    [MethodImpl(MethodImplOptions.NoInlining)] public Built() { Thread.SpinWait(1000); }
}
class BoundPatches
{
    static readonly MethodInfo method = typeof(Probe).GetMethod(nameof(Probe.Recursive));
    public static void Begin(out RimUnityTiming.Scope __state) => RimUnityTiming.Begin(method, out __state);
    public static void End(RimUnityTiming.Scope __state, Exception __exception) => RimUnityTiming.End(__state, __exception);
}
class Program
{
    static void Assert(bool result, string message) { if (!result) throw new Exception(message); }
    static string Report() { var b = new StringBuilder(); RimUnityTiming.AppendReport(b); return b.ToString(); }
    static string[] Row(string report, string method) => report.Split('\n').First(l => l.StartsWith(method, StringComparison.Ordinal)).TrimEnd('\r').Split('\t');
    static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);
    static void Main()
    {
        Verse.UnityData.Main = Environment.CurrentManagedThreadId;
        if (Environment.GetEnvironmentVariable("DPA_METADATA_ONLY") == "1") { CheckCatalog(); return; }
        H_RimUnityDetails.Active = true;
        SubmitStageChecks.Run();
        var methods = typeof(Probe).GetMethods(BindingFlags.Public | BindingFlags.Static).Cast<MethodBase>().Append(typeof(Built).GetConstructor(Type.EmptyTypes)).ToArray();
        RimUnityTiming.Publish(methods.Select(m => new RimUnityTiming.Target(m, 1, m.Name == "Scoped")).Append(RimUnitySortStages.Target(typeof(int))).ToArray());
        foreach (var method in methods)
                    {
            var pair = RimUnityProbeFactory.Get(new RimUnityTiming.Target(method, 1, false).Key);
            Assert(ReferenceEquals(pair, RimUnityProbeFactory.Get(new RimUnityTiming.Target(method, 1, false).Key)), "Probe cache is not idempotent");
            Modbase.Harmony.Patch(method, new HarmonyMethod(pair.Prefix), finalizer: new HarmonyMethod(pair.Finalizer));
        }
        Modbase.Harmony.Patch(typeof(Probe).GetMethod("Sort"), transpiler: new HarmonyMethod(typeof(RimUnitySortStages), "Transpiler"));
        Probe.Sort(); Probe.Parent(); Probe.Recursive(4); new Built();
        bool thrown = false; try { Probe.Fail(); } catch (InvalidOperationException) { thrown = true; }
        Assert(thrown, "Finalizer suppressed exception");
        Probe.Scoped(); Probe.WithScoped();
        string r = Report();
        Assert(Row(r, "RimUnity.Stage.Particles.SortOrder")[1] == "1", "Call-site sort stage missing");
        var parent = Row(r, "Probe:Parent(");
        var child = Row(r, "Probe:Child(");
        Assert(D(parent[3]) >= D(child[3]) && D(parent[4]) < D(parent[3]), "Nested inclusive/self timing is wrong");
        Assert(D(child[9]) >= 4096 && D(parent[9]) >= D(child[9]), "Allocation nesting is wrong");
        Assert(Row(r, "Probe:Recursive(")[1] == "5", "Recursive calls lost");
        Assert(Row(r, "Probe:Fail(")[2] == "1", "Exception count missing");
        Assert(Row(r, "Probe:Scoped(")[1] == "1", "Scoped host calls leaked outside root");
        Assert(Row(r, "Built:.ctor(")[1] == "1", "Constructor instrumentation failed");
        Assert(r.Contains("nesting errors=0"), "Scope stack was corrupted");
        for (int i = 1; i <= 12; i++) { UnityEngine.Time.frameCount = i; Probe.Parent(); }
        r = Report();
        Assert(r.Split("SPIKE frame=").Length - 1 == 8, "Worst-frame snapshot bound failed");
        Assert(!r.Contains("SPIKE frame=12;"), "Unfinished current frame exported as completed");
        var worker = new Thread(Probe.Parent); worker.Start(); worker.Join();
        Assert(Report().Contains("off-thread rejected=2"), "Off-thread guard failed");
        RimUnityTiming.RequestReset();
        r = Report(); Assert(!r.Contains("Probe:Parent("), "Reset retained old method totals");
        Probe.Parent();
        Probe.Recursive(0);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++) Probe.Recursive(0);
        long overhead = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert(overhead == 0, "Generated probes allocated " + overhead + " bytes");
        var recursive = typeof(Probe).GetMethod(nameof(Probe.Recursive));
        RimUnityTiming.Begin(recursive, out var warm); RimUnityTiming.End(warm, null);
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++) { RimUnityTiming.Begin(recursive, out var state); RimUnityTiming.End(state, null); }
        long directOverhead = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert(directOverhead == 0, "Timer bookkeeping allocated " + directOverhead + " bytes");
                Modbase.Harmony.Unpatch(recursive, HarmonyPatchType.All, Modbase.Harmony.Id);
        Modbase.Harmony.Patch(recursive, new HarmonyMethod(typeof(BoundPatches), "Begin"), finalizer: new HarmonyMethod(typeof(BoundPatches), "End"));
        Probe.Recursive(0);
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++) Probe.Recursive(0);
        long boundOverhead = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine("Pre-bound Harmony dispatch allocations=" + boundOverhead + " /2000 calls.");
        Console.WriteLine("Patched probe allocations=" + overhead + " /2000 calls; direct timer bookkeeping allocations=" + directOverhead + ". Harmony dispatch overhead must be considered in allocation attribution.");
        CheckRootSummary();
        Console.WriteLine("PASS: real Harmony prefixes/finalizers; constructor; exception propagation; nesting/self time; recursion; allocation accounting; scoped host calls; bounded same-frame spikes; off-thread rejection; reset; 2000 zero-allocation timer bookkeeping probes and measured Harmony dispatch allocations.");
                Analyzer.Profiling.Analyzer.Logs.Add(new ProfileLog { label = "visible-中文", average = 1.25, max = 8, total = 25, calls = 10, entries = 20, percent = .5 });
        Analyzer.Profiling.Analyzer.Logs.Add(new ProfileLog { label = "hidden", average = 9 });
        Analyzer.Profiling.Analyzer.Logs.Add(new ProfileLog { label = "visible-blocked", average = 9 });
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        string exported = ClipboardReport.Build();
        Assert(exported.Contains("visible-中文\t1.250000\t8.000000"), "Export lost Unicode or invariant numeric format");
        Assert(!exported.Contains("hidden\t") && !exported.Contains("visible-blocked\t"), "Export table filters differ from display");
        Assert(exported.Contains("RIMUNITY DETAIL") && exported.Contains("END DPA REPORT"), "Export sections missing");
        typeof(ClipboardReport).GetMethod("Copy", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        Assert(Extensions.Clipboard.Contains("visible-中文"), "Copy action did not deliver report to clipboard adapter");
        Console.WriteLine("PASS: current-table filters; Unicode; invariant numeric format under fr-FR; detail sections; clipboard adapter action.");
        if (Environment.GetEnvironmentVariable("DPA_METADATA_CHECK") == "1") CheckCatalog();
    }
    static void CheckRootSummary()
    {
        var type = typeof(RimUnityTiming);
        const BindingFlags f = BindingFlags.NonPublic | BindingFlags.Static;
        RimUnityTiming.RequestReset(); Report();
        var complete = type.GetMethod("CompleteFrame", f);
        for (int i = 0; i < 8202; i++)
        {
            type.GetField("frame", f).SetValue(null, 1000 + i);
            type.GetField("frameRoot", f).SetValue(null, (i + 1L) * System.Diagnostics.Stopwatch.Frequency / 1000);
            type.GetField("frameRootAlloc", f).SetValue(null, 16L);
            complete.Invoke(null, null);
        }
        string r = Report(); var row = Row(r, "session_root_cpu");
        Assert(row[1] == "8201" && Math.Abs(D(row[2]) - 4102) < .001 && Math.Abs(D(row[3]) - 8202) < .001 && D(row[4]) == 16,
            "Completed root totals/first-frame exclusion incorrect");
        Assert(r.Contains("8192\t1010\t9201\t4106.000000\t7793.000000\t8121.000000"), "Bounded root quantiles or frame range incorrect");
        // An invalid frame never enters the valid summary.
        type.GetField("frameInvalid", f).SetValue(null, true); complete.Invoke(null, null);
        Assert(Row(Report(), "session_root_cpu")[1] == "8201", "Invalid root frame retained");
        RimUnityTiming.RequestReset();
        Assert(Row(Report(), "session_root_cpu")[1] == "0", "Root session reset retained samples");
        RimUnityTiming.Begin(typeof(Probe).GetMethod("Parent"), out var parent);
        Probe.Child();
        RimUnityTiming.End(parent, null);
        long oneFrame = (long)type.GetField("frameRoot", f).GetValue(null);
        Assert(Math.Abs(oneFrame * 1000d / System.Diagnostics.Stopwatch.Frequency - D(Row(Report(), "Probe:Parent(")[3])) < .00001,
            "Nested child counted twice in root total");
        var token = RimUnity.Diagnostics.DpaDiagnostics.BeginSubmit();
        RimUnity.Diagnostics.DpaDiagnostics.Reset();
        RimUnity.Diagnostics.DpaDiagnostics.EndSubmit(token, 42, 4, 6, 3);
        var text = new StringBuilder(); RimUnity.Diagnostics.DpaDiagnostics.AppendReport(text);
        Assert(!text.ToString().Contains("42\t"), "Late submit survived reset");
        var t = RimUnity.Diagnostics.DpaDiagnostics.BeginSubmit();
        Thread.SpinWait(1000);
        RimUnity.Diagnostics.DpaDiagnostics.EndSubmit(t, 99, 4, 6, 3);
        Assert(Report().Contains("99\t"), "Enabled submit not recorded: " + Report());
        RimUnityTiming.RequestReset();
        Assert(!Report().Contains("99\t"), "DPA reset retained runtime submit records");
        Console.WriteLine("PASS root CPU: nested roots once; first/invalid/current exclusions; session mean/max/allocation; 8192-sample rollover and quantiles; runtime reset/late submits.");
    }
    static void CheckCatalog()
    {
        string root = Environment.GetEnvironmentVariable("RIMWORLD_DIR");
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            throw new InvalidOperationException("Set RIMWORLD_DIR to the game directory for DPA metadata checks.");
        root = Path.GetFullPath(root);
        string mod = Path.Combine(Environment.GetEnvironmentVariable("RIMUNITY_DIR") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../")), @"1.7\Assemblies");
        AssemblyLoadContext.Default.Resolving += (context, name) => {
            foreach (var dir in new[] { root, mod }) { string file = Path.Combine(dir, name.Name + ".dll"); if (File.Exists(file)) return context.LoadFromAssemblyPath(file); }
            return null;
        };
        AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(mod, "RimUnity.Core.dll"));
        AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(mod, "RimUnity.dll"));
        RimUnityInstrumentation.Install(-1); // Metadata only: never run/patch game methods outside the game.
                Console.WriteLine(RimUnityInstrumentation.InstallReport);
        var failures = new List<string>(); RimUnitySubmitStages.Install(failures);
        Assert(failures.Count == 0, "Actual submit patch compilation failed: " + string.Join("; ", failures));
        Modbase.Harmony.UnpatchAll(Modbase.Harmony.Id);
        var hdrCtor = AccessTools.TypeByName("RimUnity.Rendering.HdrDrawFrame").GetConstructors(AccessTools.all).First(c => !c.IsStatic);
        var draw = AccessTools.Method(AccessTools.TypeByName("RimUnity.ParticleSystemRenderer"), "Draw");
        foreach (var method in new MethodBase[] { hdrCtor, draw })
        {
            var original = PatchProcessor.GetOriginalInstructions(method);
            var rewritten = RimUnitySortStages.Transpiler(method, original).ToArray();
            Assert(rewritten.Count(i => i.operand is MethodInfo m && m.DeclaringType.IsGenericType && m.DeclaringType.GetGenericTypeDefinition() == typeof(RimUnitySortStage<>)) == 1,
                "Expected one actual RimUnity sort call site: " + method);
        }
        foreach (string name in new[] { "Submit", "TryAppend", "Flush" }) {
            var t = AccessTools.TypeByName(name == "Submit" ? "RimUnity.ParticleSystemRenderer" : "RimUnity.ParticleDrawBatch");
            var m = AccessTools.Method(t, name);
            var rewritten = RimUnitySubmitStages.Transpiler(m, PatchProcessor.GetOriginalInstructions(m)).ToArray();
            Assert(rewritten.Any(c => c.operand is MethodInfo w && w.DeclaringType.Assembly.GetName().Name == "DPA.RimUnity.SubmitStages"), "Submit stage call-site coverage missing: " + name);
        }
        Console.WriteLine("PASS: actual RimUnity HDR constructor and particle renderer IL each contain exactly one targeted sort call site.");
    }
}
