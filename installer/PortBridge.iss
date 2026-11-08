#ifndef AppVersion
  #define AppVersion "0.3.0"
#endif

[Setup]
AppId={{A987D88A-9222-4860-A806-6277E3A496FF}
AppName=PortBridge
AppVersion={#AppVersion}
AppVerName=PortBridge {#AppVersion}
AppPublisher=Nortea575
AppPublisherURL=https://github.com/Nortea575/PortBridge
AppSupportURL=https://github.com/Nortea575/PortBridge/issues
AppUpdatesURL=https://github.com/Nortea575/PortBridge/releases/latest
VersionInfoVersion={#AppVersion}.0
VersionInfoProductVersion={#AppVersion}.0
VersionInfoTextVersion={#AppVersion}.0
VersionInfoProductTextVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\PortBridge
UsePreviousAppDir=yes
UsePreviousTasks=no
DisableWelcomePage=no
DisableProgramGroupPage=yes
LicenseFile=LICENSE
SetupIconFile=src\PortBridge\assets\PortBridge.ico
UninstallDisplayName=PortBridge
UninstallDisplayIcon={app}\PortBridge.exe
AppMutex=Local\PortBridge.Desktop
PrivilegesRequired=lowest
SetupArchitecture=x64
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern dark
CloseApplications=no
RestartApplications=no
Compression=lzma2
SolidCompression=yes
OutputDir=dist
OutputBaseFilename=PortBridge-{#AppVersion}-Setup-x64
SourceDir=..

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Messages]
SetupAppRunningError=安装程序检测到 %1 仍在运行。请先在系统托盘中右键 PortBridge 图标并选择“退出”，停止转发后再点击“确定”重试，或点击“取消”退出安装。
UninstallAppRunningError=卸载程序检测到 %1 仍在运行。请先在系统托盘中右键 PortBridge 图标并选择“退出”，停止转发后再点击“确定”重试，或点击“取消”退出卸载。

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："; Flags: unchecked
Name: "startup"; Description: "登录 Windows 时启动 PortBridge"; GroupDescription: "登录选项："; Flags: unchecked

[Files]
Source: "dist\setup-staging\win-x64\PortBridge.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "dist\setup-staging\win-x64\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "dist\setup-staging\win-x64\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "dist\setup-staging\win-x64\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\PortBridge"; Filename: "{app}\PortBridge.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\PortBridge"; Filename: "{app}\PortBridge.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\PortBridge.exe"; Description: "启动 PortBridge"; Flags: postinstall nowait skipifsilent unchecked

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  RunValueName = 'PortBridge';

var
  StartupTaskInitialized: Boolean;

function HasExplicitTaskSelection: Boolean;
var
  I: Integer;
  Parameter: string;
begin
  Result := False;
  for I := 1 to ParamCount do
  begin
    Parameter := Uppercase(ParamStr(I));
    if (Parameter = '/TASKS') or (Copy(Parameter, 1, 7) = '/TASKS=') or
       (Parameter = '/MERGETASKS') or (Copy(Parameter, 1, 12) = '/MERGETASKS=') then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
var
  StartupCommand: string;
begin
  if (CurPageID = wpSelectTasks) and not StartupTaskInitialized then
  begin
    StartupTaskInitialized := True;
    if not HasExplicitTaskSelection then
    begin
      if RegQueryStringValue(HKCU, RunKey, RunValueName, StartupCommand) and
         (StartupCommand <> '') then
        WizardSelectTasks('startup')
      else
        WizardSelectTasks('!startup');
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if WizardIsTaskSelected('startup') then
      RegWriteStringValue(HKCU, RunKey, RunValueName,
        '"' + ExpandConstant('{app}\PortBridge.exe') + '" --startup')
    else
      RegDeleteValue(HKCU, RunKey, RunValueName);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKCU, RunKey, RunValueName);
end;
