; Inno Setup script for EdgeLink. Compile with:
;   ISCC /DAppVersion=1.2.3 /DSourceExe=<path to EdgeLink.exe> /DOutputDir=<folder> installer\EdgeLink.iss
; Installs for the current user only, so no administrator prompt is needed.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceExe
  #define SourceExe "..\out\runtime\EdgeLink.exe"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

[Setup]
; identifies the application across versions - never change it
AppId={{8ACFF77B-86C1-4453-88A4-A3848C6F47EE}
AppName=EdgeLink
AppVersion={#AppVersion}
AppPublisher=xelossan
AppPublisherURL=https://github.com/xelossan/EdgeLink
AppSupportURL=https://github.com/xelossan/EdgeLink/issues
DefaultDirName={autopf}\EdgeLink
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
DisableDirPage=yes
DisableReadyPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\src\EdgeLink\Assets\EdgeLink.ico
UninstallDisplayIcon={app}\EdgeLink.exe
UninstallDisplayName=EdgeLink
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
OutputDir={#OutputDir}
OutputBaseFilename=EdgeLink-setup-x64
; EdgeLink is stopped and started again by the code below, which also works in silent mode
CloseApplications=no

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "EdgeLink.exe"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\EdgeLink"; Filename: "{app}\EdgeLink.exe"
Name: "{autodesktop}\EdgeLink"; Filename: "{app}\EdgeLink.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\EdgeLink.exe"; Description: "{cm:LaunchProgram,EdgeLink}"; Flags: nowait postinstall skipifsilent

[Code]
const
  InstanceMutex = 'EdgeLink.SingleInstance';
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

var
  WasRunning: Boolean;

procedure StopEdgeLink;
var
  ResultCode, Attempt: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM EdgeLink.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  // the executable stays locked for a moment after the process is gone
  for Attempt := 1 to 20 do
  begin
    if not CheckForMutexes(InstanceMutex) then Break;
    Sleep(100);
  end;
  Sleep(300);
end;

function HasDesktopRuntime: Boolean;
var
  FindRec: TFindRec;
begin
  Result := FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*'), FindRec);
  if Result then FindClose(FindRec);
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  if not HasDesktopRuntime then
    Result := SuppressibleMsgBox(
      'EdgeLink needs the .NET 10 Desktop Runtime, which does not seem to be installed.' + #13#10 + #13#10 +
      'You can get it from https://dotnet.microsoft.com/download/dotnet/10.0 or with' + #13#10 +
      '"winget install Microsoft.DotNet.DesktopRuntime.10".' + #13#10 + #13#10 +
      'Install EdgeLink anyway?', mbConfirmation, MB_YESNO, IDYES) = IDYES;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  WasRunning := CheckForMutexes(InstanceMutex);
  if WasRunning then
  begin
    StopEdgeLink;
    if CheckForMutexes(InstanceMutex) then
      Result := 'EdgeLink is running and could not be closed (it may be running as administrator). Exit it from its tray icon and try again.';
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  // an unattended upgrade (winget) should leave EdgeLink running if it was running before
  if (CurStep = ssPostInstall) and WasRunning and WizardSilent then
    Exec(ExpandConstant('{app}\EdgeLink.exe'), '--tray', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep <> usUninstall then Exit;

  if CheckForMutexes(InstanceMutex) then StopEdgeLink;

  // autostart entries; the settings in %APPDATA%\EdgeLink are deliberately left alone
  RegDeleteValue(HKEY_CURRENT_USER, RunKey, 'EdgeLink');
  Exec(ExpandConstant('{sys}\schtasks.exe'), '/Delete /TN "EdgeLink" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;
