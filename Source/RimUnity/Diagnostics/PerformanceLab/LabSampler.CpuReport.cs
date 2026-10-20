using System;
using System.IO;
using System.Linq;
using System.Text;

namespace RimUnity.Diagnostics;

internal sealed partial class LabSampler
{
    private const string CpuCaveats = "CPU 阶段只覆盖明确列出的 RimUnity 主线程入口，不包含完整宿主、渲染线程或 GPU。Inclusive 包含子阶段，Self 只扣除本采样器已覆盖的子阶段；不要相加 Inclusive。分位数是每帧阶段累计值，包含零调用帧；MaxCall 是单次调用。阶段分配来自当前主线程，不是进程分配。阶段可包含宿主 API 和脚本调用，不能据此将所有分配归因于框架自身。采样从中途启动时第一帧可能不完整。Observer 仅指帧事件的统计回调，不包含源代码探针开销；关闭 CPU 阶段采样做对照才能判断探针影响。Accepted 为接纳的输入，不代表减少批次；MaterialChanged 等是连续批次结束原因，其他拒绝原因每次仅记录首个命中。UI/CameraUI/Empty 未尝试世界合批。未覆盖主线程分配是主线程总量减去所有阶段 Self，受帧边界影响；进程总量使用近似全局计数，两者不可精确相减推断其他线程分配。";

    private object CpuJson(LabCpuReport.Stage[] stages, LabCpuReport.BatchCounter[] reasons) => new
    {
        Enabled = cpu != null, Frames = cpu?.FrameCount ?? 0,
        ClockFrequency = System.Diagnostics.Stopwatch.Frequency,
        InvalidScopes = cpu?.InvalidScopes ?? 0, CrossThreadScopesSkipped = cpu?.CrossThreadScopes ?? 0,
        Caveats = CpuCaveats, Stages = stages, BatchCounters = reasons, FrameWindows = LabCpuReport.Windows(cpu),
        FrameWindowCaveats = "Disjoint timeline windows partition complete FrameEnqueued intervals; overlap source scopes, so never add them to stage times. EnqueueToPrePoll includes the preceding Observer callback, event tails and waits. Missing, partial or reordered boundaries are omitted, never reported as zero. BridgeToEnqueue covers host work after the bridge; it is not proof of a specific host subsystem's allocation.",
        FrameData = "*.stages.csv, *.batch.csv and *.windows.csv; keyed by frame in *.frames.csv"
    };

    private string CpuText(LabCpuReport.Stage[] stages, LabCpuReport.BatchCounter[] reasons)
    {
        var text = new StringBuilder("CPU 阶段测量：" + (cpu == null ? "关闭（对照采样）" : "开启") + "\n");
        long mainBytes = 0, observerBytes = 0; double observerMs = 0;
        for (int i = 0; i < count; i++)
        { mainBytes += samples[i].MainThreadAllocatedBytes; observerBytes += samples[i].ObserverAllocatedBytes; observerMs += samples[i].ObserverCpuMs; }
        double coveredBytes = stages.Sum(s => s.SelfBytesPerFrame);
        text.AppendLine(FormattableString.Invariant($"主线程分配平均 {mainBytes / (double)Math.Max(1, count):F1} B/帧；Observer 平均 {observerMs / Math.Max(1, count):F6} ms / {observerBytes / (double)Math.Max(1, count):F1} B/帧"));
        if (cpu != null)
        {
            text.AppendLine(FormattableString.Invariant($"已覆盖阶段 Self 合计 {stages.Sum(s => s.MeanSelfMs):F6} ms/帧 / {coveredBytes:F1} B/帧；未覆盖主线程分配约 {Math.Max(0, mainBytes / (double)Math.Max(1, count) - coveredBytes):F1} B/帧"));
            text.AppendLine($"记录帧数 {cpu.FrameCount}；无效/未闭合 scope {cpu.InvalidScopes}；跳过非主线程 scope {cpu.CrossThreadScopes}");
            text.AppendLine("stage\tcalls\tcalls/frame\tmean_incl_ms\tmean_self_ms\tp95_incl_ms\tp99_incl_ms\tp95_self_ms\tp99_self_ms\tmax_frame_ms\tmax_call_ms\tincl_B/frame\tself_B/frame\tvertices\tindices");
            foreach (var s in stages.OrderByDescending(s => s.MeanSelfMs))
                text.AppendLine(FormattableString.Invariant($"{s.Name}\t{s.Calls}\t{s.CallsPerFrame:F3}\t{s.MeanInclusiveMs:F6}\t{s.MeanSelfMs:F6}\t{s.P95InclusiveMs:F6}\t{s.P99InclusiveMs:F6}\t{s.P95SelfMs:F6}\t{s.P99SelfMs:F6}\t{s.MaxFrameInclusiveMs:F6}\t{s.MaxCallMs:F6}\t{s.InclusiveBytesPerFrame:F1}\t{s.SelfBytesPerFrame:F1}\t{s.Vertices}\t{s.Indices}"));
            text.AppendLine("合批诊断：reason\tcount\tper_frame");
            foreach (var r in reasons) text.AppendLine(FormattableString.Invariant($"{r.Name}\t{r.Count}\t{r.PerFrame:F3}"));
            text.AppendLine("主线程互斥时间窗口（与上述阶段重叠，不能再次相加）：window\tcomplete_frames\tmean_ms\tp95_ms\tmax_ms\tB/frame");
            foreach (var w in LabCpuReport.Windows(cpu))
                text.AppendLine(FormattableString.Invariant($"{w.Name}\t{w.CompleteFrames}\t{w.MeanMs:F6}\t{w.P95Ms:F6}\t{w.MaxMs:F6}\t{w.BytesPerFrame:F1}"));
            text.AppendLine("EnqueueToPrePoll 包含上一帧统计回调、后续事件及等待；PrePollToBridge 为桥接更新前的宿主工作；Bridge 包含测试场更新；BridgeToEnqueue 为桥接更新后的宿主工作。缺失/不完整边界不计入窗口汇总，不能当作零耗时。窗口只缩小范围，不证明分配属于某个宿主子系统。");
        }
        return text.AppendLine(CpuCaveats).ToString();
    }

    // Long-form numeric rows avoid enormous repeated JSON objects. Export is outside sampling.
    private void WriteCpuFrames(string prefix, Encoding encoding)
    {
        if (cpu == null) return;
        using var stages = new StreamWriter(prefix + ".stages.csv", false, encoding);
        using var batch = new StreamWriter(prefix + ".batch.csv", false, encoding);
        using var windows = new StreamWriter(prefix + ".windows.csv", false, encoding);
        stages.WriteLine("frame,stage,calls,inclusive_ms,self_ms,inclusive_bytes,self_bytes,max_call_ms,vertices,indices");
        batch.WriteLine("frame,reason,count");
        windows.WriteLine("frame,window,elapsed_ms,main_thread_allocated_bytes");
        for (int f = 0; f < cpu.FrameCount; f++)
        {
            for (int s = 0; s < LabCpuMetrics.StageCount; s++)
            {
                var m = cpu.Get(f, (LabCpuStage)s);
                if (m.Calls == 0 && m.Vertices == 0 && m.Indices == 0) continue;
                stages.WriteLine(FormattableString.Invariant($"{f + 1},{(LabCpuStage)s},{m.Calls},{LabCpuReport.Ms(m.Ticks):F6},{LabCpuReport.Ms(m.SelfTicks):F6},{m.Bytes},{m.SelfBytes},{LabCpuReport.Ms(m.MaxCallTicks):F6},{m.Vertices},{m.Indices}"));
            }
            for (int r = 0; r < LabCpuMetrics.ReasonCount; r++)
            {
                long value = cpu.Reason(f, (LabBatchReason)r);
                if (value != 0) batch.WriteLine(FormattableString.Invariant($"{f + 1},{(LabBatchReason)r},{value}"));
            }
            if (cpu.HasWindows(f))
                for (int w = 0; w < (int)LabFrameWindow.Count; w++)
                {
                    var value = cpu.GetWindow(f, (LabFrameWindow)w);
                    windows.WriteLine(FormattableString.Invariant($"{f + 1},{(LabFrameWindow)w},{LabCpuReport.Ms(value.Ticks):F6},{value.Bytes}"));
                }
        }
    }
}
