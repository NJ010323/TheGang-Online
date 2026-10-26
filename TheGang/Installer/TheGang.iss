; Installateur joueur : les joueurs ouvrent ensuite TheGang.exe via leurs raccourcis habituels.
#ifndef GameVersion
  #error GameVersion is required
#endif
#ifndef PublishDir
  #error PublishDir is required
#endif
#ifndef InstallerOutput
  #error InstallerOutput is required
#endif
#ifndef InstallerAppId
  #define InstallerAppId "TheGangOnline"
#endif
#ifndef InstallerFileName
  #define InstallerFileName "TheGang-Setup-" + GameVersion + "-win-x64"
#endif
#ifndef ShortcutName
  #define ShortcutName "The Gang"
#endif

[Setup]
AppId={#InstallerAppId}
AppName=The Gang
AppVersion={#GameVersion}
AppPublisher=TheGang-Online
VersionInfoVersion={#GameVersion}.0
DefaultDirName={localappdata}\Programs\TheGang
DefaultGroupName={#ShortcutName}
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.17763
WizardStyle=modern
SetupIconFile=..\View\Assets\app-icon.ico
UninstallDisplayIcon={app}\TheGang.exe
OutputDir={#InstallerOutput}
OutputBaseFilename={#InstallerFileName}
Compression=lzma2/normal
SolidCompression=yes
SetupLogging=yes
UsePreviousAppDir=yes
; Même noms que Updates.InstallerIntegration : aucune fermeture forcée d'une partie.
AppMutex=Local\TheGangOnline.Running
SetupMutex=Local\TheGangOnline.Setup
CloseApplications=no
RestartApplications=no
Uninstallable=yes
UninstallDisplayName=The Gang

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le Bureau"; GroupDescription: "Raccourcis :"

[Files]
; Liste explicite : ne jamais embarquer les sauvegardes, journaux ou outils du développeur.
Source: "{#PublishDir}\TheGang.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\TheGang.Updater.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\cloudflared.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\cloudflared-LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\update-settings.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\package.json"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#ShortcutName}"; Filename: "{app}\TheGang.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\{#ShortcutName}"; Filename: "{app}\TheGang.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\TheGang.exe"; Parameters: "--after-install"; Description: "Lancer The Gang"; Flags: nowait postinstall skipifsilent

[Code]
function MapInvariant(Locale, Flags: Cardinal; Source: String; SourceLength: Integer;
  Destination: String; DestinationLength: Integer): Integer;
  external 'LCMapStringW@kernel32.dll stdcall';

// Uppercase() d'Inno ne traite que l'ASCII. Le chemin peut contenir des accents :
// employer la casse Unicode invariante, comme le service .NET de mise à jour.
function InvariantUppercase(Value: String): String;
var
  Required: Integer;
begin
  Required := MapInvariant($007F, $00000200, Value, Length(Value), '', 0);
  if Required = 0 then RaiseException('Impossible de vérifier le dossier de mise à jour.');
  Result := StringOfChar(#0, Required);
  if MapInvariant($007F, $00000200, Value, Length(Value), Result, Required) = 0 then
    RaiseException('Impossible de vérifier le dossier de mise à jour.');
end;

// Même identifiant de dossier que UpdateFiles.StateDirectory (chemin absolu, majuscules, UTF-8).
function RecoveryPending(): Boolean;
var
  Installation, StateId: String;
begin
  Installation := InvariantUppercase(RemoveBackslashUnlessRoot(ExpandFileName(ExpandConstant('{app}'))));
  StateId := Uppercase(Copy(GetSHA256OfString(UTF8Encode(Installation)), 1, 24));
  Result := FileExists(ExpandConstant('{localappdata}\TheGang\Updates\') + StateId + '\journal.json');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExistingMS, ExistingLS, InstallerMS, InstallerLS: Cardinal;
begin
  Result := '';
  if CheckForMutexes('Local\TheGangOnline.Running') then
  begin
    Result := 'Fermez The Gang et attendez la fin de sa mise à jour avant de poursuivre.';
    Exit;
  end;
  if RecoveryPending() then
  begin
    Result := 'Une restauration est en attente. Relancez le jeu pour restaurer la version fonctionnelle avant de réinstaller.';
    Exit;
  end;
  // Ne pas remettre une ancienne version par-dessus un jeu déjà mis à jour automatiquement.
  if GetVersionNumbers(ExpandConstant('{app}\TheGang.exe'), ExistingMS, ExistingLS) and
     GetVersionNumbers(ExpandConstant('{srcexe}'), InstallerMS, InstallerLS) then
    if (ExistingMS > InstallerMS) or ((ExistingMS = InstallerMS) and (ExistingLS > InstallerLS)) then
      Result := 'Une version plus récente de The Gang est déjà installée. Utilisez son raccourci ou un installateur plus récent.';
end;

function InitializeUninstall(): Boolean;
begin
  // Garder le même verrou pendant la désinstallation pour bloquer un démarrage concurrent.
  if CheckForMutexes('Local\TheGangOnline.Setup') then
  begin
    SuppressibleMsgBox('Une installation est déjà en cours. Réessayez lorsqu''elle sera terminée.', mbError, MB_OK, IDOK);
    Result := False;
    Exit;
  end;
  CreateMutex('Local\TheGangOnline.Setup');
  Result := not RecoveryPending();
  if not Result then
    SuppressibleMsgBox('Une restauration est en attente. Relancez The Gang avant de désinstaller.', mbError, MB_OK, IDOK);
end;
