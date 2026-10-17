# Build once by default; -SkipBuild checks existing outputs without rebuilding them.
param(
    [string]$GameRoot = '',
    [string]$DataRoot = '',
    [string]$HarmonyPath = '',
    [switch]$SkipBuild,
    [switch]$Embedded
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Resolve-BuildPath.ps1')
$GameRoot = Resolve-BuildGameRoot -Path $GameRoot -Required
$repo = Split-Path $PSScriptRoot -Parent
if ($Embedded -or -not $SkipBuild) {
    $HarmonyPath = Resolve-BuildPath -Path $HarmonyPath -EnvVar 'HARMONY_PATH' -ParamName 'HarmonyPath' -Purpose 'Harmony 程序集' -Kind Leaf
}
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'Build.ps1') -GameRoot $GameRoot -HarmonyPath $HarmonyPath
}
$tool = Join-Path $repo 'Tools/bin/ShaderTool/RimUnity.ShaderTool.dll'
if (!(Test-Path -LiteralPath $tool -PathType Leaf)) { throw '缺少 ShaderTool；请先运行 Tools/Build.ps1，或去掉 -SkipBuild。' }
if ($DataRoot -and !(Test-Path -LiteralPath (Join-Path $repo '1.7/Assemblies/RimUnity.dll') -PathType Leaf)) {
    throw '缺少 RimUnity.dll；请先运行 Tools/Build.ps1。'
}
$shaders = @(Get-ChildItem -LiteralPath (Join-Path $repo '1.7/Shaders') -Filter '*.glsl' -Recurse | Select-Object -ExpandProperty FullName)
& dotnet $tool $GameRoot verify @shaders
if ($LASTEXITCODE -ne 0) { throw 'GLSL 检查失败。' }
if ($DataRoot) {
    & dotnet $tool $GameRoot inspect (Join-Path $repo '1.7/Assemblies/RimUnity.dll') $DataRoot
    if ($LASTEXITCODE -ne 0) { throw 'Prefab 静态检查失败。' }
}
if ($Embedded) {
    & (Join-Path $PSScriptRoot 'Verify-Embedded.ps1') -GameRoot $GameRoot -HarmonyPath $HarmonyPath
}
Write-Output '离线验证通过；游戏内渲染仍需单独验证。'
