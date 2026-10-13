param([string]$Output = 'E:\ModdevMics\Outputs\RimUnity', [string]$AssemblyDirectory = '', [string]$TempRoot = 'E:\ModdevMics\Temp\RimUnity\Packaging')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrEmpty($AssemblyDirectory)) { $AssemblyDirectory = Join-Path $repo '1.7\Assemblies' }
$version = (Get-Content -LiteralPath (Join-Path $repo 'UnityPackage\package.json') -Encoding UTF8 -Raw | ConvertFrom-Json).version
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$stage = Join-Path $TempRoot ([Guid]::NewGuid().ToString('N'))
$mod = Join-Path $stage 'RimUnity'
foreach ($directory in @($Output,$mod,(Join-Path $mod '1.7\Assemblies'))) { [IO.Directory]::CreateDirectory($directory) | Out-Null }
foreach ($name in @('About','LoadFolders.xml','Docs','ThirdParty','LICENSE')) { Copy-Item -LiteralPath (Join-Path $repo $name) -Destination $mod -Recurse }
foreach ($name in @('Defs','Shaders')) { Copy-Item -LiteralPath (Join-Path $repo "1.7\$name") -Destination (Join-Path $mod '1.7') -Recurse }
foreach ($name in @('RimUnity.dll','RimUnity.pdb')) { Copy-Item -LiteralPath (Join-Path $AssemblyDirectory $name) -Destination (Join-Path $mod '1.7\Assemblies') }
#框架只发布运行时和内置 Shader；导出资源由各内容模组自行打包。
$modZip = Join-Path $Output "RimUnity-$version-preview-$stamp.zip"
Compress-Archive -LiteralPath $mod -DestinationPath $modZip
$package = Join-Path $stage 'package'
Copy-Item -LiteralPath (Join-Path $repo 'UnityPackage') -Destination $package -Recurse
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $package
$tool = Join-Path $package 'Tools~\ShaderTool'
[IO.Directory]::CreateDirectory($tool) | Out-Null
Get-ChildItem -LiteralPath (Join-Path $repo 'Tools\bin\ShaderTool') -File | Where-Object { $_.Name.StartsWith('RimUnity.ShaderTool.') } | Copy-Item -Destination $tool
$tgz = Join-Path $Output "com.chezhou.rimunity-$version-$stamp.tgz"
& tar -czf $tgz -C $stage package
if ($LASTEXITCODE -ne 0) { throw 'UPM 包压缩失败。' }
Write-Output "模组包：$modZip"
Write-Output "UPM 包：$tgz"
Write-Output "打包暂存：$stage"
