# Verify-Static 的负向自测：证明「规则真的会失败」，而不只是「跑完是绿的」。
#
# 做法：用 `git clone --shared` 建一个临时工作树（对象共享，秒级），在副本里改坏一处、
# 跑一次检查器，断言退出码非零且输出里出现对应规则名；每个用例后用 `git checkout` 还原。
# 基线用例先证明「未改动的副本是通过的」，否则后面的失败没有意义。
#
# 覆盖：ps1-bom（删 BOM）、release-version（改发布号）、parse-config（写坏 JSON）、
# absolute-path（加一行盘符路径）、conflict-markers（加一行冲突标记）。
param([switch]$KeepClone)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

$repo = Split-Path -Parent $PSScriptRoot
$clone = Join-Path ([IO.Path]::GetTempPath()) ('RimUnity/VerifyStatic-' + [Guid]::NewGuid().ToString('N'))
$script:Failures = 0
$hostExe = if ($PSVersionTable.PSEdition -eq 'Core') { Join-Path $PSHOME 'pwsh.exe' } else { Join-Path $PSHOME 'powershell.exe' }
if (-not (Test-Path -LiteralPath $hostExe -PathType Leaf)) { throw "找不到当前宿主的可执行文件：$hostExe" }

function Invoke-Checker {
    $output = & $hostExe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $clone 'Tools/Verify-Static.ps1') 2>&1
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output | Out-String) }
}

function Invoke-Case {
    param([string]$Name, [string]$Rule, [scriptblock]$Mutate)
    try {
        & $Mutate
        $result = Invoke-Checker
        if ($result.ExitCode -eq 0) { throw '期望检查失败，实际退出码 0' }
        if ($result.Output -notmatch [regex]::Escape("[$Rule]")) { throw "输出里没有 [$Rule]`n$($result.Output)" }
        Write-Host "PASS  $Name"
    } catch {
        $script:Failures++
        Write-Host "FAIL  $Name -> $($_.Exception.Message)"
    } finally {
        & git -C $clone checkout -- . 2>&1 | Out-Null
    }
}

try {
    & git clone --quiet --shared --no-hardlinks $repo $clone
    if ($LASTEXITCODE -ne 0) { throw "git clone 失败：$clone" }

    # 基线：未改动的副本必须通过，否则负向用例说明不了任何问题。
    $baseline = Invoke-Checker
    if ($baseline.ExitCode -ne 0) { throw "基线未通过（负向用例失去意义）：`n$($baseline.Output)" }
    Write-Host 'PASS  基线：未改动的副本通过'

    Invoke-Case '删掉 BOM → ps1-bom 失败' 'ps1-bom' {
        $file = Join-Path $clone 'Tools/Verify.ps1'
        $bytes = [IO.File]::ReadAllBytes($file)
        [IO.File]::WriteAllBytes($file, [byte[]]$bytes[3..($bytes.Length - 1)])
    }

    Invoke-Case '改一处发布号 → release-version 失败' 'release-version' {
        $file = Join-Path $clone 'About/About.xml'
        $text = [regex]::Replace([IO.File]::ReadAllText($file), '<modVersion>[^<]+</modVersion>', '<modVersion>9.9.9</modVersion>')
        [IO.File]::WriteAllText($file, $text, [Text.UTF8Encoding]::new($true))
    }

    Invoke-Case '写坏一个 JSON → parse-config 失败' 'parse-config' {
        [IO.File]::WriteAllText((Join-Path $clone 'UnityPackage/package.json'), '{', [Text.UTF8Encoding]::new($false))
    }

    Invoke-Case '加一行盘符绝对路径 → absolute-path 失败' 'absolute-path' {
        $file = Join-Path $clone 'Tools/Verify.ps1'
        $text = [IO.File]::ReadAllText($file)
        # 盘符现场拼出来，避免脚本自身在门禁里被 absolute-path 判成违例。
        $drive = [string][char]68
        [IO.File]::WriteAllText($file, $text + "`n# " + $drive + ":\temp\probe`n", [Text.UTF8Encoding]::new($true))
    }

    Invoke-Case '加一行冲突标记 → conflict-markers 失败' 'conflict-markers' {
        $file = Join-Path $clone 'README.md'
        $text = [IO.File]::ReadAllText($file)
        [IO.File]::WriteAllText($file, $text + "`n<<<<<<< HEAD`n", [Text.UTF8Encoding]::new($false))
    }
} finally {
    if (Test-Path -LiteralPath $clone) {
        if ($KeepClone) { Write-Host "保留临时副本：$clone" }
        else { Remove-Item -LiteralPath $clone -Recurse -Force }
    }
}

if ($script:Failures -gt 0) { throw "Verify-Static 负向自测失败：$($script:Failures) 个用例" }
Write-Host 'Verify-Static 负向自测全部通过（基线 + 5 个用例）'
