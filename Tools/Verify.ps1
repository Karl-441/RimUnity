param([string]$GameRoot = 'E:\steam\steamapps\common\RimWorld', [string]$DataRoot = '')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Build.ps1') -GameRoot $GameRoot
& dotnet build (Join-Path $repo 'Source\RimUnity.ShaderTool\RimUnity.ShaderTool.csproj') -c Release --nologo "-p:RimWorldDir=$GameRoot"
if ($LASTEXITCODE -ne 0) { throw 'ShaderTool 编译失败。' }
$tool = Join-Path $repo 'Tools\bin\ShaderTool\RimUnity.ShaderTool.dll'
$shaders = @(Get-ChildItem -LiteralPath (Join-Path $repo '1.7\Shaders') -Filter '*.glsl' -Recurse | Select-Object -ExpandProperty FullName)
& dotnet $tool $GameRoot verify @shaders
if ($LASTEXITCODE -ne 0) { throw 'GLSL 检查失败。' }
if ($DataRoot) {
    & dotnet $tool $GameRoot inspect (Join-Path $repo '1.7\Assemblies\RimUnity.dll') $DataRoot
    if ($LASTEXITCODE -ne 0) { throw 'Prefab 静态检查失败。' }
}
