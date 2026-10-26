# Validation isolée : AppId, raccourcis, répertoire et marqueurs propres au test.
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [string]$PreviousPublishDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PublishDirectory = (Resolve-Path -LiteralPath $PublishDirectory).Path
$syntheticPrevious = [string]::IsNullOrWhiteSpace($PreviousPublishDirectory)
if (!$syntheticPrevious) { $PreviousPublishDirectory = (Resolve-Path -LiteralPath $PreviousPublishDirectory).Path }
$token = [Guid]::NewGuid().ToString('N')
$fixture = Join-Path $PSScriptRoot "../artifacts/installer-test-$token"
$fixture = [IO.Path]::GetFullPath($fixture)
$installation = Join-Path $fixture 'Jeu-épreuve'
$shortcutName = "The Gang Test $token"
$appId = "TheGangOnline.Test.$token"
$registry = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\${appId}_is1"
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) "$shortcutName.lnk"
$startShortcut = Join-Path ([Environment]::GetFolderPath('Programs')) "$shortcutName/$shortcutName.lnk"
$profileMarker = Join-Path $env:LOCALAPPDATA "TheGang/installer-test-$token.dat"
New-Item -ItemType Directory -Force $fixture,(Split-Path $profileMarker) | Out-Null
Set-Content -LiteralPath $profileMarker -Value 'donnees utilisateur de test'
$stateId = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($installation.ToUpperInvariant()))).Substring(0,24)
$state = Join-Path $env:LOCALAPPDATA "TheGang/Updates/$stateId"
$compiler = & (Join-Path $PSScriptRoot 'get-compiler.ps1')
$script = Join-Path $PSScriptRoot 'TheGang.iss'
function Assert-Test([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message } }
function Compile-TestInstaller([string]$Package, [string]$Name, [string]$VersionOverride = '') {
    $version = (Get-Content -LiteralPath (Join-Path $Package 'package.json') -Raw | ConvertFrom-Json).Version
    if ($VersionOverride) { $version = $VersionOverride }
    & $compiler /Q "/DGameVersion=$version" "/DPublishDir=$Package" "/DInstallerOutput=$fixture" "/DInstallerAppId=$appId" "/DInstallerFileName=$Name" "/DShortcutName=$shortcutName" $script
    if ($LASTEXITCODE -ne 0) { throw 'Compilation du fixture échouée.' }
    return (Join-Path $fixture "$Name.exe")
}
function Run-Setup([string]$Executable, [string]$LogName, [string[]]$Extra = @()) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/LOG=$(Join-Path $fixture $LogName)") + $Extra | ForEach-Object { $start.ArgumentList.Add($_) }
    $process = [Diagnostics.Process]::Start($start)
    if (!$process.WaitForExit(120000)) { throw "Processus de test bloqué : $LogName" }
    return $process.ExitCode
}
$installed = $false
try {
    $installer = Compile-TestInstaller $PublishDirectory 'Current-Test-Setup'
    # Sans ancien paquet (CI), un fixture portant la version 0.0.0 teste le refus
    # AVANT toute copie. Ce fixture n'est jamais un livrable de distribution.
    $currentVersion = [version](Get-Content -LiteralPath (Join-Path $PublishDirectory 'package.json') -Raw | ConvertFrom-Json).Version
    $testDowngrade = $currentVersion -gt [version]'0.0.0'
    $previousInstaller = $null
    if ($testDowngrade) {
        $previousInstaller = if ($syntheticPrevious) {
            Compile-TestInstaller $PublishDirectory 'Previous-Test-Setup' '0.0.0'
        } else { Compile-TestInstaller $PreviousPublishDirectory 'Previous-Test-Setup' }
    }
    Assert-Test ((Run-Setup $installer 'install.log' @("/DIR=$installation",'/TASKS=desktopicon')) -eq 0) 'Installation échouée.'
    $installed = $true
    Assert-Test (Test-Path -LiteralPath $registry) 'Entrée Windows Applications absente.'
    Assert-Test (Test-Path -LiteralPath $desktopShortcut) 'Raccourci Bureau absent.'
    Assert-Test (Test-Path -LiteralPath $startShortcut) 'Raccourci Démarrer absent.'
    $shell = New-Object -ComObject WScript.Shell
    Assert-Test ($shell.CreateShortcut($desktopShortcut).TargetPath -eq (Join-Path $installation 'TheGang.exe')) 'Mauvaise cible du raccourci.'
    $manifest = Get-Content -LiteralPath (Join-Path $PublishDirectory 'package.json') -Raw | ConvertFrom-Json
    foreach ($file in $manifest.Files.PSObject.Properties) {
        Assert-Test ((Get-FileHash -LiteralPath (Join-Path $installation $file.Name)).Hash -eq $file.Value) "Fichier mal installé : $($file.Name)"
    }
    $uninstaller = Join-Path $installation 'unins000.exe'
    $uninstallerHash = (Get-FileHash -LiteralPath $uninstaller).Hash
    $notes = Join-Path $installation 'notes-personnelles.txt'
    Set-Content -LiteralPath $notes -Value 'a conserver'
    Assert-Test ((Run-Setup $installer 'repair.log' @("/DIR=$installation")) -eq 0) 'Réinstallation échouée.'
    Assert-Test (Test-Path -LiteralPath $notes) 'Fichier personnel perdu pendant réparation.'
    if ($testDowngrade) {
        Assert-Test ((Run-Setup $previousInstaller 'downgrade-refused.log' @("/DIR=$installation")) -ne 0) 'Une ancienne version a été acceptée.'
    }

    # Simulation du signal créé par le jeu : aucun processus joueur n'est arrêté.
    $running = [Threading.Mutex]::new($false, 'Local\TheGangOnline.Running')
    try {
        Assert-Test ((Run-Setup $installer 'running-refused.log' @("/DIR=$installation")) -ne 0) 'Jeu ouvert : installation non bloquée.'
        Assert-Test ((Run-Setup $uninstaller 'uninstall-running-refused.log') -ne 0) 'Jeu ouvert : désinstallation non bloquée.'
    } finally { $running.Dispose() }

    New-Item -ItemType Directory -Force $state | Out-Null
    $journal = Join-Path $state 'journal.json'
    Set-Content -LiteralPath $journal -Value '{}'
    Assert-Test ((Run-Setup $installer 'recovery-refused.log' @("/DIR=$installation")) -ne 0) 'Restauration en attente non détectée.'
    Assert-Test ((Run-Setup $uninstaller 'uninstall-recovery-refused.log') -ne 0) 'Désinstallation autorisée pendant une restauration.'
    Remove-Item -LiteralPath $journal

    # Exécuter les vraies copies de l'updater et vérifier que les fichiers de désinstallation restent intacts.
    [Reflection.Assembly]::LoadFrom((Join-Path $PSScriptRoot '../Updates/bin/Release/net8.0/Updates.dll')) | Out-Null
    [Updates.UpdateFiles]::Install($installation, $state, $PublishDirectory)
    Assert-Test ((Get-FileHash -LiteralPath $uninstaller).Hash -eq $uninstallerHash) 'L''updater a modifié le désinstallateur.'
    Remove-Item -LiteralPath (Join-Path $state 'journal.json')
    Assert-Test ((Run-Setup $uninstaller 'uninstall.log') -eq 0) 'Désinstallation échouée.'
    $installed = $false
    Assert-Test (!(Test-Path -LiteralPath (Join-Path $installation 'TheGang.exe'))) 'Le jeu reste installé.'
    Assert-Test (!(Test-Path -LiteralPath $registry)) 'Entrée Windows non supprimée.'
    Assert-Test (!(Test-Path -LiteralPath $desktopShortcut)) 'Raccourci Bureau non supprimé.'
    Assert-Test (!(Test-Path -LiteralPath $startShortcut)) 'Raccourci Démarrer non supprimé.'
    Assert-Test (Test-Path -LiteralPath $notes) 'Fichier personnel supprimé par la désinstallation.'
    Assert-Test (Test-Path -LiteralPath $profileMarker) 'Donnée du profil supprimée.'
    $result = @{ Success=$true; Installation=$true; Shortcuts=$true; Repair=$true; DowngradeBlocked=$(if ($testDowngrade) { $true } else { $null }); RunningGameProtected=$true; RecoveryProtected=$true; UpdatePreservesUninstaller=$true; Uninstall=$true; PersonalDataPreserved=$true }
    $result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $fixture 'result.json')
    $result | ConvertTo-Json
} finally {
    # Nettoyage limité aux marqueurs créés pour ce test ; aucun répertoire du joueur n'est supprimé.
    $journal = Join-Path $state 'journal.json'
    if (Test-Path -LiteralPath $journal) { Remove-Item -LiteralPath $journal }
    if ($installed -and (Test-Path -LiteralPath (Join-Path $installation 'unins000.exe'))) {
        Run-Setup (Join-Path $installation 'unins000.exe') 'cleanup-uninstall.log' | Out-Null
    }
    if (Test-Path -LiteralPath $profileMarker) { Remove-Item -LiteralPath $profileMarker }
    if (Test-Path -LiteralPath (Join-Path $fixture 'result.json')) {
        # La sauvegarde appartient exclusivement à cette installation de test UUID.
        $stateRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'TheGang/Updates'))
        $resolvedState = [IO.Path]::GetFullPath($state)
        if (!$resolvedState.StartsWith($stateRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Nettoyage refusé : état de test hors du dossier prévu.'
        }
        if (Test-Path -LiteralPath $resolvedState) { Remove-Item -LiteralPath $resolvedState -Recurse -Force }
        foreach ($name in @('Current-Test-Setup.exe', 'Previous-Test-Setup.exe')) {
            $testBinary = Join-Path $fixture $name
            if (Test-Path -LiteralPath $testBinary) { Remove-Item -LiteralPath $testBinary }
        }
    }
}
