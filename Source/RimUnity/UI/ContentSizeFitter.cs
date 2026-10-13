namespace RimUnity.UI;

//按最小或首选尺寸调整自身，同时支持直接调用与统一布局阶段。
public class ContentSizeFitter : UnityEngine.UI.ContentSizeFitter, UnityEngine.UI.ILayoutSelfController
{
    //根据横向模式更新宽度。
    public new void SetLayoutHorizontal() => Fit(0, horizontalFit);

    //根据纵向模式更新高度。
    public new void SetLayoutVertical() => Fit(1, verticalFit);

    //未约束的轴保留调用方设置的尺寸。
    private void Fit(int axis, FitMode mode)
    {
        if (mode == FitMode.Unconstrained) return;
        RectTransform rect = (RectTransform)transform;
        float size = mode == FitMode.MinSize ? Runtime.UguiLayout.GetMinSize(rect, axis) : Runtime.UguiLayout.GetPreferredSize(rect, axis);
        rect.SetSizeWithCurrentAnchors((RectTransform.Axis)axis, size);
    }
}
