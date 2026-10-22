using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Loader;
using RimUnity;

internal static class NoiseReuseChecks
{
    private static float sink;
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    internal static void Run(string baseline)
    {
        var context = new AssemblyLoadContext("noise reuse baseline", true);
        context.Resolving += (_, name) => name.Name == "RimUnity" ? null : AssemblyLoadContext.Default.LoadFromAssemblyName(name);
        var oldAssembly = context.LoadFromAssemblyPath(baseline);
        if (oldAssembly.ManifestModule.ModuleVersionId == typeof(ParticleSystem).Assembly.ManifestModule.ModuleVersionId)
            throw new Exception("Noise baseline must differ from current runtime.");
        var old = Curl(oldAssembly); var current = Curl(typeof(ParticleSystem).Assembly);
        Simulation(oldAssembly);
        int checks = 0;
        uint[] seeds = { 0, 1, 9413, 0x80000000, 0xffffffff, 0x68bc21eb };
        // Includes lattice planes/corners, signed zero, subnormals, negative
        // positions, large coordinates and out-of-range floating point inputs.
        float[] edges = { -2147483648f, -1000000.125f, -2, -1.000001f, -1, -.999999f, -.000001f,
            -float.Epsilon, -0f, 0f, float.Epsilon, .000001f, .999999f, 1, 1.000001f, 2,
            1000000.125f, 2147483648f, float.PositiveInfinity, float.NegativeInfinity, float.NaN };
        foreach (float x in edges) foreach (float y in edges) foreach (float z in edges)
            foreach (uint seed in seeds) foreach (int quality in new[] { 0, 1, 2, -1, 7 })
                Equal(old(new(x, y, z), seed, quality), current(new(x, y, z), seed, quality), ref checks);
        var random = new Random(710239);
        var points = new Vector3[1000];
        for (int i = 0; i < points.Length; i++) points[i] = new((float)(random.NextDouble() * 100 - 50),
            (float)(random.NextDouble() * 100 - 50), (float)(random.NextDouble() * 100 - 50));
        foreach (var point in points) foreach (uint seed in seeds) foreach (int quality in new[] { 0, 1, 2 })
        {
            Vector3 p = point, a = Vector3.zero, b = Vector3.zero; float amplitude = 1;
            for (int octave = 0; octave < 6; octave++)
            {
                var expected = old(p, seed, quality); var actual = current(p, seed, quality);
                Equal(expected, actual, ref checks);
                a += expected * amplitude; b += actual * amplitude;
                Equal(a, b, ref checks);
                p *= 2.1f; amplitude *= .57f;
            }
        }
        Console.WriteLine($"PASS {checks} bitwise Vector3 noise comparisons: Low/Medium/High, invalid enum fallback, six seeds, lattice boundaries, negative/extreme coordinates, six-octave sums. No GPU.");
        foreach (int quality in new[] { 0, 1, 2 })
        {
            void Batch(Func<Vector3, uint, int, Vector3> noise)
            {
                Vector3 sum = default;
                foreach (var point in points)
                {
                    Vector3 p = point; float amplitude = 1;
                    for (int octave = 0; octave < 3; octave++)
                    { sum += noise(p, 9413, quality) * amplitude; p *= 2; amplitude *= .5f; }
                }
                sink = sum.x;
            }
            for (int i = 0; i < 60; i++) { Batch(old); Batch(current); }
            for (int run = 0; run < 3; run++) foreach (bool optimized in new[] { false, true })
            {
                var noise = optimized ? current : old;
                long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                for (int i = 0; i < 40; i++) Batch(noise);
                long ticks = Stopwatch.GetTimestamp() - start;
                if (GC.GetAllocatedBytesForCurrentThread() != bytes) throw new Exception("Noise allocates after warmup.");
                Console.WriteLine($"Noise CPU run {run + 1}, quality={quality}, optimized={optimized}: {ticks * 1000d / Stopwatch.Frequency / 40:F6} ms / 1000 particles x 3 octaves, 0 B; synthetic Curl only.");
            }
        }
        BenchmarkSimulation(oldAssembly);
    }
    private static void BenchmarkSimulation(Assembly baseline)
    {
        var old = Enumerable.Range(0, 4).Select(i => new SimulationRig(baseline, 2, 3, 1, (uint)(9413 + i), true)).ToArray();
        var current = Enumerable.Range(0, 4).Select(i => new SimulationRig(typeof(ParticleSystem).Assembly, 2, 3, 1, (uint)(9413 + i), true)).ToArray();
        try
        {
            void Batch(SimulationRig[] rigs) { foreach (var rig in rigs) rig.Step(1f / 144, false); }
            foreach (var rig in old.Concat(current)) rig.Step(4f, true);
            for (int i = 0; i < 600; i++) { Batch(old); Batch(current); }
            long baselineBytes = 0;
            for (int run = 0; run < 3; run++) foreach (bool optimized in new[] { false, true })
            {
                var rigs = optimized ? current : old;
                long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                for (int i = 0; i < 100; i++) Batch(rigs);
                long ticks = Stopwatch.GetTimestamp() - start;
                long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
                if (!optimized) baselineBytes = allocated;
                else if (allocated != baselineBytes) throw new Exception("Noise change alters explicit simulation allocations.");
                Console.WriteLine($"Simulation CPU run {run + 1}, optimized={optimized}: {ticks * 1000d / Stopwatch.Frequency / 100:F6} ms, {allocated / 100d:F0} B / four 64-particle systems, High x 3 octaves; explicit Simulate API includes its existing collection allocations, no rendering/probes.");
            }
        }
        finally { foreach (var rig in old.Concat(current)) rig.Dispose(); }
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    internal static void Simulation(Assembly baseline)
    {
        RimUnity.Runtime.NativeRuntime.InitializeOnCurrentThread();
        int cases = 0, particles = 0;
        foreach (int quality in new[] { 0, 1, 2 }) foreach (int octaves in new[] { 0, 1, 3, 5 })
        foreach (int space in new[] { 0, 1, 2 }) foreach (uint seed in new[] { 9413u, 0xffffffffu })
        {
            using var old = new SimulationRig(baseline, quality, octaves, space, seed);
            using var current = new SimulationRig(typeof(ParticleSystem).Assembly, quality, octaves, space, seed);
            for (int frame = 0; frame < 360; frame++)
            {
                float dt = frame % 7 == 0 ? 1f / 60 : 1f / 144;
                old.Step(dt, frame == 0); current.Step(dt, frame == 0);
                if (frame % 30 != 29) continue;
                int count = (int)old.Type.GetField("count", Program.Flags).GetValue(old.System);
                if (count != (int)current.Type.GetField("count", Program.Flags).GetValue(current.System)) throw new Exception("Noise simulation population differs.");
                var a = (Array)old.Type.GetField("particles", Program.Flags).GetValue(old.System);
                var b = (Array)current.Type.GetField("particles", Program.Flags).GetValue(current.System);
                foreach (var field in a.GetType().GetElementType().GetFields(Program.Flags))
                {
                    var other = b.GetType().GetElementType().GetField(field.Name, Program.Flags);
                    for (int i = 0; i < count; i++)
                    {
                        object expected = field.GetValue(a.GetValue(i)), actual = other.GetValue(b.GetValue(i));
                        if (!Equals(expected, actual)) throw new Exception($"Noise simulation differs: case {cases}, frame {frame}, particle {i}, {field.Name}.");
                    }
                }
                // SizeAt is a renderer consumer, not necessarily evaluated during
                // a physics-free simulation step. Exercise its noise path explicitly.
                for (int i = 0; i < count; i++)
                {
                    object expected = old.Type.GetMethod("SizeAt", Program.Flags).Invoke(old.System, new[] { a.GetValue(i) });
                    object actual = current.Type.GetMethod("SizeAt", Program.Flags).Invoke(current.System, new[] { b.GetValue(i) });
                    if (!Equals(expected, actual)) throw new Exception("Noise SizeAt differs after simulation.");
                }
                particles += count;
            }
            cases++;
        }
        Console.WriteLine($"PASS {cases} continuous simulation cases / {particles} particle snapshots, all particle fields identical: quality, octave count, Local/World/Custom, two seeds, variable time steps, noise position/rotation/size/remap. No GPU.");
    }
    private sealed class SimulationRig : IDisposable
    {
        internal readonly Type Type;
        internal readonly object System;
        private readonly GameObject root = new("noise comparison");
        private readonly Action<float, bool> step;
        internal SimulationRig(Assembly assembly, int quality, int octaves, int space, uint seed, bool stress = false)
        {
            root.SetActive(false);
            Type = assembly.GetType("RimUnity.ParticleSystem", true);
            assembly.GetType("RimUnity.Diagnostics.LabStressScenario", true).GetMethod("Configure", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { root, 6, null, null });
            System = root.GetComponent(Type);
            Set(System, "randomSeed", seed); Set(System, "useAutoRandomSeed", false);
            root.transform.localPosition = new Vector3(2, -3, .7f); root.transform.localRotation = Quaternion.Euler(11, -21, 7);
            root.transform.localScale = new Vector3(-.7f, 1.2f, .5f);
            var main = Type.GetField("main").GetValue(System);
            Set(main, "simulationSpace", Enum.ToObject(main.GetType().GetField("simulationSpace").FieldType, space));
            Set(main, "customSimulationSpace", root.transform);
            var noise = Type.GetField("noise").GetValue(System);
            Set(noise, "quality", Enum.ToObject(noise.GetType().GetField("quality").FieldType, quality));
            Set(noise, "octaveCount", octaves); Set(noise, "separateAxes", !stress); Set(noise, "remapEnabled", !stress);
            object Curve(float value) => Activator.CreateInstance(Type.GetNestedType("MinMaxCurve"), new object[] { value });
            Set(noise, "strengthX", Curve(.3f)); Set(noise, "strengthY", Curve(.5f)); Set(noise, "strengthZ", Curve(.7f));
            if (!stress) { Set(noise, "rotationAmount", Curve(.2f)); Set(noise, "sizeAmount", Curve(.1f)); }
            var curveType = assembly.GetType("RimUnity.AnimationCurve");
            var remap = curveType.GetMethod("Linear").Invoke(null, new object[] { 0f, -1f, 1f, 1f });
            var mapping = Activator.CreateInstance(Type.GetNestedType("MinMaxCurve"), new object[] { 1f, remap });
            foreach (string name in new[] { "remapX", "remapY", "remapZ" }) Set(noise, name, mapping);
            root.SetActive(true);
            var dt = Expression.Parameter(typeof(float)); var restart = Expression.Parameter(typeof(bool));
            step = Expression.Lambda<Action<float, bool>>(Expression.Call(Expression.Constant(System, Type), Type.GetMethod("Simulate"),
                dt, Expression.Constant(false), restart, Expression.Constant(true)), dt, restart).Compile();
        }
        internal void Step(float dt, bool restart) => step(dt, restart);
        private static void Set(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, Program.Flags);
            if (field != null) field.SetValue(target, value); else target.GetType().GetProperty(name, Program.Flags).SetValue(target, value);
        }
        public void Dispose() => RimUnity.Object.DestroyImmediate(root);
    }
    private static void Equal(Vector3 a, Vector3 b, ref int checks)
    {
        if (BitConverter.SingleToInt32Bits(a.x) != BitConverter.SingleToInt32Bits(b.x)
            || BitConverter.SingleToInt32Bits(a.y) != BitConverter.SingleToInt32Bits(b.y)
            || BitConverter.SingleToInt32Bits(a.z) != BitConverter.SingleToInt32Bits(b.z))
            throw new Exception($"Noise differs at comparison {checks}: {a} / {b}");
        checks++;
    }
    private static Func<Vector3, uint, int, Vector3> Curl(Assembly assembly)
    {
        var method = assembly.GetType("RimUnity.ParticleNoise", true).GetMethod("Curl", BindingFlags.Static | BindingFlags.NonPublic);
        var point = Expression.Parameter(typeof(Vector3)); var seed = Expression.Parameter(typeof(uint)); var quality = Expression.Parameter(typeof(int));
        return Expression.Lambda<Func<Vector3, uint, int, Vector3>>(Expression.Call(method, point, seed,
            Expression.Convert(quality, method.GetParameters()[2].ParameterType)), point, seed, quality).Compile();
    }
}
