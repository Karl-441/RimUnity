using System;

namespace RimUnity.UI.Runtime;

//收集时共享祖先遮罩与射线规则，只有遇到新遮罩才创建一组成员数组。
internal static partial class UguiRuntime
{
    private readonly struct CollectContext
    {
        internal readonly Mask[] Masks;
        internal readonly RectMask2D[] RectMasks;
        internal readonly bool GroupsAllowRaycast;

        //数组只在收集阶段创建，后续节点和绘制阶段只读共享。
        private CollectContext(Mask[] masks, RectMask2D[] rectMasks, bool groupsAllowRaycast)
        {
            Masks = masks; RectMasks = rectMasks; GroupsAllowRaycast = groupsAllowRaycast;
        }

        //根画布外也可以存在遮罩与 CanvasGroup，保持原来的祖先查询范围。
        internal static CollectContext FromAncestors(Transform node)
        {
            if (node == null) return new(Array.Empty<Mask>(), Array.Empty<RectMask2D>(), true);
            return FromAncestors(node.parent).WithGroup(node.GetComponent<CanvasGroup>())
                .WithMasks(node.GetComponent<Mask>(), node.GetComponent<RectMask2D>());
        }

        //忽略父组时只保留当前组的限制，后续子组仍可以重新禁止命中。
        internal CollectContext WithGroup(CanvasGroup group)
            => group == null || !group.enabled ? this :
                new(Masks, RectMasks, group.blocksRaycasts && (group.ignoreParentGroups || GroupsAllowRaycast));

        //当前节点图形不受自身遮罩影响，追加后的上下文仅传给子节点。
        internal CollectContext WithMasks(Mask mask, RectMask2D rectangle)
            => new(mask != null && mask.MaskEnabled() ? Prepend(mask, Masks) : Masks,
                rectangle != null && rectangle.isActiveAndEnabled ? Prepend(rectangle, RectMasks) : RectMasks, GroupsAllowRaycast);

        //最近的遮罩排在前面，与原来的向根扫描顺序一致。
        private static T[] Prepend<T>(T value, T[] ancestors)
        {
            T[] result = new T[ancestors.Length + 1];
            result[0] = value;
            Array.Copy(ancestors, 0, result, 1, ancestors.Length);
            return result;
        }
    }

    //记录层级顺序与共享遮罩，后续不再逐图形扫描祖先和分配数组。
    private static Entry CreateEntry(Canvas canvas, RectTransform rect, Graphic graphic, float alpha, bool raycast, Rect clip, CollectContext context)
        => new(canvas, rect, graphic, alpha, raycast, clip)
        { Order = Entries.Count, Masks = context.Masks, RectMasks = context.RectMasks };
}
