# 颜色、透明混合与 Bloom

## 当前接线

- 导出器记录 Unity 工程实际的 `Gamma`／`Linear` 工作空间。自定义 Shader 的变体 ID 包含工作空间；`UNITY_COLORSPACE_GAMMA` 按实际工程设置参与预处理。
- Linear 程序在读取颜色属性和 sRGB 贴图时解码，在写入游戏显示颜色缓冲前编码。Alpha、UV、普通 Vector 和标为 Linear 的数值贴图不做颜色解码。自定义 Shader、内置 Unlit 和标准粒子共用颜色转换代码。
- 材质调用 `SetTexture` 时同步对应贴图的 sRGB 标记，换成噪声、遮罩等数值贴图时不会继续沿用颜色纹理的解码方式。
- Blend、ZWrite、Cull、队列和自定义 Shader 中的 Alpha 运算继续取自原材质。不能因为使用 `One / OneMinusSrcAlpha` 就再次强制预乘 RGB：例如 Unity Standard Unlit 的相应模式本来就保留独立 RGB。Cutout 的自定义阈值现在随材质导出。
- 地图 Bloom 按对象的 Unity Layer 筛选地图绘制。选中层使用原材质、顶点流与实例数据单独绘制到浮点缓冲，复用地图深度遮挡且不改写深度／模板，再进行 GPU 亮部提取、五级降采样、柔化与合成。合成在地图之后、GUI 之前；每个在途帧持有独立缓冲。

## 使用

在“选项 → 模组设置 → RimUnity”中启用 Bloom 并勾选参与的 Layer（0～31），再调整阈值、强度、柔和度与扩散。默认关闭且不选层，阈值 0.8、强度 0.6。未选层的地图内容不会成为泛光源；光晕仍会自然扩散到旁边的像素，过高强度仍会发白。普通地表通常使用 0 层，不建议勾选。颜色与透明度接线不依赖 Bloom 开关。

Unity 中直接设置特效对象及其子节点的 Layer 后正常导出，编号随预制体保留。运行时修改 `gameObject.layer` 会反映到下一帧的粒子、拖尾和模型绘制；根对象的层不会自动改变子节点。自定义层名称不覆盖游戏全局 LayerMask 名称表；不同 Unity 工程应自行协调编号。

内容模组可在主线程调用 `RimUnityMod.Bloom.SetLayerEnabled(layer, enabled)`，让自己的设置直接控制指定层，不依赖 RimUnity 的手动开关；关闭时也会禁止该层，不会被手动勾选重新开启。结束游戏生命周期时调用 `ReleaseLayer(layer)` 撤销接管。同一层由一个内容模组负责，阈值和强度等参数仍共用 RimUnity 设置。这些接管状态不写入 RimUnity 配置，由内容模组在读档／新游戏时重新同步。

涅瓦莲已将原有 Bloom 开关接到第 29 层（`NivarianBloom`）：沿用原有默认开启和保存值，在地图中即时生效，不需要在 RimUnity 再开启或勾选 Layer 29。第 29 层仅由涅瓦莲指定，框架没有硬编码该项目。

更新插件和离线 ShaderTool 后，正常重新导出即可使用颜色空间接线。已有资源不会仅靠更换 DLL 自动获得新的自定义 Shader 程序。

`Tools/Upgrade-LinearExports.ps1 -DataRoot <内容模组的 Data/RimUnity>` 用于本轮旧资源升级：调用前必须确认旧程序来自 Linear 工程、已经选择 Linear 源程序分支。它只补 GPU 输入／输出颜色边界，不能把已编译的 Gamma 分支改写成 Linear。所有不同程序通过编译和参数布局检查后才发布，并保存原文件。

## 仍有差异

- 当前 RimWorld 场景使用 UNorm 显示颜色缓冲。透明物体相互叠加仍沿用宿主的 Gamma 混合；这里没有改写整个游戏的混合管线，不能承诺与 Unity Linear 混合逐像素相同。
- 选中层在独立浮点附件重新绘制，Bloom 可保留原 Shader 输出的超亮值；游戏主画面仍使用原场景格式，这不是完整 HDR 渲染管线。尚未接入 Unity 的曝光、色调映射、镜头污渍等后处理效果。
- 当前为地图 Layer Bloom，不是完整 Unity Camera／Volume 后处理栈。筛选对象必须原本提交给地图主通道；UI、世界地图、离屏相机不在该通道中。共享的是最终地图深度／模板，依赖中途模板写入和特殊多 Pass 顺序的材质不能保证同等效果。透明对象沿用原本的深度测试行为。
- 颜色属性通过 Shader 读取边界转换。脚本向 Color 属性调用 `SetVector` 时仍应提供导出色值；Unity 中利用 `SetVector` 绕过颜色转换的特殊用法尚未单独区分。通过 MaterialPropertyBlock 更换颜色编码不同的纹理时，需要同时设置 `_RimUnitySrgb<纹理属性名>`（sRGB 为 1，数值纹理为 0）。
- 当前转换器支持使用全局纹理的采样和单个 sampler2D 参数的帮助函数；复杂多参数纹理函数会明确报错，不会悄悄漏解码。

运行时、UPM 和离线工具已通过编译，Bloom 及本次转换程序已通过宿主 Shader 编译和布局核对。游戏画面、驱动实际执行与性能仍需用户启动游戏验收；编译通过不代表视觉效果已经验收。
