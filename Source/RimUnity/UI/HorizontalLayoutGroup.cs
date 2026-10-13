//源自 ChezhouLib UGUI；迁移来源与第三方许可见 Docs/ThirdParty.md。
namespace RimUnity.UI;

//复用线性布局参数，沿水平方向放置子节点。
public class HorizontalLayoutGroup : VerticalLayoutGroup
{
    internal override bool Horizontal => true;
}
