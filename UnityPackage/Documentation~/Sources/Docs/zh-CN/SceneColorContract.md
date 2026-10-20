# 地图颜色契约

原生地图 Shader 输出显示颜色数值，原生 `PostFxComposite.frag.glsl` 对场景采样后直接做饱和度处理。RimUnity 在接管地图时必须保留这套数值语义。

不要用 `VkFormat` 猜测内容是显示颜色还是线性颜色。sRGB 附件的写入编码与后续采样解码是一对存储转换；浮点或 10 位格式本身也不赋予数据线性语义。

- `HdrDrawTarget.Begin`：保留的原生场景按显示颜色进入；显式清零的 Bloom 光缓冲从线性零开始。
- 原生绘制段在显示颜色空间混合，声明为 Linear 的 Shader 使用独立线性程序；跨段时转换 RGB，保持 Alpha。
- `HdrDrawTarget.Result` 明确是线性 HDR。
- `WriteScene` 将线性 HDR 编码为原生场景预期的显示颜色数值，再交由附件完成自己的存储转换。
- 没有实际 Bloom 和屏幕特效时沿用原生最终合成。
- 有 Bloom 时在线性空间加光，最终显示编码一次；ScreenEfx 的输入语义由是否传入明确的线性 HDR 图像决定。

这些规则适用于所有遵守导出颜色空间声明的资源，不包含资源名白名单或特判。新 Shader 仍需正确声明颜色空间，且应避免输出无穷或 NaN。

验证入口：`Tools/SceneColorTests` 的隐藏窗口 Vulkan 像素回归，以及 `Tools/ScreenEfxTests` 的帧与生命周期回归。前者覆盖 8 位、sRGB、10 位及浮点附件，并包含旧逻辑导致额外编码的负例。

规范依据：[Vulkan 图像格式的 Texel Decode/Encode](https://docs.vulkan.org/spec/latest/chapters/images.html)。GPU 回归不替代地图透明排序、读档、窗口缩放和其他显卡驱动的实机验收。
