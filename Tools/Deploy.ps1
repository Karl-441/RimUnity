param([string]$GameRoot = 'E:\steam\steamapps\common\RimWorld')
$ErrorActionPreference = 'Stop'
if (Get-Process RimWorld,RimWorldWin64 -ErrorAction SilentlyContinue) { throw '请先由你关闭 RimWorld，再部署程序集。脚本不会自动关闭游戏。' }
$repo = Split-Path $PSScriptRoot -Parent
$destination = Join-Path $GameRoot 'Mods\RimUnity'
if (!(Test-Path -LiteralPath (Join-Path $GameRoot 'Assembly-CSharp.dll'))) { throw '游戏目录无效。' }
if ([IO.Path]::GetFullPath($destination) -eq [IO.Path]::GetFullPath($repo)) { throw '部署目录不能等于开发目录。' }
[IO.Directory]::CreateDirectory($destination) | Out-Null
#清除旧框架版本自带的测试面板和样例数据，避免增量复制后残留入口。
foreach ($relative in @('1.7\Defs\RimUnityTestPanel.xml', '1.7\Data\RimUnity', '1.7\Shaders\RimUnity\PreviewOpaque.frag.glsl')) {
    $legacy = [IO.Path]::GetFullPath((Join-Path $destination $relative))
    if (!$legacy.StartsWith([IO.Path]::GetFullPath($destination) + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '旧测试资源路径越界。' }
    if (Test-Path -LiteralPath $legacy) { Remove-Item -LiteralPath $legacy -Recurse -Force }
}
foreach ($name in @('About','LoadFolders.xml','1.7','Docs','ThirdParty')) {
    Copy-Item -LiteralPath (Join-Path $repo $name) -Destination $destination -Recurse -Force
}
Write-Output "已部署：$destination"
Write-Output '请自行在游戏中启用 Harmony 与 RimUnity；启用列表和存档未由脚本修改。'
