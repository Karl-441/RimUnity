//源自 ChezhouLib UGUI；迁移来源与第三方许可见 Docs/ThirdParty.md。
using UnityEngine;
using UnityEngine.UI;
namespace RimUnity.UI;

//沿用 Text 内容、字体和颜色属性，补充矩形、文字边距与换行设置。
public class Text : UnityEngine.UI.Text, ILayoutElement
{
    public new RectTransform rectTransform => (RectTransform)transform;
    public bool wordWrap { get; set; } = true;
    public bool clipText { get; set; } = true;
    //边距顺序为左、上、右、下；负边距会扩大排版区域。
    public Vector4 margin { get; set; }
    public bool resizeTextForBestFit { get; set; }
    public int resizeTextMinSize { get; set; } = 1;
    public int resizeTextMaxSize { get; set; } = 40;
    public HorizontalWrapMode horizontalOverflow { get => wordWrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow; set => wordWrap = value == HorizontalWrapMode.Wrap; }
    public VerticalWrapMode verticalOverflow { get => clipText ? VerticalWrapMode.Truncate : VerticalWrapMode.Overflow; set => clipText = value == VerticalWrapMode.Truncate; }
    public float minWidth => 0f;
    public float minHeight => 0f;
    public float flexibleWidth => -1f;
    public float flexibleHeight => -1f;
    public int layoutPriority => 0;
    public float preferredWidth => Runtime.UguiTextRenderer.Measure(this, -1f, false).x + margin.x + margin.z;
    public float preferredHeight => Runtime.UguiTextRenderer.Measure(this, Mathf.Max(1f, ContentRect.width), wordWrap).y + margin.y + margin.w;
    //文字尺寸按当前内容和字体即时查询。
    public void CalculateLayoutInputHorizontal() { }
    //纵向测量沿用横向布局确定的内容宽度。
    public void CalculateLayoutInputVertical() { }

    //局部坐标从下向上增长，下边距改变底边，上边距改变顶边。
    internal Rect ContentRect
    {
        get
        {
            Rect r = rectTransform.rect;
            return Rect.MinMaxRect(r.xMin + margin.x, r.yMin + margin.w,
                r.xMax - margin.z, r.yMax - margin.y);
        }
    }
}
