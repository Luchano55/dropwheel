; Установщик DropWheel (Inno Setup 6). Решение D-RELEASE, инструкция — docs/winget.md.
;
; Собирает CI (.github/workflows/build.yml, задача windows) из готовой папки out\DropWheel:
;   iscc /DAppVersion=0.3.0 /DFileVersion=0.3.57.0 /DAppSource=<папка программы> /DOutputDir=<куда> DropWheel.iss
; Результат: DropWheel-Setup-<версия>.exe.
;
; Установка — только для текущего пользователя, без прав администратора, в %LocalAppData%\Programs\DropWheel:
; папка доступна на запись, поэтому самообновление из трея (UpdateApplier) работает как в zip-версии.
; Удаление: закрывает программу (DropWheel.exe --exit), убирает пункт меню проводника (--unregister) и автозапуск,
; стирает профиль Edge %LocalAppData%\DropWheel\WebView2. Настройки и журнал (%AppData%\DropWheel) остаются.

#ifndef AppVersion
  #define AppVersion "0.0.0-local"
#endif
#ifndef FileVersion
  #define FileVersion "0.0.0.0"
#endif
#ifndef AppSource
  #define AppSource "..\..\out\DropWheel"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\out\installer"
#endif

#define AppName "DropWheel"
#define AppExeName "DropWheel.exe"
#define AppPublisher "Luchano55"
#define AppUrl "https://github.com/Luchano55/dropwheel"

[Setup]
; AppId не менять никогда: по нему установщик узнаёт уже установленную программу (обновление поверх).
AppId={{631AF0D8-69CA-4F61-9028-6FD03A7E2126}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
AppCopyright=© 2026 {#AppPublisher}, MIT License
VersionInfoVersion={#FileVersion}
VersionInfoProductVersion={#FileVersion}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoDescription={#AppName} Setup

; Без администратора, только для себя.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\{#AppName}
DisableDirPage=yes
UsePreviousAppDir=yes
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 1809 и новее — как у самой программы (SupportedOSPlatformVersion 10.0.17763).
MinVersion=10.0.17763

OutputDir={#OutputDir}
OutputBaseFilename=DropWheel-Setup-{#AppVersion}
SetupIconFile=..\..\src\DropWheel\Assets\DropWheel.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
SetupLogging=yes
ShowLanguageDialog=auto

; Запущенный DropWheel закрываем сами (PrepareToInstall: --exit), Restart Manager — запасной путь.
CloseApplications=yes
CloseApplicationsFilter=*.exe,*.dll
RestartApplications=no

[Languages]
; Первый — язык по умолчанию, если язык Windows не нашёлся среди перечисленных.
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
en.AutoStartTask=Start DropWheel with Windows
ru.AutoStartTask=Запускать вместе с Windows
en.OtherTasks=Other:
ru.OtherTasks=Прочее:

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "{cm:AutoStartTask}"; GroupDescription: "{cm:OtherTasks}"

[Files]
; Вся папка сборки: DropWheel.exe, dropwheel-cli.exe, tools\ (ffmpeg, ImageMagick, qpdf, poppler, 7-Zip) и прочее.
Source: "{#AppSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
; Тот же автозапуск, что включает сама программа (AutoStartRegistration): значение DropWheel в HKCU\…\Run.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DropWheel"; ValueData: """{app}\{#AppExeName}"" --autostart"; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Файлы, которые появились после установки (самообновление, остатки *.old), — только известные типы,
; чтобы не стереть лишнего, если папку указали вручную (/DIR=).
Type: filesandordirs; Name: "{app}\tools"
Type: files; Name: "{app}\*.old"
Type: files; Name: "{app}\*.exe"
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\*.cmd"
Type: files; Name: "{app}\*.json"
Type: dirifempty; Name: "{app}"

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  MenuKey = 'Software\Classes\*\shell\DropWheel.Convert';
  FolderMenuKey = 'Software\Classes\Directory\shell\DropWheel.Convert';

var
  WasRunning: Boolean;

function AppExePath: String;
begin
  Result := ExpandConstant('{app}\{#AppExeName}');
end;

{ Принудительно завершить все DropWheel.exe, которые можем (свои процессы; чужие без прав администратора
  taskkill не тронет). Без cmd, tasklist и фильтров: tasklist /FI USERNAME на некоторых ПК зависал,
  и установщик висел на «Подготовке к установке». true — что-то было запущено и завершено. }
function KillAllDropWheel: Boolean;
var
  Code: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExeName}', '', SW_HIDE, ewWaitUntilTerminated, Code)
    and (Code = 0);
  Log(Format('taskkill finished with code %d', [Code]));
  if Result then Sleep(1000); { процесс отпускает файлы и Mutex }
end;

{ Принудительно завершить DropWheel.exe только из папки программы (при удалении zip-копию в другой папке не трогаем). }
procedure KillFromAppFolder;
var
  Code: Integer;
  Folder: String;
begin
  Folder := ExpandConstant('{app}');
  { Апостроф в пути (O'Brien) — удвоить для строки PowerShell в одинарных кавычках. }
  StringChangeEx(Folder, #39, #39#39, True);
  Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "Get-Process -Name DropWheel -ErrorAction SilentlyContinue | '
      + 'Where-Object { $_.Path -like ' + #39 + Folder + '\*' + #39 + ' } | Stop-Process -Force"',
    '', SW_HIDE, ewWaitUntilTerminated, Code);
  Log(Format('Stop-Process (app folder only) finished with code %d', [Code]));
end;

{ Вежливо закрыть установленный DropWheel: --exit через канал первому экземпляру (задачи отменяются, значок убирается).
  Код 2 — был запущен и закрылся, 0 — не был запущен, 1 — не закрылся. true — был запущен. }
function ExitInstalled: Boolean;
var
  Code: Integer;
begin
  Result := False;
  if not FileExists(AppExePath) then Exit;
  Log('Closing DropWheel: ' + AppExePath + ' --exit');
  if Exec(AppExePath, '--exit', '', SW_HIDE, ewWaitUntilTerminated, Code) then
  begin
    Log(Format('--exit finished with code %d', [Code]));
    Result := Code <> 0;
  end
  else
    Log('--exit could not be started: ' + SysErrorMessage(Code));
end;

{ При установке закрываем любой DropWheel.exe: старая версия (до 0.3.0 нет --exit) или zip-копия
  из другой папки держит Mutex, и новый не запустится. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  WasRunning := ExitInstalled;
  if KillAllDropWheel then WasRunning := True;
  Log(Format('DropWheel was running before install: %d', [Ord(WasRunning)]));
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
begin
  { Тихое обновление (winget upgrade): программа работала — запустить снова, тихо, в трей. }
  if (CurStep = ssPostInstall) and WasRunning and WizardSilent then
  begin
    Log('Restarting DropWheel after silent install');
    ExecAsOriginalUser(AppExePath, '--autostart', ExpandConstant('{app}'), SW_SHOWNORMAL, ewNoWait, Code);
  end;
end;

{ Автозапуск убираем, только если он указывает на эту папку (у zip-копии в другой папке он свой). }
procedure RemoveAutoStart;
var
  Value: String;
begin
  if RegQueryStringValue(HKCU, RunKey, 'DropWheel', Value) then
    if Pos(Lowercase(ExpandConstant('{app}')), Lowercase(Value)) > 0 then
    begin
      RegDeleteValue(HKCU, RunKey, 'DropWheel');
      Log('Autostart value removed');
    end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Code: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    ExitInstalled;
    KillFromAppFolder;
    { Пункт «Конвертировать…» в проводнике: программа знает все свои ключи (и старые, по расширениям). }
    if FileExists(AppExePath) then
    begin
      Exec(AppExePath, '--unregister', '', SW_HIDE, ewWaitUntilTerminated, Code);
      Log(Format('--unregister finished with code %d', [Code]));
    end;
    { На случай, если exe не запустился, — основные ключи (файлы и папки) удаляем сами. }
    RegDeleteKeyIncludingSubkeys(HKCU, MenuKey);
    RegDeleteKeyIncludingSubkeys(HKCU, FolderMenuKey);
    RemoveAutoStart;
  end;

  if CurUninstallStep = usPostUninstall then
  begin
    { Профиль Edge (WebView2) для печати в PDF — служебный, пересоздаётся сам. Настройки в %AppData%\DropWheel не трогаем. }
    DelTree(ExpandConstant('{localappdata}\DropWheel\WebView2'), True, True, True);
    RemoveDir(ExpandConstant('{localappdata}\DropWheel'));
  end;
end;
