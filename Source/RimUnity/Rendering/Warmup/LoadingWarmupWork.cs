using System;
using System.Threading;

namespace RimUnity.Rendering;

//一次加载请求可分帧执行，后台加载线程只等待完成信号。
internal sealed class LoadingWarmupWork : IDisposable
{
    //宿主会释放帧载荷；借用包装不能取得共享请求的所有权。
    internal sealed record FramePayload(LoadingWarmupWork Work);
    internal FramePayload Payload { get; }
    private readonly ManualResetEventSlim completed = new(false);
    private int state; //0 等待下一批，1 执行中，2 完成，3 已取消，4 当前批结束后取消
    internal Exception Error { get; private set; }
    internal bool Finished => completed.IsSet;
    internal bool Cancelled => Volatile.Read(ref state) == 3;
    //所有排队帧借用同一个请求，完成后的迟到帧不再执行工作。
    internal LoadingWarmupWork() => Payload = new FramePayload(this);

    //返回 false 时释放执行权，让下一渲染帧继续剩余任务。
    internal void RunBatch(Func<bool> action)
    {
        if (Interlocked.CompareExchange(ref state, 1, 0) != 0) return;
        bool finished = false;
        try { finished = action(); }
        catch (Exception error) { Error = error; finished = true; }
        finally
        {
            int next = finished ? 2 : 0;
            if (Interlocked.CompareExchange(ref state, next, 1) == 4)
            {
                next = 3;
                Volatile.Write(ref state, next);
            }
            if (next is 2 or 3) completed.Set();
        }
    }
    //仅在两批之间取消，不能释放渲染线程正在使用的资源。
    internal bool CancelPending()
    {
        if (Interlocked.CompareExchange(ref state, 3, 0) != 0) return false;
        completed.Set(); return true;
    }
    internal void Wait(TimeSpan? timeout = null)
    {
        //最小化窗口可能不再提交帧；超时后最多等待当前批完成。
        if (completed.Wait(timeout ?? TimeSpan.FromSeconds(30))) return;
        while (true)
        {
            int observed = Volatile.Read(ref state);
            if (observed >= 2) break;
            int next = observed == 0 ? 3 : 4;
            if (Interlocked.CompareExchange(ref state, next, observed) != observed) continue;
            if (next == 3) completed.Set();
            break;
        }
        completed.Wait();
    }
    //加载完成且所属入口已解除引用后释放等待句柄。
    public void Dispose() => completed.Dispose();
}
