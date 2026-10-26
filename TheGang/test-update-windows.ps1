# Essais développeur des exécutables publiés ; aucun accès à une Release ni aux données réelles du joueur.
param(
    [Parameter(Mandatory)][string]$Baseline,
    [Parameter(Mandatory)][string]$NewPackage,
    [ValidateSet('Success', 'Rollback', 'Recovery')][string]$Scenario = 'Success'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$baselinePath = (Resolve-Path -LiteralPath $Baseline).Path
$newPath = (Resolve-Path -LiteralPath $NewPackage).Path
$fixtureRoot = Join-Path $PSScriptRoot ('artifacts/update-smoke-' + [Guid]::NewGuid().ToString('N'))
$installation = Join-Path $fixtureRoot 'game'
New-Item -ItemType Directory -Force $installation | Out-Null
Get-ChildItem -LiteralPath $baselinePath -File | Copy-Item -Destination $installation
$hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($installation.ToUpperInvariant()))).Substring(0,24)
$state = Join-Path $env:LOCALAPPDATA "TheGang/Updates/$hash"
$staged = Join-Path $state 'fixture-staged'
$runner = Join-Path $state 'runner'
New-Item -ItemType Directory -Force $staged,$runner | Out-Null
Get-ChildItem -LiteralPath $newPath -File | Copy-Item -Destination $staged
Copy-Item -LiteralPath (Join-Path $baselinePath 'TheGang.Updater.exe') -Destination $runner
$manifest = Get-Content -LiteralPath (Join-Path $staged 'package.json') -Raw | ConvertFrom-Json
$personal = Join-Path $installation 'personal-test.dat'
Set-Content -LiteralPath $personal -Value 'Donnees personnelles de test a conserver.'
$personalHash = (Get-FileHash -LiteralPath $personal).Hash
$expected = if ($Scenario -eq 'Success') { $newPath } else { $baselinePath }
$expectedHash = (Get-FileHash -LiteralPath (Join-Path $expected 'TheGang.exe')).Hash
$helper = $null
$dummy = $null
try {
    if ($Scenario -eq 'Rollback') {
        # Le paquet est intègre mais son exécutable ne démarre pas : tester le retour arrière réel.
        Set-Content -LiteralPath (Join-Path $staged 'TheGang.exe') -Value 'executable volontairement invalide'
        $manifest.Files.'TheGang.exe' = (Get-FileHash -LiteralPath (Join-Path $staged 'TheGang.exe') -Algorithm SHA256).Hash
        $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $staged 'package.json')
    }
    if ($Scenario -eq 'Recovery') {
        # Simuler une coupure après journal durable et premier remplacement de fichier.
        $backup = Join-Path $state 'backup'
        New-Item -ItemType Directory -Force $backup | Out-Null
        $files = @($manifest.Files.PSObject.Properties.Name) + @('package.json')
        $existed = @($files | Where-Object { Test-Path -LiteralPath (Join-Path $installation $_) })
        $existed | ForEach-Object { Copy-Item -LiteralPath (Join-Path $installation $_) -Destination $backup }
        @{ Files = $files; Existed = $existed } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $state 'journal.json')
        Set-Content -LiteralPath (Join-Path $state 'failed-version.txt') -Value "v$($manifest.Version)" -NoNewline
        Copy-Item -LiteralPath (Join-Path $staged 'TheGang.exe') -Destination (Join-Path $installation 'TheGang.exe') -Force
        $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $installation 'TheGang.exe'))
        $start.UseShellExecute = $false
        $start.WorkingDirectory = $installation
        [Diagnostics.Process]::Start($start) | Out-Null
    } else {
        # Un processus inoffensif représente le parent qui doit se terminer avant les copies.
        $dummy = Start-Process -FilePath powershell.exe -WindowStyle Hidden -ArgumentList '-NoProfile','-Command','Start-Sleep -Seconds 2' -PassThru
        $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $runner 'TheGang.Updater.exe'))
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        @($installation,$staged,"v$($manifest.Version)",$dummy.Id.ToString(),$dummy.StartTime.ToUniversalTime().Ticks.ToString()) | ForEach-Object { $start.ArgumentList.Add($_) }
        $helper = [Diagnostics.Process]::Start($start)
    }
    $deadline = [DateTime]::UtcNow.AddSeconds(75)
    do {
        Start-Sleep -Milliseconds 250
        $journal = Test-Path -LiteralPath (Join-Path $state 'journal.json')
        $healthy = Test-Path -LiteralPath (Join-Path $state 'healthy')
        $failed = Test-Path -LiteralPath (Join-Path $state 'failed-version.txt')
        # Ne pas ouvrir l'exécutable pendant les copies : Get-FileHash le verrouille sous Windows.
        $canInspect = !$journal -and $(if ($helper) { $helper.HasExited } else { Test-Path -LiteralPath (Join-Path $state 'failure.txt') })
        $hashMatches = $canInspect -and ((Get-FileHash -LiteralPath (Join-Path $installation 'TheGang.exe')).Hash -eq $expectedHash)
        $clientReady = @(Get-Process -Name TheGang -ErrorAction SilentlyContinue | Where-Object {
            try { $_.MainModule.FileName -eq (Join-Path $installation 'TheGang.exe') -and $_.MainWindowHandle -ne 0 } catch { $false }
        }).Count -gt 0
        $done = $clientReady -and $hashMatches -and !$journal -and $(if ($Scenario -eq 'Success') { $healthy -and $helper.HasExited } else { $failed -and $(if ($helper) { $helper.HasExited } else { Test-Path -LiteralPath (Join-Path $state 'failure.txt') }) })
    } while (!$done -and [DateTime]::UtcNow -lt $deadline)
    if (!$done) { throw "Echec du scenario $Scenario. Diagnostic : $state" }
    if ((Get-FileHash -LiteralPath $personal).Hash -ne $personalHash) { throw 'Donnees personnelles modifiees.' }
    # Vérifier aussi le remplacement (ou la restauration) de l'auxiliaire.
    if ((Get-FileHash -LiteralPath (Join-Path $installation 'TheGang.Updater.exe')).Hash -ne (Get-FileHash -LiteralPath (Join-Path $expected 'TheGang.Updater.exe')).Hash) { throw 'Auxiliaire incorrect.' }
    $result = [PSCustomObject]@{ Scenario=$Scenario; Success=$true; Installation=$installation; State=$state; PersonalDataPreserved=$true; JournalPending=$journal }
    $result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $fixtureRoot 'result.json')
    $result | ConvertTo-Json
} finally {
    # Dossiers uniques créés par CE test : ne jamais arrêter les autres clients du joueur.
    Get-Process -Name TheGang -ErrorAction SilentlyContinue | Where-Object {
        try { $_.MainModule.FileName -eq (Join-Path $installation 'TheGang.exe') } catch { $false }
    } | Stop-Process -Force
    Get-Process -Name TheGang.Updater -ErrorAction SilentlyContinue | Where-Object {
        try { $_.MainModule.FileName -eq (Join-Path $runner 'TheGang.Updater.exe') } catch { $false }
    } | Stop-Process -Force
}
