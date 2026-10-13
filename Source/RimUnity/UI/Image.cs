//源自 ChezhouLib UGUI；迁移来源与第三方许可见 Docs/ThirdParty.md。
namespace RimUnity.UI;

//沿用引擎的 Image 属性，同时暴露具有完整尺寸属性的矩形。
public class Image : UnityEngine.UI.Image
{
    public new RectTransform rectTransform => (RectTransform)transform;

    //按精灵像素与画布参考比例设置原生尺寸。
    public new void SetNativeSize()
    {
        UnityEngine.Sprite s = overrideSprite != null ? overrideSprite : sprite;
        if (s == null) return;
        Canvas canvas = this.GetComponentInParent<Canvas>();
        float ratio = s.pixelsPerUnit / (canvas != null ? canvas.referencePixelsPerUnit : 100f);
        rectTransform.anchorMax = rectTransform.anchorMin;
        rectTransform.sizeDelta = s.rect.size / ratio;
    }
}
