# CI 使用方式

这个目录里的两个 workflow 分工不同：**不需要游戏就能跑的**放第一层，**需要游戏安装的**放第二层。

| 文件 | 层 | 跑在哪 | 需要什么 |
| --- | --- | --- | --- |
| `repo-checks.yml` | 第一层 | GitHub 托管的 `windows-latest` | 无（不碰游戏、不编译） |
| `manual-verify.yml` | 第二层 | 自托管 runner，标签 `self-hosted, windows, rimworld-1.7` | RimWorld 1.7 + Harmony + .NET 10 SDK |

---

## 第一层：`repo-checks.yml`

**什么时候跑**：`main` / `keep_branch` 的 `push`，以及 `pull_request`。

**跑什么**：`Tools/Verify-Static.ps1` 与 `Tools/Test-BuildWorkflow.ps1`（两个 PowerShell 宿主各运行一次）。静态检查做的是不需要编译、不需要游戏的那一类检查——

- 离线文档镜像（`UnityPackage/Documentation~/`）与 `sources.json` 是否与源文件一致；
- 发布号是否单一来源（`About/About.xml`、`Source/Directory.Build.props`、`UnityPackage/package.json`）、
  兼容下限是否一致且不高于发布号、README 与 `EmbeddedRuntime.md` 的下载链接是否指向当前版本；
- 所有 `*.ps1` 含非 ASCII 却没有 UTF-8 BOM；
- 被跟踪文件里出现盘符绝对路径；
- 跟踪的 XML / JSON 是否都能解析；
- 冲突标记残留；
- 以及 `Resolve-BuildPath` 的 `-Require` / `-Verify` 用法、测试项目的 `PackageReference` 是否被改动。

**为什么同一个检查跑两遍**：脚本要在 PowerShell 7 和 Windows PowerShell 5.1 下都能解析、都能执行。
仓库里的脚本历史上就吃过亏——同一个脚本在 5.1 上会给出和 7 不同的结论（`ConvertTo-Json` 的缩进与
`\uXXXX` 转义、`Sort-Object` 跟随区域性排序、`[IO.Path]::GetRelativePath` 在 5.1 上不存在）。

**为什么检出前要关 `core.autocrlf`**：默认的自动转换会让同一份内容在不同机器上呈现不同的行尾，
`Get-FileHash` 比对和"提交范围内有没有混入 CR"这类检查都会因此误报。workflow 里先
`git config --global core.autocrlf false` 再 `actions/checkout`，保证检出的就是仓库里的字节。

**本地等价命令**：

```powershell
pwsh -File Tools/Verify-Static.ps1                                    # PowerShell 7
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Tools/Verify-Static.ps1   # Windows PowerShell 5.1
```

5.1 上必须带 `-ExecutionPolicy Bypass`，否则未签名的脚本会被执行策略挡下。

---

## 第二层：`manual-verify.yml`

**什么时候跑**：手动触发。runner 没注册时不要改成 `push` / `tag` 触发——任务只会在队列里堆积。

```powershell
gh workflow run manual-verify.yml -f run-pack=true    # run-pack 可选，额外打发布产物
gh run list --workflow=manual-verify.yml
gh run view <run-id> --log
```

**一次性准备（在自托管机器上）**：

1. 注册 runner 时打上标签 `self-hosted`、`windows`、`rimworld-1.7`；
2. 仓库 Settings → Secrets and variables → Actions → Variables 配两个仓库变量：
   - `RIMWORLD_DIR`：游戏根目录（含 `Assembly-CSharp.dll` 等），例如 `<Steam 库目录>\steamapps\common\RimWorld`；
   - `HARMONY_PATH`：Harmony 的 `0Harmony.dll`，典型位置是创意工坊里 Harmony 模组的
     `<创意工坊目录>\content\294100\2009463077\1.7\Assemblies\0Harmony.dll`；
3. 装 .NET 10 SDK。`Source/Directory.Build.props` 的目标框架是 `net10.0`，只有 .NET 8 SDK 会直接报
   `NETSDK1045`。

**跑什么**：`Tools/Build.ps1` → `Tools/Verify.ps1 -SkipBuild -Embedded`，然后还原被构建改写的
两个**被跟踪**二进制（`1.7/Assemblies/RimUnity.dll`、`1.7/Assemblies/RimUnity.Core.dll`，见 `.gitignore`
里的白名单），并断言工作区干净。`Build.ps1` 已经会经 `RimUnity.Spine.csproj` 的 `BuildEmbeddedRuntime`
目标顺带构建内置运行时，所以不需要单独调 `Tools/Build-Embedded.ps1`。

**安全**：自托管 runner 跑在自己机器上，公开仓库里**不要**让 fork 提交的 PR 触发它
（这条 workflow 因此只有 `workflow_dispatch`，没有 `pull_request`）。

**本地等价命令**：

```powershell
$env:RIMWORLD_DIR = '<游戏根目录>'
$env:HARMONY_PATH = '<创意工坊>\content\294100\2009463077\1.7\Assemblies\0Harmony.dll'

./Tools/Build.ps1                                          # 参数 → 环境变量 → 内置回退 依次解析
./Tools/Verify.ps1 -SkipBuild -Embedded
./Tools/Pack.ps1                                           # 可选：检查文档镜像后打发布产物
```

---

## 怎么读 `Tools/Verify-Static.ps1` 的输出

三种行，语义不同：

- `[通过]`：真的通过了；
- `[已知缺口]`：清单里已经记录、但**还没修**的项，脚本里放在 `$KnownDebt` 允许清单里，只提示、不阻塞。
  这不是"批准"，只是别让新 CI 一上来就红一片；
- `[失败]`：新引入的问题，或者允许清单里的项被改坏了，必须修。

**收紧方式**：修掉一项，就从 `$KnownDebt` 里删掉对应条目，门禁会自己变严。允许清单里每条都带
问题所在文件和清单编号。

---

## 已知的坑

- **`pwsh` 不一定是装好的**：`Source/RimUnity.Spine/RimUnity.Spine.csproj` 的内置构建目标调的是裸
  `pwsh`，机器上只有 Windows PowerShell 5.1 时会失败，先装 PowerShell 7。
- **`.ps1` 的 BOM**：含中文的脚本必须是 UTF-8 **带 BOM**，否则 5.1 会按 ANSI 解码成乱码。
  注意有些编辑器/工具写入时会顺手把 BOM 去掉，改完脚本用 `Tools/Verify-Static.ps1` 复查一遍。
- **构建会改写被跟踪的 DLL**：`1.7/Assemblies/RimUnity.dll` 与 `RimUnity.Core.dll` 是被跟踪的，
  编译后工作区就会变脏。CI 里跑完统一 `git checkout --` 还原，不要拿"构建后干净"当门禁条件。
- **`NETSDK1045`**：目标框架是 `net10.0`，SDK 版本低了就直接失败。

## 还没接进来的

- `Tools/Test-EmbeddedInGame.ps1`：需要桌面会话、会临时移动真实的 `Mods/RimUnity`，
  而且 `-FixtureRoot` 目前只能从上一步 `Tools/Verify-Embedded.ps1` 的输出里手工取，没有自动化入口；
- `Tools/Pack.ps1`：只在手动触发时用 `run-pack=true` 打产物，不进每次检查。
