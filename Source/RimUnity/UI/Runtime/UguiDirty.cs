using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace RimUnity.UI.Runtime;

//按画布缓存布局，只在尺寸依赖变化时重建。
internal static class UguiDirty
{
    private sealed class State
    {
        internal bool Dirty = true;
        internal readonly List<Func<bool>> Changed = new();
    }
    private sealed record PendingDestroy(UnityEngine.Object Target, State[] States);
    private static readonly ConditionalWeakTable<RectTransform, State> States = new();
    private static readonly List<PendingDestroy> Destroyed = new();
    internal static int RebuildDepth { get; private set; }

    //由画布 Prepare 调用；重复 GUI 事件不重复执行布局。
    internal static void EnsureLayout(RectTransform root)
    {
        CheckDestroyed();
        State state = States.GetValue(root, _ => new State());
        if (!state.Dirty)
            foreach (Func<bool> changed in state.Changed)
                if (changed()) { state.Dirty = true; break; }
        if (state.Dirty) Force(root);
    }

    //显式刷新仍然立即执行，并更新缓存基线。
    internal static void Force(RectTransform root)
    {
        Mark(root);
        State state = States.GetValue(root, _ => new State());
        RebuildDepth++;
        try
        {
            UguiLayout.Rebuild(root);
            state.Changed.Clear();
            CaptureMutable(root, state.Changed);
            state.Dirty = false;
        }
        finally { RebuildDepth--; }
    }

    //当前矩形和所有已缓存祖先都可能依赖该布局尺寸。
    internal static void Mark(Transform transform)
    {
        if (RebuildDepth > 0) return;
        for (Transform node = transform; node != null; node = node.parent)
            if (node is RectTransform rect && States.TryGetValue(rect, out State state)) state.Dirty = true;
    }

    //延迟销毁完成后再次标脏，避免销毁请求和真实移除之间提前清掉缓存。
    internal static void TrackDestroy(UnityEngine.Object target, Transform transform)
    {
        List<State> affected = new();
        for (Transform node = transform; node != null; node = node.parent)
            if (node is RectTransform rect && States.TryGetValue(rect, out State state))
            { state.Dirty = true; affected.Add(state); }
        if (affected.Count > 0) Destroyed.Add(new PendingDestroy(target, affected.ToArray()));
    }

    //只检查仍待完成的销毁对象，不扫描整个 UI 层级。
    private static void CheckDestroyed()
    {
        for (int i = Destroyed.Count - 1; i >= 0; i--)
            if (Destroyed[i].Target == null)
            {
                foreach (State state in Destroyed[i].States) state.Dirty = true;
                Destroyed.RemoveAt(i);
            }
    }

    //重建后登记会绕过 setter 的边距和公开字段，普通节点无需逐事件遍历。
    private static void CaptureMutable(RectTransform root, List<Func<bool>> checks)
    {
        foreach (Component component in root.GetComponents<Component>())
        {
            if (component is UnityEngine.UI.LayoutGroup group)
            {
                RectOffset padding = group.padding;
                (int, int, int, int) value = (padding.left, padding.right, padding.top, padding.bottom);
                checks.Add(() => group == null || !ReferenceEquals(group.padding, padding) ||
                    value != (padding.left, padding.right, padding.top, padding.bottom));
            }
            if (component is ScrollRect scroll)
            {
                var value = (scroll.content, scroll.viewport, scroll.horizontal, scroll.vertical);
                checks.Add(() => scroll == null || value != (scroll.content, scroll.viewport, scroll.horizontal, scroll.vertical));
            }
        }
        foreach (RectTransform child in UguiLayout.Children(root)) CaptureMutable(child, checks);
    }
}
