#Requires -Version 7.2
<#
  Демо-ролик DropWheel с настоящего экрана и заодно сквозной тест (GitHub Actions, windows-latest).
  Подробности — docs/demo.md.

  Что делает:
    1. экран: диагностика, разрешение 1280x720 (если получится);
    2. папка C:\Demo\Мои файлы: фото «Отпуск.heic» (или .jpg), видео «Видео.mov», PDF на 3 страницы, песня;
    3. чистый рабочий стол (обои-градиент, без значков), DropWheel в демо-режиме (DROPWHEEL_DEMO=1),
       проводник с папкой в режиме «Крупные значки»;
    4. DemoDriver: запись экрана (ffmpeg gdigrab) + сценарий (фото → PNG, видео → «Сжать», окно задач);
    5. монтаж: demo.mp4 / demo.gif (+ demo-ru.*) с подписями — в папку OutDir (её публикует CI);
    6. проверка: PNG и сжатое MP4 действительно появились рядом с исходниками, иначе — код выхода 1.
  Журналы и скриншоты кладутся в OutDir в любом случае — по ним видно, что пошло не так.
#>
param(
    [string]$AppDir = "out/DropWheel",
    [string]$Driver = "out/DemoDriver/DemoDriver.exe",
    [string]$OutDir = "screenshots",
    [string]$WorkDir = "",
    [string]$DemoRoot = "C:\Demo",
    [string[]]$Resolutions = @("1280x720", "1366x768", "1280x800", "1600x900", "1920x1080")
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
# Вывод DemoDriver и утилит — в UTF-8 (кириллица в журнале CI).
try { [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false) } catch { Write-Host "OutputEncoding: $_" }
$OutputEncoding = [Text.UTF8Encoding]::new($false)

$script:started = Get-Date
$script:failures = [Collections.Generic.List[string]]::new()
$script:userEnvSet = $false
$script:dataDir = $null
if (-not $WorkDir) {
    $WorkDir = if ($env:RUNNER_TEMP) { Join-Path $env:RUNNER_TEMP "demo" } else { Join-Path ([IO.Path]::GetTempPath()) "dropwheel-demo" }
}

function Write-Step([string]$Text) {
    $elapsed = ((Get-Date) - $script:started).TotalSeconds
    Write-Host ("[{0:HH:mm:ss.fff} +{1,6:N1}s] ==> {2}" -f (Get-Date), $elapsed, $Text)
}

function Add-Failure([string]$Text) {
    $script:failures.Add($Text)
    Write-Host "::error::Демо: $Text"
}

# Запуск утилиты: аргументы через ArgumentList (кириллица и пробелы без ручных кавычек), вывод — в журнал.
function Invoke-Tool([string]$Exe, [string[]]$Arguments, [switch]$ShowOutput) {
    Write-Host ("    > {0} {1}" -f [IO.Path]::GetFileName($Exe), ($Arguments -join " "))
    $psi = [Diagnostics.ProcessStartInfo]::new($Exe)
    foreach ($a in $Arguments) { $psi.ArgumentList.Add($a) }
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $psi.StandardOutputEncoding = [Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [Text.Encoding]::UTF8
    $p = [Diagnostics.Process]::Start($psi)
    $out = $p.StandardOutput.ReadToEndAsync()
    $err = $p.StandardError.ReadToEndAsync()
    $p.WaitForExit()
    $text = ($out.Result + "`n" + $err.Result).Trim()
    if ($text -and ($ShowOutput -or $p.ExitCode -ne 0)) {
        $text -split "`r?`n" | Select-Object -Last 25 | ForEach-Object { Write-Host "    | $_" }
    }
    if ($p.ExitCode -ne 0) { Write-Host "    код выхода $($p.ExitCode)" }
    return $p.ExitCode
}

# DemoDriver: вывод — сразу в журнал CI, плюс копия в driver.log.
function Invoke-Driver([string[]]$Arguments) {
    & $script:driverExe @Arguments --log $script:driverLog | Out-Host
    return $LASTEXITCODE
}

function Get-AppLog {
    $logs = Join-Path $script:dataDir "logs"
    Get-ChildItem $logs -Filter *.log -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1
}

$app = $null
try {
    # ---------------------------------------------------------------- 0. пути
    Write-Step "Пути и утилиты"
    $AppDir = (Resolve-Path $AppDir).Path
    $script:driverExe = (Resolve-Path $Driver).Path
    $appExe = Join-Path $AppDir "DropWheel.exe"
    $toolsDir = Join-Path $AppDir "tools"
    $ffmpeg = Join-Path $toolsDir "ffmpeg\ffmpeg.exe"
    $magick = Join-Path $toolsDir "imagemagick\magick.exe"
    foreach ($f in @($appExe, $ffmpeg, $magick, $script:driverExe)) {
        if (-not (Test-Path $f)) { throw "Нет файла: $f" }
    }
    New-Item -ItemType Directory -Force -Path $OutDir, $WorkDir | Out-Null
    $OutDir = (Resolve-Path $OutDir).Path
    $WorkDir = (Resolve-Path $WorkDir).Path
    $script:driverLog = Join-Path $WorkDir "driver.log"
    $script:dataDir = Join-Path $WorkDir "appdata"
    Write-Host "    DropWheel: $appExe"
    Write-Host "    DemoDriver: $($script:driverExe)"
    Write-Host "    результат: $OutDir, рабочая папка: $WorkDir"
    Invoke-Tool $ffmpeg @("-hide_banner", "-version") -ShowOutput | Out-Null
    Invoke-Tool $magick @("-version") -ShowOutput | Out-Null

    $fonts = Join-Path $env:WINDIR "Fonts"
    $font = @("segoeui.ttf", "arial.ttf") | ForEach-Object { Join-Path $fonts $_ } | Where-Object { Test-Path $_ } | Select-Object -First 1
    $fontBold = @("seguisb.ttf", "segoeuib.ttf", "arialbd.ttf") | ForEach-Object { Join-Path $fonts $_ } | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $fontBold) { $fontBold = $font }
    Write-Host "    шрифты: $font, $fontBold"

    # ---------------------------------------------------------------- 1. экран
    Write-Step "Экран: диагностика и разрешение"
    Invoke-Driver @("info") | Out-Null
    $code = Invoke-Driver (@("set-resolution") + $Resolutions)
    if ($code -ne 0 -and (Get-Command Set-DisplayResolution -ErrorAction SilentlyContinue)) {
        Write-Host "    пробую Set-DisplayResolution 1280x720"
        try { Set-DisplayResolution -Width 1280 -Height 720 -Force -ErrorAction Stop } catch { Write-Host "    Set-DisplayResolution: $_" }
        Start-Sleep -Seconds 2
    }

    # ---------------------------------------------------------------- 2. файлы
    Write-Step "Файлы для ролика"
    $folder = Join-Path $DemoRoot "Мои файлы"
    if (Test-Path $DemoRoot) { Remove-Item $DemoRoot -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    $gen = Join-Path $WorkDir "gen"
    New-Item -ItemType Directory -Force -Path $gen | Out-Null

    # Фото: закат над морем (рисует ImageMagick).
    $photoPng = Join-Path $gen "photo.png"
    $code = Invoke-Tool $magick @(
        "-size", "1600x1000", "gradient:#2B1A5E-#FF8A5C",
        "-fill", "#FFD36E", "-draw", "circle 800,640 800,500",
        "(", "-size", "1600x360", "gradient:#2E5C9A-#0B1F3A", ")", "-geometry", "+0+640", "-composite",
        "-fill", "#1E1238", "-draw", "polygon 0,640 0,520 140,450 300,560 460,430 640,640",
        "-fill", "#24163F", "-draw", "polygon 1000,640 1180,480 1320,560 1460,420 1600,520 1600,640",
        "-fill", "#FFD36E99", "-draw", "rectangle 690,662 910,668", "-draw", "rectangle 720,690 880,695",
        "-draw", "rectangle 750,718 850,722", "-draw", "rectangle 772,746 828,749",
        $photoPng)
    if ($code -ne 0) { throw "ImageMagick не нарисовал фото" }
    # HEIC, если ImageMagick умеет его записывать; иначе JPG.
    $photoName = "Отпуск.heic"
    $photoPath = Join-Path $folder $photoName
    $code = Invoke-Tool $magick @($photoPng, "-quality", "80", $photoPath)
    $heicOk = $code -eq 0 -and (Test-Path $photoPath) -and (Get-Item $photoPath).Length -gt 1000
    if ($heicOk) { $heicOk = (Invoke-Tool $magick @("identify", $photoPath) -ShowOutput) -eq 0 }
    if (-not $heicOk) {
        Write-Host "    HEIC не записался — беру JPG"
        Remove-Item $photoPath -Force -ErrorAction SilentlyContinue
        $photoName = "Отпуск.jpg"
        $photoPath = Join-Path $folder $photoName
        if ((Invoke-Tool $magick @($photoPng, "-quality", "90", $photoPath)) -ne 0) { throw "Не удалось записать фото" }
    }

    # Видео: 10 с фрактала со звуком.
    $videoName = "Видео.mov"
    $videoPath = Join-Path $folder $videoName
    $audio = "aevalsrc='0.15*sin(2*PI*t*(220+110*floor(mod(t*2,4))))':s=44100:c=stereo"
    $code = Invoke-Tool $ffmpeg @("-hide_banner", "-y", "-f", "lavfi", "-i", "mandelbrot=size=1280x720:rate=30",
        "-f", "lavfi", "-i", $audio, "-t", "10", "-map", "0:v", "-map", "1:a", "-vf", "hue=s=1.3",
        "-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-pix_fmt", "yuv420p",
        "-c:a", "aac", "-b:a", "128k", "-shortest", $videoPath)
    if ($code -ne 0) {
        Write-Host "    mandelbrot не получился — беру testsrc2"
        $code = Invoke-Tool $ffmpeg @("-hide_banner", "-y", "-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=30",
            "-f", "lavfi", "-i", $audio, "-t", "10", "-map", "0:v", "-map", "1:a",
            "-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "128k", "-shortest", $videoPath)
        if ($code -ne 0) { throw "ffmpeg не сделал видео" }
    }

    # PDF на три страницы.
    $pages = @()
    foreach ($i in 1..3) {
        $page = Join-Path $gen "page$i.png"
        $code = Invoke-Tool $magick @("-size", "1240x1754", "xc:white",
            "-fill", "#2F80ED", "-draw", "rectangle 0,0 1240,240",
            "-font", $font, "-fill", "white", "-pointsize", "76", "-annotate", "+100+160", "Отчёт за квартал",
            "-fill", "#1F2937", "-pointsize", "44", "-annotate", "+100+360", "Страница $i из 3",
            "-fill", "#D1D5DB", "-draw", "rectangle 100,440 1140,458", "-draw", "rectangle 100,500 1080,518",
            "-draw", "rectangle 100,560 1120,578", "-draw", "rectangle 100,620 760,638",
            "-fill", "#F2994A", "-draw", "rectangle 100,$(950 - $i * 80) 460,1100",
            "-fill", "#27AE60", "-draw", "rectangle 560,820 760,1100",
            "-fill", "#9B51E0", "-draw", "rectangle 860,$(1100 - $i * 120) 1060,1100",
            $page)
        if ($code -eq 0) { $pages += $page }
    }
    if ($pages.Count -eq 3) {
        Invoke-Tool $magick (@("-units", "PixelsPerInch", "-density", "150") + $pages + @((Join-Path $folder "Отчёт.pdf"))) | Out-Null
    }

    # Песня: 12 с арпеджио.
    $song = "aevalsrc='0.2*sin(2*PI*t*220*pow(2,floor(mod(t*4,8))*2/12))*exp(-4*mod(t,0.25))+0.08*sin(2*PI*t*110)':s=44100:d=12"
    Invoke-Tool $ffmpeg @("-hide_banner", "-y", "-f", "lavfi", "-i", $song, "-c:a", "libmp3lame", "-b:a", "192k",
        (Join-Path $folder "Песня.mp3")) | Out-Null

    Get-ChildItem $folder | ForEach-Object { Write-Host ("    {0,-24} {1,12:N0} байт" -f $_.Name, $_.Length) }

    # ---------------------------------------------------------------- 3. рабочий стол, DropWheel, проводник
    Write-Step "Рабочий стол: окна, обои, значки, расширения, курсор"
    Invoke-Driver @("prepare-desktop", "--wallpaper", (Join-Path $WorkDir "wallpaper.bmp")) | Out-Null

    Write-Step "Запуск DropWheel в демо-режиме"
    New-Item -ItemType Directory -Force -Path $script:dataDir | Out-Null
    # Без проверки обновлений (сеть, «есть новая версия») и без записи в реестр пункта меню.
    '{ "checkForUpdates": false, "contextMenuEnabled": false }' |
        Set-Content (Join-Path $script:dataDir "settings.json") -Encoding utf8NoBOM
    $env:DROPWHEEL_DEMO = "1"
    $env:DROPWHEEL_DATA_DIR = $script:dataDir
    $uac = (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" -ErrorAction SilentlyContinue).EnableLUA
    $elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
    Write-Host "    UAC (EnableLUA): $uac, скрипт с правами администратора: $elevated"
    if ($uac -ne 0 -and $elevated) {
        # UAC включён, а мы повышены: проводник работает с обычными правами, и Windows (UIPI) не даст бросить
        # файл из него в окно колеса повышенного процесса (решение D9). Запускаем DropWheel через проводник —
        # с обычными правами; переменные передаём через окружение пользователя (проводник их перечитает).
        Write-Host "    запускаю DropWheel через проводник (обычные права)"
        $script:userEnvSet = $true
        [Environment]::SetEnvironmentVariable("DROPWHEEL_DEMO", "1", "User")
        [Environment]::SetEnvironmentVariable("DROPWHEEL_DATA_DIR", $script:dataDir, "User")
        Start-Sleep -Seconds 2
        Start-Process (Join-Path $env:WINDIR "explorer.exe") -ArgumentList "`"$appExe`""
        $deadline = (Get-Date).AddSeconds(20)
        while (-not $app -and (Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 300
            $app = Get-Process DropWheel -ErrorAction SilentlyContinue | Select-Object -First 1
        }
        if (-not $app) { throw "DropWheel.exe не запустился через проводник" }
    }
    else {
        $psi = [Diagnostics.ProcessStartInfo]::new($appExe)
        $psi.UseShellExecute = $false
        $psi.WorkingDirectory = $AppDir
        $psi.Environment["DROPWHEEL_DEMO"] = "1"
        $psi.Environment["DROPWHEEL_DATA_DIR"] = $script:dataDir
        $app = [Diagnostics.Process]::Start($psi)
    }
    Write-Host "    pid $($app.Id)"

    $hooked = $false
    $logText = ""
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline) {
        if ($app.HasExited) { Add-Failure "DropWheel.exe завершился сразу, код $($app.ExitCode)"; break }
        $log = Get-AppLog
        if ($log) {
            $logText = Get-Content $log.FullName -Raw -Encoding utf8
            if ($logText -match "Хуки установлены") { $hooked = $true; break }
            if ($logText -match "Не удалось включить колесо") { break }
        }
        Start-Sleep -Milliseconds 300
    }
    Write-Host "    журнал DropWheel:"
    $logText -split "`r?`n" | Where-Object { $_ } | ForEach-Object { Write-Host "    | $_" }
    if (-not $hooked) { Add-Failure "хуки DropWheel не установлены за 30 с (журнал выше)" }
    elseif ($logText -notmatch "DROPWHEEL_DEMO") { Write-Host "::warning::Демо: в журнале нет отметки DROPWHEEL_DEMO — программные нажатия могут игнорироваться" }

    Write-Step "Проводник с папкой «$folder»"
    if ((Invoke-Driver @("open-explorer", "--folder", $folder)) -ne 0) { Add-Failure "проводник с папкой не открылся" }
    # Подсказка «DropWheel работает» у часов успевает исчезнуть.
    Start-Sleep -Seconds 4
    Invoke-Driver @("screenshot", (Join-Path $OutDir "demo-00-ready.png")) | Out-Null
    Invoke-Driver @("info") | Out-Null

    # ---------------------------------------------------------------- 4. сценарий с записью
    Write-Step "Сценарий с записью экрана"
    $rawPath = Join-Path $WorkDir "raw.mkv"
    $timeline = Join-Path $WorkDir "timeline.json"
    $runCode = Invoke-Driver @("run", "--folder", $folder, "--app", $appExe, "--tools", $toolsDir,
        "--app-log-dir", (Join-Path $script:dataDir "logs"), "--shots", $OutDir, "--timeline", $timeline,
        "--ffmpeg", $ffmpeg, "--raw", $rawPath, "--record-log", (Join-Path $WorkDir "record-ffmpeg.log"),
        "--photo", $photoName, "--video", $videoName)
    switch ($runCode) {
        0 { Write-Host "    сценарий прошёл" }
        2 { Add-Failure "сквозной тест: результат не появился (подробности — в журнале DemoDriver выше)" }
        3 { Add-Failure "сценарий не начался: нет окна проводника или DropWheel" }
        default { Add-Failure "DemoDriver run завершился с кодом $runCode" }
    }

    # ---------------------------------------------------------------- 5. монтаж
    Write-Step "Монтаж ролика"
    if ((Test-Path $rawPath) -and (Test-Path $timeline)) {
        $renderCode = Invoke-Driver @("render", "--raw", $rawPath, "--timeline", $timeline, "--ffmpeg", $ffmpeg,
            "--font", $font, "--font-bold", $fontBold, "--out", $OutDir, "--work", (Join-Path $WorkDir "render"))
        if ($renderCode -ne 0) { Add-Failure "монтаж ролика не удался (код $renderCode)" }
    }
    else {
        Add-Failure "нет записи экрана ($rawPath) — ролик не собран"
    }

    # ---------------------------------------------------------------- 6. проверка результатов
    Write-Step "Проверка: результаты рядом с исходниками"
    Get-ChildItem $folder | ForEach-Object { Write-Host ("    {0,-28} {1,12:N0} байт" -f $_.Name, $_.Length) }
    # Временные файлы конвертера (.dropwheel-<guid>.ext) не считаются.
    $png = Get-ChildItem $folder -Filter *.png | Where-Object { -not $_.Name.StartsWith(".dropwheel") } | Select-Object -First 1
    if (-not $png -or $png.Length -eq 0) { Add-Failure "PNG из «$photoName» не появился" }
    elseif ((Invoke-Tool $magick @("identify", $png.FullName) -ShowOutput) -ne 0) { Add-Failure "PNG «$($png.Name)» не читается" }
    else { Write-Host "    OK: $($png.Name)" }

    $mp4 = Get-ChildItem $folder -Filter *.mp4 | Where-Object { -not $_.Name.StartsWith(".dropwheel") } | Sort-Object { $_.Name -notlike "*сжато*" } | Select-Object -First 1
    if (-not $mp4 -or $mp4.Length -eq 0) { Add-Failure "сжатое видео из «$videoName» не появилось" }
    elseif ((Invoke-Tool $ffmpeg @("-hide_banner", "-v", "error", "-i", $mp4.FullName, "-t", "2", "-f", "null", "-")) -ne 0) {
        Add-Failure "«$($mp4.Name)» не декодируется"
    }
    else { Write-Host ("    OK: {0} ({1:N1} МБ из {2:N1} МБ)" -f $mp4.Name, ($mp4.Length / 1MB), ((Get-Item $videoPath).Length / 1MB)) }

    foreach ($pair in @(@("demo.mp4", 10MB), @("demo.gif", 8MB), @("demo-ru.mp4", 10MB), @("demo-ru.gif", 8MB))) {
        $file = Join-Path $OutDir $pair[0]
        if (-not (Test-Path $file)) { Add-Failure "нет $($pair[0])"; continue }
        $size = (Get-Item $file).Length
        Write-Host ("    {0}: {1:N2} МБ" -f $pair[0], ($size / 1MB))
        if ($size -gt $pair[1]) { Add-Failure ("{0} больше {1} МБ" -f $pair[0], ($pair[1] / 1MB)) }
    }
}
catch {
    Add-Failure "скрипт упал: $_"
    Write-Host $_.ScriptStackTrace
}
finally {
    # ---------------------------------------------------------------- 7. журналы — всегда
    Write-Step "Журналы в $OutDir"
    try {
        if ($app -and -not $app.HasExited) { Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue }
        Get-Process DropWheel -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        if ($script:userEnvSet) {
            [Environment]::SetEnvironmentVariable("DROPWHEEL_DEMO", $null, "User")
            [Environment]::SetEnvironmentVariable("DROPWHEEL_DATA_DIR", $null, "User")
        }
        if ($script:dataDir -and (Test-Path (Join-Path $script:dataDir "logs"))) {
            Get-ChildItem (Join-Path $script:dataDir "logs") -Filter *.log | Sort-Object Name |
                Get-Content -Encoding utf8 | Set-Content (Join-Path $OutDir "demo-dropwheel-log.txt") -Encoding utf8NoBOM
            Write-Host "    журнал DropWheel (конец):"
            Get-Content (Join-Path $OutDir "demo-dropwheel-log.txt") -Tail 40 -Encoding utf8 | ForEach-Object { Write-Host "    | $_" }
        }
        foreach ($pair in @(@("driver.log", "demo-driver-log.txt"), @("record-ffmpeg.log", "demo-record-ffmpeg-log.txt"),
                @("timeline.json", "demo-timeline.json"), @("render\render-ffmpeg.log", "demo-render-ffmpeg-log.txt"))) {
            $src = Join-Path $WorkDir $pair[0]
            if (Test-Path $src) { Copy-Item $src (Join-Path $OutDir $pair[1]) -Force }
        }
        if ($env:GITHUB_STEP_SUMMARY) {
            $summary = @("## Демо-ролик", "")
            $summary += Get-ChildItem $OutDir -File | Where-Object { $_.Name -like "demo*.mp4" -or $_.Name -like "demo*.gif" } |
                ForEach-Object { "- {0} — {1:N2} МБ" -f $_.Name, ($_.Length / 1MB) }
            if ($script:failures.Count -gt 0) { $summary += @("", "**Проблемы:**") + ($script:failures | ForEach-Object { "- $_" }) }
            else { $summary += @("", "Сквозной тест прошёл: PNG и сжатое видео появились рядом с исходниками.") }
            $summary | Add-Content $env:GITHUB_STEP_SUMMARY -Encoding utf8
        }
    }
    catch {
        Write-Host "    сбор журналов: $_"
    }
}

if ($script:failures.Count -gt 0) {
    Write-Step "НЕ ПРОШЛО:"
    $script:failures | ForEach-Object { Write-Host "    - $_" }
    exit 1
}
Write-Step "Готово: ролик и сквозной тест"
exit 0
