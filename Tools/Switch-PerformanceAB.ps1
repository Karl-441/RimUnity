param(
    [Parameter(Mandatory)][string]$Bundle,
    [ValidateSet('Inspect','A','B')][string]$Variant = 'Inspect'
)
$ErrorActionPreference = 'Stop'
$Bundle = [IO.Path]::GetFullPath($Bundle)
$manifest = Get-Content -LiteralPath (Join-Path $Bundle 'bundle.json') -Raw | ConvertFrom-Json
if ($manifest.Schema -ne 1 -or @($manifest.Files).Count -eq 0) { throw 'Unsupported or empty A/B bundle.' }
function Get-SourcePath([string]$relative) {
    $path = [IO.Path]::GetFullPath((Join-Path $Bundle $relative))
    if (!$path.StartsWith($Bundle.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Bundle source escapes its directory: $relative"
    }
    return $path
}
$targets = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($file in $manifest.Files) {
    if (![IO.Path]::IsPathFullyQualified($file.Target) -or !$targets.Add([IO.Path]::GetFullPath($file.Target))) {
        throw "Invalid or duplicate target: $($file.Target)"
    }
    foreach ($side in @('A','B')) {
        $source = Get-SourcePath $file.$side.Source
        if ((Get-FileHash -LiteralPath $source).Hash -ne $file.$side.Sha256) { throw "Bundle hash mismatch: $source" }
    }
    $actual = (Get-FileHash -LiteralPath $file.Target).Hash
    if ($actual -ne $file.A.Sha256 -and $actual -ne $file.B.Sha256) {
        throw "Target differs from both pinned versions; refusing to overwrite newer or unrelated files: $($file.Target)"
    }
    $state = if ($file.A.Sha256 -eq $file.B.Sha256) { 'Shared' } elseif ($actual -eq $file.A.Sha256) { 'A' } else { 'B' }
    Write-Output "$state $($file.Target)"
}
if ($Variant -eq 'Inspect') { return }
if (Get-Process RimWorld,RimWorldWin64 -ErrorAction SilentlyContinue) { throw 'Exit RimWorld before switching A/B versions.' }
# Validate every input before mutation. Keep a recovery copy of every current file.
# A stopped copy can be retried: only hashes from the two pinned versions are accepted.
$backup = Join-Path $Bundle ('switch-backups/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($backup) | Out-Null
$index = 0
foreach ($file in $manifest.Files) {
    $name = "$index.bin"
    Copy-Item -LiteralPath $file.Target -Destination (Join-Path $backup $name)
    [pscustomobject]@{Target=$file.Target; Backup=$name; Sha256=(Get-FileHash -LiteralPath $file.Target).Hash} |
        ConvertTo-Json -Compress | Add-Content -LiteralPath (Join-Path $backup 'manifest.jsonl') -Encoding utf8
    $index++
}
foreach ($file in $manifest.Files) {
    # Recheck immediately before each replacement in case another deployment intervened.
    $current = (Get-FileHash -LiteralPath $file.Target).Hash
    if ($current -ne $file.A.Sha256 -and $current -ne $file.B.Sha256) { throw "Target changed during switch: $($file.Target)" }
    Copy-Item -LiteralPath (Get-SourcePath $file.$Variant.Source) -Destination $file.Target -Force
    if ((Get-FileHash -LiteralPath $file.Target).Hash -ne $file.$Variant.Sha256) { throw "Copy verification failed: $($file.Target)" }
}
[pscustomobject]@{Utc=[DateTime]::UtcNow.ToString('o'); Variant=$Variant; Backup=$backup; RuntimeMvid=$manifest.$Variant.RuntimeMvid; PayloadSha256=$manifest.$Variant.PayloadSha256} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Bundle 'last-switch.json') -Encoding utf8
Write-Output "Selected $Variant; Runtime MVID=$($manifest.$Variant.RuntimeMvid); payload=$($manifest.$Variant.PayloadSha256); backup=$backup"
