# Prépare un dossier autonome : les joueurs lancent TheGang.exe sans installer cloudflared.
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts/win-x64'),
    [string]$Version = '1.0.0',
    [string]$DistributionRepository = (Get-Content (Join-Path $PSScriptRoot 'update-settings.json') -Raw | ConvertFrom-Json).Repository
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw 'Version attendue : X.Y.Z stable.' }
if ($DistributionRepository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Dépôt attendu : propriétaire/nom.' }
# Refuser un dossier déjà rempli : aucun fichier personnel ou ancien binaire dans le paquet.
if ((Test-Path -LiteralPath $OutputDirectory) -and (Get-ChildItem -LiteralPath $OutputDirectory -Force | Select-Object -First 1)) {
    throw 'Choisissez un dossier de sortie vide pour produire un paquet reproductible.'
}
$cloudflaredVersion = '2026.10.1'
$expectedSha256 = '4D2490E74082C5D48DBFEDAF92576FA5BD1702379AB88EB7FAA0CFBE8DC891B6'
$cloudflaredUrl = "https://github.com/cloudflare/cloudflared/releases/download/$cloudflaredVersion/cloudflared-windows-amd64.exe"
$licenseUrl = "https://raw.githubusercontent.com/cloudflare/cloudflared/$cloudflaredVersion/LICENSE"

dotnet publish (Join-Path $PSScriptRoot 'View/View.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=$Version -p:DebugType=None -p:DebugSymbols=false -o $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'La publication du jeu a échoué.' }

# Écarte l'ancien nom pour ne pas laisser deux versions du jeu dans un dossier republié.
$legacyExecutable = Join-Path $OutputDirectory 'View.exe'
if (Test-Path -LiteralPath $legacyExecutable) { Remove-Item -LiteralPath $legacyExecutable }

$binaryPath = Join-Path $OutputDirectory 'cloudflared.exe'
if (-not (Test-Path -LiteralPath $binaryPath) -or
    (Get-FileHash -LiteralPath $binaryPath -Algorithm SHA256).Hash -ne $expectedSha256) {
    Invoke-WebRequest -Uri $cloudflaredUrl -OutFile $binaryPath
}
$actualSha256 = (Get-FileHash -LiteralPath $binaryPath -Algorithm SHA256).Hash
if ($actualSha256 -ne $expectedSha256) {
    Remove-Item -LiteralPath $binaryPath
    throw 'La somme SHA-256 de cloudflared ne correspond pas à la version officielle.'
}
Invoke-WebRequest -Uri $licenseUrl -OutFile (Join-Path $OutputDirectory 'cloudflared-LICENSE.txt')

Write-Output "Jeu prêt : $(Join-Path $OutputDirectory 'TheGang.exe')"

# L'auxiliaire autonome est copié hors installation avant de remplacer sa propre version.
dotnet publish (Join-Path $PSScriptRoot 'Updater/Updater.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=$Version -p:DebugType=None -p:DebugSymbols=false -o $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw "La publication de l'auxiliaire a échoué." }
@{ Repository = $DistributionRepository } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'update-settings.json') -Encoding utf8
# Le manifeste couvre exactement les fichiers distribués ; le ZIP est aussi vérifié par le digest GitHub.
$files = [ordered]@{}
Get-ChildItem -LiteralPath $OutputDirectory -File | Sort-Object Name | ForEach-Object {
    $files[$_.Name] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
}
@{ Version = $Version; Files = $files } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'package.json') -Encoding utf8
