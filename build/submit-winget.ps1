# Отправить манифесты DropWheel в winget (microsoft/winget-pkgs) через wingetcreate (решение D-RELEASE).
# Токен — секрет репозитория WINGET_TOKEN (classic, галочка public_repo, аккаунт Luchano55).
# Нет токена — предупреждение и выход без ошибки: релиз не должен краснеть из-за winget.
param(
    [Parameter(Mandatory)] [string] $ManifestDir,  # папка manifests/l/Luchano55/DropWheel/<версия>
    [Parameter(Mandatory)] [string] $Version,
    [switch] $Required                             # ручной запуск: без токена — ошибка, а не предупреждение
)
$ErrorActionPreference = 'Stop'

if (-not $env:WINGET_TOKEN) {
    if ($Required) { throw "Секрета WINGET_TOKEN нет (как добавить — docs/winget.md)" }
    Write-Warning "Секрета WINGET_TOKEN нет — в winget не отправляю (как добавить — docs/winget.md)"
    exit 0
}
if (-not (Test-Path (Join-Path $ManifestDir "Luchano55.DropWheel.yaml"))) {
    throw "Нет манифестов в $ManifestDir"
}

$exe = Join-Path $env:RUNNER_TEMP "wingetcreate.exe"
if (-not (Test-Path $exe)) {
    Invoke-WebRequest -Uri "https://aka.ms/wingetcreate/latest" -OutFile $exe
}

& $exe submit --token $env:WINGET_TOKEN --prtitle "Luchano55.DropWheel version $Version" $ManifestDir
if ($LASTEXITCODE -ne 0) { throw "wingetcreate submit: код $LASTEXITCODE" }
Write-Host "Заявка в winget отправлена: https://github.com/microsoft/winget-pkgs/pulls?q=Luchano55.DropWheel"
"Заявка в winget для $Version отправлена: https://github.com/microsoft/winget-pkgs/pulls?q=Luchano55.DropWheel" >> $env:GITHUB_STEP_SUMMARY
