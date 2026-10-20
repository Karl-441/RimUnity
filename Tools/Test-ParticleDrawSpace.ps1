param([Parameter(Mandatory)][string]$GameRoot, [Parameter(Mandatory)][string]$HarmonyPath, [string]$VertexBaseline)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repo 'Artifacts/verify'
[IO.Directory]::CreateDirectory($output) | Out-Null
$baseline = Join-Path $output 'drawspace-baseline.dll'
# Keep binary stdout as bytes. PowerShell text redirection is not portable for DLLs.
$info = [Diagnostics.ProcessStartInfo]::new()
$info.FileName = 'git'
$info.Arguments = 'show a5abd1ee84d32a88981fade591217c245a0a7ea8:1.7/Assemblies/RimUnity.dll'
$info.WorkingDirectory = $repo
$info.UseShellExecute = $false
$info.RedirectStandardOutput = $true
$info.RedirectStandardError = $true
$process = [Diagnostics.Process]::Start($info)
$stream = [IO.File]::Create($baseline)
try {
    $process.StandardOutput.BaseStream.CopyTo($stream)
    $errorText = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "Cannot extract frozen baseline: $errorText" }
} finally { $stream.Dispose(); $process.Dispose() }
& dotnet run --project (Join-Path $PSScriptRoot 'ParticleDrawSpaceTests') -c Release --no-restore -- `
    (Join-Path $repo '1.7/Assemblies') $GameRoot $HarmonyPath $baseline $VertexBaseline
if ($LASTEXITCODE -ne 0) { throw 'Particle draw-space differential checks failed.' }
