namespace RimUnity.UI;

//向模组公开与自动布局相同的尺寸查询规则。
public static class LayoutUtility
{
    //读取指定轴的最小尺寸。
    public static float GetMinSize(RectTransform rect, int axis) => Runtime.UguiLayout.GetMinSize(rect, axis);
    //读取指定轴不低于最小值的首选尺寸。
    public static float GetPreferredSize(RectTransform rect, int axis) => Runtime.UguiLayout.GetPreferredSize(rect, axis);
    //读取指定轴的弹性权重。
    public static float GetFlexibleSize(RectTransform rect, int axis) => Runtime.UguiLayout.GetFlexibleSize(rect, axis);
    //读取最小宽度。
    public static float GetMinWidth(RectTransform rect) => GetMinSize(rect, 0);
    //读取最小高度。
    public static float GetMinHeight(RectTransform rect) => GetMinSize(rect, 1);
    //读取首选宽度。
    public static float GetPreferredWidth(RectTransform rect) => GetPreferredSize(rect, 0);
    //读取首选高度。
    public static float GetPreferredHeight(RectTransform rect) => GetPreferredSize(rect, 1);
    //读取横向弹性权重。
    public static float GetFlexibleWidth(RectTransform rect) => GetFlexibleSize(rect, 0);
    //读取纵向弹性权重。
    public static float GetFlexibleHeight(RectTransform rect) => GetFlexibleSize(rect, 1);
}
