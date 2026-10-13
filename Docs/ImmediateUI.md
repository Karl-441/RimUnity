# IMGUI 自定义材质

独立材质导出时设置 `ExportRequest.ui = true`，生成宿主 GUI 使用的顶点程序。名称、参数、纹理和混合状态随材质写入清单，不需要手写 ShaderDef。

```csharp
PrefabExporter.Export(modDirectory, new ExportRequest {
    asset = material,
    ui = true,
    path = "Bundles/example/Assets/UI/Flow.mat"
});
```

矩形图片可继续使用游戏的 `Graphics.DrawTexture` 材质重载。任意四边形使用 `RimUnity.UI.ImmediateMesh.DrawQuad`，传入当前 GUI 分组内的四角坐标、各角 UV 和顶点色。该入口复用 RimUnity 的 UI 材质提交，逐次快照参数，服从当前 GUI 裁切栈；仅在 Repaint 提交。

Waylith 的 `GL.End` 普通图元入口不会执行自定义材质，`GL.Viewport` 也没有实现，因此不能只用 `Material.SetPass` 接通旧 GL 界面代码。

Overlay 材质不使用场景深度。导出器识别 Unity 的 `unity_GUIZTestMode` 为 Always；不写深度的 LEqual 材质转换为 Always 并记录近似说明。深度写入及不支持的比较模式仍拒绝导出。

涅瓦莲研究连线使用该公共入口，保留原连续 UV、转角、波包、颜色及高亮参数。游戏内缩放、裁切和最终画面仍需实测。
