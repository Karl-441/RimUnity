param([string]$UnityEditorDir = 'E:\UN\2022.3.35f1\Editor\Data', [string]$UnityProject = 'E:\ModdevMics\Temp\RimUnity\BuiltIn')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
#只编译 UPM 源码，不启动编辑器，不执行导出或场景。
& dotnet build (Join-Path $repo 'Source\RimUnity.UnityCompile\RimUnity.UnityCompile.csproj') -c Release --nologo "-p:UnityEditorDir=$UnityEditorDir" "-p:UnityProject=$UnityProject"
if ($LASTEXITCODE -ne 0) { throw 'UPM 编译失败。' }
