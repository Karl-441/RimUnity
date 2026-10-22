using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace RimUnity.Diagnostics;

internal static partial class DpaDiagnostics
{
    internal enum HdrStage { Setup, FilterDynamic, SortDynamic, MergeAndLookup, BindingRefresh, CommandCopy }
    private struct HdrMetric { internal long Calls, Ticks, Bytes; }
    [InlineArray(6)] private struct HdrMetrics { private HdrMetric element; }
    private static readonly HdrMetric[] hdrTotals = new HdrMetric[12];
    // Numeric session totals only, separately owned by HDR and Bloom. No scene references.
    private static readonly long[] hdrWork = new long[14];
    internal readonly record struct HdrToken(long Start, long Bytes);
    internal struct HdrCapture
    {
        private readonly bool active, bloom;
        private readonly int epoch, uniformStructBytes;
        private HdrMetrics stages;
        internal long DynamicInput, PersistentInput, Commands, Bindings;
        internal HdrCapture(bool bloom, int uniformStructBytes = 0)
        {
            this = default; active = enabled; this.bloom = bloom; epoch = generation; this.uniformStructBytes = uniformStructBytes;
        }
        internal HdrToken Begin() => active ? new(Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread()) : default;
        internal void End(HdrStage stage, HdrToken token)
        {
            if (token.Start == 0) return;
            ref var metric = ref stages[(int)stage];
            metric.Ticks += Math.Max(0, Stopwatch.GetTimestamp() - token.Start);
            metric.Bytes += Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - token.Bytes);
            metric.Calls++;
        }
        internal void Complete(bool success)
        {
            if (!active) return;
            lock (gate)
            {
                if (!enabled || epoch != generation) return;
                int offset = bloom ? 6 : 0, work = bloom ? 7 : 0;
                for (int i = 0; i < 6; i++)
                {
                    hdrTotals[offset + i].Calls += stages[i].Calls;
                    hdrTotals[offset + i].Ticks += stages[i].Ticks;
                    hdrTotals[offset + i].Bytes += stages[i].Bytes;
                }
                hdrWork[work]++; if (!success) hdrWork[work + 1]++;
                hdrWork[work + 2] += DynamicInput; hdrWork[work + 3] += PersistentInput;
                hdrWork[work + 4] += Commands; hdrWork[work + 5] += Bindings;
                hdrWork[work + 6] += Commands * uniformStructBytes;
            }
        }
    }
    private static void ResetHdr() { Array.Clear(hdrTotals); Array.Clear(hdrWork); }
    private static void AppendHdrReport(StringBuilder text)
    {
        text.AppendLine("HDR SNAPSHOT STAGES v1 (detailed mode only; CPU incl. nested probes, not GPU)");
        text.AppendLine("Stage timers accumulate locally per snapshot; one merge into session counters. Clock/allocation reader overhead affects fine mode. Compare with light/Cleanup.");
        text.AppendLine("snapshot\tattempts\tfailed\tdynamic_input\tpersistent_input\tcommands_copied\tunique_bindings_refreshed\tcommand_uniform_struct_bytes");
        lock (gate)
        {
            for (int role = 0; role < 2; role++)
            {
                text.Append(role == 0 ? "HDR" : "Bloom");
                for (int i = 0; i < 7; i++) text.Append('\t').Append(hdrWork[role * 7 + i]);
                text.AppendLine();
            }
            text.AppendLine("Uniform bytes are logical inline struct bytes per copied command, excluding texture/storage contents, referenced arrays and other command fields; not memory bandwidth or allocation.");
            text.AppendLine("snapshot\tstage\tcalls\tcpu_ms\tallocation_bytes");
            for (int role = 0; role < 2; role++) for (int i = 0; i < 6; i++)
            {
                var m = hdrTotals[role * 6 + i];
                text.Append(role == 0 ? "HDR" : "Bloom").Append('\t').Append((HdrStage)i).Append('\t')
                    .Append(m.Calls).Append('\t').Append((m.Ticks * 1000d / Stopwatch.Frequency).ToString("F6", CultureInfo.InvariantCulture))
                    .Append('\t').Append(m.Bytes).AppendLine();
            }
        }
    }
}
