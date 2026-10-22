# Скачивает в папку tools/: ffmpeg (GPL-сборка BtbN, свежая release-ветка), ImageMagick 7 portable,
# qpdf (официальная сборка MSVC), poppler (pdftoppm и pdftotext, сборка oschwartz10612/poppler-windows)
# и 7-Zip (официальная сборка с GitHub ip7z/7zip).
# Результат:
#   tools/ffmpeg/ffmpeg.exe, tools/ffmpeg/ffprobe.exe
#   tools/imagemagick/magick.exe (+ его dll)
#   tools/qpdf/qpdf.exe (+ его dll)
#   tools/poppler/pdftoppm.exe, tools/poppler/pdftotext.exe (+ их dll)
#   tools/7zip/7z.exe, tools/7zip/7z.dll, tools/7zip/License.txt
param([string]$Destination = "tools")

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$headers = @{ "User-Agent" = "dropwheel-build" }
if ($env:GITHUB_TOKEN) { $headers["Authorization"] = "Bearer $env:GITHUB_TOKEN" }

$dest = New-Item -ItemType Directory -Force -Path $Destination
$work = New-Item -ItemType Directory -Force -Path (Join-Path ([IO.Path]::GetTempPath()) "dropwheel-tools")

# --- ffmpeg ---
# Самая новая release-ветка (не master) из релиза «latest»: ffmpeg-nX.Y-latest-win64-gpl-X.Y.zip.
# Релиз «latest» пересобирается ежедневно, и пока идёт выкладка, файлов в нём может не быть — тогда берём
# такую же сборку из последних ежедневных релизов autobuild-…: ffmpeg-nX.Y.Z-…-win64-gpl-X.Y.zip.
function Find-FfmpegAsset($release) {
    $release.assets | Where-Object { $_.name -match '^ffmpeg-n[\d.]+-[\w.-]+-win64-gpl-[\d.]+\.zip$' } |
        Sort-Object { [version]($_.name -replace '^ffmpeg-n([\d.]+)-.*$', '$1') } -Descending | Select-Object -First 1
}
$ffAsset = Find-FfmpegAsset (Invoke-RestMethod -Headers $headers "https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/latest")
if (-not $ffAsset) {
    Write-Warning "В релизе ffmpeg latest нет сборки win64-gpl — ищу в ежедневных"
    foreach ($ffRelease in (Invoke-RestMethod -Headers $headers "https://api.github.com/repos/BtbN/FFmpeg-Builds/releases?per_page=10")) {
        if ($ffRelease.tag_name -notlike 'autobuild-*') { continue }
        $ffAsset = Find-FfmpegAsset $ffRelease
        if ($ffAsset) { break }
    }
}
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
# pdftotext (PDF → TXT) лежит в той же папке bin — его dll уже скопированы вместе с pdftoppm.
$pdftotextSrc = Get-ChildItem $popplerExtract -Recurse -Filter pdftotext.exe | Select-Object -First 1
if (-not $pdftotextSrc) { throw "В архиве poppler нет pdftotext.exe" }
Copy-Item $pdftotextSrc.FullName (Split-Path $pdftoppmExe)
$pdftotextExe = Join-Path (Split-Path $pdftoppmExe) "pdftotext.exe"

# --- 7-Zip (распаковать 7z/RAR/TAR/GZ/BZ2/XZ, упаковать в 7z) ---
# Нужна полная версия: 7z.exe + 7z.dll (в ней и RAR). Урезанные 7za.exe/7zr.exe RAR не умеют.
# В релизе ip7z/7zip (официальный GitHub автора 7-Zip) есть 7zr.exe и установщик 7zNNNN-x64.exe;
# установщик — это 7z-архив с SFX-заголовком: не запускаем его, а распаковываем через 7zr.exe.
$szRelease = Invoke-RestMethod -Headers $headers "https://api.github.com/repos/ip7z/7zip/releases/latest"
$szNames = ($szRelease.assets | ForEach-Object { $_.name }) -join ", "
$szSetupAsset = $szRelease.assets | Where-Object { $_.name -match '^7z\d{4}-x64\.exe$' } | Select-Object -First 1
$sz7zrAsset = $szRelease.assets | Where-Object { $_.name -eq "7zr.exe" } | Select-Object -First 1
if (-not $szSetupAsset -or -not $sz7zrAsset) {
    throw "В релизе ip7z/7zip $($szRelease.tag_name) нет 7zr.exe или установщика 7zNNNN-x64.exe. Есть: $szNames"
}
Write-Host "7-Zip: $($szSetupAsset.name) ($($szRelease.tag_name))"
$szWork = Join-Path $work "7zip"
if (Test-Path $szWork) { Remove-Item $szWork -Recurse -Force }
$szWork = New-Item -ItemType Directory -Force -Path $szWork
$szSetup = Join-Path $szWork $szSetupAsset.name
$sz7zr = Join-Path $szWork "7zr.exe"
Invoke-WebRequest -Headers $headers $szSetupAsset.browser_download_url -OutFile $szSetup
Invoke-WebRequest -Headers $headers $sz7zrAsset.browser_download_url -OutFile $sz7zr
$szExtract = Join-Path $szWork "files"
& $sz7zr x $szSetup "-o$szExtract" -y | Out-Null
if ($LASTEXITCODE -ne 0) {
    # Запасной путь — 7-Zip, установленный в системе (на раннерах GitHub Actions он есть).
    $system7z = Get-Command 7z -ErrorAction SilentlyContinue
    if (-not $system7z) { throw "7zr.exe не смог распаковать $($szSetupAsset.name) (код $LASTEXITCODE), а 7z в системе нет" }
    Write-Host "7zr.exe не справился (код $LASTEXITCODE), распаковываю системным 7z: $($system7z.Source)"
    & $system7z.Source x $szSetup "-o$szExtract" -y | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Не удалось распаковать $($szSetupAsset.name) (код $LASTEXITCODE)" }
}
$szDest = Join-Path $dest "7zip"
if (Test-Path $szDest) { Remove-Item $szDest -Recurse -Force }
$szDest = New-Item -ItemType Directory -Force -Path $szDest
foreach ($name in "7z.exe", "7z.dll", "License.txt") {
    $file = Get-ChildItem $szExtract -Recurse -File | Where-Object { $_.Name -eq $name } | Select-Object -First 1
    if (-not $file) { throw "В установщике 7-Zip нет $name. Есть: $((Get-ChildItem $szExtract -Recurse -File | ForEach-Object { $_.Name }) -join ', ')" }
    Copy-Item $file.FullName $szDest
}
$szExe = Join-Path $szDest "7z.exe"

& (Join-Path $ffDest "ffmpeg.exe") -hide_banner -version | Select-Object -First 1
& (Join-Path $imDest "magick.exe") -version | Select-Object -First 1
& $qpdfExe --version | Select-Object -First 1
# pdftoppm печатает версию в stderr.
$ErrorActionPreference = "Continue"
& $pdftoppmExe -v 2>&1 | ForEach-Object { "$_" } | Select-Object -First 1
& $pdftotextExe -v 2>&1 | ForEach-Object { "$_" } | Select-Object -First 1
# 7z без команды печатает строку версии и справку (код выхода 0).
& $szExe | Where-Object { $_ -match '^7-Zip' } | Select-Object -First 1
