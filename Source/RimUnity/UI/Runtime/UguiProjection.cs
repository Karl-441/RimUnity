using Verse;

namespace RimUnity.UI.Runtime;

//一次图形收集共用画布投影，避免每个角点重复查找画布和求相机逆矩阵。
internal readonly struct UguiProjection
{
    private readonly bool overlay;
    private readonly float canvasScale, guiScale, screenHeight;
    private readonly Matrix4x4 viewProjection;
    private readonly Rect viewport;

    //在当前画布准备完成后读取投影，下一次收集重新读取相机和屏幕状态。
    internal UguiProjection(Canvas canvas)
    {
        Canvas root = canvas.rootCanvas;
        overlay = root.renderMode == RenderMode.ScreenSpaceOverlay;
        canvasScale = canvas.scaleFactor;
        guiScale = Prefs.UIScale;
        screenHeight = Screen.height;
        if (overlay)
        {
            viewProjection = Matrix4x4.identity;
            viewport = default;
        }
        else
        {
            Camera camera = root.worldCamera != null ? root.worldCamera : Camera.main;
            viewProjection = camera.projectionMatrix * camera.worldToCameraMatrix;
            viewport = camera.pixelRect;
        }
    }

    //四角使用同一世界矩阵，保留旋转、缩放和透视画布的包围矩形语义。
    internal Rect Bounds(RectTransform transform, Rect rect)
    {
        Matrix4x4 matrix = transform.localToWorldMatrix;
        Vector2 a = ToGui(matrix.MultiplyPoint3x4(new Vector3(rect.xMin, rect.yMin)));
        Vector2 b = ToGui(matrix.MultiplyPoint3x4(new Vector3(rect.xMax, rect.yMax)));
        Vector2 c = ToGui(matrix.MultiplyPoint3x4(new Vector3(rect.xMin, rect.yMax)));
        Vector2 d = ToGui(matrix.MultiplyPoint3x4(new Vector3(rect.xMax, rect.yMin)));
        return Rect.MinMaxRect(Mathf.Min(Mathf.Min(a.x, b.x), Mathf.Min(c.x, d.x)), Mathf.Min(Mathf.Min(a.y, b.y), Mathf.Min(c.y, d.y)),
            Mathf.Max(Mathf.Max(a.x, b.x), Mathf.Max(c.x, d.x)), Mathf.Max(Mathf.Max(a.y, b.y), Mathf.Max(c.y, d.y)));
    }

    //与 Camera.WorldToScreenPoint 保持相同的透视除法和 GUI 原点转换。
    internal Vector2 ToGui(Vector3 point)
    {
        if (overlay) return new Vector2(point.x * canvasScale, screenHeight - point.y * canvasScale) / guiScale;
        Vector4 clip = viewProjection * new Vector4(point.x, point.y, point.z, 1);
        if (Mathf.Abs(clip.w) < 1e-7f) return new Vector2(0, screenHeight) / guiScale;
        float x = viewport.x + (clip.x / clip.w + 1) * .5f * viewport.width;
        float y = viewport.y + (1 - clip.y / clip.w) * .5f * viewport.height;
        return new Vector2(x, screenHeight - y) / guiScale;
    }
}
