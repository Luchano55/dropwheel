# Скачивает ffmpeg (GPL-сборка BtbN, свежая release-ветка) и ImageMagick 7 portable в папку tools/.
# Результат:
#   tools/ffmpeg/ffmpeg.exe, tools/ffmpeg/ffprobe.exe
#   tools/imagemagick/magick.exe (+ его dll)
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

& (Join-Path $ffDest "ffmpeg.exe") -hide_banner -version | Select-Object -First 1
& (Join-Path $imDest "magick.exe") -version | Select-Object -First 1
