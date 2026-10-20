using System.Collections;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Loader;
using RimUnity;
using Object = RimUnity.Object;

internal static class Program
{
    internal const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static string game, runtime, harmony;
    private static int checks;
    private static class BenchmarkState { internal static Vector3 Sink; }
    private static int Main(string[] args)
    {
        runtime = Path.GetFullPath(args[0]); game = Path.GetFullPath(args[1]); harmony = Path.GetFullPath(args[2]);
        AssemblyLoadContext.Default.Resolving += (_, name) => Resolve(name);
        try { Run(Path.GetFullPath(args[3]), args.Length > 4 && !string.IsNullOrWhiteSpace(args[4]) ? Path.GetFullPath(args[4]) : null); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static Assembly Resolve(AssemblyName name)
    {
        foreach (string folder in new[] { runtime, game })
        {
            string path = Path.Combine(folder, name.Name + ".dll");
            if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        }
        return name.Name == "0Harmony" ? AssemblyLoadContext.Default.LoadFromAssemblyPath(harmony) : null;
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void Run(string baseline, string vertexBaseline)
    {
        RimUnity.Runtime.NativeRuntime.InitializeOnCurrentThread();
        var context = new AssemblyLoadContext("frozen particle baseline", true);
        context.Resolving += (_, name) => name.Name == "RimUnity" ? null : Resolve(name);
        var oldAssembly = context.LoadFromAssemblyPath(baseline);
        Check(oldAssembly.GetType("RimUnity.ParticleSystem").GetNestedType("DrawSpace", BindingFlags.NonPublic) == null,
            "baseline must predate the DrawSpace optimization");
        using var old = new Rig(oldAssembly);
        using var current = new Rig(typeof(ParticleSystem).Assembly);
        int scenario = 0;
        foreach (string simulation in new[] { "Local", "World", "Custom" })
        foreach (string scaling in new[] { "Hierarchy", "Local", "Shape" })
        foreach (string mode in new[] { "Billboard", "Stretch", "HorizontalBillboard", "VerticalBillboard", "Mesh" })
        foreach (string alignment in new[] { "View", "World", "Local", "Facing", "Velocity" })
        for (int camera = 0; camera < 3; camera++)
        {
            old.Configure(simulation, scaling, mode, alignment, camera, scenario);
            current.Configure(simulation, scaling, mode, alignment, camera, scenario);
            var expected = old.Geometry(camera, scenario);
            var actual = current.Geometry(camera, scenario);
            foreach (string field in expected.Keys)
            {
                Check(expected[field].SequenceEqual(actual[field]), $"geometry mismatch {scenario} {simulation}/{scaling}/{mode}/{alignment}/camera{camera}: {field}");
            }
            scenario++;
        }
        Console.WriteLine($"PASS {scenario} old-DLL versus new-DLL geometry scenarios, {scenario * 9} exact channel comparisons (vertices/indices/normals/tangents/colors/UV/custom streams). No GPU/pixel rendering.");
        if (vertexBaseline != null) CheckVertexReuse(vertexBaseline, current);
        Benchmark(current);
        CheckPopulation(current);
    }
    private static void CheckVertexReuse(string baseline, Rig current)
    {
        var context = new AssemblyLoadContext("frozen vertex baseline", true);
        context.Resolving += (_, name) => name.Name == "RimUnity" ? null : Resolve(name);
        using var old = new Rig(context.LoadFromAssemblyPath(baseline));
        Check(old.Renderer.GetType().Assembly.ManifestModule.ModuleVersionId != current.Renderer.GetType().Assembly.ManifestModule.ModuleVersionId, "marginal baseline is the current runtime");
        var layouts = new List<string[]> { Array.Empty<string>(), new[] { "Position", "Color", "Normal", "Tangent" }, new[] { "Position", "Normal", "Color", "UV" },
            new[] { "Color", "UV", "Normal", "Position", "Tangent", "Color" }, new[] { "UV", "UV" },
            new[] { "UV", "UV2", "UV3", "UV4", "VertexID", "ParticleIndex", "AnimBlend", "AnimFrame" },
            new[] { "Custom2XYZ", "UV", "Custom1XYZW", "Speed", "VertexID", "MeshIndex" },
            new[] { "StableRandomXYZW", "VaryingRandomXYZW", "StableRandomXYZW" },
            new[] { "Speed", "Speed", "UV", "Center", "UV2", "SizeXYZ" } };
        var streamType = current.Renderer.GetType().GetProperty("vertexStreams").PropertyType.GetElementType();
        foreach (string name in Enum.GetNames(streamType))
        {
            try { current.Streams(name); layouts.Add(new[] { name }); }
            catch (TargetInvocationException ex) when (ex.InnerException is NotSupportedException) { }
        }
        int cases = 0;
        foreach (string mode in new[] { "Billboard", "Mesh" })
        foreach (var layout in layouts)
        {
            old.Configure("Local", "Hierarchy", mode, "View", 1, cases);
            current.Configure("Local", "Hierarchy", mode, "View", 1, cases);
            old.Streams(layout); current.Streams(layout);
            old.AnimatedStreams(); current.AnimatedStreams();
            var expected = old.Geometry(1, cases); var actual = current.Geometry(1, cases);
            foreach (string name in expected.Keys) Check(expected[name].SequenceEqual(actual[name]), $"stream geometry mismatch {mode}/{string.Join(',', layout)}: {name}");
            // ClearMesh must invalidate prepared values before the trail-style Vertex path.
            expected = old.UnpreparedVertices(); actual = current.UnpreparedVertices();
            foreach (string name in expected.Keys) Check(expected[name].SequenceEqual(actual[name]), $"unprepared stream mismatch {mode}/{string.Join(',', layout)}: {name}");
            cases++;
        }
        foreach (var rig in new[] { old, current })
        {
            rig.Streams("Speed", "UV");
            try { rig.Streams("Custom1XYZW", "Custom1XYZW", "Custom2XYZW", "Speed"); throw new Exception("over-capacity layout accepted"); }
            catch (TargetInvocationException ex) when (ex.InnerException is NotSupportedException) { }
        }
        var left = old.Geometry(1, 0); var right = current.Geometry(1, 0);
        foreach (string name in left.Keys) Check(left[name].SequenceEqual(right[name]), "failed layout setter changed output: " + name);
        Console.WriteLine($"PASS {cases} frozen marginal-baseline DLL stream/layout cases, exact nine-channel geometry and ClearMesh/unprepared fallback, repeated layout changes and rejected-layout preservation.");
        foreach (string mode in new[] { "Billboard", "Mesh" })
        foreach (var layout in new[] { Array.Empty<string>(), new[] { "UV" }, new[] { "Position", "Normal", "Color", "UV" }, new[] { "UV", "Velocity", "Center", "SizeXYZ", "Speed" }, new[] { "UV", "StableRandomXYZW", "VaryingRandomXYZW", "Speed", "VertexID" } })
        {
            foreach (var rig in new[] { old, current }) { rig.Configure("Local", "Hierarchy", mode, "View", 1, 1); rig.Streams(layout); }
            var slow = old.BuildGeometryBatch(); var fast = current.BuildGeometryBatch();
            for (int i = 0; i < 50; i++) { slow(); fast(); }
            for (int round = 0; round < 3; round++) foreach (bool optimized in new[] { round % 2 == 0, round % 2 != 0 })
            {
                var action = optimized ? fast : slow;
                long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                for (int i = 0; i < 100; i++) action();
                long ticks = Stopwatch.GetTimestamp() - start;
                Check(GC.GetAllocatedBytesForCurrentThread() == bytes, "geometry batch allocated");
                Console.WriteLine($"vertex batch {mode}/{(layout.Length == 0 ? "default" : string.Join('+', layout))} optimized={optimized}: {ticks * 1000d / Stopwatch.Frequency / 100:F6} ms; 4000 particles/300 chunks, setup included, 0 B; CPU geometry only, no GPU.");
            }
        }
    }
    private static void CheckPopulation(Rig rig)
    {
        var assembly = typeof(ParticleSystem).Assembly;
        var labType = assembly.GetType("RimUnity.Diagnostics.PerformanceLab", true);
        var lab = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(labType);
        var liveField = labType.GetField("live", Flags);
        var live = (IList)Activator.CreateInstance(liveField.FieldType); liveField.SetValue(lab, live);
        var entryType = liveField.FieldType.GetGenericArguments()[0];
        live.Add(Activator.CreateInstance(entryType, Flags, null, new object[] { rig.Owner, new[] { (ParticleSystem)rig.System }, double.PositiveInfinity }, null));
        var active = (HashSet<ParticleSystem>)typeof(ParticleSystem).GetField("Active", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        active.Add((ParticleSystem)rig.System);
        using var background = new FixtureObject(new GameObject("background diagnostic fixture"));
        var ps = background.Value.AddComponent<ParticleSystem>(); background.Value.AddComponent<ParticleSystemRenderer>(); active.Add(ps);
        Camera.main = (Camera)rig.Camera;
        var population = assembly.GetType("RimUnity.Diagnostics.LabPopulation", true);
        var roots = new HashSet<GameObject>();
        object state = population.GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { lab, roots });
        int Value(string name) => (int)state.GetType().GetProperty(name).GetValue(state);
        Check(Value("OwnedSystems") == 1 && Value("BackgroundSystems") >= 1 && roots.Count == 0, "real population ownership or scratch cleanup mismatch");
        object boundary = population.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { lab, roots });
        Check(((Array)boundary.GetType().GetProperty("Background").GetValue(boundary)).Length >= 1
            && ((Array)boundary.GetType().GetProperty("OwnedEmptyRenderers").GetValue(boundary)).Length == 1 && roots.Count == 0,
            "real boundary inventory or owned-empty classification mismatch");
        var call = Expression.Call(population.GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic), Expression.Constant(lab, labType), Expression.Constant(roots));
        var read = Expression.Lambda<Action>(Expression.Block(call, Expression.Empty())).Compile();
        for (int i = 0; i < 10000; i++) read();
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) read();
        Check(GC.GetAllocatedBytesForCurrentThread() == bytes && roots.Count == 0, "real population scan allocated or retained roots");
        Console.WriteLine("PASS real runtime population scan: live-root attribution, global/background classification, zero warmed allocation and scratch reference clearing.");
        Camera.main = null;
    }
    private sealed class FixtureObject(GameObject value) : IDisposable
    {
        internal GameObject Value => value;
        public void Dispose() => Object.DestroyImmediate(value);
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    private static void Benchmark(Rig rig)
    {
        var system = (ParticleSystem)rig.System;
        rig.Configure("Local", "Hierarchy", "Billboard", "View", 0, 1);
        var particle = (ParticleSystem.Particle)rig.Particle(1, false);
        var type = typeof(ParticleSystem).GetNestedType("DrawSpace", BindingFlags.NonPublic);
        var ctor = type.GetConstructor(Flags, null, new[] { typeof(ParticleSystem) }, null);
        var position = typeof(ParticleSystem).GetMethod("WorldPosition", Flags, null, new[] { typeof(ParticleSystem.Particle) }, null).CreateDelegate<Func<ParticleSystem.Particle, Vector3>>(system);
        var velocity = typeof(ParticleSystem).GetMethod("WorldVelocity", Flags).CreateDelegate<Func<ParticleSystem.Particle, Vector3>>(system);
        var scale = typeof(ParticleSystem).GetMethod("ParticleScale", Flags).CreateDelegate<Func<ParticleSystem.Particle, Vector3>>(system);
        object space = ctor.Invoke(new object[] { system });
        var fastPosition = type.GetMethod("WorldPosition", Flags).CreateDelegate<Func<ParticleSystem.Particle, Vector3>>(space);
        var fastVelocity = type.GetMethod("WorldVelocity", Flags).CreateDelegate<Func<ParticleSystem.Particle, Vector3>>(space);
        var fastScale = type.GetMethod("ParticleScale", Flags).CreateDelegate<Func<ParticleSystem.Particle, Vector3>>(space);
        void Calls(bool fast, int count)
        {
            var p = fast ? fastPosition : position; var v = fast ? fastVelocity : velocity; var s = fast ? fastScale : scale;
            for (int i = 0; i < count; i++) BenchmarkState.Sink = p(particle) + v(particle) + s(particle);
        }
        Calls(false, 20000); Calls(true, 20000);
        for (int round = 0; round < 3; round++) foreach (bool fast in new[] { false, true })
        {
            long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
            Calls(fast, 400000);
            long elapsed = Stopwatch.GetTimestamp() - start, allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
            Check(allocated == 0, "warmed coordinate evaluation allocates: " + allocated);
            Console.WriteLine($"coordinate kernel snapshot={fast}: {elapsed * 1e9 / Stopwatch.Frequency / 400000:F1} ns/particle; {allocated} B (3 transforms; snapshot setup excluded, not whole renderer).");
        }
        Vector3 saved = fastPosition(particle);
        rig.Root.transform.localPosition += new Vector3(3, -2, 5);
        Check(fastPosition(particle).Equals(saved), "snapshot unexpectedly follows later transform changes");
        object next = ctor.Invoke(new object[] { system });
        var nextPosition = type.GetMethod("WorldPosition", Flags).CreateDelegate<Func<ParticleSystem.Particle, Vector3>>(next);
        Check(!nextPosition(particle).Equals(saved) && nextPosition(particle).Equals(position(particle)), "next draw failed to refresh transformed hierarchy");
        Console.WriteLine("PASS zero-allocation coordinate kernels, call-local snapshot isolation and next-call refresh.");
        // Include snapshot construction once per 40 particles, matching 100 systems / 4000 particles.
        Action<ParticleSystem.Particle[]> BuildBatch(bool fast)
        {
            var data = Expression.Parameter(typeof(ParticleSystem.Particle[]), "data");
            var i = Expression.Variable(typeof(int), "i"); var frameSpace = Expression.Variable(type, "space");
            var p = Expression.Variable(typeof(ParticleSystem.Particle), "p"); var done = Expression.Label();
            Expression Eval(string name) => fast ? Expression.Call(frameSpace, type.GetMethod(name, Flags), p)
                : Expression.Call(Expression.Constant(system), typeof(ParticleSystem).GetMethod(name, Flags, null, new[] { typeof(ParticleSystem.Particle) }, null), p);
            var statements = new List<Expression>();
            if (fast) statements.Add(Expression.IfThen(Expression.Equal(Expression.Modulo(i, Expression.Constant(40)), Expression.Constant(0)),
                Expression.Assign(frameSpace, Expression.New(ctor, Expression.Constant(system)))));
            statements.Add(Expression.Assign(p, Expression.ArrayIndex(data, i)));
            statements.Add(Expression.Assign(Expression.Field(null, typeof(BenchmarkState).GetField("Sink", BindingFlags.Static | BindingFlags.NonPublic)),
                Expression.Add(Expression.Add(Eval("WorldPosition"), Eval("WorldVelocity")), Eval("ParticleScale"))));
            statements.Add(Expression.PostIncrementAssign(i)); statements.Add(Expression.Empty());
            return Expression.Lambda<Action<ParticleSystem.Particle[]>>(Expression.Block(new[] { i, frameSpace, p }, Expression.Assign(i, Expression.Constant(0)),
                Expression.Loop(Expression.IfThenElse(Expression.LessThan(i, Expression.ArrayLength(data)), Expression.Block(statements), Expression.Break(done)), done)), data).Compile();
        }
        var slowBatch = BuildBatch(false); var fastBatch = BuildBatch(true);
        var particles = Enumerable.Repeat(particle, 4000).ToArray();
        for (int i = 0; i < 50; i++) { slowBatch(particles); fastBatch(particles); }
        for (int round = 0; round < 3; round++) foreach (bool fast in new[] { false, true })
        {
            var batch = fast ? fastBatch : slowBatch;
            long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
            for (int i = 0; i < 200; i++) batch(particles);
            long ticks = Stopwatch.GetTimestamp() - start;
            Check(GC.GetAllocatedBytesForCurrentThread() == bytes, "batched coordinates or snapshot setup allocated");
            Console.WriteLine($"4000-particle coordinate batch snapshot={fast}: {ticks * 1000d / Stopwatch.Frequency / 200:F6} ms; setup included (100 snapshots/batch), 0 B; not a whole-renderer or game measurement.");
        }
    }
    private sealed class Rig : IDisposable
    {
        internal readonly GameObject Root = new("draw-space root"), Mid = new("draw-space mid"), Owner = new("draw-space system"), Custom = new("custom space"), CameraOwner = new("draw-space camera");
        internal readonly object System, Renderer, Camera;
        private readonly Type ps, renderer, particle, eventSpace, cameraState, drawSpace;
        private readonly object main;
        private readonly Mesh mesh;
        internal Rig(Assembly assembly)
        {
            ps = assembly.GetType("RimUnity.ParticleSystem", true); renderer = assembly.GetType("RimUnity.ParticleSystemRenderer", true);
            particle = ps.GetNestedType("Particle"); eventSpace = ps.GetNestedType("EventSpace", BindingFlags.NonPublic);
            cameraState = renderer.GetNestedType("ParticleCameraState", BindingFlags.NonPublic); drawSpace = ps.GetNestedType("DrawSpace", BindingFlags.NonPublic);
            Mid.transform.SetParent(Root.transform, false); Owner.transform.SetParent(Mid.transform, false);
            System = Owner.AddComponent(ps); Renderer = Owner.AddComponent(renderer);
            Camera = CameraOwner.AddComponent(assembly.GetType("RimUnity.Camera", true)); main = Get(System, "main");
            mesh = new Mesh { vertices = new[] { new Vector3(-1, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) },
                normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward },
                tangents = new[] { new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, 1) },
                uv = new[] { Vector2.zero, Vector2.one, new Vector2(.5f, 1) },
                uv3 = new[] { new Vector2(.1f, .2f), new Vector2(.3f, .4f), new Vector2(.5f, .6f) },
                uv4 = new[] { new Vector2(.7f, .8f), new Vector2(.9f, 1), new Vector2(1.1f, 1.2f) }, triangles = new[] { 0, 1, 2 } };
            Set(Renderer, "mesh", mesh);
            Set(Camera, "pixelRect", new Rect(0, 0, 1600, 900));
        }
        internal void Configure(string simulation, string scaling, string mode, string alignment, int camera, int scenario)
        {
            Root.transform.localPosition = new Vector3(1, -2, 3); Root.transform.localRotation = Quaternion.Euler(10, 30, -15);
            Root.transform.localScale = scenario % 3 == 0 ? new Vector3(0, -2, 3) : new Vector3(-1.5f, 2, .7f);
            Mid.transform.localRotation = Quaternion.Euler(35, -20, 11); Mid.transform.localScale = new Vector3(.5f, 1.7f, -2);
            Owner.transform.localPosition = new Vector3(2, 1, -3); Owner.transform.localRotation = Quaternion.Euler(-8, 19, 42);
            Owner.transform.localScale = new Vector3(1.3f, -.4f, 2.1f);
            Custom.transform.localPosition = new Vector3(4, -5, 1); Custom.transform.localRotation = Quaternion.Euler(40, 12, 7); Custom.transform.localScale = new Vector3(-2, 0, 3);
            Set(Get(System, "textureSheetAnimation"), "enabled", false);
            SetEnum(main, "simulationSpace", simulation); SetEnum(main, "scalingMode", scaling);
            Set(main, "customSimulationSpace", scenario % 2 == 0 ? null : Custom.transform); Set(main, "startRotation3D", scenario % 2 == 0);
            SetEnum(Renderer, "renderMode", mode); SetEnum(Renderer, "alignment", alignment);
            Set(Renderer, "pivot", new Vector3(.1f, -.2f, .3f)); Set(Renderer, "flip", new Vector3(.4f, .7f, .5f));
            Set(Renderer, "allowRoll", scenario % 2 == 0); Set(Renderer, "velocityScale", .3f);
            Set(Renderer, "minParticleSize", .01f); Set(Renderer, "maxParticleSize", .2f);
            CameraOwner.transform.localPosition = camera == 1 ? new Vector3(8, 3, -12) : new Vector3(-6, 14, 4);
            CameraOwner.transform.localRotation = Quaternion.Euler(30, 15, 7); Set(Camera, "orthographic", camera == 2);
            // Explicit streams exercise values that consume the transformed position/size/velocity.
            Streams(scenario % 2 == 0 ? new[] { "UV", "Velocity", "Center", "SizeXYZ", "Speed" } : Array.Empty<string>());
        }
        internal void Streams(params string[] names)
        {
            var streamType = renderer.GetProperty("vertexStreams").PropertyType.GetElementType();
            var data = Array.CreateInstance(streamType, names.Length);
            for (int i = 0; i < names.Length; i++) data.SetValue(Enum.Parse(streamType, names[i]), i);
            Set(Renderer, "vertexStreams", data);
        }
        internal void AnimatedStreams()
        {
            var module = Get(System, "textureSheetAnimation");
            Set(module, "enabled", true); Set(module, "numTilesX", 3); Set(module, "numTilesY", 2);
            SetEnum(module, "timeMode", "FPS"); Set(module, "fps", 7f);
        }
        internal Dictionary<string, object[]> UnpreparedVertices()
        {
            renderer.GetMethod("ClearMesh", Flags).Invoke(Renderer, null);
            for (int i = 0; i < 3; i++) renderer.GetMethod("Vertex", Flags).Invoke(Renderer, new object[] {
                new Vector3(i, 2, 3), new Vector2(.2f * i, .3f), new Color32(10, 20, 30, 40), new Vector4(i, 2, 3, 4), new Vector4(5, 6, 7, i) });
            return Channels();
        }
        internal Action BuildGeometryBatch()
        {
            var i = Expression.Variable(typeof(int), "i"); var j = Expression.Variable(typeof(int), "j");
            var cam = Expression.Variable(cameraState, "camera"); var space = Expression.Variable(drawSpace, "space");
            var data = Array.CreateInstance(particle, 14);
            for (int k = 0; k < data.Length; k++) data.SetValue(Particle(k, k % 3 == 0), k);
            var outerDone = Expression.Label(); var innerDone = Expression.Label();
            var self = Expression.Constant(Renderer, renderer); var sys = Expression.Constant(System, ps);
            return Expression.Lambda<Action>(Expression.Block(new[] { i, j, cam, space },
                Expression.Assign(i, Expression.Constant(0)),
                Expression.Loop(Expression.IfThenElse(Expression.LessThan(i, Expression.Constant(300)), Expression.Block(
                    Expression.Call(self, renderer.GetMethod("ClearMesh", Flags)),
                    Expression.Assign(cam, Expression.New(cameraState.GetConstructors(Flags).Single(), Expression.Constant(Camera, Camera.GetType()), Expression.Constant(null, cameraState.GetConstructors(Flags).Single().GetParameters()[1].ParameterType), Expression.Constant(true))),
                    Expression.Assign(space, Expression.New(drawSpace.GetConstructors(Flags).Single(), sys)),
                    Expression.Assign(j, Expression.Constant(0)),
                    Expression.Loop(Expression.IfThenElse(Expression.LessThan(j, Expression.Condition(Expression.LessThan(i, Expression.Constant(100)), Expression.Constant(14), Expression.Constant(13))), Expression.Block(
                        Expression.Call(self, renderer.GetMethod("AppendParticle", Flags), sys, Expression.ArrayIndex(Expression.Constant(data, particle.MakeArrayType()), j), cam, space),
                        Expression.PostIncrementAssign(j), Expression.Empty()), Expression.Break(innerDone)), innerDone),
                    Expression.PostIncrementAssign(i), Expression.Empty()), Expression.Break(outerDone)), outerDone))).Compile();
        }
        internal object Particle(int id, bool withEvent)
        {
            object p = Activator.CreateInstance(particle);
            Set(p, "position", new Vector3(id - 2, id * .2f, 1 - id)); Set(p, "velocity", new Vector3(id * .3f, -2, .4f));
            Set(p, "moduleVelocity", new Vector3(.1f, .4f, -.2f)); Set(p, "startSize3D", new Vector3(1, .7f, 1.2f));
            Set(p, "startColor", new Color32(150, 80, 200, 170)); Set(p, "rotation3D", new Vector3(id * 7, -12, 34));
            Set(p, "angularVelocity3D", new Vector3(id + 3, -4, 7));
            Set(p, "startLifetime", id == 0 ? 0f : 5f + id); Set(p, "remainingLifetime", 3f); Set(p, "elapsedAge", 2f + id * .1f); Set(p, "randomSeed", (uint)(123 + id));
            Set(p, "alignedToDirection", id == 3);
            if (withEvent)
            {
                object space = Activator.CreateInstance(eventSpace, true);
                Set(space, "local", id % 2 == 0); Set(space, "position", new Vector3(10 + id, -3, 4));
                Set(space, "rotation", Quaternion.Euler(id * 20, 45, 8)); Set(space, "scale", new Vector3(-2, .5f, id == 2 ? 0 : 3));
                Set(p, "eventSpace", space);
            }
            return p;
        }
        internal Dictionary<string, object[]> Geometry(int camera, int scenario)
        {
            renderer.GetMethod("ClearMesh", Flags).Invoke(Renderer, null);
            object cameraData = Activator.CreateInstance(cameraState, Flags, null, new[] { camera == 0 ? null : Camera, null, (object)(scenario % 2 == 0) }, null);
            object space = drawSpace == null ? null : Activator.CreateInstance(drawSpace, Flags, null, new[] { System }, null);
            var append = renderer.GetMethod("AppendParticle", Flags);
            for (int i = 0; i < 4; i++)
            {
                object p = Particle(i, i % 2 == 0 || i == 3);
                append.Invoke(Renderer, drawSpace == null ? new[] { System, p, cameraData } : new[] { System, p, cameraData, space });
            }
            return Channels();
        }
        private Dictionary<string, object[]> Channels()
        {
            var output = new Dictionary<string, object[]>();
            foreach (string name in new[] { "vertices", "indices", "normals", "tangents", "colors", "uv", "packedUV", "custom1", "custom2" })
                output.Add(name, ((IEnumerable)Get(Renderer, name)).Cast<object>().ToArray());
            return output;
        }
        public void Dispose()
        {
            Object.DestroyImmediate(Root); Object.DestroyImmediate(Custom); Object.DestroyImmediate(CameraOwner); Object.DestroyImmediate(mesh);
        }
        private static object Get(object target, string name) => target.GetType().GetField(name, Flags).GetValue(target);
        private static void Set(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, Flags);
            if (field != null) field.SetValue(target, value); else target.GetType().GetProperty(name, Flags).SetValue(target, value);
        }
        private static void SetEnum(object target, string name, string value) => Set(target, name, Enum.Parse(target.GetType().GetField(name, Flags).FieldType, value));
    }
}
