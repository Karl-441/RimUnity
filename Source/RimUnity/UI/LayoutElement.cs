//源自 ChezhouLib UGUI；迁移来源与第三方许可见 Docs/ThirdParty.md。
using UnityEngine;
namespace RimUnity.UI;

//声明子节点参与自动布局时的尺寸与排除规则。
public class LayoutElement : MonoBehaviour, UnityEngine.UI.ILayoutElement
{
    public bool ignoreLayout { get; set; }
    public float minWidth { get; set; } = -1f;
    public float minHeight { get; set; } = -1f;
    public float preferredWidth { get; set; } = -1f;
    public float preferredHeight { get; set; } = -1f;
    public float flexibleWidth { get; set; } = -1f;
    public float flexibleHeight { get; set; } = -1f;
    public int layoutPriority { get; set; } = 1;

    //显式尺寸不需要额外的横向测量。
    public void CalculateLayoutInputHorizontal() { }
    //显式尺寸不需要额外的纵向测量。
    public void CalculateLayoutInputVertical() { }
}
