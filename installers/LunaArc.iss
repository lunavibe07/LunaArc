; Build through build_windows_packages.ps1, which supplies these definitions.
#ifndef AppVersion
  #error AppVersion must be provided
#endif
#ifndef SourceDir
  #error SourceDir must be provided
#endif
#ifndef OutputDir
  #error OutputDir must be provided
#endif

[Setup]
AppId=LunaArc.Archiver.Windows
AppName=LunaArc Archiver
AppVersion={#AppVersion}
AppPublisher=LunaArc Software
AppPublisherURL=https://github.com/lunavibe07/LunaArc
AppSupportURL=https://github.com/lunavibe07/LunaArc/issues
DefaultDirName={localappdata}\Programs\LunaArc
DefaultGroupName=LunaArc
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename=LunaArc-{#AppVersion}-setup-win-x64
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\branding\app_logo.ico
LicenseFile=..\branding\EULA_License.txt
UninstallDisplayIcon={app}\LunaArc.exe
CloseApplications=yes
RestartApplications=no
ChangesAssociations=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "contextmenu"; Description: "Add LunaArc extraction and compression commands to Explorer"; GroupDescription: "Explorer integration:"
Name: "modernmenu"; Description: "Windows 11 menu preview (trust a test certificate for Windows; administrator permission may be required)"; GroupDescription: "Explorer integration:"; Flags: unchecked
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\LunaArc"; Filename: "{app}\LunaArc.exe"
Name: "{autoprograms}\Uninstall LunaArc"; Filename: "{uninstallexe}"
Name: "{autodesktop}\LunaArc"; Filename: "{app}\LunaArc.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\LunaArc.exe"; Description: "Launch LunaArc"; Flags: nowait postinstall skipifsilent

[Code]
procedure UpdateContextMenu(const Arguments: String);
var
  ResultCode: Integer;
begin
  if not Exec(ExpandConstant('{app}\luna-cli.exe'), Arguments, ExpandConstant('{app}'),
      SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    RaiseException('Could not run LunaArc to update the Explorer context menu.');
  if ResultCode <> 0 then
    RaiseException('LunaArc could not update the Explorer context menu. Exit code: ' + IntToStr(ResultCode));
end;

function EnableModernMenu: Boolean;
var
  Version: TWindowsVersion;
begin
  Result := WizardIsTaskSelected('modernmenu');
  if Result then exit;
  GetWindowsVersionEx(Version);
  if WizardSilent or not WizardIsTaskSelected('contextmenu') or (Version.Build < 22000) then exit;
  Result := MsgBox('Show LunaArc extraction commands in the main Windows 11 right-click menu?' + #13#10 + #13#10 +
    'This preview uses a test signing certificate. Enabling it adds the supplied certificate to Windows Trusted People for all users. Windows may ask for administrator permission. Only certificate import is elevated; menu registration stays with your current account.' + #13#10 + #13#10 +
    'Choose No to keep the classic menu under Show more options.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then begin
    if WizardIsTaskSelected('contextmenu') then
      UpdateContextMenu('register-shell')
    else
      UpdateContextMenu('unregister-shell --if-path "' + ExpandConstant('{app}') + '"');
    if EnableModernMenu then begin
      if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
          '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\RegisterWindows11Menu.ps1') + '" -TrustCertificate',
          ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
        MsgBox('Windows could not enable the modern menu. The classic LunaArc submenu remains available. Run InstallWindows11Menu.cmd for details.', mbInformation, MB_OK);
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usUninstall) and FileExists(ExpandConstant('{app}\luna-cli.exe')) then
    UpdateContextMenu('unregister-shell --if-path "' + ExpandConstant('{app}') + '"');
end;
