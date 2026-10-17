# Build release inputs only. Deployment remains an explicit Tools/Deploy.ps1 operation.
param(
    [string]$GameRoot = '',
    [string]$HarmonyPath = '',
    [switch]$SkipEmbedded
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Resolve-BuildPath.ps1')
$GameRoot = Resolve-BuildGameRoot -Path $GameRoot -Required
$HarmonyPath = Resolve-BuildPath -Path $HarmonyPath -EnvVar 'HARMONY_PATH' -ParamName 'HarmonyPath' -Purpose 'Harmony 程序集' -Kind Leaf
$repo = Split-Path $PSScriptRoot -Parent
# Spine references Runtime/Core; its MSBuild target owns the required deferred payload.
# SkipEmbedded is retained for CLI compatibility; standalone also needs this payload.
& dotnet build (Join-Path $repo 'Source/RimUnity.Spine/RimUnity.Spine.csproj') -c Release --nologo "-p:RimWorldDir=$GameRoot" "-p:HarmonyPath=$HarmonyPath" "-p:SkipEmbedded=$($SkipEmbedded.IsPresent.ToString().ToLowerInvariant())"
if ($LASTEXITCODE -ne 0) { throw 'RimUnity 编译失败。' }
& dotnet build (Join-Path $repo 'Source/RimUnity.ShaderTool/RimUnity.ShaderTool.csproj') -c Release --nologo "-p:RimWorldDir=$GameRoot"
if ($LASTEXITCODE -ne 0) { throw 'ShaderTool 编译失败。' }
Write-Output '构建完成：1.7/Assemblies、Tools/bin/ShaderTool。'
Write-Output '延迟载荷与内置接入包：artifacts/Embedded。'
