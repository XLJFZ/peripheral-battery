; 外设电量 · Windows 安装包
; 构建：build-installer.ps1 调用 ISCC.exe 编译本文件
; 说明：per-user 安装（%LOCALAPPDATA%\Programs），不需要管理员权限

#define AppName "外设电量"
#define AppVersion "0.3.13"
#define AppPublisher "XLJFZ"
#define AppUrl "https://github.com/XLJFZ/peripheral-battery"
#define AppExe "外设电量.exe"
#define SourceExe "..\dist\portable-" + AppVersion + "\BluetoothBatteryMonitor.App.exe"

[Setup]
AppId={{9E2E5C3A-4B71-4F0D-9A6C-2D5B8E1F7C33}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={localappdata}\Programs\PeripheralBattery
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist\installer
OutputBaseFilename=Peripheral-Battery-Setup-{#AppVersion}
SetupIconFile=..\native\src\BluetoothBatteryMonitor.App\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
AppMutex=Local\PeripheralBattery_AppMutex
MinVersion=10.0.17763

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："; Flags: unchecked
Name: "autostart"; Description: "开机自动启动（登录后最小化到托盘）"; GroupDescription: "附加任务："

[Files]
Source: {#SourceExe}; DestDir: "{app}"; DestName: "{#AppExe}"; Flags: ignoreversion
Source: "..\notes\v{#AppVersion}.md"; DestDir: "{app}"; DestName: "发布说明.md"; Flags: ignoreversion
Source: "..\native\LICENSE"; DestDir: "{app}"; DestName: "LICENSE-BluetoothBatteryMonitor.txt"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "查看外设电量"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; The app reads this to locate its own install dir for in-app updates (works for the
; per-machine "Program Files" choice too, not just the default per-user path).
Root: HKCU; Subkey: "Software\PeripheralBattery"; ValueType: string; \
    ValueName: "InstallDir"; ValueData: "{app}"; \
    Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "PeripheralBatteryNative"; ValueData: """{app}\{#AppExe}"" --minimized"; \
    Flags: uninsdeletevalue; Tasks: autostart

[Run]
; 注意：这里不能加 skipifsilent —— 应用内自动更新走静默安装，装完需要自己把托盘程序拉起来
Filename: "{app}\{#AppExe}"; Description: "立即启动 {#AppName}"; Flags: nowait postinstall runascurrentuser

[UninstallRun]
Filename: "{cmd}"; Parameters: "/c taskkill /f /im ""{#AppExe}"""; Flags: runhidden; RunOnceId: "stopapp"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
var
  RemoveSettings: Boolean;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  { 静默卸载时默认保留设置，避免无人值守卡在对话框 }
  RemoveSettings := SuppressibleMsgBox('是否同时删除本机的设置与托盘配置？' + #13#10 +
    '（保存在 %APPDATA%\PeripheralBatteryNative，删除后外观、阈值等会重置）',
    mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  SettingsDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    { 安装时若未勾选开机启动，注册表项可能仍指向旧路径，这里兜底清理 }
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'PeripheralBatteryNative');
    if RemoveSettings then
    begin
      SettingsDir := ExpandConstant('{userappdata}\PeripheralBatteryNative');
      DelTree(SettingsDir, True, True, True);
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  { 用户取消"开机自动启动"时，清掉之前由便携版写入的自启项 }
  if (CurStep = ssPostInstall) and (not WizardIsTaskSelected('autostart')) then
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'PeripheralBatteryNative');
end;
