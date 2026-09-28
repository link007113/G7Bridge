#ifndef AppVersion
  #define AppVersion "1.3.0-rc.1"
#endif
#ifndef PackageDir
  #define PackageDir "..\artifacts\package"
#endif

[Setup]
AppId={{EE3C8A87-8DA9-4A19-8382-716F559B6587}
AppName=G7 Bridge
AppVersion={#AppVersion}
AppPublisher=G7 Bridge contributors
AppPublisherURL=https://github.com/link007113/G7Bridge
AppSupportURL=https://github.com/link007113/G7Bridge/issues
AppUpdatesURL=https://github.com/link007113/G7Bridge/releases
LicenseFile=..\vendor\licenses\GameInput-LICENSE.txt
DefaultDirName={autopf}\Grimm\G7Bridge
UsePreviousAppDir=no
DisableDirPage=yes
DisableProgramGroupPage=yes
DefaultGroupName=G7 Bridge
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.26100
PrivilegesRequired=admin
OutputDir=..\artifacts\installer
OutputBaseFilename=G7Bridge-{#AppVersion}-Setup-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\G7Bridge.exe
AppMutex=Local\Grimm.G7Bridge
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
ShowLanguageDialog=no
LanguageDetectionMethod=uilanguage
UsePreviousLanguage=no
VersionInfoVersion=1.2.0.0
VersionInfoProductName=G7 Bridge
VersionInfoDescription=G7 Bridge Setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "dutch"; MessagesFile: "compiler:Languages\Dutch.isl"

[CustomMessages]
english.DesktopShortcut=Create a desktop shortcut
dutch.DesktopShortcut=Een snelkoppeling op het bureaublad maken
english.Launch=Open G7 Bridge
dutch.Launch=G7 Bridge openen
english.Components=Installs G7 Bridge, its controller service, and required HidHide / USB virtual-controller drivers. A driver installation may briefly reconnect USB devices. Restart Windows if requested. No firmware updates or Windows Developer Mode are needed.
dutch.Components=Installeert G7 Bridge, de controllerdienst en de benodigde HidHide-/virtuele USB-controllerdrivers. Bij driverinstallatie kunnen USB-apparaten kort opnieuw verbinden. Herstart Windows als daarom wordt gevraagd. Firmwareupdates en Windows-ontwikkelaarsmodus zijn niet nodig.
english.Preparing=Preparing the controller service...
dutch.Preparing=De controllerdienst voorbereiden...
english.Configuring=Installing controller drivers and configuring G7 Bridge. This may take a few minutes...
dutch.Configuring=Controllerdrivers installeren en G7 Bridge instellen. Dit kan enkele minuten duren...
english.Failed=G7 Bridge setup could not complete (code %1). See %2 for details. Restart Windows if a driver installation is pending, then run this installer again.
dutch.Failed=De installatie van G7 Bridge kon niet worden voltooid (code %1). Zie %2 voor details. Herstart Windows als een driverinstallatie nog niet voltooid is en voer deze installer opnieuw uit.
english.FixedPath=G7 Bridge must be installed in its default Program Files folder.
dutch.FixedPath=G7 Bridge moet in de standaardmap onder Program Files worden geinstalleerd.
english.CloseFirst=Exit G7 Bridge from its system tray menu before continuing.
dutch.CloseFirst=Sluit G7 Bridge via het systeemvakmenu voordat je verdergaat.
english.RemovalFailed=The controller service or its device-hiding rules could not be removed. Files have been kept. See %1.
dutch.RemovalFailed=De controllerdienst of de eigen verbergregels konden niet worden verwijderd. De bestanden zijn behouden. Zie %1.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"

[Files]
Source: "{#PackageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{commonprograms}\G7 Bridge\G7 Bridge"; Filename: "{app}\G7Bridge.exe"; WorkingDir: "{app}"
Name: "{commondesktop}\G7 Bridge"; Filename: "{app}\G7Bridge.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\G7Bridge.exe"; Description: "{cm:Launch}"; Flags: postinstall nowait skipifsilent runasoriginaluser; Check: CanLaunch

[Code]
var
  DriverRestartRequired: Boolean;
  Prepared: Boolean;

function ErrorLog: String;
begin
  Result := ExpandConstant('{commonappdata}\G7Bridge\setup-error.txt');
end;

function CanLaunch: Boolean;
begin
  Result := not DriverRestartRequired;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
  Helper: String;
begin
  Result := '';
  if CompareText(ExpandConstant('{app}'), ExpandConstant('{autopf}\Grimm\G7Bridge')) <> 0 then
  begin
    Result := CustomMessage('FixedPath');
    Exit;
  end;
  if CheckForMutexes('Local\Grimm.G7Bridge') then
  begin
    Result := CustomMessage('CloseFirst');
    Exit;
  end;
  if Prepared then Exit;
  WizardForm.StatusLabel.Caption := CustomMessage('Preparing');
  // Run the new helper before overwriting an older installation. The same files
  // are stored once in the installer. No installed app or service is launched.
  ExtractTemporaryFiles('{app}\*');
  Helper := ExpandConstant('{tmp}\') + '{app}\G7Bridge.exe';
  if not Exec(Helper, '--prepare-setup', ExtractFileDir(Helper), SW_HIDE, ewWaitUntilTerminated, Code) then
    Result := FmtMessage(CustomMessage('Failed'), [IntToStr(Code), ErrorLog])
  else if Code <> 0 then
    Result := FmtMessage(CustomMessage('Failed'), [IntToStr(Code), ErrorLog])
  else
    Prepared := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    WizardForm.StatusLabel.Caption := CustomMessage('Configuring');
    if not Exec(ExpandConstant('{app}\G7Bridge.exe'), '--configure-service', ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, Code) then
      RaiseException(FmtMessage(CustomMessage('Failed'), [IntToStr(Code), ErrorLog]));
    if (Code <> 0) and (Code <> 3010) then
      RaiseException(FmtMessage(CustomMessage('Failed'), [IntToStr(Code), ErrorLog]));
    DriverRestartRequired := Code = 3010;
  end;
end;

function NeedRestart: Boolean;
begin
  Result := DriverRestartRequired;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := CustomMessage('Components') + NewLine + NewLine + MemoDirInfo + NewLine + MemoTasksInfo;
end;

function InitializeUninstall: Boolean;
begin
  Result := not CheckForMutexes('Local\Grimm.G7Bridge');
  if not Result then MsgBox(CustomMessage('CloseFirst'), mbError, MB_OK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Code: Integer;
begin
  // Run after the user's uninstall confirmation, before Inno deletes any files.
  if CurUninstallStep = usUninstall then
  begin
    if not Exec(ExpandConstant('{app}\G7Bridge.exe'), '--uninstall-service', ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, Code) then
      RaiseException(FmtMessage(CustomMessage('RemovalFailed'), [ErrorLog]));
    if Code <> 0 then RaiseException(FmtMessage(CustomMessage('RemovalFailed'), [ErrorLog]));
    // The app creates its sign-in start per user; remove it for the uninstalling user.
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'G7 Bridge');
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run', 'G7 Bridge');
  end;
end;
