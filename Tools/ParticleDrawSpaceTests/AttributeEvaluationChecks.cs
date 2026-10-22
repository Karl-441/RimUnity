using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using RimUnity;

internal static partial class Program
{
    private static void CheckAttributeEvaluation(string baseline)
    {
        RimUnity.Runtime.NativeRuntime.InitializeOnCurrentThread();
        var context = new AssemblyLoadContext("particle attribute baseline", true);
        context.Resolving += (_, name) => name.Name == "RimUnity" ? null : Resolve(name);
        var oldAssembly = context.LoadFromAssemblyPath(baseline);
        using var old = new Rig(oldAssembly);
        using var current = new Rig(typeof(ParticleSystem).Assembly);
        Check(oldAssembly.ManifestModule.ModuleVersionId != typeof(ParticleSystem).Module.ModuleVersionId, "attribute baseline must differ");
        int cases = 0;
        for (int mask = 0; mask < 64; mask++) for (int variant = 0; variant < 4; variant++)
        {
            foreach (var rig in new[] { old, current })
            {
                rig.Configure(new[] { "Local", "World", "Custom" }[mask % 3], new[] { "Hierarchy", "Local", "Shape" }[variant % 3], "Billboard", "View", 1, variant);
                rig.AttributeModules(mask, variant);
            }
            for (int edge = 0; edge < 10; edge++)
            {
                var left = old.AttributeValues(edge); var right = current.AttributeValues(edge);
                Check(MemoryMarshal.AsBytes(left.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(right.AsSpan())), $"attribute bits differ: modules={mask}, variant={variant}, edge={edge}");
                cases++;
            }
        }
        Console.WriteLine($"PASS {cases} bytewise attribute cases: all 64 module masks, four curve/gradient variants, age and size edges, random seeds, custom override precedence, module edits and coordinate spaces. No GPU.");
        NoiseReuseChecks.Simulation(oldAssembly);
        foreach (int mask in new[] { 0, 1, 8, 32, 63 })
        foreach (var layout in new[] { Array.Empty<string>(), new[] { "UV", "Custom1XYZW", "Custom2XYZW", "VertexID" } })
        {
            foreach (var rig in new[] { old, current }) { rig.Configure("Local", "Hierarchy", "Billboard", "View", 1, 1); rig.AttributeModules(mask, 1); rig.Streams(layout); }
            var slow = old.BuildGeometryBatch(256, 4); var fast = current.BuildGeometryBatch(256, 4);
            for (int i = 0; i < 60; i++) { slow(); fast(); }
            for (int run = 0; run < 3; run++) foreach (bool optimized in new[] { run % 2 == 0, run % 2 != 0 })
            {
                var action = optimized ? fast : slow;
                long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                for (int i = 0; i < 100; i++) action();
                long ticks = Stopwatch.GetTimestamp() - start;
                Check(GC.GetAllocatedBytesForCurrentThread() == bytes, "attribute evaluation allocates after warmup");
                Console.WriteLine($"Attribute CPU run {run + 1}, modules={mask}, layout={(layout.Length == 0 ? "default" : "custom")}, optimized={optimized}: {ticks * 1000d / Stopwatch.Frequency / 100:F6} ms per 256 particles/4 chunks; full CPU geometry, 0 B, no upload/GPU.");
            }
        }
    }

    private sealed partial class Rig
    {
        internal void AttributeModules(int mask, int variant)
        {
            var curve = ps.GetNestedType("MinMaxCurve"); var gradient = ps.GetNestedType("MinMaxGradient");
            var animation = ps.Assembly.GetType("RimUnity.AnimationCurve");
            var line = animation.GetMethod("Linear").Invoke(null, new object[] { 0f, .2f, 1f, 1.1f });
            var low = animation.GetMethod("Linear").Invoke(null, new object[] { 0f, -.3f, 1f, .4f });
            object Curve(float constant) => variant switch {
                0 => Activator.CreateInstance(curve, new object[] { constant }),
                1 => Activator.CreateInstance(curve, new object[] { constant * .3f, constant }),
                2 => Activator.CreateInstance(curve, new object[] { constant, line }),
                _ => Activator.CreateInstance(curve, new object[] { constant, low, line })
            };
            var colors = new Gradient();
            colors.SetKeys(new[] { new GradientColorKey(new Color(.1f, .3f, .7f, 1), 0), new GradientColorKey(new Color(.9f, .2f, .5f, .3f), 1) },
                new[] { new GradientAlphaKey(.2f, 0), new GradientAlphaKey(.8f, 1) });
            object ColorCurve() => variant switch {
                0 => Activator.CreateInstance(gradient, new object[] { new Color(.7f, .4f, .2f, .8f) }),
                1 => Activator.CreateInstance(gradient, new object[] { Color.black, Color.white }),
                2 => Activator.CreateInstance(gradient, new object[] { colors }),
                _ => Activator.CreateInstance(gradient, new object[] { colors, colors })
            };
            var lifeSize = Get(System, "sizeOverLifetime"); Set(lifeSize, "enabled", (mask & 1) != 0);
            var speedSize = Get(System, "sizeBySpeed"); Set(speedSize, "enabled", (mask & 2) != 0);
            foreach (var module in new[] { lifeSize, speedSize })
            {
                Set(module, "separateAxes", variant % 2 == 0);
                foreach (string name in new[] { "size", "x", "y", "z" }) Set(module, name, Curve(name == "y" ? -.6f : .8f));
            }
            Set(speedSize, "range", variant % 2 == 0 ? new Vector2(2, 2) : new Vector2(-1, 8));
            var noise = Get(System, "noise"); Set(noise, "enabled", (mask & 4) != 0); Set(noise, "sizeAmount", Curve(.1f)); Set(noise, "octaveCount", 2);
            var lifeColor = Get(System, "colorOverLifetime"); Set(lifeColor, "enabled", (mask & 8) != 0); Set(lifeColor, "color", ColorCurve());
            var speedColor = Get(System, "colorBySpeed"); Set(speedColor, "enabled", (mask & 16) != 0); Set(speedColor, "color", ColorCurve());
            Set(speedColor, "range", new Vector2(-1, 5));
            EnableCustomChannels(); Set(Get(System, "customData"), "enabled", (mask & 32) != 0);
            var streams = (Array)Get(Get(System, "customData"), "streams");
            for (int channel = 0; channel < 2; channel++)
            {
                var stream = streams.GetValue(channel); SetEnum(stream, "mode", variant == 0 ? "Disabled" : variant == 3 ? "Color" : "Vector");
                Set(stream, "components", variant == 1 ? 1 : 4); Set(stream, "color", ColorCurve());
                var curves = (Array)Get(stream, "curves");
                for (int i = 0; i < 4; i++) curves.SetValue(Curve((channel == 0 ? 1 : -1) * (.1f + i)), i);
                var overrides = (Dictionary<ulong, Vector4>)((Array)Get(System, "customOverrides")).GetValue(channel);
                overrides.Clear(); overrides[2] = new Vector4(4 + channel, 3, -2, 1);
            }
        }

        internal float[] AttributeValues(int edge)
        {
            var p = Particle(edge, edge % 2 == 0); Set(p, "identity", (ulong)(edge + 1));
            var lifetime = new[] { 0f, -1, 1, float.PositiveInfinity, float.NaN, float.Epsilon, float.MaxValue, 1, 1, 5 };
            var remaining = new[] { 0f, -1, .5f, 9, 1, -float.Epsilon, 3, float.PositiveInfinity, float.NaN, -1 };
            Set(p, "startLifetime", lifetime[edge]); Set(p, "remainingLifetime", remaining[edge]);
            Set(p, "startSize3D", edge == 8 ? new Vector3(float.NaN, -0f, float.PositiveInfinity) : new Vector3(-edge, .7f, edge % 3 == 0 ? 0 : 1));
            Set(p, "startColor", new Color32((byte)(edge * 28), (byte)(255 - edge * 28), 0, 255));
            float age = (float)ps.GetMethod("Age", BindingFlags.Static | Flags).Invoke(null, new[] { p });
            var size = (Vector3)ps.GetMethod("SizeAt", Flags).Invoke(System, new[] { p });
            var color = (Color)ps.GetMethod("ColorAt", Flags).Invoke(System, new[] { p });
            var c1 = (Vector4)ps.GetMethod("CustomAt", Flags).Invoke(System, new[] { p, (object)0 });
            var c2 = (Vector4)ps.GetMethod("CustomAt", Flags).Invoke(System, new[] { p, (object)1 });
            return new[] { age, size.x, size.y, size.z, color.r, color.g, color.b, color.a, c1.x, c1.y, c1.z, c1.w, c2.x, c2.y, c2.z, c2.w };
        }
    }
}
