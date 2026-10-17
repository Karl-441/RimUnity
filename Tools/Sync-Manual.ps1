param([switch]$Check)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$manual = Join-Path $repo 'UnityPackage/Documentation~'
$files = @('README.md', 'README.en.md', 'UnityPackage/README.md', 'UnityPackage/README.en.md', 'Tools/Steam/README.md', '1.6/README.txt')
# 只取被 git 跟踪的文档：未跟踪的本地笔记不该进离线镜像，也不该让 -Check 误判。
# 顺带避开 [IO.Path]::GetRelativePath——它只存在于 .NET Core，Windows PowerShell 5.1 上没有。
$files += @(& git -C $repo -c core.quotePath=false ls-files -- 'Docs/*.md')
# 排序也必须固定：Sort-Object 用的是当前区域性的比较器，PowerShell 5.1（zh-CN）和 PowerShell 7
# 会把 Docs/显存归责.md 排在完全不同的位置，于是两个宿主互相判定成“索引未同步”。
$ordered = [string[]]@($files | Sort-Object -Unique)
[Array]::Sort($ordered, [StringComparer]::Ordinal)
$index = @()
foreach ($file in $ordered) {
    $source = Join-Path $repo $file
    $destination = Join-Path $manual "Sources/$file"
    if ($Check) {
        if (!(Test-Path -LiteralPath $destination) -or (Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $destination).Hash) { throw "离线文档未同步：$file" }
    } else {
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination -Force
    }
    $text = [IO.File]::ReadAllText($source)
    $title = [regex]::Match($text, '(?m)^# (.+)').Groups[1].Value.Trim()
    if (!$title) { $title = [IO.Path]::GetFileName($file) }
    $category = if ($file -match 'README.en|Docs/en/') { '原文·English' } elseif ($file -match 'Docs/zh-CN/|(^|/)README.md$') { '原文·中文' } else { '其他原文' }
    $index += [ordered]@{ title = "$title · $file"; source = $file; file = "Sources/$file"; category = $category }
}
$indexPath = Join-Path $manual 'sources.json'

# 自己拼 JSON，不用 ConvertTo-Json：Windows PowerShell 5.1 的 ConvertTo-Json 用 4 空格缩进、
# 冒号后两个空格，还会把非 ASCII 写成 \uXXXX，和 PowerShell 7 的输出不一致，
# 会让人在两个宿主下互相判定成“离线文档索引未同步”。这里固定成现有文件的格式。
function ConvertTo-MirrorJsonString([string]$value) {
    $builder = [Text.StringBuilder]::new()
    foreach ($ch in $value.ToCharArray()) {
        if ($ch -eq '"') { [void]$builder.Append('\"') }
        elseif ($ch -eq '\') { [void]$builder.Append('\\') }
        elseif ([int]$ch -lt 0x20) { [void]$builder.AppendFormat('\u{0:x4}', [int]$ch) }
        else { [void]$builder.Append($ch) }
    }
    return '"' + $builder.ToString() + '"'
}
$lines = @('[')
for ($i = 0; $i -lt $index.Count; $i++) {
    $entry = $index[$i]
    $lines += '  {'
    $lines += '    "title": ' + (ConvertTo-MirrorJsonString $entry.title) + ','
    $lines += '    "source": ' + (ConvertTo-MirrorJsonString $entry.source) + ','
    $lines += '    "file": ' + (ConvertTo-MirrorJsonString $entry.file) + ','
    $lines += '    "category": ' + (ConvertTo-MirrorJsonString $entry.category)
    $tail = if ($i -lt $index.Count - 1) { '  },' } else { '  }' }
    $lines += $tail
}
$lines += ']'
$json = if ($index.Count -eq 0) { '[]' } else { $lines -join "`n" }
if ($Check) {
    if (([IO.File]::ReadAllText($indexPath).Replace("`r`n", "`n").Trim()) -ne $json.Replace("`r`n", "`n").Trim()) { throw '离线文档索引未同步。' }
} else { [IO.File]::WriteAllText($indexPath, $json + "`n", [Text.UTF8Encoding]::new($false)) }
Write-Output "已核对 $($index.Count) 篇原始文档。"
