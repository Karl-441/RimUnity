namespace RimUnity;

//按视口比例限制粒子面片尺寸，不改变模拟尺寸、网格粒子或拖尾宽度。
public sealed partial class ParticleSystemRenderer
{
    public float minParticleSize;
    public float maxParticleSize = .5f;

    //沿两个面片轴计算屏幕覆盖范围，统一缩放以保留长宽比和旋转。
    private void ClampBillboardSize(Vector3 center, Camera camera, ref Vector3 x, ref Vector3 y)
    {
        Vector2 horizontal, vertical;
        if (uiEntry != null)
        {
            var canvas = uiEntry.Canvas;
            horizontal = RectTransformUtility.ToGui(canvas, center + x) - RectTransformUtility.ToGui(canvas, center - x);
            vertical = RectTransformUtility.ToGui(canvas, center + y) - RectTransformUtility.ToGui(canvas, center - y);
            horizontal = new Vector2(horizontal.x / Verse.UI.screenWidth, horizontal.y / Verse.UI.screenHeight);
            vertical = new Vector2(vertical.x / Verse.UI.screenWidth, vertical.y / Verse.UI.screenHeight);
        }
        else
        {
            if (camera == null) return;
            Vector3 h = camera.WorldToScreenPoint(center + x) - camera.WorldToScreenPoint(center - x);
            Vector3 v = camera.WorldToScreenPoint(center + y) - camera.WorldToScreenPoint(center - y);
            horizontal = new Vector2(h.x / camera.pixelWidth, h.y / camera.pixelHeight);
            vertical = new Vector2(v.x / camera.pixelWidth, v.y / camera.pixelHeight);
        }
        float extent = Mathf.Max(Mathf.Abs(horizontal.x) + Mathf.Abs(vertical.x), Mathf.Abs(horizontal.y) + Mathf.Abs(vertical.y));
        if (extent <= 0) return;
        float maximum = Mathf.Max(0, maxParticleSize);
        float minimum = Mathf.Clamp(minParticleSize, 0, maximum);
        float scale = Mathf.Clamp(extent, minimum, maximum) / extent;
        x *= scale;
        y *= scale;
    }
}
