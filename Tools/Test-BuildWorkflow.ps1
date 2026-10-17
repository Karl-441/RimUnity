# Offline orchestration regressions: no SDK, game DLLs or deployment required.
$ErrorActionPreference = 'Stop'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('RimUnity/Workflow-' + [Guid]::NewGuid().ToString('N'))
$tools = Join-Path $fixture 'Tools'
[IO.Directory]::CreateDirectory($tools) | Out-Null
foreach ($name in @('Build.ps1', 'Verify.ps1', 'Pack.ps1', 'Resolve-BuildPath.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $tools
}
function Put-File([string]$relative, [string]$value = '') {
    $path = Join-Path $fixture $relative
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
    [IO.File]::WriteAllText($path, $value, [Text.UTF8Encoding]::new($true))
}
function Assert([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}
$global:rimUnityWorkflowTestState = @{ Calls = [Collections.Generic.List[string]]::new(); FailBuild = $false }
function dotnet {
    $global:rimUnityWorkflowTestState.Calls.Add(($args -join '|'))
    $global:LASTEXITCODE = if ($global:rimUnityWorkflowTestState.FailBuild -and $args[0] -eq 'build') { 1 } else { 0 }
}
$game = Join-Path $fixture 'game'
$harmony = Join-Path $fixture 'game/0Harmony.dll'
foreach ($name in @('Assembly-CSharp.dll', 'Waylith.dll', '0Harmony.dll')) { Put-File "game/$name" }
Put-File 'Tools/bin/ShaderTool/RimUnity.ShaderTool.dll'
Put-File '1.7/Assemblies/RimUnity.dll'
Put-File '1.7/Shaders/test.glsl'
Put-File 'Tools/Verify-Embedded.ps1' 'param($GameRoot, $HarmonyPath); $global:rimUnityWorkflowTestState.Calls.Add("embedded-tests")'
try {
    & (Join-Path $tools 'Verify.ps1') -GameRoot $game -HarmonyPath $harmony
    Assert ($global:rimUnityWorkflowTestState.Calls.Count -eq 3) 'Verify must build runtime/tool once each, then verify shaders.'
    Assert ($global:rimUnityWorkflowTestState.Calls[0] -like 'build*RimUnity.Spine.csproj*') 'Runtime must build first.'
    Assert ($global:rimUnityWorkflowTestState.Calls[1] -like 'build*RimUnity.ShaderTool.csproj*') 'Tool must build once.'
    Assert ($global:rimUnityWorkflowTestState.Calls[2] -like '*|verify|*') 'Shader check must follow builds.'

    $global:rimUnityWorkflowTestState.Calls.Clear()
    & (Join-Path $tools 'Verify.ps1') -GameRoot $game -HarmonyPath $harmony -SkipBuild -Embedded
    Assert ($global:rimUnityWorkflowTestState.Calls.Count -eq 2 -and $global:rimUnityWorkflowTestState.Calls[1] -eq 'embedded-tests') 'SkipBuild must skip release builds and still run requested embedded tests.'

    $global:rimUnityWorkflowTestState.Calls.Clear()
    & (Join-Path $tools 'Build.ps1') -GameRoot $game -HarmonyPath $harmony -SkipEmbedded
    Assert ($global:rimUnityWorkflowTestState.Calls.Count -eq 2 -and $global:rimUnityWorkflowTestState.Calls[0] -like '*SkipEmbedded=true*') 'Build must forward SkipEmbedded and still build ShaderTool.'

    $global:rimUnityWorkflowTestState.Calls.Clear()
    $global:rimUnityWorkflowTestState.FailBuild = $true
    $caught = $false
    try { & (Join-Path $tools 'Verify.ps1') -GameRoot $game -HarmonyPath $harmony } catch { $caught = $true }
    Assert ($caught -and $global:rimUnityWorkflowTestState.Calls.Count -eq 1) 'Failed build must stop verification before later stages.'
    $global:rimUnityWorkflowTestState.FailBuild = $false

    # A missing embedded input must fail before archives or output directories exist.
    Put-File 'Tools/Sync-Manual.ps1' 'param([switch]$Check); if (-not $Check) { throw "Pack must never update documentation" }'
    foreach ($name in @('RimUnity.Core.dll', 'RimUnity.Spine.dll')) { Put-File "1.7/Assemblies/$name" }
    foreach ($name in @('RimUnity.ShaderTool.exe', 'RimUnity.ShaderTool.deps.json', 'RimUnity.ShaderTool.runtimeconfig.json', 'RimUnity.ShaderTool.pdb')) { Put-File "Tools/bin/ShaderTool/$name" }
    $output = Join-Path $fixture 'release'
    $caught = $false
    try { & (Join-Path $tools 'Pack.ps1') -Output $output } catch { $caught = $_.Exception.Message -like '*RimUnity.Embedded.dll*' }
    Assert ($caught -and -not (Test-Path -LiteralPath $output)) 'Missing embedded package must fail before packaging.'

    Put-File 'Tools/Sync-Manual.ps1' 'param([switch]$Check); if ($Check) { throw "stale-mirror" }; throw "Unexpected mirror mutation"'
    $caught = $false
    try { & (Join-Path $tools 'Pack.ps1') -Output $output } catch { $caught = $_.Exception.Message -eq 'stale-mirror' }
    Assert ($caught -and -not (Test-Path -LiteralPath $output)) 'Stale documentation must fail without regenerating it.'
    # The expected failure above leaves LASTEXITCODE=1. Actions propagates it even
    # when all assertions pass; clear it only after the complete suite succeeds.
    $global:LASTEXITCODE = 0
    Write-Output 'PASS: build/verify ordering, SkipBuild, SkipEmbedded, failure propagation, package preflight and read-only mirror check.'
} finally {
    # Leave the small fixture for inspection; no recursive deletion of computed paths.
    Write-Output "Workflow fixture: $fixture"
}
