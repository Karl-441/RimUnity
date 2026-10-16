using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Brutal.VulkanApi;
using HarmonyLib;
using Waylith;
using Waylith.Graphics;
using Waylith.Graphics.Meshes;
using Waylith.Rendering;
using NativeShader = Waylith.Rendering.Shader;

namespace RimUnity.Rendering;

//在加载画面之间分批填充宿主管线缓存，不提交可见绘制。
internal sealed partial class LoadingWarmupFeature : IRenderFeature
{
    private readonly EngineApplication app;
    private readonly IRenderPass[] passes;
    private LoadingWarmupWork work;
    private NativeShader[] shaders = Array.Empty<NativeShader>();
    private VertexLayout[] layouts = Array.Empty<VertexLayout>();
    private bool bloom, presented, effectsWarmed;
    private string keywords;
    private ScreenEfx.EfxFrame effects;
    private List<PipelineRequest> requests;
    private readonly List<string> firstErrors = new();
    private int nextRequest, failures, completedCount, processedCount, totalCount;
    internal LoadingWarmupWork Work => Volatile.Read(ref work);
    internal int CompletedCount => Volatile.Read(ref completedCount);
    internal int ProcessedCount => Volatile.Read(ref processedCount);
    internal int TotalCount => Volatile.Read(ref totalCount);
    public bool IsActive => Work is { Finished: false };
    public IReadOnlyList<IRenderPass> Passes => passes;

    private static readonly AccessTools.FieldRef<MeshBatchRenderer, MeshPipelineCache> Cache = AccessTools.FieldRefAccess<MeshBatchRenderer, MeshPipelineCache>("pipelines");
    private static readonly AccessTools.FieldRef<MeshBatchRenderer, VkDescriptorSetLayout> FrameLayout = AccessTools.FieldRefAccess<MeshBatchRenderer, VkDescriptorSetLayout>("frameDescriptorSetLayout");
    private static readonly AccessTools.FieldRef<MeshBatchRenderer, VkDescriptorSetLayout> GlobalLayout = AccessTools.FieldRefAccess<MeshBatchRenderer, VkDescriptorSetLayout>("globalTextureDescriptorSetLayout");

    //预热通道与宿主渲染器使用相同的资源生命周期。
    internal LoadingWarmupFeature(EngineApplication app) { this.app = app; passes = new IRenderPass[] { new Pass(this) }; }

    //主线程完成输入快照，再发布请求给渲染线程。
    internal void Request(NativeShader[] programs, VertexLayout[] vertexLayouts, bool warmBloom, string globalKeywords, ScreenEfx.EfxFrame screenEffects)
    {
        screenEffects?.Retain();
        shaders = programs; layouts = vertexLayouts; bloom = warmBloom; keywords = globalKeywords; effects = screenEffects;
        presented = false;
        ResetPlan();
        Volatile.Write(ref work, new LoadingWarmupWork());
    }

    //完成信号发出后，主线程释放冻结的输入和计划引用。
    internal void Release(LoadingWarmupWork request)
    {
        if (!ReferenceEquals(Work, request)) return;
        Volatile.Write(ref work, null); shaders = Array.Empty<NativeShader>(); layouts = Array.Empty<VertexLayout>();
        requests = null;
        effects?.Drop(); effects = null;
    }

    //界面提示由原版加载窗口更新，帧只携带借用的工作请求。
    public object CaptureFrame(in FrameTiming timing) => Work?.Payload;

    //窗口重建会使旧渲染器失效，剩余请求必须按新附件重新建立。
    public void OnRenderTargetRebuilt(IRenderTarget target)
    {
        if (Work is { Finished: false }) ResetPlan();
    }

    //首次展示和计划构建各占一帧，之后每帧最多执行 16 项或约 8 毫秒。
    private bool WarmBatch(in FrameContext ctx)
    {
        if (!presented) { presented = true; return false; }
        if (requests == null) { Prepare(in ctx); return false; }
        long started = Stopwatch.GetTimestamp();
        int batchCount = 0;
        while (nextRequest < requests.Count)
        {
            var request = requests[nextRequest++];
            try
            {
                var frameLayout = FrameLayout(request.Renderer); var globalLayout = GlobalLayout(request.Renderer);
                var state = request.Binding.PipelineState;
                var pipeline = Cache(request.Renderer).GetOrCreatePipeline(in frameLayout, in globalLayout, request.Binding, request.Layout,
                    MeshTopology.Triangles, in state, request.Pass, request.Samples, 1, HdrDrawFrame.MapPass, "RimUnity.Warmup");
                if (pipeline.IsNull()) throw new InvalidOperationException("管线被宿主拒绝。");
                Interlocked.Increment(ref completedCount);
            }
            catch (Exception error)
            {
                failures++;
                if (firstErrors.Count < 8) firstErrors.Add(request.Binding.Shader.Key + " / layout=" + request.Layout.Id + "：" + error.Message);
            }
            Interlocked.Increment(ref processedCount);
            //单次驱动调用不能中断；它返回后立刻把本帧交还给加载界面。
            if (++batchCount >= 16 || Stopwatch.GetElapsedTime(started).TotalMilliseconds >= 8) return false;
        }
        if (effects != null && !effectsWarmed)
        {
            //屏幕特效复用原有预热入口，单独留出一帧，不叠加地图管线批次。
            if (batchCount != 0) return false;
            Interlocked.Add(ref completedCount, WarmEffects(in ctx));
            effectsWarmed = true;
            Interlocked.Increment(ref processedCount);
            return false;
        }
        if (failures != 0) throw new InvalidOperationException(failures + " 个管线预热失败；已完成 " + CompletedCount + " 个。\n" + string.Join("\n", firstErrors));
        return true;
    }

    //只在请求发布前或宿主渲染目标安全重建时清除计划。
    private void ResetPlan()
    {
        requests = null; nextRequest = failures = 0; effectsWarmed = false; firstErrors.Clear();
        Volatile.Write(ref totalCount, 0); Volatile.Write(ref processedCount, 0); Volatile.Write(ref completedCount, 0);
    }

    //所有 GPU 调用仍在渲染线程，后台加载线程不接触渲染器。
    private sealed class Pass(LoadingWarmupFeature owner) : IRenderPass
    {
        public byte PassId => 0;
        public PassAttachment Attachment => null;
        public bool PreBackbuffer => true;
        public void OnRenderTargetRebuilt(IRenderTarget target) { }
        public void Dispose() { }
        public void Record(in FrameContext ctx, object payload)
        {
            if (payload is not LoadingWarmupWork.FramePayload frame) return;
            var context = ctx;
            frame.Work.RunBatch(() => owner.WarmBatch(in context));
        }
    }
}
