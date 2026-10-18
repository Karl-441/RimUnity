using System.Collections;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using RimUnity.Rendering;
using Waylith.Rendering;

internal static class Program
{
    private static int assertions;
    private static int shaderId;
    private delegate FinalDrawItem RentDraw(Matrix4x4 matrix, byte passId, MeshDrawGeometry mesh, MaterialBinding binding,
        bool hasOverride, in MaterialUniformBytes uniforms, in MaterialTextureResourceSet<Texture> resources,
        int order, int queue, InstancedDrawInfo instancing, int layer, ushort hostOrder, InstanceStorageBinding[] storage);
    private static RentDraw rent;
    private static int Main(string[] args)
    {
        string root = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("RIMWORLD_DIR");
        if (string.IsNullOrEmpty(root)) throw new ArgumentException("Pass the game root or set RIMWORLD_DIR.");
        AssemblyLoadContext.Default.Resolving += (_, name) => {
            string path = Path.Combine(root, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path)) : null;
        };
        return Run();
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run()
    {
        rent = AccessTools.Method(typeof(FinalDrawItem), "Rent").CreateDelegate<RentDraw>();
        Equivalence();
        Lifetimes();
        BindingReuse();
        Failures();
        Limits();
        CrossThreadRelease();
        Benchmark();
        HdrDrawFrame.ClearWorkspaces();
        Check(PoolCount() == 0, "graphics shutdown drops idle workspaces");
        Console.WriteLine($"PASS: {assertions} HDR snapshot assertions (real host draw/frame/material types; no GPU).");
        return 0;
    }
    private static Shader MakeShader(bool refract = false)
    {
        var shader = (Shader)RuntimeHelpers.GetUninitializedObject(typeof(Shader));
        //The real variant registry compares numeric shader identities, not reference identity.
        AccessTools.Field(typeof(Shader), "<Id>k__BackingField").SetValue(shader, Interlocked.Increment(ref shaderId));
        AccessTools.Field(typeof(Shader), "sceneGrabResolved").SetValue(shader, true);
        if (refract) AccessTools.Field(typeof(Shader), "sceneGrabPoint").SetValue(shader,
            RuntimeHelpers.GetUninitializedObject(typeof(SceneGrabPoint)));
        return shader;
    }
    private static MaterialBinding MakeBinding(int id, Shader shader)
    {
        var layout = MaterialLayout.Empty();
        MaterialUniformBytes uniforms = default;
        uniforms.Write(0, MaterialUniformValue.FromFloat(id * .25f));
        MaterialTextureResourceSet<Texture> resources = default;
        resources.Set(0, new FakeTexture());
        var binding = new MaterialBinding(id, id, 17, "snapshot-test-" + id, shader, layout, uniforms, resources, 2000 + id % 3,
            new ShaderVariantKey(shader, 0, "", true), PipelineState.Opaque, null, false);
        binding.UpdateStorageArrays(new[] { new MaterialStorageArray(20, id, new byte[] { 1, 2, 3, 4 }) });
        return binding;
    }
    private static FinalDrawItem MakeDraw(MaterialBinding binding, int order, byte pass, int layer)
    {
        var geometry = (MeshDrawGeometry)RuntimeHelpers.GetUninitializedObject(typeof(MeshDrawGeometry));
        var matrix = Matrix4x4.CreateTranslation(order, layer, 3);
        return rent(matrix, pass, geometry, binding, true, in binding.Uniforms, in binding.Resources,
            order, binding.RenderQueue, null, layer, 0, null);
    }
    private static MeshDrawFrame Source(int count = 360, int materials = 45)
    {
        var gamma = MakeShader(); var linear = MakeShader(); var variant = MakeShader(); var refraction = MakeShader(true);
        LinearShaderVariants.Variants[linear] = variant;
        var bindings = Enumerable.Range(0, materials).Select(i => MakeBinding(i + 1, i % 3 == 0 ? linear : i % 3 == 1 ? gamma : refraction)).ToArray();
        var frame = new MeshDrawFrame(17, default, MeshDrawFrame.RentList(), MeshDrawFrame.RentList(), default);
        for (int i = 0; i < count; i++)
        {
            var draw = MakeDraw(bindings[i % bindings.Length], (i * 71) % Math.Max(1, count), (byte)(i % 7 == 0 ? 2 : i % 2), i % 2 == 0 ? 5 : 29);
            if (i % 3 == 0) frame.PersistentDraws.Add(draw); else frame.Draws.Add(draw);
        }
        frame.PersistentDraws.Sort((a, b) => a.SortKey.CompareTo(b.SortKey));
        //Equal keys: the persistent item must precede its matching dynamic item.
        if (count > 0)
        {
            var binding = bindings[0];
            frame.PersistentDraws.Add(MakeDraw(binding, 0, 0, 29));
            frame.Draws.Add(MakeDraw(binding, 0, 0, 29));
            frame.PersistentDraws.Sort((a, b) => a.SortKey.CompareTo(b.SortKey));
        }
        foreach (var property in typeof(MeshDrawFrame).GetProperties().Where(p => p.CanWrite && p.PropertyType == typeof(float))) property.SetValue(frame, 13.25f);
        return frame;
    }
    private static void Equivalence()
    {
        using var source = Source();
        foreach (int mask in new[] { -1, 0, 1 << 5, 1 << 29 })
        foreach (bool depth in new[] { false, true })
        {
            using var expected = new BaselineHdrDrawFrame(source, mask, depth);
            using var actual = new HdrDrawFrame(source, mask, depth);
            Check(actual.Frame.Draws.Count == expected.Frame.Draws.Count, "draw count matches baseline");
            Check(actual.Frame.PersistentDraws.Count == 0, "snapshot owns a single merged draw list");
            Check(actual.Frame.FrameNumber == expected.Frame.FrameNumber && actual.Frame.View.Equals(expected.Frame.View), "frame identity/view preserved");
            for (int i = 0; i < actual.Frame.Draws.Count; i++)
            {
                var a = actual.Frame.Draws[i]; var e = expected.Frame.Draws[i];
                Check(a.SortKey.Equals(e.SortKey) && a.Matrix.Equals(e.Matrix) && ReferenceEquals(a.Mesh, e.Mesh), "draw order/geometry/matrix preserved");
                Check(a.PassId == e.PassId && a.Layer == e.Layer && a.RenderQueue == e.RenderQueue && a.HasOverride == e.HasOverride, "pass/layer/queue/overrides preserved");
                Check(a.Binding.Shader == e.Binding.Shader && a.Binding.PipelineState.Equals(e.Binding.PipelineState)
                    && a.Binding.ShaderVariant.Equals(e.Binding.ShaderVariant) && a.Binding.ShaderVariantId == e.Binding.ShaderVariantId
                    && a.Binding.PipelineSortId == e.Binding.PipelineSortId && a.Binding.WrapModeOverride == e.Binding.WrapModeOverride
                    && a.Binding.UsesGlobals == e.Binding.UsesGlobals, "shader identity and depth/stencil state preserved");
                Check(a.Binding.Id == e.Binding.Id && a.Binding.MaterialId == e.Binding.MaterialId
                    && a.Binding.MaterialVersion == e.Binding.MaterialVersion && a.Binding.MaterialLayout == e.Binding.MaterialLayout
                    && a.Binding.Uniforms.Equals(e.Binding.Uniforms) && a.Binding.Resources.Equals(e.Binding.Resources)
                    && a.Resources.Equals(e.Resources) && a.Uniforms.Equals(e.Uniforms), "material identity and captured parameters preserved");
                Check(a.Binding.StorageArrays.SequenceEqual(e.Binding.StorageArrays), "material storage arrays preserved");
                Check(actual.IsLinear(i) == expected.Linear.Contains(e), "sequential linear flag matches baseline hash set");
            }
            foreach (var property in typeof(MeshDrawFrame).GetProperties().Where(p => p.CanWrite && p.PropertyType == typeof(float)))
                Check(Equals(property.GetValue(actual.Frame), property.GetValue(expected.Frame)), "host value metadata preserved");
            //Check queue slicing independently of the index: skipping entries must not shift linear flags.
            var expectedSlice = expected.Frame.Draws.Where(d => d.RenderQueue == 2001).Select(d => expected.Linear.Contains(d)).ToArray();
            var actualSlice = Enumerable.Range(0, actual.Frame.Draws.Count).Where(i => actual.Frame.Draws[i].RenderQueue == 2001).Select(actual.IsLinear).ToArray();
            Check(expectedSlice.SequenceEqual(actualSlice), "queue-filtered flags remain aligned");
        }
        using var emptySource = Source(0);
        using var empty = new HdrDrawFrame(emptySource);
        Check(empty.Frame.Draws.Count == 0 && !HdrDrawFrame.Needed(emptySource), "empty scenes work without linear draws");
    }
    private static void Lifetimes()
    {
        HdrDrawFrame.ClearWorkspaces();
        using var source = Source();
        using var map = new HdrDrawFrame(source);
        using var bloom = new HdrDrawFrame(source, 1 << 29, true);
        using var preview = new HdrDrawFrame(source);
        Check(!ReferenceEquals(Work(map), Work(bloom)) && !ReferenceEquals(Work(map), Work(preview)), "map/Bloom/preview in flight own independent workspaces");
        object released = Work(bloom);
        bool[] mapFlags = Enumerable.Range(0, map.Frame.Draws.Count).Select(map.IsLinear).ToArray();
        bloom.Dispose(); Check(Cleared(released), "returned workspace drops all draw and binding references");
        using var next = new HdrDrawFrame(source);
        Check(ReferenceEquals(released, Work(next)), "idle workspace is reused");
        bloom.Dispose();
        Check(next.Frame.Draws.Count > 0 && mapFlags.SequenceEqual(Enumerable.Range(0, map.Frame.Draws.Count).Select(map.IsLinear)), "stale duplicate dispose cannot return a new owner's buffers");
        for (int i = 0; i < next.Frame.Draws.Count; i++) _ = next.IsLinear(i);
    }
    private static void BindingReuse()
    {
        HdrDrawFrame.ClearWorkspaces();
        using var source = Source(0);
        var original = MakeBinding(101, MakeShader());
        source.Draws.Add(MakeDraw(original, 0, 0, 29));
        var old = new HdrDrawFrame(source);
        var captured = old.Frame.Draws[0].Binding;
        var oldShader = captured.Shader; var oldBlock = captured.StorageArrays[0];
        var state = original.PipelineState with { Depth = original.PipelineState.Depth with { Write = false } };
        original.Update(18, "changed", original.Shader, original.MaterialLayout, original.Uniforms, original.Resources,
            3020, original.ShaderVariant, state, original.WrapModeOverride, original.UsesGlobals);
        original.UpdateStorageArrays(new[] { new MaterialStorageArray(20, 55, new byte[] { 9, 8 }) });
        using (var changed = new HdrDrawFrame(source))
        {
            var binding = changed.Frame.Draws[0].Binding;
            Check(binding.MaterialVersion == 18 && binding.RenderQueue == 3020 && !binding.PipelineState.Depth.Write
                && binding.StorageArrays[0].Bytes[0] == 9, "current material values are fully captured");
            Check(captured.MaterialVersion == 17 && captured.RenderQueue != 3020 && captured.PipelineState.Depth.Write
                && captured.StorageArrays[0].Equals(oldBlock) && captured.Shader == oldShader,
                "material mutation and a new snapshot cannot change the in-flight old snapshot");
            Check(binding.ResolvedDataVersion != captured.ResolvedDataVersion && binding.ResolvedDataVersion < 0,
                "concurrent derived bindings have unique host upload generations");
        }
        object work = Work(old); old.Dispose();
        Check(Cleared(work), "retired binding buffers retain no shaders/layouts/textures/storage arrays");
        using (var reused = new HdrDrawFrame(source))
        {
            Check(ReferenceEquals(Work(reused), work) && ReferenceEquals(reused.Frame.Draws[0].Binding, captured),
                "compatible identity reuses cleared binding buffer");
            Check(captured.MaterialVersion == 18 && captured.StorageArrays[0].Bytes[0] == 9, "reused binding is populated from current values");
        }
        //A different source variant with the same material IDs must receive a fresh immutable variant identity.
        var replacement = MakeBinding(101, MakeShader());
        var previousDraw = source.Draws[0]; source.Draws.Clear(); FinalDrawItem.Return(previousDraw);
        source.Draws.Add(MakeDraw(replacement, 0, 0, 29));
        using var newVariant = new HdrDrawFrame(source);
        Check(!ReferenceEquals(newVariant.Frame.Draws[0].Binding, captured)
            && newVariant.Frame.Draws[0].Binding.ShaderVariantId == replacement.ShaderVariantId,
            "shader variant changes replace the immutable binding identity");
    }
    private static void Failures()
    {
        HdrDrawFrame.ClearWorkspaces();
        using var source = Source();
        var warm = new HdrDrawFrame(source); object workspace = Work(warm); warm.Dispose();
        LinearShaderVariants.FailOnCall = 3;
        Throws(() => new HdrDrawFrame(source), "partial clone failure propagates");
        Check(PoolCount() == 1 && Cleared(workspace), "partial clone failure returns clean workspace");
        //Failure during filtering occurs before the old constructor's try block.
        source.Draws.Add(null);
        Throws(() => new HdrDrawFrame(source), "filter failure propagates");
        source.Draws.RemoveAt(source.Draws.Count - 1);
        Check(PoolCount() == 1 && Cleared(workspace), "early filter failure returns clean workspace");
        Throws(() => new HdrDrawFrame(null), "null source rejected before lease acquisition");
        using var good = new HdrDrawFrame(source);
        Check(good.Frame.Draws.Count > 0, "pool remains usable after failed constructors");
    }
    private static void Limits()
    {
        HdrDrawFrame.ClearWorkspaces();
        using var source = Source(12, 3);
        var snapshots = Enumerable.Range(0, 12).Select(_ => new HdrDrawFrame(source)).ToArray();
        foreach (var snapshot in snapshots) snapshot.Dispose();
        Check(PoolCount() == 8, "idle pool retains at most eight workspaces");
        HdrDrawFrame.ClearWorkspaces();
        using (var largeSource = Source(14000, 3))
        {
            var large = new HdrDrawFrame(largeSource); object work = Work(large); large.Dispose();
            Check(Cleared(work) && PoolCount() == 0, "oversized draw buffers are cleared and discarded");
        }
        using (var diverseSource = Source(7000, 2300))
        {
            var diverse = new HdrDrawFrame(diverseSource); object work = Work(diverse); diverse.Dispose();
            Check(Cleared(work) && PoolCount() == 0, "oversized binding dictionary is cleared and discarded");
        }
    }
    private static void CrossThreadRelease()
    {
        //No game state is touched. Reproduce the render-thread retirement of immutable draw snapshots.
        HdrDrawFrame.ClearWorkspaces();
        using var source = Source(30, 3);
        var snapshots = Enumerable.Range(0, 12).Select(_ => new HdrDrawFrame(source)).ToArray();
        var retire = Task.Run(() => { foreach (var snapshot in snapshots) { snapshot.Dispose(); snapshot.Dispose(); } });
        for (int i = 0; i < 24; i++) { using var active = new HdrDrawFrame(source); }
        retire.GetAwaiter().GetResult();
        Check(PoolCount() == 8, "render-thread return obeys pool bound and duplicate-release guard");
        using var next = new HdrDrawFrame(source);
        Check(next.Frame.Draws.Count > 0, "main thread reuses render-retired workspace");
        HdrDrawFrame.ClearWorkspaces();
        next.Dispose();
        Check(PoolCount() == 0, "late returns cannot repopulate a cleared pool epoch");
        using var raced = new HdrDrawFrame(source);
        using var gate = new Barrier(3);
        var a = Task.Run(() => { gate.SignalAndWait(); raced.Dispose(); });
        var b = Task.Run(() => { gate.SignalAndWait(); raced.Dispose(); });
        gate.SignalAndWait(); Task.WaitAll(a, b);
        Check(PoolCount() == 1, "simultaneous duplicate release returns exactly one workspace");
    }
    private static void Benchmark()
    {
        using var source = Source(360, 45);
        Action baseline = () => { using var map = new BaselineHdrDrawFrame(source); using var bloom = new BaselineHdrDrawFrame(source, 1 << 29, true); };
        Action optimized = () => { using var map = new HdrDrawFrame(source); using var bloom = new HdrDrawFrame(source, 1 << 29, true); };
        for (int i = 0; i < 2000; i++) { baseline(); optimized(); }
        var old = Measure(baseline); var next = Measure(optimized);
        Console.WriteLine($"Two snapshots / synthetic frame: baseline={old.bytes:F0} B, {old.us:F3} us; optimized={next.bytes:F0} B, {next.us:F3} us; allocation reduction={1 - next.bytes / old.bytes:P2}.");
        Check(next.bytes <= old.bytes * .1, "warm snapshot allocation reduced by at least 90% in matched synthetic scene");
    }
    private static (double bytes, double us) Measure(Action action)
    {
        const int frames = 5000;
        var watch = new Stopwatch();
        long before = GC.GetAllocatedBytesForCurrentThread(); watch.Start();
        for (int i = 0; i < frames; i++) action();
        watch.Stop();
        return ((GC.GetAllocatedBytesForCurrentThread() - before) / (double)frames, watch.Elapsed.TotalMicroseconds / frames);
    }
    private static object Work(HdrDrawFrame frame) => AccessTools.Field(typeof(HdrDrawFrame), "workspace").GetValue(frame);
    private static int PoolCount() => ((ICollection)AccessTools.Field(typeof(HdrDrawFrame), "Workspaces").GetValue(null)).Count;
    private static bool Cleared(object work)
    {
        if (!new[] { "Dynamic", "Bindings", "Linear" }.All(name =>
            (int)AccessTools.Field(work.GetType(), name).GetValue(work).GetType().GetProperty("Count").GetValue(AccessTools.Field(work.GetType(), name).GetValue(work)) == 0)) return false;
        return ((IEnumerable<MaterialBinding>)AccessTools.Field(work.GetType(), "BindingSlots").GetValue(work)).All(binding =>
            binding.Shader == null && binding.MaterialLayout == null && binding.DebugName == null && binding.ShaderVariant.Shader == null
            && binding.StorageArrays.Length == 0 && binding.Resources.Equals(default(MaterialTextureResourceSet<Texture>))
            && binding.Uniforms.Equals(default(MaterialUniformBytes)));
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); assertions++; }
    private static void Throws(Action action, string message)
    {
        bool failed = false;
        try { action(); } catch { failed = true; }
        Check(failed, message);
    }
    private sealed class FakeTexture : Texture { public override int width { get; set; } = 1; public override int height { get; set; } = 1; }
}
