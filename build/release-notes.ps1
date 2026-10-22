# Текст релиза из docs/CHANGELOG.md: раздел «## <версия>» до следующего «## ». Решение D-RELEASE.
# Раздела нет — короткий текст по умолчанию и предупреждение в журнале CI (релиз всё равно создаётся).
param(
  [Parameter(Mandatory)] [string] $Version,
  [Parameter(Mandatory)] [string] $OutFile,
  [string] $Changelog = "docs/CHANGELOG.md",
  [string] $Footer = ""
)
$ErrorActionPreference = "Stop"

$section = @()
if (Test-Path $Changelog) {
  $inside = $false
  foreach ($line in [System.IO.File]::ReadAllLines((Resolve-Path $Changelog).Path, [System.Text.Encoding]::UTF8)) {
    if ($line -match '^##\s+\[?v?([^\]\s]+)\]?') {
      if ($inside) { break }
      $inside = $Matches[1] -eq $Version
      continue
    }
    if ($inside) { $section += $line }
  }
}

$text = ($section -join "`n").Trim()
if (-not $text) {
  Write-Warning "В $Changelog нет раздела '## $Version' — описание релиза будет коротким"
  $text = "DropWheel $Version."
}
if ($Footer) { $text += "`n`n$Footer" }
[System.IO.File]::WriteAllText($OutFile, $text + "`n", [System.Text.UTF8Encoding]::new($false))
Write-Host "---- Описание релиза $Version"
Write-Host $text
