using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Analyzer.Profiling;
using HarmonyLib;

internal static class SubmitStageChecks
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Value(int n) => n < 0 ? throw new InvalidOperationException("stage expected") : n + 3;
    internal static void Run()
    {
        RimUnityDiagnosticBridge.Connect();
        var create = typeof(RimUnitySubmitStages).GetMethod("Create", BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo Wrap(MethodBase original, string key, int id) => (MethodInfo)create.Invoke(null, new object[] { original, key, id });
        var value = Wrap(AccessTools.Method(typeof(SubmitStageChecks), "Value"), "test.stage.value", 1000);
        var add = Wrap(AccessTools.Method(typeof(PublicReceiver), "Add"), "test.stage.privateinstance", 1003);
        var pubCtor = Wrap(AccessTools.Constructor(typeof(PublicReceiver), new[] { typeof(int) }), "test.stage.publicctor", 1004);
        var targets = new[] { value, add, pubCtor }.Select((m, i) => new RimUnityTiming.Target(m, 1, false, true, new[] { "test.stage.value", "test.stage.privateinstance", "test.stage.publicctor" }[i])).ToArray();
        RimUnityTiming.Publish(targets);
        var invoke = (Func<int, int>)value.CreateDelegate(typeof(Func<int, int>));
        var factory = (Func<int, PublicReceiver>)pubCtor.CreateDelegate(typeof(Func<int, PublicReceiver>));
        var instanceCall = (Func<PublicReceiver, int, int>)add.CreateDelegate(typeof(Func<PublicReceiver, int, int>));
        if (invoke(4) != 7 || instanceCall(factory(10), 8) != 18) throw new Exception("Typed wrapper changed arguments/return values");
        try { invoke(-1); throw new Exception("Suppressed exception"); } catch (InvalidOperationException) { }
        for (int i = 0; i < 2000; i++) invoke(i);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++) invoke(i);
        if (GC.GetAllocatedBytesForCurrentThread() != before) throw new Exception("Typed submit wrapper allocates after warmup");
        var report = new StringBuilder(); RimUnityTiming.AppendReport(report);
        if (!report.ToString().Contains("nesting errors=0") || !report.ToString().Contains("test.stage.value\t4002\t1")) throw new Exception("Wrapper exception stack/totals invalid");
        Analyzer.Profiling.Analyzer.CurrentlyPaused = true; RimUnityDiagnosticBridge.Sync();
        report.Clear(); RimUnity.Diagnostics.DpaDiagnostics.AppendReport(report);
        if (!report.ToString().Contains("enabled=False")) throw new Exception("Pause leaves diagnostic counters enabled");
        Analyzer.Profiling.Analyzer.CurrentlyPaused = false; RimUnityDiagnosticBridge.Sync();
        Analyzer.Profiling.Analyzer.CurrentlyCleaningUp = true;
        RimUnityDiagnosticBridge.Disable();
        RimUnityDiagnosticBridge.Sync();
        report.Clear(); RimUnity.Diagnostics.DpaDiagnostics.AppendReport(report);
        if (!report.ToString().Contains("enabled=False")) throw new Exception("Concurrent cleanup can reactivate diagnostics");
        Analyzer.Profiling.Analyzer.CurrentlyCleaningUp = false;
        Console.WriteLine("PASS: typed submit wrappers preserve private calls, arguments, returns and exceptions; zero warm allocations; bridge pause/disable.");
    }
}
public sealed class PublicReceiver
{
    private readonly int value;
    public PublicReceiver(int value) => this.value = value;
    private int Add(int n) => n + value;
}
