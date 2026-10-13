using System;

namespace RimUnity;

//过渡只在没有进行中混合时选取；复杂中断属于明确排除范围。
public sealed partial class Animator
{
    private void Advance(LayerPlayback layer, float dt)
    {
        float old = layer.currentTime;
        layer.currentTime += dt * StateSpeed(layer.current);
        if (fireEvents) Events(layer.current.motion, old, layer.currentTime, 1, layer.current.cycleOffset);
        if (layer.next != null)
        {
            float previous = layer.nextTime;
            layer.nextTime += dt * StateSpeed(layer.next);
            if (fireEvents) Events(layer.next.motion, previous, layer.nextTime, 1, layer.next.cycleOffset);
            layer.fade += Math.Abs(dt);
            if (layer.fade >= layer.fadeDuration) { layer.current = layer.next; layer.currentTime = layer.nextTime; layer.next = null; }
            return;
        }
        if (Choose(layer, layer.definition.anyTransitions, old)) return;
        Choose(layer, layer.current.transitions, old);
    }
    private bool Choose(LayerPlayback layer, AnimatorTransition[] transitions, float old)
    {
        float duration = Duration(layer.current.motion), before = old / duration, now = layer.currentTime / duration;
        foreach (var t in transitions)
        {
            var destination = FindState(layer.definition, t.destination);
            if (!t.canTransitionToSelf && destination == layer.current) continue;
            if (t.hasExitTime)
            {
                float exit = t.exitTime;
                if (exit < 1 && Loops(layer.current.motion)) exit += (float)Math.Floor(before);
                if (!(before <= exit && now >= exit)) continue;
            }
            bool passes = true;
            foreach (var c in t.conditions)
            {
                float value = GetFloat(c.parameter);
                passes &= c.mode switch { 1 => value != 0, 2 => value == 0, 3 => value > c.threshold, 4 => value < c.threshold, 6 => value == c.threshold, 7 => value != c.threshold, _ => throw new NotSupportedException("未知 Animator 条件模式：" + c.mode) };
            }
            if (!passes) continue;
            foreach (var c in t.conditions) foreach (var p in parameters) if (p.name == c.parameter && p.type == AnimatorControllerParameterType.Trigger) ResetTrigger(c.parameter);
            Transition(layer, destination, t.fixedDuration ? t.duration : t.duration * duration, t.offset);
            return true;
        }
        return false;
    }
    private void Transition(LayerPlayback layer, AnimatorState destination, float duration, float offset)
    {
        if (duration <= 0) { layer.current = destination; layer.currentTime = offset * Duration(destination.motion); layer.next = null; return; }
        layer.next = destination; layer.nextTime = offset * Duration(destination.motion); layer.fade = 0; layer.fadeDuration = duration;
    }
    private float StateSpeed(AnimatorState state) => state.speed * (string.IsNullOrEmpty(state.speedParameter) ? 1 : GetFloat(state.speedParameter));
    private float Duration(AnimationMotion motion)
    {
        if (motion == null) return 1;
        if (motion.clip != null) return Math.Max(.0001f, motion.clip.length);
        Blend(motion, out var a, out var b, out float blend);
        float da = Duration(a.motion) / Math.Max(.0001f, Math.Abs(a.timeScale));
        return b == null ? da : da * (1 - blend) + Duration(b.motion) / Math.Max(.0001f, Math.Abs(b.timeScale)) * blend;
    }
    private static bool Loops(AnimationMotion motion)
    { if (motion == null) return false; if (motion.clip != null) return motion.clip.isLooping; foreach (var c in motion.children) if (Loops(c.motion)) return true; return false; }
    //导出端按 threshold 排序，因此求值只需要相邻两个节点。
    private void Blend(AnimationMotion motion, out MotionChild a, out MotionChild b, out float blend)
    {
        if (motion.children.Length == 0) throw new InvalidOperationException("混合树没有子运动。");
        float value = GetFloat(motion.parameter); a = motion.children[0]; b = null; blend = 0;
        if (value <= a.threshold) return;
        for (int i = 1; i < motion.children.Length; i++)
        {
            b = motion.children[i];
            if (value <= b.threshold) { blend = (value - a.threshold) / (b.threshold - a.threshold); return; }
            a = b;
        }
        b = null;
    }
}
