param([Parameter(Mandatory)][string]$GameRoot, [Parameter(Mandatory)][string]$HarmonyPath)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
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
$standaloneDir = Join-Path $fixtures 'Standalone/Bootstrap/Assemblies'
[IO.Directory]::CreateDirectory($standaloneDir) | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'artifacts/Embedded/RimUnity.Embedded.dll') -Destination $standaloneDir
foreach ($scenario in @('standalone-only','single','duplicates','reverse','standalone-first','standalone-middle','standalone-last','scale')) {
    & dotnet (Join-Path $PSScriptRoot 'EmbeddedTests/bin/Release/net10.0/EmbeddedTests.dll') $repo $GameRoot $HarmonyPath $fixtures $scenario
    if ($LASTEXITCODE -ne 0) { throw "离线验证失败：$scenario；夹具目录：$fixtures" }
}
#固定历史源码测试真实旧入口，而不是用新入口伪装旧版本。
$legacyRevision = '1d10c5570219710a8f93b0edb28b8090fb34c404'
$legacyRoot = Join-Path $fixtures 'LegacyBootstrap'
[IO.Directory]::CreateDirectory($legacyRoot) | Out-Null
foreach ($name in @('EmbeddedBootstrap.cs','RuntimeSelection.cs','RuntimePayload.cs','DeferredContent.cs')) {
    $source = & git -c "safe.directory=$($repo.Replace('\','/'))" -C $repo show ('{0}:Source/RimUnity.Embedded/{1}' -f $legacyRevision,$name)
    if ($LASTEXITCODE -ne 0) { throw "无法读取固定历史加载器：$legacyRevision/$name" }
    [IO.File]::WriteAllLines((Join-Path $legacyRoot $name), [string[]]$source, [Text.UTF8Encoding]::new($false))
}
$legacyProject = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>RimUnity.Embedded.Legacy011</AssemblyName>
    <FileVersion>0.1.11.0</FileVersion>
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
    Copy-Item -LiteralPath (Join-Path $legacyRoot 'bin/RimUnity.Embedded.Legacy011.dll') -Destination $legacyTarget -Force
    foreach ($order in @('legacy-first','legacy-last')) {
        & dotnet (Join-Path $PSScriptRoot 'EmbeddedTests/bin/Release/net10.0/EmbeddedTests.dll') $repo $GameRoot $HarmonyPath $fixtures $order
        if ($LASTEXITCODE -ne 0) { throw "历史加载器混装失败：$order" }
    }
} finally {
    Copy-Item -LiteralPath (Join-Path $repo 'artifacts/Embedded/RimUnity.Embedded.dll') -Destination $legacyTarget -Force
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
foreach ($variant in @('older','conflict')) {
    $variantRoot = Join-Path $fixtures $variant
    [IO.Directory]::CreateDirectory($variantRoot) | Out-Null
    $variantZip = Join-Path $variantRoot 'payload.zip'
    Copy-Item -LiteralPath (Join-Path $repo 'artifacts/Embedded/payload.zip') -Destination $variantZip
    $zip = [IO.Compression.ZipFile]::Open($variantZip, [IO.Compression.ZipArchiveMode]::Update)
    try {
        $entry = $zip.GetEntry($(if ($variant -eq 'older') { 'runtime.json' } else { 'LICENSE' }))
        $name = $entry.FullName
        $reader = [IO.StreamReader]::new($entry.Open())
        try { $value = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $entry.Delete()
        if ($variant -eq 'older') { $value = '{"protocol":1,"version":"0.1.8"}' } else { $value += "`n" }
        $writer = [IO.StreamWriter]::new($zip.CreateEntry($name).Open(), [Text.UTF8Encoding]::new($false))
        try { $writer.Write($value) } finally { $writer.Dispose() }
    } finally { $zip.Dispose() }
    $containerId = (Get-FileHash -LiteralPath $variantZip -Algorithm SHA256).Hash
    & dotnet build (Join-Path $repo 'Source/RimUnity.Embedded/RimUnity.Embedded.csproj') -c Release --nologo "-p:RimWorldDir=$GameRoot" "-p:HarmonyPath=$HarmonyPath" "-p:PayloadPath=$variantZip" "-p:ContainerId=$containerId" "-p:OutputPath=$variantRoot/bin/"
    if ($LASTEXITCODE -ne 0) { throw "测试容器编译失败：$variant" }
    $destination = if ($variant -eq 'older') { 'A' } else { 'B' }
    Copy-Item -LiteralPath (Join-Path $variantRoot 'bin/RimUnity.Embedded.dll') -Destination (Join-Path $fixtures "$destination/1.7/Assemblies/RimUnity.Embedded.dll") -Force
    $scenario = if ($variant -eq 'older') { 'newest' } else { 'conflict' }
    & dotnet (Join-Path $PSScriptRoot 'EmbeddedTests/bin/Release/net10.0/EmbeddedTests.dll') $repo $GameRoot $HarmonyPath $fixtures $scenario
    if ($LASTEXITCODE -ne 0) { throw "候选载荷验证失败：$scenario" }
    Copy-Item -LiteralPath (Join-Path $repo 'artifacts/Embedded/RimUnity.Embedded.dll') -Destination (Join-Path $fixtures "$destination/1.7/Assemblies/RimUnity.Embedded.dll") -Force

    if ($variant -eq 'older') {
        Copy-Item -LiteralPath (Join-Path $variantRoot 'bin/RimUnity.Embedded.dll') -Destination (Join-Path $standaloneDir 'RimUnity.Embedded.dll') -Force
        foreach ($order in @('standalone-older-first','standalone-older-last')) {
            & dotnet (Join-Path $PSScriptRoot 'EmbeddedTests/bin/Release/net10.0/EmbeddedTests.dll') $repo $GameRoot $HarmonyPath $fixtures $order
            if ($LASTEXITCODE -ne 0) { throw "旧独立包选版失败：$order" }
        }
        Copy-Item -LiteralPath (Join-Path $repo 'artifacts/Embedded/RimUnity.Embedded.dll') -Destination (Join-Path $standaloneDir 'RimUnity.Embedded.dll') -Force
        foreach ($target in @('A','B')) {
            Copy-Item -LiteralPath (Join-Path $variantRoot 'bin/RimUnity.Embedded.dll') -Destination (Join-Path $fixtures "$target/1.7/Assemblies/RimUnity.Embedded.dll") -Force
        }
        & dotnet (Join-Path $PSScriptRoot 'EmbeddedTests/bin/Release/net10.0/EmbeddedTests.dll') $repo $GameRoot $HarmonyPath $fixtures 'standalone-newer-first'
        if ($LASTEXITCODE -ne 0) { throw '新版独立包选版失败。' }
        foreach ($target in @('A','B')) {
            Copy-Item -LiteralPath (Join-Path $repo 'artifacts/Embedded/RimUnity.Embedded.dll') -Destination (Join-Path $fixtures "$target/1.7/Assemblies/RimUnity.Embedded.dll") -Force
        }
    }

}
Write-Output "离线加载验证通过。夹具与缓存：$fixtures。未验证游戏内渲染和原生挂载。"
