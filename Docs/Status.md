# 当前能力与限制

制作基线为 Unity 2022.3 LTS，支持 Built-in 与 URP 14 的已适配部分。运行基线为 Windows x64、RimWorld 1.7 Waylith、.NET 10；宿主更新后需要重新核对接口。

## 已有实现

- 明文清单、Prefab 层级、资源依赖、模板加载与克隆、脚本字段／事件引用。
- 常用 UGUI 控件、布局、指针事件、传统 Text、TMP 导出 SDF 图集及基础裁切。
- 粒子模拟与批量绘制、曲线／渐变、发射、形状、运动模块、图集、子发射器、拖尾、自定义数据、力场。
- 桥接碰撞器、Trigger、简化刚体与地图格子粒子照明，详见 [粒子能力](ParticleExpansion.md)。
- 静态／骨骼网格、基础 Animator 状态机与 1D 混合、属性动画、动画事件、Legacy Animation、LineRenderer／TrailRenderer。
- 内置材质映射、自定义 Shader 转换、Gamma／Linear 输入输出转换、按 Layer 筛选的地图 Bloom。内容模组可用自己的设置接管对应层。

框架不包含局内测试入口、背景预览窗口、测试资源或 Unity 样例生成器。使用方自行制作和验证内容。

## 主要限制

- 不是 Unity 全 API 兼容层。自定义脚本需要调整引用并重新编译；复杂序列化、第三方 DLL 自动适配不受支持。
- TMP 不支持完整动态字形和复杂排版；基础 Mask 不等同任意 Stencil 运算。自定义 UI 材质与裁切组合需实际验证。
- 粒子随机轨迹、缩放、部分顶点流及渲染参数仍与 Unity 有差异；不存在完整原生粒子 Jobs／GPU Instancing 性能路径。
- 物理不是 PhysX／Box2D，游戏建筑与角色不会自动变成碰撞体。地图 Lights 不等同三维实时光照与阴影。
- Animator 不覆盖 Humanoid、IK、完整嵌套状态机和复杂过渡中断。
- Shader Graph 和 Live2D 未实现。复杂光照及渲染管线宏存在转换限制；不支持的输入应明确报错。
- 宿主主画面仍使用自身颜色及透明混合管线；Layer Bloom 不等同完整 Unity 后处理栈，详见 [颜色与 Bloom](ColorAndBloom.md)。

编译通过、资源导出成功与游戏视觉一致是不同结论。各功能尚未完成全面游戏对照；没有完整 CPU／GPU、GC、内存及加载性能数据，不承诺与 Unity 等效的性能或视觉。
