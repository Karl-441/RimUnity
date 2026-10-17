param([Parameter(Mandatory)][string]$Destination, [string]$AssemblyDirectory = '')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent

if ((Test-Path -LiteralPath $Destination) -and (Get-ChildItem -LiteralPath $Destination -Force)) { throw '发布暂存目录必须为空。' }
$carrier = Join-Path $repo 'artifacts/Embedded/RimUnity.Embedded.dll'
if (!(Test-Path -LiteralPath $carrier)) { throw '请先构建延迟运行库载荷。' }
if (!$AssemblyDirectory) { $AssemblyDirectory = Join-Path $repo '1.7/Assemblies' }
foreach ($name in @('RimUnity.Core.dll','RimUnity.dll','RimUnity.Spine.dll')) {
    $reference = Join-Path $repo "artifacts/Embedded/References/$name"
    if ((Get-FileHash -LiteralPath (Join-Path $AssemblyDirectory $name)).Hash -ne (Get-FileHash -LiteralPath $reference).Hash) {
        throw "运行时与延迟载荷不一致，请重新运行 Build.ps1：$name"
    }
}
[xml]$about = Get-Content -LiteralPath (Join-Path $repo 'About/About.xml') -Raw
if (([version][Diagnostics.FileVersionInfo]::GetVersionInfo($carrier).FileVersion).ToString(3) -ne ([version]$about.ModMetaData.modVersion).ToString(3)) {
    throw '延迟入口与 About 版本不一致，请重新构建。'
}

#部署与压缩包使用同一清单；1.7 的 DLL、Def、Shader 统一来自载荷。
$files = @('About\About.xml', 'About\PublishedFileId.txt', 'About\Preview.png', 'About\ModIcon.png', 'LoadFolders.xml', 'LICENSE', 'README.md', 'README.en.md', '1.6\README.txt')
foreach ($directory in @('1.6\Languages', 'Docs\zh-CN', 'Docs\en', 'Docs\Images', 'ThirdParty')) {
    $files += Get-ChildItem -LiteralPath (Join-Path $repo $directory) -File -Recurse | ForEach-Object { [IO.Path]::GetRelativePath($repo, $_.FullName) }
}
foreach ($relative in $files) {
    $target = Join-Path $Destination $relative
    [IO.Directory]::CreateDirectory((Split-Path $target -Parent)) | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo $relative) -Destination $target
}
$target = Join-Path $Destination 'Bootstrap/Assemblies/RimUnity.Embedded.dll'
[IO.Directory]::CreateDirectory((Split-Path $target -Parent)) | Out-Null
Copy-Item -LiteralPath $carrier -Destination $target
