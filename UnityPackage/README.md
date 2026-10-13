# RimUnity Exporter

在 Unity 2022.3 中通过本地 package.json 或 tgz 安装。菜单：Tools → RimUnity → 导出明文预制体。

制作使用 Unity 原生 GameObject、RectTransform、UGUI。导出器解析嵌套 Prefab 和 Variant，将最终层级、组件字段、引用及持久 UnityEvent 写入内容 Mod 的 `1.7/Data/RimUnity`。导出窗口要求目标目录含有效 `About/About.xml`。

当前已接通的范围：

- 普通 Transform，Overlay／Camera／World Canvas，CanvasGroup、嵌套画布和 CanvasScaler。
- Image 简单、九宫格、平铺与径向填充；RawImage、传统 Text、导出 SDF 图集的 TMP 中文与基础标签／描边。
- Button、Toggle（含淡入）、Slider、Scrollbar；ScrollRect 的弹性／惯性／限位，Scrollbar 绑定、焦点、导航和 EventTrigger。
- 基础 Mask、RectMask2D、LayoutElement、横向／纵向／网格布局、ContentSizeFitter、AspectRatioFitter。
- 静态三角网格、多子网格、Texture2D、Sprite、TTF／OTF／TTC 字体；Builtin Unlit/Color、Unlit/Texture、Unlit/Transparent，URP Unlit 的基础不透明／Alpha 模式。
- 自定义 MonoBehaviour 的基础字段、Unity 数学值、数组／List、可序列化数据、层级内对象引用、已支持资产引用及持久事件。
- 自有粒子模拟与批量绘制，时间／距离发射、常用形状、曲线／渐变、运动模块、图集、拖尾、Birth／Death 子发射器和 Custom1／2；部分模块参数仍未覆盖。
- 完整权重数据的 GPU 蒙皮，基础 Animator 状态／条件／Trigger／CrossFade／1D Blend Tree／层与骨骼遮罩／属性曲线／动画事件。
- 单 Pass 自定义 ShaderLab 的明文 GLSL 转换及资源引用；需要配置离线工具与游戏目录，具体限制见项目 Docs/CustomShaderConversion.md。

上述是代码实现范围，并不表示逐项通过导出或游戏验收。仍有限制：动态 TMP 字形、任意 Stencil 运算、自定义材质与 UI Mask 的组合、世界画布自定义材质、完整粒子顶点流、嵌套 Animator 状态机、场景实例及子树再次克隆。输入框／IME、光照、Humanoid、IK、物理和多 Pass 不在首阶段范围。

内置 CanvasRenderer 和默认 StandaloneInputModule 由桥接绘制与指针调度器承担，不在游戏中创建空实现组件。自定义脚本必须在游戏端改引用后重新编译。UGUI 指针处理接口使用 `RimUnity.EventSystems`。

失败批次保留诊断文件，catalog 仅在整个批次成功后替换。成功清单对应的旧文件不被删除；发布工具仅打包当前清单可达资源。

批量入口：`RimUnity.Editor.PrefabExporter.Export(modDirectory, ExportRequest[])`。

插件只包含制作数据类型和导出工具，不附带样例资源或测试场景生成器。资源应导出到使用方的内容模组。

源码与第三方许可见模组的 Docs/ThirdParty.md。发布包不包含游戏、Unity、Harmony 或 Slang 原生库。
