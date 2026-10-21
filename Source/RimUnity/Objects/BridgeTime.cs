using Verse;

namespace RimUnity;

//默认跟随游戏 Tick；装饰粒子可使用响应暂停的帧时钟，非缩放模式始终使用真实时间。
internal sealed class BridgeTime
{
    private int previousTick = -1;
    internal float Step(bool unscaled, bool followGameSpeed = true)
    {
        int tick = Current.ProgramState == ProgramState.Playing ? Find.TickManager.TicksGame : -1;
        // Decorative particles use the frame clock, while still respecting game pause.
        // Feeding accelerated ticks into fixed-step simulation multiplies CPU work at fast speeds.
        float delta = unscaled ? Time.unscaledDeltaTime
            : !followGameSpeed ? tick >= 0 && Find.TickManager.Paused ? 0 : Time.deltaTime
            : tick >= 0 && previousTick >= 0 ? Mathf.Max(0, tick - previousTick) / 60f : tick < 0 ? Time.deltaTime : 0;
        previousTick = tick;
        return delta;
    }
    internal void Reset() => previousTick = Current.ProgramState == ProgramState.Playing ? Find.TickManager.TicksGame : -1;
}
