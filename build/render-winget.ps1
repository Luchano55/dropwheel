# Готовит манифесты winget для конкретной сборки (решение D-RELEASE, docs/winget.md).
# Берёт шаблон packaging/winget/<версия>/ (нет такой папки — самую новую из имеющихся), подставляет версию,
# адрес установщика, его SHA256 и дату, убирает служебные комментарии и кладёт результат в
# <OutDir>/manifests/l/Luchano55/DropWheel/<версия>/ — ровно так, как в репозитории microsoft/winget-pkgs.
param(
  [Parameter(Mandatory)] [string] $Version,
  [Parameter(Mandatory)] [string] $InstallerUrl,
  [Parameter(Mandatory)] [string] $Sha256,
  [Parameter(Mandatory)] [string] $OutDir,
  [string] $ReleaseNotesUrl = "",
  [string] $TemplateRoot = "packaging/winget",
  [string] $ReleaseDate = (Get-Date -Format "yyyy-MM-dd")
)
$ErrorActionPreference = "Stop"

$template = Join-Path $TemplateRoot $Version
if (-not (Test-Path $template)) {
  $latest = Get-ChildItem $TemplateRoot -Directory |
    Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' } |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
  if (-not $latest) { throw "Нет шаблонов манифестов winget в $TemplateRoot" }
  $template = $latest.FullName
  Write-Host "Шаблона для $Version нет — беру $($latest.Name)"
}

$target = Join-Path $OutDir "manifests/l/Luchano55/DropWheel/$Version"
New-Item -ItemType Directory $target -Force | Out-Null
$utf8 = [System.Text.UTF8Encoding]::new($false)

foreach ($file in Get-ChildItem $template -Filter *.yaml) {
  $lines = [System.IO.File]::ReadAllLines($file.FullName, $utf8)
  $out = foreach ($line in $lines) {
    # Комментарии шаблона (по-русски, для разработчика) в winget-pkgs не нужны; ссылку на схему оставляем.
    if ($line -match '^\s*#' -and $line -notmatch 'yaml-language-server') { continue }
    $line = $line -replace '^PackageVersion: .*$', "PackageVersion: $Version"
    $line = $line -replace '^(\s*)InstallerUrl: .*$', "`${1}InstallerUrl: $InstallerUrl"
    $line = $line -replace '^(\s*)InstallerSha256: .*$', "`${1}InstallerSha256: $($Sha256.ToUpperInvariant())"
    $line = $line -replace '^ReleaseDate: .*$', "ReleaseDate: $ReleaseDate"
    if ($ReleaseNotesUrl) { $line = $line -replace '^ReleaseNotesUrl: .*$', "ReleaseNotesUrl: $ReleaseNotesUrl" }
    $line
  }
  # Пустые строки подряд после удалённых комментариев — схлопнуть.
  $text = (($out -join "`n") -replace "`n{3,}", "`n`n").TrimStart("`n") + "`n"
  if ($text -match '<filled by CI>') { throw "В $($file.Name) осталась заглушка <filled by CI>" }
  [System.IO.File]::WriteAllText((Join-Path $target $file.Name), $text, $utf8)
  Write-Host "---- $($file.Name)"
  Write-Host $text
}

Write-Host "Манифесты winget: $target"
