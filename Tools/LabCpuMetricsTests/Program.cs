using System.Diagnostics;
using System.Text.Json;
using RimUnity;
using RimUnity.Diagnostics;
using RimUnity.Rendering;

internal static class Program
{
    private static int checks;
    private static object keep;
    private static void Check(bool valid, string message)
    { if (!valid) throw new Exception(message); checks++; }
    private static void Main()
    {
        Nesting(); Lifecycle(); Windows(); Batch(); Pages(); ObserverCost(); Reports();
        Console.WriteLine($"PASS {checks} CPU capture/batch checks (production collector and batch code; fake mesh/host, no GPU).");
    }
    private static void Nesting()
    {
        var cpu = new LabCpuMetrics(3); cpu.Start();
        using (LabCpuMetrics.Measure(LabCpuStage.ParticleRenderer))
        {
            keep = new byte[1024];
            using (LabCpuMetrics.Measure(LabCpuStage.ParticleSubmit))
            { keep = new byte[2048]; Thread.SpinWait(1000); }
        }
        cpu.CompleteFrame(); cpu.CompleteFrame(); cpu.Stop();
        var parent = cpu.Get(0, LabCpuStage.ParticleRenderer); var child = cpu.Get(0, LabCpuStage.ParticleSubmit);
        Check(parent.Calls == 1 && child.Calls == 1, "nested calls counted once");
        Check(parent.Bytes == parent.SelfBytes + child.Bytes && child.SelfBytes == child.Bytes, "nested allocation attribution conserves bytes");
        Check(parent.SelfBytes >= 1024 && child.Bytes >= 2048, "real thread allocation measured");
        Check(parent.Ticks == parent.SelfTicks + child.Ticks, "nested CPU attribution conserves time");
        Check(cpu.Get(1, LabCpuStage.ParticleSubmit).Calls == 0, "frame counters reset");
        var stage = LabCpuReport.Stages(cpu)[(int)LabCpuStage.ParticleRenderer];
        Check(stage.CallsPerFrame == .5 && stage.P95InclusiveMs == stage.MaxFrameInclusiveMs, "summary includes zero-call frames");
        Check(JsonSerializer.Serialize(LabCpuReport.Stages(cpu)).Contains("SelfBytesPerFrame"), "JSON retains numeric metrics");
        Check(cpu.InvalidScopes == 0, "balanced nesting valid");
    }
    private static void Lifecycle()
    {
        var old = new LabCpuMetrics(2); old.Start();
        try { using var scope = LabCpuMetrics.Measure(LabCpuStage.MeshFill); throw new Exception(); }
        catch { }
        old.CompleteFrame();
        Check(old.Get(0, LabCpuStage.MeshFill).Calls == 1, "exception scope closes");
        Task.Run(() => { using var scope = LabCpuMetrics.Measure(LabCpuStage.MeshFill); LabCpuMetrics.Batch(LabBatchReason.Accepted); }).GetAwaiter().GetResult();
        Check(old.CrossThreadScopes == 1, "worker scope explicitly skipped");
        var late = LabCpuMetrics.Measure(LabCpuStage.MeshFill); old.Stop();
        var next = new LabCpuMetrics(1); next.Start(); late.Dispose();
        using (LabCpuMetrics.Measure(LabCpuStage.DrawMesh)) { }
        next.CompleteFrame(); next.CompleteFrame(); next.Stop(); next.Stop();
        Check(next.FrameCount == 1 && next.Get(0, LabCpuStage.MeshFill).Calls == 0, "late scope cannot contaminate next session; capacity bounded");
        Check(next.Get(0, LabCpuStage.DrawMesh).Calls == 1 && next.InvalidScopes == 0, "new session isolated");
        using (LabCpuMetrics.Measure(LabCpuStage.DrawMesh)) { }
        Check(next.Get(0, LabCpuStage.DrawMesh).Calls == 1, "stop disables collection");
    }
    private static void Windows()
    {
        var cpu = new LabCpuMetrics(4); cpu.Start(); cpu.CompleteFrame();
        long before = GC.GetAllocatedBytesForCurrentThread();
        keep = new byte[128]; cpu.BeginMainFrame();
        keep = new byte[256];
        using (LabCpuMetrics.BridgeWindow()) { keep = new byte[512]; }
        keep = new byte[1024];
        long after = GC.GetAllocatedBytesForCurrentThread();
        cpu.CompleteFrame(); cpu.CompleteFrame();
        cpu.BeginMainFrame();
        using (LabCpuMetrics.BridgeWindow()) { }
        using (LabCpuMetrics.BridgeWindow()) { }
        cpu.CompleteFrame(); cpu.Stop();
        Check(!cpu.HasWindows(0) && cpu.HasWindows(1) && !cpu.HasWindows(2), "partial/missing boundaries omitted");
        Check(!cpu.HasWindows(3), "duplicate bridge boundaries omitted");
        Check(cpu.GetWindow(1, LabFrameWindow.Bridge).Bytes >= 512, "bridge allocation captured");
        Check(cpu.GetWindow(1, LabFrameWindow.PrePollToBridge).Bytes >= 256, "pre-bridge allocation captured");
        Check(cpu.GetWindow(1, LabFrameWindow.BridgeToEnqueue).Bytes >= 1024, "post-bridge allocation captured");
        var windows = LabCpuReport.Windows(cpu);
        Check(windows.Length == 4 && windows.All(w => w.CompleteFrames == 1), "only complete intervals summarized");
        Check(windows.Sum(w => w.BytesPerFrame) >= after - before, "timeline allocation partition covers inter-frame work");
    }
    private static readonly List<Vector3> v = new() { default, default, default };
    private static readonly List<Vector3> n = new();
    private static readonly List<Vector4> t = new();
    private static readonly List<Color32> c = new();
    private static readonly List<int> indices = new() { 0, 1, 2 };
    private static bool Append(Material m, int layer = 0, MaterialPropertyBlock block = null, List<Vector3> vertices = null, List<int> ix = null)
        => ParticleDrawBatch.TryAppend(m, layer, block, vertices ?? v, n, t, c, t, t, t, ix ?? indices);
    private static void Batch()
    {
        var cpu = new LabCpuMetrics(2); cpu.Start(); Time.frameCount++;
        var a = new Material(); var b = new Material();
        Check(!Append(a), "inactive draw not batched");
        ParticleDrawBatch.Begin();
        Check(Append(a) && Append(a) && Append(b), "compatible adjacent inputs accepted");
        Check(Graphics.Draws.Count == 0, "material change closes a range without publishing an unfinished page");
        Append(b, 1); b.engineMaterial.Version++; Append(b, 1);
        Check(!Append(a, block: new MaterialPropertyBlock { PropertyCount = 1 }), "property block preserves independent path");
        Check(Graphics.Draws[0].Indices.SequenceEqual(new[] { 0, 1, 2, 3, 4, 5 }), "shared page preserves triangle order and offsets");
        Check(Graphics.Draws.Count == 4 && Graphics.Draws.Select(d => d.Mesh).Distinct().Count() == 1, "incompatible input drains all prior ranges in one shared page");
        Check(!Append(a, block: new MaterialPropertyBlock { HasStorageBindings = true }), "storage binding preserves independent path");
        ParticleDebugWindow.Capturing = true; Check(!Append(a), "debug capture rejected"); ParticleDebugWindow.Capturing = false;
        Check(!Append(new Material { passCount = 2 }), "multipass rejected");
        Check(!Append(new Material { Eligibility = ParticleBatchShaders.Eligibility.UnknownShader }), "unknown shader rejected");
        Check(!Append(new Material { Eligibility = ParticleBatchShaders.Eligibility.UnsafeShader }), "unsafe shader rejected");
        Check(!Append(a, vertices: Enumerable.Repeat(default(Vector3), ParticleDrawBatch.VertexLimit + 1).ToList()), "oversize vertices rejected");
        Check(!Append(a, ix: Enumerable.Repeat(0, ParticleDrawBatch.IndexLimit + 1).ToList()), "oversize indices rejected");
        Append(a, vertices: Enumerable.Repeat(default(Vector3), ParticleDrawBatch.VertexLimit).ToList()); Append(a);
        Append(a, ix: Enumerable.Repeat(0, ParticleDrawBatch.IndexLimit).ToList()); Append(a);
        ParticleDrawBatch.Flush(); ParticleDrawBatch.End();
        cpu.CompleteFrame();
        foreach (var reason in new[] { LabBatchReason.Inactive, LabBatchReason.MaterialChanged, LabBatchReason.LayerChanged,
            LabBatchReason.MaterialVersionChanged, LabBatchReason.PropertyBlock, LabBatchReason.StorageBindings,
            LabBatchReason.DebugCapture, LabBatchReason.MultiplePasses, LabBatchReason.UnknownShader, LabBatchReason.UnsafeShader,
            LabBatchReason.InputVertexLimit, LabBatchReason.InputIndexLimit, LabBatchReason.BatchVertexLimit, LabBatchReason.BatchIndexLimit })
            Check(cpu.Reason(0, reason) > 0, "reason recorded: " + reason);
        ParticleDrawBatch.Begin(); Mesh.FailAppend = true;
        try { Append(a); } catch (InvalidOperationException) { } finally { Mesh.FailAppend = false; ParticleDrawBatch.End(); }
        ParticleDrawBatch.Begin(); Check(Append(a), "append exception can cleanly restart"); ParticleDrawBatch.Flush(); ParticleDrawBatch.End();
        cpu.CompleteFrame(); cpu.Stop();
        Check(cpu.InvalidScopes == 0, "batch exception leaves balanced collector stack");
        Check(LabCpuReport.Reasons(cpu)[(int)LabBatchReason.Accepted].Count > 0, "reason report populated");
        ParticleDrawBatch.Clear();
    }
    private static void Pages()
    {
        Graphics.Draws.Clear(); ParticleDrawBatch.Sampling = true; Time.frameCount++;
        var a = new Material(); var b = new Material();
        ParticleDrawBatch.Begin(); Append(a); Append(b); Append(a); ParticleDrawBatch.Flush(); ParticleDrawBatch.End();
        Check(Graphics.Draws.Select(d => d.Material).SequenceEqual(new[] { a, b, a }), "ABA materials retain order, no global regrouping");
        Check(Graphics.Draws.Select(d => d.Mesh).Distinct().Count() == 1 && ParticleDrawBatch.UploadPages == 1, "multiple material ranges share one upload page");
        Check(Graphics.Draws.SelectMany(d => d.Indices).SequenceEqual(Enumerable.Range(0, 9)), "range index offsets retain all triangles");
        var firstPage = Graphics.Draws[0].Mesh;
        ParticleDrawBatch.Begin(); Append(a); ParticleDrawBatch.Flush(); ParticleDrawBatch.End();
        Check(!ReferenceEquals(firstPage, Graphics.Draws[^1].Mesh), "same-frame cameras get independent mesh pages");
        Check(firstPage.vertexCount == 9 && firstPage.Submeshes.Count == 3, "later camera cannot overwrite earlier page geometry");
        Time.frameCount++; Graphics.Draws.Clear();
        ParticleDrawBatch.Begin();
        for (int i = 0; i < ParticleDrawBatch.SegmentLimit + 1; i++) Append(i % 2 == 0 ? a : b);
        ParticleDrawBatch.Flush(); ParticleDrawBatch.End();
        Check(Graphics.Draws.Count == ParticleDrawBatch.SegmentLimit + 1 && ParticleDrawBatch.UploadPages == 2, "range limit creates bounded pages without losing draws");
        Time.frameCount++; Graphics.Draws.Clear();
        ParticleDrawBatch.Begin(); Append(a); Append(b); ParticleDrawBatch.End();
        Check(Graphics.Draws.Count == 0, "aborted page publishes no unfinished ranges");
        ParticleDrawBatch.Begin(); Append(a); ParticleDrawBatch.Flush(); ParticleDrawBatch.End();
        Check(Graphics.Draws.Count == 1 && Graphics.Draws[0].Submesh == 0, "aborted range references do not leak into the next page");
        ParticleDrawBatch.Clear(); ParticleDrawBatch.Sampling = false;
    }
    private static void ObserverCost()
    {
        const int iterations = 100000;
        var cpu = new LabCpuMetrics(1); cpu.Start();
        for (int i = 0; i < 10000; i++) { using var scope = LabCpuMetrics.Measure(LabCpuStage.DrawMesh); }
        long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++)
        { using var scope = LabCpuMetrics.Measure(LabCpuStage.DrawMesh); LabCpuMetrics.Batch(LabBatchReason.Accepted); }
        long ticks = Stopwatch.GetTimestamp() - start, allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
        cpu.CompleteFrame(); cpu.Stop();
        Check(allocated == 0, "warmed active scopes and counters allocate zero bytes");
        bytes = GC.GetAllocatedBytesForCurrentThread(); start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++) { using var scope = LabCpuMetrics.Measure(LabCpuStage.DrawMesh); }
        long idleTicks = Stopwatch.GetTimestamp() - start;
        Check(GC.GetAllocatedBytesForCurrentThread() == bytes, "inactive scopes allocate zero bytes");
        Console.WriteLine($"Observer microbenchmark: active scope + counter {ticks * 1e9 / Stopwatch.Frequency / iterations:F1} ns/call; inactive {idleTicks * 1e9 / Stopwatch.Frequency / iterations:F1} ns/call; active allocations {allocated} B. Synthetic, not game frame overhead.");
    }
    private static void Reports()
    {
        var lab = new PerformanceLab();
        using var sampler = new LabSampler(lab);
        sampler.Start(60);
        Check(sampler.Sampling && Waylith.EngineApplication.Instance.Subscribers == 1, "sampler subscribes once");
        for (int i = 0; i < 2; i++)
        {
            Waylith.EngineApplication.Instance.Poll();
            using (LabCpuMetrics.BridgeWindow())
                using (LabCpuMetrics.Measure(LabCpuStage.BridgeUpdate)) { keep = new byte[256]; }
            LabCpuMetrics.Batch(LabBatchReason.MaterialChanged);
            Waylith.EngineApplication.Instance.Emit();
        }
        sampler.Stop("fixture", export: true);
        Check(!sampler.Sampling && Waylith.EngineApplication.Instance.Subscribers == 0 && Waylith.EngineApplication.Instance.PrePollSubscribers == 0 && !ParticleDrawBatch.Sampling, "stop unsubscribes all sampler state");
        string prefix = Path.ChangeExtension(sampler.LastReport, null);
        using var json = JsonDocument.Parse(File.ReadAllText(prefix + ".json"));
        var root = json.RootElement;
        Check(root.GetProperty("Schema").GetInt32() == 6 && root.GetProperty("Cpu").GetProperty("Frames").GetInt32() == 2, "schema and frame count exported");
        Check(root.GetProperty("Cpu").GetProperty("Enabled").GetBoolean() && !root.GetProperty("Configuration").GetProperty("RuntimeIsStandalone").GetBoolean(), "source/measurement metadata exported");
        var bridge = root.GetProperty("Cpu").GetProperty("Stages")[0];
        Check(bridge.GetProperty("Calls").GetInt64() == 2 && bridge.GetProperty("SelfBytesPerFrame").GetDouble() >= 256, "real sampler exports phase calls/allocations");
        var rows = File.ReadAllLines(prefix + ".frames.csv");
        Check(rows.Length == 3 && rows.All(row => row.Split(',').Length == 23), "frame CSV headers/rows align including observer/page columns");
        Check(File.ReadAllLines(prefix + ".stages.csv").Length == 3 && File.ReadAllLines(prefix + ".batch.csv").Length == 3, "stage/batch CSV frame rows exported");
        Check(root.GetProperty("Frames")[0].GetProperty("ObserverAllocatedBytes").GetInt64() >= 0, "observer counters serialized");
        Check(root.GetProperty("ProcessMemory").GetProperty("EndCaptured").GetBoolean(), "separate end memory query recorded");
        Check(root.GetProperty("Cpu").GetProperty("FrameWindows")[0].GetProperty("CompleteFrames").GetInt32() == 1, "complete windows exported");
        Check(File.ReadAllLines(prefix + ".windows.csv").Length == 5, "window CSV skips partial first frame");
        lab.MeasureCpuStages = false;
        Thread.Sleep(2); sampler.Start(60); Waylith.EngineApplication.Instance.Emit(); sampler.Stop("baseline", export: true);
        prefix = Path.ChangeExtension(sampler.LastReport, null);
        using var baseline = JsonDocument.Parse(File.ReadAllText(prefix + ".json"));
        Check(!baseline.RootElement.GetProperty("Cpu").GetProperty("Enabled").GetBoolean(), "baseline records CPU capture disabled");
        Check(!File.Exists(prefix + ".stages.csv") && !File.Exists(prefix + ".batch.csv") && !File.Exists(prefix + ".windows.csv"), "disabled capture exports no fictitious zero stage rows");
        lab.Playing = false; lab.LiveCount = 0; lab.StableReady = false;
        Thread.Sleep(2); sampler.Start(60); Waylith.EngineApplication.Instance.Emit(); sampler.Stop("empty", export: true);
        using var empty = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(sampler.LastReport, ".json")));
        Check(empty.RootElement.GetProperty("Configuration").GetProperty("EmptyBaseline").GetBoolean(), "empty baseline can sample without playback");
        Check(empty.RootElement.GetProperty("Frames")[0].GetProperty("Particles").GetInt32() == 0, "empty baseline reports zero test particles");
        // Force the old periodic refresh branch repeatedly without waiting a minute.
        lab.MeasureCpuStages = true; sampler.Start(60);
        var memoryClock = typeof(LabSampler).GetField("memoryTime", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        for (int i = 0; i < 60; i++) { memoryClock.SetValue(sampler, 0L); Waylith.EngineApplication.Instance.Emit(); }
        sampler.Stop("refresh-cost", export: true);
        using var refresh = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(sampler.LastReport, ".json")));
        var observer = refresh.RootElement.GetProperty("Frames").EnumerateArray().Select(f => f.GetProperty("ObserverCpuMs").GetDouble()).ToArray();
        Console.WriteLine($"Forced periodic GC/UI refresh (60 callbacks): Observer mean={observer.Average():F6} ms, max={observer.Max():F6} ms; process query moved outside collection.");
        lab.MeasureCpuStages = true; sampler.Start(60); lab.IsCurrent = false; Waylith.EngineApplication.Instance.Emit();
        Check(!sampler.Sampling && Waylith.EngineApplication.Instance.Subscribers == 0, "leaving scene stops collection");
        Console.WriteLine("Sampler schema/CSV/lifecycle report fixtures: " + Verse.GenFilePaths.ConfigFolderPath);
    }
}
