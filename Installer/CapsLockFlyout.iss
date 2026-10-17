#define MyAppName "CapsLockFlyout"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "Personal"
#define MyAppExeName "CapsLockFlyout.exe"

#ifndef PackageSourceDir
#define PackageSourceDir "..\artifacts\package-win-x64"
#endif

[Setup]
AppId={{7D253A96-4AE0-4B36-A8E6-5582D3763F40}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
VersionInfoVersion={#MyAppVersion}.0
AppPublisher={#MyAppPublisher}
DefaultDirName={code:GetDefaultDirName}
DisableDirPage=no
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=CapsLockFlyout-Setup
SetupIconFile=..\FluentFlyout-2.14.0\FluentFlyout-2.14.0\FluentFlyoutWPF\Resources\FluentFlyout2.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
ChangesEnvironment=no
MinVersion=10.0.22000

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "autostart"; Description: "使用 Windows 服务开机自启"; GroupDescription: "自动启动："; Flags: checkedonce
Name: "startmenu"; Description: "添加到开始菜单"; GroupDescription: "快捷方式："; Flags: checkedonce

[Files]
Source: "{#PackageSourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,Data\*,*.log,*.jsonl"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README-CapsLockFlyout.md"; DestDir: "{app}"; DestName: "说明.md"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\NOTICE.md"; DestDir: "{app}"; Flags: ignoreversion

[Dirs]
Name: "{app}\Data"; Permissions: users-modify

[Icons]
Name: "{autoprograms}\CapsLockFlyout"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Comment: "CapsLockFlyout 设置与托盘程序"; Tasks: startmenu

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--exit"; StatusMsg: "正在关闭旧版 CapsLockFlyout…"; Flags: runhidden waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Parameters: "--install-service"; StatusMsg: "正在启用服务开机自启…"; Flags: runhidden waituntilterminated; Tasks: autostart
Filename: "{app}\{#MyAppExeName}"; Parameters: "--uninstall-service"; StatusMsg: "正在确认不开机自启…"; Flags: runhidden waituntilterminated; Tasks: not autostart
Filename: "{app}\{#MyAppExeName}"; Parameters: "--startup"; Description: "安装完成后运行 CapsLockFlyout"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--exit"; Flags: runhidden waituntilterminated; RunOnceId: "StopCapsLockFlyout"
Filename: "{app}\{#MyAppExeName}"; Parameters: "--uninstall-service"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveCapsLockFlyoutService"

[UninstallDelete]
Type: filesandordirs; Name: "{app}\Data"

[Code]
function GetDefaultDirName(Param: String): String;
begin
  if DirExists('D:\') then
    Result := 'D:\Programs\CapsLockFlyout'
  else
    Result := ExpandConstant('{autopf}\CapsLockFlyout');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  OldExe: String;
begin
  Result := '';
  OldExe := AddBackslash(WizardDirValue) + '{#MyAppExeName}';
  if FileExists(OldExe) then
    Exec(OldExe, '--exit', WizardDirValue, SW_HIDE, ewWaitUntilTerminated, ResultCode);

  Exec(ExpandConstant('{sys}\sc.exe'), 'stop CapsLockFlyoutService', '',
    SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\sc.exe'), 'delete CapsLockFlyoutService', '',
    SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(400);
end;
