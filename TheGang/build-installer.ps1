# Construit un seul EXE à distribuer à partir d'un paquet autonome déjà publié et vérifié.
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts/installer'),
    [string]$CompilerPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PublishDirectory = (Resolve-Path -LiteralPath $PublishDirectory).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$manifest = Get-Content -LiteralPath (Join-Path $PublishDirectory 'package.json') -Raw | ConvertFrom-Json
$version = $manifest.Version
if ($version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw 'Version stable invalide.' }
$required = @('TheGang.exe','TheGang.Updater.exe','cloudflared.exe','cloudflared-LICENSE.txt','update-settings.json')
# Le script ISS embarque exactement ces fichiers : tout ajout futur doit mettre les deux listes à jour.
if (@($manifest.Files.PSObject.Properties).Count -ne $required.Count) { throw 'Le contenu du manifeste ne correspond pas à cet installateur.' }
foreach ($name in $required) {
    if (!$manifest.Files.PSObject.Properties[$name]) { throw "Fichier absent du manifeste : $name" }
    if ((Get-FileHash -LiteralPath (Join-Path $PublishDirectory $name) -Algorithm SHA256).Hash -ne $manifest.Files.$name) { throw "Fichier corrompu : $name" }
}
foreach ($binary in @('TheGang.exe', 'TheGang.Updater.exe')) {
    $binaryVersion = (Get-Item -LiteralPath (Join-Path $PublishDirectory $binary)).VersionInfo.FileVersion
    if ($binaryVersion -ne "$version.0") { throw "Le manifeste et la version de $binary doivent correspondre." }
}
if (!$CompilerPath) { $CompilerPath = & (Join-Path $PSScriptRoot 'Installer/get-compiler.ps1') }
$CompilerPath = (Resolve-Path -LiteralPath $CompilerPath).Path
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$installer = Join-Path $OutputDirectory "TheGang-Setup-$version-win-x64.exe"
if (Test-Path -LiteralPath $installer) { throw 'Un installateur de cette version existe déjà dans le dossier de sortie.' }
& $CompilerPath /Q "/DGameVersion=$version" "/DPublishDir=$PublishDirectory" "/DInstallerOutput=$OutputDirectory" (Join-Path $PSScriptRoot 'Installer/TheGang.iss')
if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $installer)) { throw "Compilation de l'installateur échouée." }
Write-Output "Installateur prêt : $installer"
