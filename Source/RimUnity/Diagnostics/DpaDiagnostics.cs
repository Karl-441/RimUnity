using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;

namespace RimUnity.Diagnostics;

// Optional DPA bridge, accessed through cached delegates. Only numbers survive a capture.
// Counters are session-wide; reset clears records, disable invalidates in-flight tokens.
internal static partial class DpaDiagnostics
{
    private static volatile bool enabled;
    private static int generation;
    private static readonly long[] counters = new long[10];
    private static readonly object gate = new();
    private static readonly SubmitRecord[] slowest = new SubmitRecord[8];
    private readonly record struct SubmitRecord(long Ticks, int Frame, int Vertices, int Indices, int Route);
    internal readonly record struct Token(long Start, int Generation);
    internal static void SetEnabled(bool value)
    {
        lock (gate)
        {
            Interlocked.Increment(ref generation);
            enabled = value;
        }
    }
    internal static void Reset()
    {
        lock (gate)
        {
            Interlocked.Increment(ref generation);
            for (int i = 0; i < counters.Length; i++) Interlocked.Exchange(ref counters[i], 0);
            Array.Clear(slowest);
            ResetHdr();
        }
    }
    internal static void Binding(bool bloom, int reason)
    {
        if (enabled) Interlocked.Increment(ref counters[(bloom ? 5 : 0) + reason]);
    }
    internal static void Workspace(bool bloom, bool discarded)
    {
        if (enabled) Interlocked.Increment(ref counters[(bloom ? 5 : 0) + (discarded ? 4 : 3)]);
    }
    internal static Token BeginSubmit() => enabled ? new(Stopwatch.GetTimestamp(), Volatile.Read(ref generation)) : default;
    // Routes: 0 failed/incomplete, 1 empty, 2 UI, 3 batched, 4 independent, 5 camera UI.
    internal static void EndSubmit(Token token, int frame, int vertices, int indices, int route)
    {
        if (token.Start == 0 || !enabled) return;
        long ticks = Stopwatch.GetTimestamp() - token.Start;
        lock (gate)
        {
            if (!enabled || token.Generation != generation) return;
            int slot = 0;
            for (int i = 1; i < slowest.Length; i++) if (slowest[i].Ticks < slowest[slot].Ticks) slot = i;
            if (ticks > slowest[slot].Ticks) slowest[slot] = new(ticks, frame, vertices, indices, route);
        }
    }
    internal static void AppendReport(StringBuilder text)
    {
        AppendHdrReport(text);
        text.AppendLine("HDR BINDING REUSE (optional DPA session counters; HDR/Bloom separated)");
        text.AppendLine("enabled=" + enabled + "; new allocations include caller-owned binding objects, not just constructor body bytes");
        text.AppendLine("snapshot\treused\tnew_identity\tsame_identity_exhausted\tworkspace_created\tworkspace_oversize_discarded");
        for (int role = 0; role < 2; role++)
        {
            text.Append(role == 0 ? "HDR" : "Bloom");
            for (int i = 0; i < 5; i++) text.Append('\t').Append(Interlocked.Read(ref counters[role * 5 + i]));
            text.AppendLine();
        }
        text.AppendLine("SLOWEST PARTICLE SUBMITS (frame clock=UnityEngine.Time.frameCount; 8 calls; CPU incl. profiling overhead; not GPU duration)");
        text.AppendLine("frame\tcpu_ms\tvertices\tindices\troute\tbatch_result");
        lock (gate)
            foreach (var r in slowest)
                if (r.Ticks > 0)
                {
                    string route = r.Route switch { 1 => "empty", 2 => "UI", 3 => "world", 4 => "world", 5 => "cameraUI", _ => "failed" };
                    string batch = r.Route switch { 3 => "accepted", 4 => "rejected", _ => "not_attempted_or_failed" };
                    text.Append(r.Frame).Append('\t').Append((r.Ticks * 1000.0 / Stopwatch.Frequency).ToString("F6", CultureInfo.InvariantCulture))
                        .Append('\t').Append(r.Vertices).Append('\t').Append(r.Indices).Append('\t').Append(route).Append('\t').Append(batch).AppendLine();
                }
    }
}
