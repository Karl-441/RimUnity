# 颜色、透明混合与 Bloom

## 颜色与材质

导出器读取 Unity 工程的 Gamma／Linear 设置，并保留材质颜色、Alpha、纹理、混合模式和绘制状态。制作时将噪声、遮罩等数值贴图设为 Linear，将颜色贴图按实际用途设置为 sRGB。

脚本使用 `Material.SetTexture` 更换纹理时会同步颜色编码。通过 `MaterialPropertyBlock` 换用颜色编码不同的纹理时，需要同时设置 `_RimUnitySrgb<纹理属性名>`：sRGB 为 1，数值纹理为 0。

向颜色属性调用 `SetVector` 时仍应提供导出色值；该调用不用于绕过颜色空间转换。

## 启用 Bloom

在“选项 → 模组设置 → RimUnity”中启用 Bloom，勾选参与的 Layer（0～31），再调整阈值、强度、柔和度与扩散。

默认关闭且不选层，阈值为 0.8，强度为 0.6。未选层的物体不会成为泛光源，光晕仍会扩散到周围像素。过高强度会使画面发白。普通地表通常使用 0 层，不建议勾选。材质颜色与透明度不受 Bloom 开关控制。

## 设置 Layer

在 Unity 中为特效对象及需要泛光的子节点设置专用 Layer，再正常导出。运行时可设置 `gameObject.layer`；修改根对象不会自动改变子节点。

Layer 使用编号，不按名称匹配。不同 Unity 工程和内容模组应协调编号。

## 接入内容模组设置

在地图初始化和设置变化时，从游戏主线程调用：

```csharp
RimUnity.RimUnityMod.Bloom.SetLayerEnabled(29, myBloomEnabled);
```

29 仅为示例。该调用使内容模组的开关控制对应层，优先于 RimUnity 的手动选择。关闭时，该层也不会被手动勾选重新开启。

结束游戏生命周期时撤销控制：

```csharp
RimUnity.RimUnityMod.Bloom.ReleaseLayer(29);
```

同一层应由一个内容模组负责。阈值、强度等参数共用 RimUnity 设置。层控制状态不写入 RimUnity 配置，内容模组应在读档或新游戏时同步自己的保存值。

## 使用限制

- Bloom 适用于地图主通道中的粒子、拖尾和模型；UI、世界地图及离屏相机不在该范围内。
- 不支持 Unity Camera／Volume 完整后处理、曝光、色调映射和镜头污渍。
- 地图透明混合与 Unity Linear 模式存在差异；Bloom 不会将整个游戏画面变成 HDR 管线。
- 泛光沿用地图深度遮挡和材质深度测试。依赖中途 Stencil 写入或特殊多 Pass 顺序的材质可能有显示差异。
