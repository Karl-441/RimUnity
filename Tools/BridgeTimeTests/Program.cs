using RimUnity;
using Verse;

static void Equal(float actual, float expected, string label)
{
    if (Math.Abs(actual - expected) > 0.00001f) throw new Exception($"{label}: {actual} != {expected}");
}

Current.ProgramState = ProgramState.Playing;
Time.deltaTime = Time.unscaledDeltaTime = 1f / 60;
var visual = new BridgeTime();
var gameplay = new BridgeTime();
visual.Reset(); gameplay.Reset();
foreach (int ticks in new[] { 0, 1, 3, 6, 15, 60 })
{
    Find.TickManager.TicksGame += ticks;
    float delta = visual.Step(false, followGameSpeed: false);
    Equal(delta, 1f / 60, $"visual frame with {ticks} ticks");
    if (Math.Ceiling(delta * 60d) > 2) throw new Exception("Tick acceleration multiplied particle substeps");
    Equal(gameplay.Step(false), ticks / 60f, "gameplay clock retained");
}
Find.TickManager.Paused = true;
Equal(visual.Step(false, false), 0, "paused particles");
Equal(visual.Step(true, false), Time.unscaledDeltaTime, "explicit unscaled particles during pause");
Find.TickManager.Paused = false;
visual.Reset();
Find.TickManager.TicksGame += 10000;
Equal(visual.Step(false, false), Time.deltaTime, "enable/load has no catch-up");
Time.deltaTime = 0.1f;
Equal(visual.Step(false, false), 0.1f, "slow frame retains real elapsed time");
Current.ProgramState = ProgramState.Entry;
Find.TickManager = null;
Equal(visual.Step(false, false), Time.deltaTime, "menu does not access game tick manager");
Console.WriteLine("PASS: visual clock independent of 0/1/3/6/15/60 game ticks, pause, unscaled, reset, slow frame, menu; gameplay clock preserved.");

namespace RimUnity
{
    internal static class Time { internal static float deltaTime, unscaledDeltaTime; }
    internal static class Mathf { internal static float Max(float a, float b) => Math.Max(a, b); }
}
namespace Verse
{
    internal enum ProgramState { Entry, Playing }
    internal static class Current { internal static ProgramState ProgramState; }
    internal sealed class TickManager { internal int TicksGame; internal bool Paused; }
    internal static class Find { internal static TickManager TickManager = new(); }
}
