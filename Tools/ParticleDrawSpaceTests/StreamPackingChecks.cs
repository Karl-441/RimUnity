using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using RimUnity;

internal static partial class Program
{
    private static void CheckStreamPacking(string baseline)
    {
        RimUnity.Runtime.NativeRuntime.InitializeOnCurrentThread();
        var context = new AssemblyLoadContext("vertex stream packing baseline", true);
        context.Resolving += (_, name) => name.Name == "RimUnity" ? null : Resolve(name);
        using var old = new Rig(context.LoadFromAssemblyPath(baseline));
        using var current = new Rig(typeof(ParticleSystem).Assembly);
        Check(old.Renderer.GetType().Assembly.ManifestModule.ModuleVersionId != typeof(ParticleSystem).Module.ModuleVersionId, "stream baseline must differ");
        var layouts = new List<string[]> {
            Array.Empty<string>(), new[] { "Position", "Normal", "Color", "Tangent" }, new[] { "UV" },
            new[] { "UV", "UV2", "AnimBlend" },
            new[] { "UV", "StableRandomXYZW", "VaryingRandomXYZ", "Custom1XY", "VertexID" },
            new[] { "UV", "UV2", "UV3", "UV4", "VertexID", "ParticleIndex", "AnimBlend", "AnimFrame" },
            new[] { "UV2", "UV2", "UV", "UV", "UV3", "UV4" },
            new[] { "Custom1XYZW", "Custom2XYZW", "VertexID", "UV2" },
            new[] { "VertexID", "VertexID", "UV4", "UV3", "UV2", "UV", "VertexID", "VertexID" }
        };
        var streamType = current.Renderer.GetType().GetProperty("vertexStreams").PropertyType.GetElementType();
        foreach (string name in Enum.GetNames(streamType))
        {
            try { current.Streams(name); layouts.Add(new[] { name }); }
            catch (TargetInvocationException ex) when (ex.InnerException is NotSupportedException) { }
        }
        int cases = 0;
        foreach (var layout in layouts)
        foreach (string mode in new[] { "Billboard", "Stretch", "Mesh" })
        for (int camera = 0; camera < 3; camera++)
        {
            foreach (var rig in new[] { old, current })
            {
                rig.Configure(new[] { "Local", "World", "Custom" }[cases % 3], new[] { "Hierarchy", "Local", "Shape" }[cases / 3 % 3], mode, "View", camera, cases);
                rig.EnableCustomChannels(); rig.Streams(layout);
            }
            for (int draw = 0; draw < 3; draw++)
            {
                if (draw > 0) foreach (var rig in new[] { old, current }) { rig.AnimatedStreams(); rig.MoveSortRig(draw); }
                Compare(old.Geometry(camera, cases), current.Geometry(camera, cases), "prepared");
                Compare(old.UnpreparedVertices(), current.UnpreparedVertices(), "clear/unprepared");
            }
            cases++;
            void Compare(Dictionary<string, object[]> left, Dictionary<string, object[]> right, string step)
            {
                foreach (string channel in left.Keys)
                    Check(ChannelBytes(channel, left[channel]).SequenceEqual(ChannelBytes(channel, right[channel])), $"stream bits differ {cases}/{mode}/{camera}/{step}/{channel}/{string.Join('+', layout)}");
            }
        }
        Console.WriteLine($"PASS {cases} stream configurations x3 draws x prepared/unprepared, nine bytewise channels; every supported stream, repeated streams, full 12-component layouts, custom data, animation, flips, mesh UV3/4 and VertexID. No GPU.");
        int edgeCases = 0;
        foreach (var rect in new[] { new Rect(0, 0, 0, 0), new Rect(-0f, 0, float.Epsilon, -float.Epsilon),
            new Rect(-2, .7f, -.3f, .2f), new Rect(float.PositiveInfinity, 0, 1, 1), new Rect(float.NaN, -.2f, .3f, .7f) })
        foreach (bool prepared in new[] { false, true })
        {
            var left = old.PackedUvEdges(rect, prepared); var right = current.PackedUvEdges(rect, prepared);
            foreach (string channel in left.Keys)
                Check(ChannelBytes(channel, left[channel]).SequenceEqual(ChannelBytes(channel, right[channel])), $"UV edge bits differ: {rect}/{prepared}/{channel}");
            edgeCases++;
        }
        Console.WriteLine($"PASS {edgeCases} prepared/fallback UV2 edge cases: zero/negative/epsilon extents, signed zero and nonfinite values; bytewise output matches.");
        foreach (var layout in new[] { Array.Empty<string>(), new[] { "UV", "UV2", "AnimBlend" },
            new[] { "UV", "StableRandomXYZW", "VaryingRandomXYZ", "Custom1XY", "VertexID" },
            new[] { "UV", "UV2", "UV3", "UV4", "VertexID", "ParticleIndex", "AnimBlend", "AnimFrame" } })
        foreach (string mode in new[] { "Billboard", "Mesh" })
        {
            foreach (var rig in new[] { old, current }) { rig.Configure("Local", "Hierarchy", mode, "View", 1, 1); rig.Streams(layout); rig.AnimatedStreams(); }
            var slow = old.BuildGeometryBatch(256, 4); var fast = current.BuildGeometryBatch(256, 4);
            for (int i = 0; i < 100; i++) { slow(); fast(); }
            for (int run = 0; run < 3; run++) foreach (bool optimized in new[] { run % 2 == 0, run % 2 != 0 })
            {
                var action = optimized ? fast : slow;
                long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                for (int i = 0; i < 300; i++) action();
                long ticks = Stopwatch.GetTimestamp() - start;
                Check(GC.GetAllocatedBytesForCurrentThread() == bytes, "stream packing allocates after warmup");
                Console.WriteLine($"Stream CPU run {run + 1}, {mode}/{(layout.Length == 0 ? "default" : string.Join('+', layout))}, optimized={optimized}: {ticks * 1000d / Stopwatch.Frequency / 300:F6} ms per 256 particles/4 chunks, animation included; 0 B, CPU geometry only, no upload/GPU.");
            }
        }
    }

    private sealed partial class Rig
    {
        internal Dictionary<string, object[]> PackedUvEdges(Rect rect, bool prepared)
        {
            Streams("UV2", "UV", "UV2", "VertexID", "Custom1XYZW");
            renderer.GetMethod("ClearMesh", Flags).Invoke(Renderer, null);
            Set(Renderer, "streamCurrentUV", rect); Set(Renderer, "streamNextUV", new Rect(.2f, -.3f, .4f, -.5f));
            if (prepared) renderer.GetMethod("PrepareParticleStreams", Flags).Invoke(Renderer, new object[] { new Vector4(1, 2, 3, 4), Vector4.zero });
            foreach (var uv in new[] { new Vector2(0, 0), new Vector2(-0f, .5f), new Vector2(-1, 2), new Vector2(float.NaN, float.PositiveInfinity) })
                renderer.GetMethod("Vertex", Flags).Invoke(Renderer, new object[] { Vector3.zero, uv, new Color32(1, 2, 3, 4), new Vector4(1, 2, 3, 4), Vector4.zero });
            return Channels();
        }
    }
}
