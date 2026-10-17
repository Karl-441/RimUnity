param([Parameter(Mandatory)][string]$GameRoot, [Parameter(Mandatory)][string]$FixtureRoot, [string]$LegacyCarrier = '', [string]$WorkshopRoot = '')
$ErrorActionPreference = 'Stop'
if ($WorkshopRoot -and !(Test-Path -LiteralPath (Join-Path $WorkshopRoot '1.7/Assemblies/RimUnity.dll'))) { throw '工坊运行时目录无效。' }
if ($LegacyCarrier -and !(Test-Path -LiteralPath $LegacyCarrier -PathType Leaf)) { throw '历史加载器文件不存在。' }
if (Get-Process RimWorld,RimWorldWin64 -ErrorAction SilentlyContinue) { throw '游戏正在运行；请结束当前会话后再执行测试。' }
$repo = Split-Path $PSScriptRoot -Parent
$run = Join-Path ([IO.Path]::GetTempPath()) ('RimUnity/GameVerification-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($run) | Out-Null
$modsRoot = [IO.Path]::GetFullPath((Join-Path $GameRoot 'Mods'))
$live = [IO.Path]::GetFullPath((Join-Path $modsRoot 'RimUnity'))
$backup = [IO.Path]::GetFullPath((Join-Path $run 'Original-RimUnity'))
$testMods = @('A','B','C','Ordinary') | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $modsRoot "RimUnity.EmbeddedVerification.$_")) }
#所有移动、删除目标都先核对绝对路径；原模组备份在游戏扫描目录之外。
foreach ($path in @($live) + $testMods) {
    if ([IO.Path]::GetDirectoryName($path) -ne $modsRoot) { throw "测试路径越界：$path" }
}
if (!$backup.StartsWith([IO.Path]::GetFullPath($run) + [IO.Path]::DirectorySeparatorChar)) { throw '备份路径越界。' }
foreach ($path in $testMods) { if (Test-Path -LiteralPath $path) { throw "已有测试目录，未覆盖：$path" } }
if ((Test-Path -LiteralPath $live) -and (Get-Item -LiteralPath $live).LinkType) { throw '测试不会替换链接形式的 RimUnity；请指定独立测试安装。' }
$logs = Join-Path $env:USERPROFILE 'AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios/Logs'
if (Test-Path -LiteralPath $logs) { Copy-Item -LiteralPath $logs -Destination (Join-Path $run 'Logs-before') -Recurse }
$hadOriginal = Test-Path -LiteralPath $live
$movedOriginal = $false
$liveCreated = $false
$process = $null
#进程退出后杀毒/文件映射可能短暂占用 DLL；清理失败仍必须恢复原独立版。
function Remove-TestDirectory([string]$path) {
    $absolute = [IO.Path]::GetFullPath($path)
    if ([IO.Path]::GetDirectoryName($absolute) -ne $modsRoot) { throw "清理路径越界：$path" }
    for ($attempt = 0; $attempt -lt 10; $attempt++) {
        try {
            if (Test-Path -LiteralPath $absolute) { Remove-Item -LiteralPath $absolute -Recurse -Force }
            return
        } catch [IO.IOException], [UnauthorizedAccessException] {
            if ($attempt -eq 9) { throw }
            Start-Sleep -Milliseconds 500
        }
    }
}
try {
    if ($hadOriginal) { Move-Item -LiteralPath $live -Destination $backup; $movedOriginal = $true }
    $liveCreated = $true
    & (Join-Path $PSScriptRoot 'Copy-RuntimePackage.ps1') -Destination $live
    for ($i = 0; $i -lt 3; $i++) {
        $letter = @('A','B','C')[$i]
        $target = $testMods[$i]
        if ($letter -ne 'C') { Copy-Item -LiteralPath (Join-Path $FixtureRoot $letter) -Destination $target -Recurse }
        foreach ($folder in @('About','1.7/Assemblies','1.7/Defs')) { [IO.Directory]::CreateDirectory((Join-Path $target $folder)) | Out-Null }
        $id = $letter.ToLowerInvariant()
        [IO.File]::WriteAllText((Join-Path $target 'About/About.xml'), "<ModMetaData><name>RimUnity Embedded Test $letter</name><author>RimUnity verification</author><packageId>test.$id</packageId><supportedVersions><li>1.7</li></supportedVersions><description>Temporary embedded runtime verification fixture.</description><modDependencies><li><packageId>brrainz.harmony</packageId><displayName>Harmony</displayName></li></modDependencies></ModMetaData>", [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $target 'LoadFolders.xml'), '<loadFolders><v1.7><li>1.7</li></v1.7></loadFolders>', [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $target '1.7/Defs/Marker.xml'), "<Defs><Fixture.MarkerDef><defName>EmbeddedTest_$id</defName></Fixture.MarkerDef></Defs>", [Text.UTF8Encoding]::new($false))
        $carrier = if ($letter -eq 'A' -and $LegacyCarrier) { $LegacyCarrier } else { Join-Path $repo 'artifacts/Embedded/RimUnity.Embedded.dll' }
        Copy-Item -LiteralPath $carrier -Destination (Join-Path $target '1.7/Assemblies/RimUnity.Embedded.dll') -Force
    }
    $ordinary = $testMods[3]
    Copy-Item -LiteralPath (Join-Path $FixtureRoot 'Ordinary') -Destination $ordinary -Recurse
    [IO.Directory]::CreateDirectory((Join-Path $ordinary 'About')) | Out-Null
    [IO.File]::WriteAllText((Join-Path $ordinary 'About/About.xml'), '<ModMetaData><name>RimUnity Ordinary DLL Test</name><author>RimUnity verification</author><packageId>test.ordinary</packageId><supportedVersions><li>1.7</li></supportedVersions><description>Ordinary DLL compatibility fixture.</description><modDependencies><li><packageId>Chezhou.RimUnity</packageId><displayName>RimUnity</displayName></li></modDependencies><loadAfter><li>Chezhou.RimUnity</li></loadAfter></ModMetaData>', [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $ordinary 'LoadFolders.xml'), '<loadFolders><v1.7><li>1.7</li></v1.7></loadFolders>', [Text.UTF8Encoding]::new($false))
    $modes = @('standalone','embedded')
    if ($WorkshopRoot) { $modes += 'workshop' }
    foreach ($mode in $modes) {
        $save = Join-Path $run $mode
        [IO.Directory]::CreateDirectory((Join-Path $save 'Config')) | Out-Null
        $standalone = if ($mode -eq 'standalone') { '<li>chezhou.rimunity</li><li>test.ordinary</li>' } elseif ($mode -eq 'workshop') { '<li>chezhou.rimunity_steam</li><li>test.ordinary</li>' } else { '' }
        [IO.File]::WriteAllText((Join-Path $save 'Config/ModsConfig.xml'), "<ModsConfigData><version>1.7.4980 rev60</version><activeMods><li>brrainz.harmony</li><li>test.a</li>$standalone<li>ludeon.rimworld</li><li>test.b</li><li>test.c</li></activeMods></ModsConfigData>", [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $save 'Config/Prefs.xml'), '<PrefsData><screenWidth>1024</screenWidth><screenHeight>768</screenHeight><fullscreen>False</fullscreen><volumeMaster>0</volumeMaster><langFolderName>English</langFolderName><devMode>True</devMode></PrefsData>', [Text.UTF8Encoding]::new($false))
        $process = Start-Process -FilePath (Join-Path $GameRoot 'RimWorld.exe') -WorkingDirectory $GameRoot -ArgumentList "-savedatafolder=$save", '-RimUnityEmbeddedTest' -WindowStyle Hidden -PassThru
        Write-Output "启动测试：$mode；PID=$($process.Id)；数据目录=$save"
        $timer = [Diagnostics.Stopwatch]::StartNew()
        while (!$process.HasExited -and $timer.Elapsed.TotalSeconds -lt 240) { Start-Sleep -Seconds 2; $process.Refresh() }
        if (!$process.HasExited) { throw "测试进程超时：$mode" }
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) { throw "游戏测试异常退出：$mode；exit=$($process.ExitCode)；目录=$save" }
        if (Test-Path -LiteralPath $logs) { Copy-Item -LiteralPath $logs -Destination (Join-Path $save 'Logs') -Recurse }
        $reportPath = Join-Path $save 'RimUnity-embedded-report.json'
        if (!(Test-Path -LiteralPath $reportPath)) { throw "游戏未生成测试报告：$save" }
        $report = Get-Content -LiteralPath $reportPath -Encoding UTF8 -Raw | ConvertFrom-Json
        $expectedSource = if ($mode -eq 'embedded') { 'embedded' } else { 'standalone' }
        if (!$report.passed -or $report.source -ne $expectedSource -or $report.copies -ne 3) { throw "游戏测试未通过：$reportPath" }
        $hasCache = Test-Path -LiteralPath (Join-Path $save 'RimUnity/Embedded')
        if ($hasCache -ne ($mode -eq 'embedded')) { throw '独立版不应释放内置载荷；内置版应创建资源缓存。' }
        if ($mode -ne 'embedded' -and !$report.ordinaryDll) { throw '普通 DLL 未通过真实游戏兼容验证。' }
        if ($mode -eq 'workshop') {
            if ($report.runtimeOwner -ne 'chezhou.rimunity_steam' -or
                [IO.Path]::GetFullPath($report.resourceRoot) -ne [IO.Path]::GetFullPath((Join-Path $WorkshopRoot '1.7'))) {
                throw "没有使用指定的工坊副本：$reportPath"
            }
        }
        Write-Output "PASS 游戏启动：$mode；3 个内置测试模组；报告=$reportPath"
    }
} finally {
    if ($process -and !$process.HasExited) { Stop-Process -Id $process.Id -Force }
    #只清理本次预先核对过、由本脚本创建的路径，并恢复原前置。
    try {
        foreach ($path in $testMods) { Remove-TestDirectory $path }
    } finally {
        if ($liveCreated) { Remove-TestDirectory $live }
        if ($movedOriginal) { Move-Item -LiteralPath $backup -Destination $live }
    }
    Write-Output "原模组已恢复，测试资料保留：$run"
}
