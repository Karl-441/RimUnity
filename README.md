# RimUnity

Unity 原生制作 → 明文 Prefab 与依赖 → RimWorld 1.7 Waylith。

仓库包含兼容运行时、Unity UPM 导出插件与离线 Shader 转换工具。内容资源由使用方模组管理，框架不附带测试预制体、游戏内调试面板或样例素材。

目前为开发预览版本，部分 Unity 功能仍有差异，不能视为完整引擎替代。能力与限制见 [状态说明](Docs/Status.md)。

## 使用

1. Unity 2022.3：Package Manager → Add package from disk → 选择 `UnityPackage/package.json`。支持 Built-in 与 URP 14 的已适配功能。
2. 制作 Prefab，通过 `Tools → RimUnity` 导出到含 `About/About.xml` 的内容模组；资源存入该模组的 `1.7/Data/RimUnity`。
3. 游戏侧以 .NET 10 编译，引用 `RimUnity.dll` 和目标游戏的程序集，按 [类型迁移](Docs/Usage.md) 调整组件引用。

```csharp
var template = RimUnity.Assets.PrefabLoader.Load(contentMod, "Effects/BasicBurst");
var instance = UnityEngine.Object.Instantiate(template);
instance.transform.position = position;
```

接入文档：[API 快速接入](Docs/ApiQuickStart.md)、[资源载入](Docs/ResourceLoading.md)、[制作到发布工作流](Docs/Workflow.md)。

专题说明：[粒子](Docs/ParticleQuickStart.md)、[Shader 转换](Docs/CustomShaderConversion.md)、[颜色与 Bloom](Docs/ColorAndBloom.md)。

## 构建和发布

需要 .NET 10 SDK、目标 RimWorld 1.7 安装及 Harmony。构建脚本提供本机默认路径，可通过参数和 MSBuild 属性指定安装位置。

```powershell
./Tools/Build.ps1 -GameRoot '<RimWorld目录>'
dotnet build ./Source/RimUnity.ShaderTool/RimUnity.ShaderTool.csproj -c Release -p:RimWorldDir='<RimWorld目录>'
./Tools/Compile-Editor.ps1 -UnityEditorDir '<Unity Editor/Data>' -UnityProject '<已有Unity工程>'
./Tools/Pack.ps1 -Output '<发布输出目录>'
```

插件源码编译使用已有 Unity 工程中的 UGUI／TMP 引用，不会启动 Unity。`Verify.ps1` 提供可选的 Shader 静态检查；传入 `-DataRoot` 时检查指定内容模组的资源。

`Pack.ps1` 生成 Mod ZIP 和 UPM TGZ，不包含游戏程序集、测试素材或编译缓存。关闭游戏后可使用 `Deploy.ps1 -GameRoot '<RimWorld目录>'` 部署框架，脚本不会启动游戏或修改启用列表。

## 许可

本项目使用 [MIT 许可证](LICENSE)。来源和第三方版权声明见 [ThirdParty.md](Docs/ThirdParty.md)；内容模组的美术、字体和第三方资源不因使用本框架而自动获得 MIT 授权。
