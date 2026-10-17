# Tools 使用索引

## 构建、检查与发布

- `Build.ps1`：构建运行时、延迟载荷和 ShaderTool，不执行部署。`-SkipEmbedded` 保留参数兼容；独立版也依赖载荷，因此仍会生成。
- `Build-Embedded.ps1`：生成内置载荷、编译引用及独立版 Bootstrap；由运行时构建调用。
- `Compile-Editor.ps1`：离线编译 Unity 插件源码，需要 Unity Editor 和工程路径。
- `Verify.ps1`：构建并检查 GLSL，可额外检查指定资源目录。此入口调用 Build，不执行部署。
- `Verify-Embedded.ps1`：离线验证版本选择、程序集身份及延迟内容加载。
- `Test-EmbeddedInGame.ps1`：启动隔离存档目录的真实游戏测试，临时安装测试模组，结束后恢复原独立版。
- `Deploy.ps1`：备份并替换游戏中的独立版。
- `Pack.ps1`：生成独立版、内置版与 Unity UPM 发布包。
- `Sync-Manual.ps1`：同步原始文档到 Unity 包；`-Check` 仅核对。

`Resolve-BuildPath.ps1`、`Copy-RuntimePackage.ps1` 是上述脚本共用的路径和发布清单实现。`RimUnity.Embedded.targets` 是内容模组的构建接入文件。`Directory.Build.props` 提供 Tools 工程共用的 Unity 路径配置。

## 回归测试

这些工程没有被主构建自动调用，但覆盖当前源码，不属于废弃文件：

- `EmbeddedTests`：加载器、版本选择、跨程序集脚本与游戏启动夹具。
- `CatalogPolicyTests`：资源目录加载策略。
- `ExportProcessorTests`：Unity 导出处理器与文档库。
- `PublishingTests`：资源发布、回滚、清单及增量导出。
- `MemoryAttributionTests`：显存归责模型。
- `RenderDiagnosticsTests`：运行时渲染诊断。
- `ScreenEfxTests`：屏幕特效播放与参数快照。
- `SpikeCaptureTests`：性能尖峰采样和诊断数据。
- `TrailGeometryTests`：拖尾几何、折射与资源绑定；参数见其 README。
- `WarmupTests`：加载预热任务调度。

各项目所需参数和外部引用不同，运行前查看对应 csproj、Program.cs 或领域文档。

## 按需工具与素材

- `ResourcePacker`：整理已有资源导出、共享去重并生成 ZIP；输出到新目录，不是独立运行库打包入口。用法见 `Docs/zh-CN/ResourceLoading.md`。
- `Upgrade-LinearExports.ps1`：修正已确认来自 Linear 工程的旧导出；会修改指定资源并生成备份。不能用于 Gamma 工程，也不属于日常构建步骤。
- `Artwork`：封面、图标和工坊说明图的生成源码。
- `Steam`：工坊标题、双语说明和配图。

## 生成文件

- `bin/ShaderTool` 是 ShaderTool 的构建输出，仍被检查、资源升级和打包脚本使用。缺失时重新构建 `Source/RimUnity.ShaderTool/RimUnity.ShaderTool.csproj`。
- 测试工程的 `bin`、`obj` 可重新构建，不需要进入版本控制。
- 临时诊断构建与旧备份应放在仓库 `artifacts/`，不要继续堆放在 Tools 下。

本次整理将旧诊断构建、测试编译缓存及 Tools/obj 的空备份目录移至 `artifacts/ToolsCleanup/`。每次归档的 `manifest.json` 记录原位置和归档位置，可按需恢复。源码工具和当前 ShaderTool 保留。
