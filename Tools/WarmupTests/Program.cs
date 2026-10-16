using RimUnity.Rendering;

//断言请求所有权和跨帧调度的边界。
void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
using (var work = new LoadingWarmupWork())
{
    object first = work.Payload, queued = work.Payload;
    //宿主在回收或丢弃帧时使用相同的释放规则。
    if (first is IDisposable owner) owner.Dispose();
    work.RunBatch(() => true); work.Wait();
    if (queued is IDisposable anotherOwner) anotherOwner.Dispose();
    work.RunBatch(() => throw new Exception("Already executed"));
    Check(work.Finished && work.Error == null && first is not IDisposable,
        "retiring borrowed frame payloads cannot dispose the shared loading request");
}
{
    var work = new LoadingWarmupWork(); object queued = work.Payload;
    work.RunBatch(() => true); work.Wait(); work.Dispose();
    ((LoadingWarmupWork.FramePayload)queued).Work.RunBatch(() => throw new Exception("Retired request executed again"));
    Check(true, "a queued frame after owner cleanup safely ignores the completed request");
}
using (var work = new LoadingWarmupWork())
{
    int calls = 0;
    Parallel.For(0, 64, _ => work.RunBatch(() => { Interlocked.Increment(ref calls); return true; }));
    work.Wait(); Check(calls == 1 && work.Finished && work.Error == null, "queued render frames execute one warmup only");
}
using (var work = new LoadingWarmupWork())
{
    Check(work.CancelPending(), "request can be cancelled before rendering");
    work.RunBatch(() => throw new Exception("Must not execute")); work.Wait();
    Check(work.Cancelled && work.Error == null, "cancelled request never reaches GPU work");
}
using (var work = new LoadingWarmupWork())
{
    work.Wait(TimeSpan.Zero);
    work.RunBatch(() => throw new Exception("No render frame should execute after the loading timeout"));
    Check(work.Cancelled && work.Error == null, "missing render frames release loading at the timeout and reject late work");
}
using (var work = new LoadingWarmupWork())
{
    var expected = new InvalidOperationException("driver failure");
    var task = Task.Run(() => work.RunBatch(() => throw expected)); work.Wait(); await task;
    Check(work.Finished && ReferenceEquals(work.Error, expected), "render failure is published before loading resumes");
}
using (var work = new LoadingWarmupWork())
using (var entered = new ManualResetEventSlim())
using (var release = new ManualResetEventSlim())
{
    var task = Task.Run(() => work.RunBatch(() => { entered.Set(); release.Wait(); return true; }));
    entered.Wait(); Check(!work.CancelPending() && !work.Finished, "running GPU work cannot be cancelled out from under the render thread");
    release.Set(); work.Wait(); await task;
    Check(work.Finished && !work.Cancelled, "loading releases request only after rendering finishes");
}
using (var work = new LoadingWarmupWork())
{
    int batches = 0;
    work.RunBatch(() => { batches++; return false; });
    Check(!work.Finished, "unfinished batch keeps loading active");
    work.RunBatch(() => { batches++; return false; });
    work.RunBatch(() => { batches++; return true; }); work.Wait();
    work.RunBatch(() => throw new Exception("Completed batches must not repeat"));
    Check(batches == 3 && work.Finished, "successive render frames resume and finish one request");
}
using (var work = new LoadingWarmupWork())
{
    work.RunBatch(() => false); work.Wait(TimeSpan.Zero);
    work.RunBatch(() => throw new Exception("Cancelled remainder executed"));
    Check(work.Cancelled, "minimized window can cancel between batches without another render frame");
}
using (var work = new LoadingWarmupWork())
using (var entered = new ManualResetEventSlim())
using (var waiting = new ManualResetEventSlim())
using (var release = new ManualResetEventSlim())
{
    var render = Task.Run(() => work.RunBatch(() => { entered.Set(); release.Wait(); return false; }));
    entered.Wait();
    var loading = Task.Run(() => { waiting.Set(); work.Wait(TimeSpan.Zero); });
    waiting.Wait(); release.Set();
    await Task.WhenAll(render, loading);
    Check(work.Cancelled, "timeout during a batch finishes without requiring a future frame");
}
