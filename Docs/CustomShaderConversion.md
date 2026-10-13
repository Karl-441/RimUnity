# 自定义单 Pass Shader 转换

离线工具使用目标 RimWorld 1.7 目录中的 Slang 和 Waylith 编译器，不运行游戏或 Unity 场景。源顶点／片元函数保留，只适配资源绑定、已支持的 Unity 宏和坐标约定。

```powershell
& .\Tools\bin\ShaderTool\RimUnity.ShaderTool.exe '<RimWorld目录>' shaderlab '<源文件.shader>' '<输出目录>' world 'KEYWORD_A,KEYWORD_B'
```

`world` 与 `ui` 必须显式选择。世界配置使用 set 0 / binding 0 的 ViewProj 和对象模型 push constant；UI 配置使用 set 0 / binding 1 的 GUI Frame.viewProj，输入已是屏幕坐标。UI 配置不提供对象模型矩阵。

## 输出与运行时约定

- `shader.json`：版本、原 Shader 名称、profile、入口、GLSL 相对文件名、Properties、tags、states、选定 keywords 和 keywordGroups。
- `shader.vert.glsl`、`shader.frag.glsl`：游戏编译器已接受的明文程序。片元文件包含两阶段完整材质布局；顶点使用的纹理有 `@vertex` 注解。
- `shader.hlsl`：适配后的中间程序，用于诊断。失败时不生成成功描述文件。

set 2 / binding 0 是统一 std140 材质块；其余绑定是原名称的 sampler2D，绑定号可能不连续。Slang 临时更名不会泄漏成材质属性名。`_ST` 缓冲区采用引擎 offset.xy / scale.zw；适配表达式转回 Unity scale.xy / offset.zw，不能在导出器再交换一次。采样包装统一翻转 V，UV 动画仍按源方向计算。

运行时用 `ShaderMetadataParser.ExtractMaterialLayout(fragmentSource, key, 2)` 获取材质布局，用 `ExtractVertexInputLayout(vertexSource, key)` 获取顶点输入。`ValidateStageInterface` 返回 true 表示发现错误。状态需要由运行时转换成宿主 PipelineState；本工具只解析与保留状态，不隐式选择近似状态。

`_Time`、`_SinTime`、`_CosTime`、`unity_DeltaTime` 是无本地默认值的标准名称材质 uniform，依赖运行时更新 Shader 全局向量。帧块首个 vec4 是 SunDirection，不能用于时间。

## ShaderLab 范围

只接受一个 SubShader、一个 Pass 和一个显式顶点片元程序。支持 Float、Range、Int／Integer、Color、Vector、贴图属性元数据，以及 Blend、BlendOp、Cull、ZTest、ZWrite、ZClip、AlphaToMask、ColorMask、Offset、Stencil 和 Tags。绘制状态中的 `[属性名]` 保留给材质导出器按实际值解析。动态状态解析结果必须参与运行时 Shader key，避免两个材质共享错误管线。

Blend 保存两个或四个因子，例如 `SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha`；BlendOp 保存一个或两个操作；Offset 保存 `factor, units`；Stencil 字段保存为 `Stencil.Ref`、`Stencil.Comp` 等键。缺省状态遵循 ShaderLab 默认，Pass 状态覆盖 SubShader。不存在宿主映射的状态应在导出时明确失败。

支持 shader_feature／multi_compile 的普通、local 与 fragment 关键字组；同一组最多选一个关键字。支持相对路径本地 include、UnityCG／UnityShaderVariables／UnityUI，以及 URP Core、Core Common／SpaceTransforms 的有限兼容入口。包含文件的其余功能必须属于已实现函数，否则编译器明确报错。

已适配 UnityObjectToClipPos、TransformObjectToHClip、世界配置的 TransformObjectToWorld／TransformWorldToHClip、TRANSFORM_TEX、UnityGet2DClipping、基础 appdata 结构，以及 fixed／half 类型。支持 sampler2D 的 tex2D／lod／bias／proj／grad；URP 支持 TEXTURE2D、SAMPLER、SAMPLE_TEXTURE2D／LOD／BIAS，采样器采用 `sampler_贴图名` 命名并与该纹理配对。

不接受多 Pass、Surface Shader、ShaderGraph、UsePass／GrabPass、实例化／雾／光照宏、非 UnityPerMaterial 自定义 cbuffer、材质数组、非二维纹理采样、跨纹理共享采样器、Texture2D 直接 Sample／Load／Gather 调用及未实现的 Unity API。世界配置的对象／相机顶点块不允许在片元阶段访问，需要顶点 varying 传递结果。拒绝这些输入时不会自动替换成 Unlit。

## Unity 导出接入

编辑器保存 `RimUnity.ShaderTool`（exe）和 `RimUnity.GameRoot`（游戏目录）两个 EditorPrefs。`CustomShaderWriter.Write(material, stagingDirectory, ui, diagnosticPath)` 返回 `Id`、`Directory` 和 `Manifest`；另有显式工具路径重载。源依赖哈希、绘制配置和关键字参与 Id。调用方负责在全批次成功后发布暂存目录，以及导出材质实际属性值、纹理变换和动态状态。

转换失败会保留具体程序和布局诊断。编译通过不能替代在目标游戏中的材质、UV、透明度和颜色对照。
