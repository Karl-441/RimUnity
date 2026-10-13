using UnityEngine;

namespace RimUnity.UI;

//Waylith GUI 子通道没有 Stencil 附件，使用同等覆盖率测试实现基础 Mask。
public sealed class Mask : MonoBehaviour
{
    public bool showMaskGraphic { get; set; } = true;
    public new RectTransform transform => (RectTransform)base.transform;
    public UnityEngine.UI.Graphic graphic => GetComponent<UnityEngine.UI.Graphic>();
    public bool MaskEnabled() => isActiveAndEnabled && graphic != null && graphic.isActiveAndEnabled;
}
