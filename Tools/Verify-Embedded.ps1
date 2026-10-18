param([Parameter(Mandatory)][string]$GameRoot, [Parameter(Mandatory)][string]$HarmonyPath, [string]$WorkshopRoot = '')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
function Read-GitBlob {
    # 按字节从 git 取出历史文件：PowerShell 管道会破坏二进制，所以直接拷 StandardOutput 流。
    param([string]$Repository, [string]$Revision, [string]$Path, [string]$Destination)
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = 'git'
    $info.Arguments = "cat-file blob $Revision`:$Path"
    $info.WorkingDirectory = $Repository
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $process = [System.Diagnostics.Process]::Start($info)
    $stream = [IO.File]::Create($Destination)
    try { $process.StandardOutput.BaseStream.CopyTo($stream) } finally { $stream.Dispose() }
    $process.WaitForExit()
    if ($process.ExitCode -ne 0 -or (Get-Item -LiteralPath $Destination).Length -lt 1024) { throw "无法从 git 读取历史文件：$Revision`:$Path" }
}
$fixtures = Join-Path ([IO.Path]::GetTempPath()) ('RimUnity/Embedded-' + [Guid]::NewGuid().ToString('N'))
foreach ($name in @('A','B')) {
    $about = Join-Path $fixtures "$name/About"
    [IO.Directory]::CreateDirectory($about) | Out-Null
    [IO.File]::WriteAllText((Join-Path $about 'About.xml'), "<ModMetaData><name>$name</name><packageId>test.$name</packageId></ModMetaData>", [Text.UTF8Encoding]::new($false))
}
& dotnet build (Join-Path $PSScriptRoot 'EmbeddedTests/Fixtures/Consumer/Consumer.csproj') -c Release --nologo "-p:RimWorldDir=$GameRoot" "-p:FixtureRoot=$fixtures" "-p:EmbeddedPackage=$repo/artifacts/Embedded"
if ($LASTEXITCODE -ne 0) { throw '内容程序集接入构建失败。' }
& dotnet build (Join-Path $PSScriptRoot 'EmbeddedTests/EmbeddedTests.csproj') -c Release --nologo "-p:RimWorldDir=$GameRoot" "-p:HarmonyPath=$HarmonyPath"
if ($LASTEXITCODE -ne 0) { throw '离线验证程序编译失败。' }
& (Join-Path $PSScriptRoot 'Copy-RuntimePackage.ps1') -Destination (Join-Path $fixtures 'Standalone')
# 同名程序集重复（issue #7 的真实场景）：旧版残留的 RimUnity.dll 与新版本并存，
# 原版会把两者都登记进 loadedAssemblies；选择器必须给出可读的双语错误，而不是 Single 抛裸异常。
$duplicateRoot = Join-Path $fixtures 'Duplicate'
Copy-Item -LiteralPath (Join-Path $fixtures 'Standalone') -Destination $duplicateRoot -Recurse
$duplicateDll = Join-Path $duplicateRoot '1.7/Assemblies/old/RimUnity.dll'
[IO.Directory]::CreateDirectory((Split-Path -Parent $duplicateDll)) | Out-Null
Read-GitBlob -Repository $repo -Revision '1d10c5570219710a8f93b0edb28b8090fb34c404' -Path '1.7/Assemblies/RimUnity.dll' -Destination $duplicateDll
& dotnet (Join-Path $PSScriptRoot 'EmbeddedTests/bin/Release/net10.0/EmbeddedTests.dll') $repo $GameRoot $HarmonyPath $fixtures 'duplicate-runtime'
if ($LASTEXITCODE -ne 0) { throw '离线验证失败：duplicate-runtime' }
& dotnet build (Join-Path $PSScriptRoot 'EmbeddedTests/Fixtures/Ordinary/Ordinary.csproj') -c Release --nologo "-p:RimWorldDir=$GameRoot" "-p:FixtureRoot=$fixtures" "-p:RuntimeDirectory=$repo/1.7/Assemblies"
if ($LASTEXITCODE -ne 0) { throw '普通 DLL 兼容夹具编译失败。' }
foreach ($scenario in @('workshop-first','workshop-last','ordinary-workshop','renamed-runtime-provider','incomplete-runtime-provider','ambiguous-runtime-provider','missing-runtime-resources','ordinary-standalone','ordinary-standalone-only','standalone-only','single','duplicates','reverse','standalone-first','standalone-middle','standalone-last','scale')) {
    & dotnet (Join-Path $PSScriptRoot 'EmbeddedTests/bin/Release/net10.0/EmbeddedTests.dll') $repo $GameRoot $HarmonyPath $fixtures $scenario
    if ($LASTEXITCODE -ne 0) { throw "离线验证失败：$scenario；夹具目录：$fixtures" }
}
if ($WorkshopRoot) {
    if (!(Test-Path -LiteralPath (Join-Path $WorkshopRoot '1.7/Assemblies/RimUnity.dll') -PathType Leaf)) { throw '工坊测试目录缺少运行时。' }
    #保留未启用的本地版夹具，只将工坊副本登记为活动模组。
    Copy-Item -LiteralPath $WorkshopRoot -Destination (Join-Path $fixtures 'Workshop') -Recurse
    foreach ($scenario in @('actual-workshop-first','actual-workshop-last','actual-workshop-ordinary')) {
        & dotnet (Join-Path $PSScriptRoot 'EmbeddedTests/bin/Release/net10.0/EmbeddedTests.dll') $repo $GameRoot $HarmonyPath $fixtures $scenario
        if ($LASTEXITCODE -ne 0) { throw "工坊实际 DLL 验证失败：$scenario" }
    }
}
#固定历史源码测试真实旧入口，而不是用新入口伪装旧版本。
foreach ($legacy in @(
    @{Revision='1d10c5570219710a8f93b0edb28b8090fb34c404';Name='LegacyBootstrap';Assembly='RimUnity.Embedded.Legacy011';Version='0.1.11.0';Orders=@('legacy-first','legacy-last')},
    @{Revision='c06c6cd3a606b94b258026d6cfc5f078e618e918';Name='DeferredBootstrap';Assembly='RimUnity.Embedded.Deferred013';Version='0.1.13.0';Orders=@('workshop-legacy-first','workshop-legacy-last')}
)) {
$legacyRevision = $legacy.Revision
$legacyRoot = Join-Path $fixtures $legacy.Name
[IO.Directory]::CreateDirectory($legacyRoot) | Out-Null
foreach ($name in @('EmbeddedBootstrap.cs','RuntimeSelection.cs','RuntimePayload.cs','DeferredContent.cs')) {
    # 按字节取：主机代码页不是 UTF-8 时，`& git show` 的文本解码会把中文注释和下一行粘在一起（CS1014）。
    Read-GitBlob -Repository $repo -Revision $legacyRevision -Path "Source/RimUnity.Embedded/$name" -Destination (Join-Path $legacyRoot $name)
}
$legacyProject = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>$($legacy.Assembly)</AssemblyName>
    <FileVersion>$($legacy.Version)</FileVersion>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <OutputPath>bin/</OutputPath>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="Assembly-CSharp" HintPath="$GameRoot/Assembly-CSharp.dll" Private="false" />
    <Reference Include="UnityStubs" HintPath="$GameRoot/UnityStubs.dll" Private="false" />
    <Reference Include="0Harmony" HintPath="$HarmonyPath" Private="false" />
    <EmbeddedResource Include="$repo/artifacts/Embedded/payload.zip" LogicalName="RimUnity.Payload.zip" />
  </ItemGroup>
</Project>
"@
[IO.File]::WriteAllText((Join-Path $legacyRoot 'Legacy.csproj'), $legacyProject, [Text.UTF8Encoding]::new($false))
& dotnet build (Join-Path $legacyRoot 'Legacy.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw '历史加载器编译失败。' }
$legacyTarget = Join-Path $fixtures 'A/1.7/Assemblies/RimUnity.Embedded.dll'
try {
    Copy-Item -LiteralPath (Join-Path $legacyRoot ("bin/" + $legacy.Assembly + ".dll")) -Destination $legacyTarget -Force
    foreach ($order in $legacy.Orders) {
        & dotnet (Join-Path $PSScriptRoot 'EmbeddedTests/bin/Release/net10.0/EmbeddedTests.dll') $repo $GameRoot $HarmonyPath $fixtures $order
        if ($LASTEXITCODE -ne 0) { throw "历史加载器混装失败：$order" }
    }
} finally {
    Copy-Item -LiteralPath (Join-Path $repo 'artifacts/Embedded/RimUnity.Embedded.dll') -Destination $legacyTarget -Force
}
}
#提升要求超过所有候选；保留目标分支的动态最低版本计算。
$manifest = Join-Path $fixtures 'A/1.7/Assemblies/EmbeddedFixture.Library.rimunity.json'
$originalManifest = [IO.File]::ReadAllText($manifest)
$standaloneVersion = [Version][Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $repo '1.7/Assemblies/RimUnity.dll')).FileVersion
$requiredVersion = [Version]::new($standaloneVersion.Major, $standaloneVersion.Minor, $standaloneVersion.Build, [Math]::Max($standaloneVersion.Revision, 0) + 1)
[IO.File]::WriteAllText($manifest, '{"protocol":1,"minimumVersion":"' + $requiredVersion + '"}', [Text.UTF8Encoding]::new($false))

& dotnet (Join-Path $PSScriptRoot 'EmbeddedTests/bin/Release/net10.0/EmbeddedTests.dll') $repo $GameRoot $HarmonyPath $fixtures 'incompatible-standalone'
if ($LASTEXITCODE -ne 0) { throw '不兼容独立前置验证失败。' }
[IO.File]::WriteAllText($manifest, $originalManifest, [Text.UTF8Encoding]::new($false))
#测试容器只改变归档元数据或许可字节；实际运行时仍使用本次编译的 DLL。
foreach ($variant in @('older','newer','conflict')) {
    $variantRoot = Join-Path $fixtures $variant
    [IO.Directory]::CreateDirectory($variantRoot) | Out-Null
    $variantZip = Join-Path $variantRoot 'payload.zip'
    Copy-Item -LiteralPath (Join-Path $repo 'artifacts/Embedded/payload.zip') -Destination $variantZip
    $zip = [IO.Compression.ZipFile]::Open($variantZip, [IO.Compression.ZipArchiveMode]::Update)
    try {
        $entry = $zip.GetEntry($(if ($variant -ne 'conflict') { 'runtime.json' } else { 'LICENSE' }))
        $name = $entry.FullName
        $reader = [IO.StreamReader]::new($entry.Open())
        try { $value = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $entry.Delete()
        if ($variant -eq 'older') { $value = '{"protocol":1,"version":"0.1.8"}' }
        elseif ($variant -eq 'newer') {
            $current = [version]($value | ConvertFrom-Json).version
            $next = [version]::new($current.Major, $current.Minor, $current.Build + 1)
            $value = '{"protocol":1,"version":"' + $next + '"}'
        } else { $value += "`n" }
        $writer = [IO.StreamWriter]::new($zip.CreateEntry($name).Open(), [Text.UTF8Encoding]::new($false))
        try { $writer.Write($value) } finally { $writer.Dispose() }
    } finally { $zip.Dispose() }
    $containerId = (Get-FileHash -LiteralPath $variantZip -Algorithm SHA256).Hash
    & dotnet build (Join-Path $repo 'Source/RimUnity.Embedded/RimUnity.Embedded.csproj') -c Release --nologo "-p:RimWorldDir=$GameRoot" "-p:HarmonyPath=$HarmonyPath" "-p:PayloadPath=$variantZip" "-p:ContainerId=$containerId" "-p:OutputPath=$variantRoot/bin/"
    if ($LASTEXITCODE -ne 0) { throw "测试容器编译失败：$variant" }
    $destination = if ($variant -eq 'older') { 'A' } else { 'B' }
    Copy-Item -LiteralPath (Join-Path $variantRoot 'bin/RimUnity.Embedded.dll') -Destination (Join-Path $fixtures "$destination/1.7/Assemblies/RimUnity.Embedded.dll") -Force
    $scenario = if ($variant -ne 'conflict') { 'newest' } else { 'conflict' }
    & dotnet (Join-Path $PSScriptRoot 'EmbeddedTests/bin/Release/net10.0/EmbeddedTests.dll') $repo $GameRoot $HarmonyPath $fixtures $scenario
    if ($LASTEXITCODE -ne 0) { throw "候选载荷验证失败：$scenario" }
    Copy-Item -LiteralPath (Join-Path $repo 'artifacts/Embedded/RimUnity.Embedded.dll') -Destination (Join-Path $fixtures "$destination/1.7/Assemblies/RimUnity.Embedded.dll") -Force

    if ($variant -ne 'older') {
        Copy-Item -LiteralPath (Join-Path $variantRoot 'bin/RimUnity.Embedded.dll') -Destination (Join-Path $fixtures 'B/1.7/Assemblies/RimUnity.Embedded.dll') -Force
        $standaloneScenario = if ($variant -eq 'newer') { 'standalone-newer-payload' } else { 'standalone-conflicting-payloads' }
        & dotnet (Join-Path $PSScriptRoot 'EmbeddedTests/bin/Release/net10.0/EmbeddedTests.dll') $repo $GameRoot $HarmonyPath $fixtures $standaloneScenario
        if ($LASTEXITCODE -ne 0) { throw "独立版固定优先验证失败：$standaloneScenario" }
        Copy-Item -LiteralPath (Join-Path $repo 'artifacts/Embedded/RimUnity.Embedded.dll') -Destination (Join-Path $fixtures 'B/1.7/Assemblies/RimUnity.Embedded.dll') -Force
    }

}
# 把夹具路径写成机器可读的记录，第三层（游戏内）工作流据此把 -FixtureRoot 传下去。
$fixtureRecord = Join-Path $repo 'artifacts/verify/embedded-fixtures.txt'
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fixtureRecord)) | Out-Null
[IO.File]::WriteAllText($fixtureRecord, $fixtures, [Text.UTF8Encoding]::new($false))
Write-Output "离线加载验证通过。夹具与缓存：$fixtures。未验证游戏内渲染和原生挂载。"
Write-Output "RIMUNITY_FIXTURES=$fixtures"
