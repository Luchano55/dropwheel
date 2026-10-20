# Скачивает в папку tools/: ffmpeg (GPL-сборка BtbN, свежая release-ветка), ImageMagick 7 portable,
# qpdf (официальная сборка MSVC) и poppler (pdftoppm, сборка oschwartz10612/poppler-windows).
# Результат:
#   tools/ffmpeg/ffmpeg.exe, tools/ffmpeg/ffprobe.exe
#   tools/imagemagick/magick.exe (+ его dll)
#   tools/qpdf/qpdf.exe (+ его dll)
#   tools/poppler/pdftoppm.exe (+ его dll)
param([string]$Destination = "tools")

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$headers = @{ "User-Agent" = "dropwheel-build" }
if ($env:GITHUB_TOKEN) { $headers["Authorization"] = "Bearer $env:GITHUB_TOKEN" }

$dest = New-Item -ItemType Directory -Force -Path $Destination
$work = New-Item -ItemType Directory -Force -Path (Join-Path ([IO.Path]::GetTempPath()) "dropwheel-tools")

# --- ffmpeg ---
$ffRelease = Invoke-RestMethod -Headers $headers "https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/latest"
# Самая новая release-ветка (не master): ffmpeg-nX.Y-latest-win64-gpl-X.Y.zip
$ffAsset = $ffRelease.assets | Where-Object { $_.name -match '^ffmpeg-n[\d.]+-latest-win64-gpl-[\d.]+\.zip$' } |
    Sort-Object { [version]($_.name -replace '^ffmpeg-n([\d.]+)-.*$', '$1') } -Descending | Select-Object -First 1
if (-not $ffAsset) { throw "Не найдена сборка ffmpeg win64-gpl" }
Write-Host "ffmpeg: $($ffAsset.name)"
$ffZip = Join-Path $work "ffmpeg.zip"
Invoke-WebRequest -Headers $headers $ffAsset.browser_download_url -OutFile $ffZip
Expand-Archive $ffZip -DestinationPath (Join-Path $work "ffmpeg") -Force
$ffBin = Get-ChildItem (Join-Path $work "ffmpeg") -Recurse -Filter ffmpeg.exe | Select-Object -First 1
$ffDest = New-Item -ItemType Directory -Force -Path (Join-Path $dest "ffmpeg")
Copy-Item (Join-Path $ffBin.DirectoryName "ffmpeg.exe"), (Join-Path $ffBin.DirectoryName "ffprobe.exe") $ffDest
$ffLicense = Get-ChildItem (Join-Path $work "ffmpeg") -Recurse -Filter LICENSE.txt | Select-Object -First 1
if ($ffLicense) { Copy-Item $ffLicense.FullName (Join-Path $ffDest "LICENSE.txt") }

# --- ImageMagick ---
$imUrl = $null
try {
    $imRelease = Invoke-RestMethod -Headers $headers "https://api.github.com/repos/ImageMagick/ImageMagick/releases/latest"
    $imAsset = $imRelease.assets | Where-Object { $_.name -match 'portable-Q16-x64\.(zip|7z)$' } | Select-Object -First 1
    if ($imAsset) { $imUrl = $imAsset.browser_download_url }
} catch { Write-Host "GitHub API ImageMagick: $_" }
if (-not $imUrl) {
    $listing = Invoke-WebRequest -UseBasicParsing "https://imagemagick.org/archive/binaries/"
    $names = [regex]::Matches($listing.Content, 'ImageMagick-7[\d.\-]+-portable-Q16-x64\.(zip|7z)') | ForEach-Object { $_.Value } | Sort-Object -Unique
    if (-not $names) { throw "Не найден ImageMagick portable" }
    $imUrl = "https://imagemagick.org/archive/binaries/" + ($names | Select-Object -Last 1)
}
Write-Host "ImageMagick: $imUrl"
$imArchive = Join-Path $work ([IO.Path]::GetFileName($imUrl))
Invoke-WebRequest -Headers $headers $imUrl -OutFile $imArchive
$imExtract = Join-Path $work "imagemagick"
if ($imArchive.EndsWith(".7z")) { & 7z x $imArchive "-o$imExtract" -y | Out-Null } else { Expand-Archive $imArchive -DestinationPath $imExtract -Force }
$magick = Get-ChildItem $imExtract -Recurse -Filter magick.exe | Select-Object -First 1
if (-not $magick) { throw "В архиве ImageMagick нет magick.exe" }
$imDest = Join-Path $dest "imagemagick"
if (Test-Path $imDest) { Remove-Item $imDest -Recurse -Force }
Copy-Item $magick.DirectoryName $imDest -Recurse
# В portable-сборке каждая утилита (montage, mogrify, stream…) — отдельная копия по ~30 МБ.
# DropWheel вызывает только magick.exe (а ffmpeg.exe у нас свой).
Get-ChildItem $imDest -Filter *.exe | Where-Object { $_.Name -ne "magick.exe" } | Remove-Item -Force

# Скачать архив из последнего релиза GitHub, распаковать, вернуть папку распаковки.
function Get-GitHubAsset([string]$repo, [string]$pattern, [string]$name) {
    $release = Invoke-RestMethod -Headers $headers "https://api.github.com/repos/$repo/releases/latest"
    $asset = $release.assets | Where-Object { $_.name -match $pattern } | Select-Object -First 1
    if (-not $asset) { throw "Не найдена сборка $name ($repo, $pattern)" }
    Write-Host "${name}: $($asset.name)"
    $zip = Join-Path $work "$name.zip"
    Invoke-WebRequest -Headers $headers $asset.browser_download_url -OutFile $zip
    $extract = Join-Path $work $name
    if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
    Expand-Archive $zip -DestinationPath $extract -Force
    return $extract
}

# Из папки bin берём только нужный exe и все dll (+ файлы лицензии, если есть в архиве).
function Copy-ToolBin([string]$extract, [string]$exeName, [string]$targetName) {
    $exe = Get-ChildItem $extract -Recurse -Filter $exeName | Select-Object -First 1
    if (-not $exe) { throw "В архиве нет $exeName" }
    $target = Join-Path $dest $targetName
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    $target = New-Item -ItemType Directory -Force -Path $target
    Copy-Item $exe.FullName $target
    Get-ChildItem $exe.DirectoryName -Filter *.dll | Copy-Item -Destination $target
    Get-ChildItem $extract -Recurse -File | Where-Object { $_.Name -match '^(LICENSE|COPYING|NOTICE)' } |
        Select-Object -First 3 | ForEach-Object { Copy-Item $_.FullName (Join-Path $target $_.Name) -Force }
    return (Join-Path $target $exeName)
}

# --- qpdf (склеить / разбить / оптимизировать PDF) ---
$qpdfExtract = Get-GitHubAsset "qpdf/qpdf" '-msvc64\.zip$' "qpdf"
$qpdfExe = Copy-ToolBin $qpdfExtract "qpdf.exe" "qpdf"

# --- poppler: pdftoppm (страницы PDF → PNG/JPG) ---
$popplerExtract = Get-GitHubAsset "oschwartz10612/poppler-windows" '^Release-.*\.zip$' "poppler"
$pdftoppmExe = Copy-ToolBin $popplerExtract "pdftoppm.exe" "poppler"

& (Join-Path $ffDest "ffmpeg.exe") -hide_banner -version | Select-Object -First 1
& (Join-Path $imDest "magick.exe") -version | Select-Object -First 1
& $qpdfExe --version | Select-Object -First 1
# pdftoppm печатает версию в stderr.
$ErrorActionPreference = "Continue"
& $pdftoppmExe -v 2>&1 | ForEach-Object { "$_" } | Select-Object -First 1
