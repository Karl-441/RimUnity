//源自 ChezhouLib UGUI；迁移来源与第三方许可见 Docs/ThirdParty.md。
using UnityEngine;

using RimUnity.UI;
namespace RimUnity;

//统一控制子树透明度与鼠标命中。
public class CanvasGroup : MonoBehaviour
{
    public float alpha { get; set; } = 1f;
    public bool interactable { get; set; } = true;
    public bool blocksRaycasts { get; set; } = true;
    public bool ignoreParentGroups { get; set; }
}
