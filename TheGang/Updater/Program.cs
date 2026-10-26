using System.Diagnostics;
using Updates;

// Arguments produits uniquement par le service interne du jeu.
if (args.Length != 5) return;
var installation = Path.GetFullPath(args[0]);
var state = UpdateFiles.StateDirectory(installation);
var staged = Path.GetFullPath(args[1]);
var version = args[2];
var pid = int.Parse(args[3]);
var startTicks = long.Parse(args[4]);
Directory.CreateDirectory(state);
// Verrou non lié au thread, conservé aussi pendant le redémarrage asynchrone.
// Deux auxiliaires ne doivent jamais restaurer le journal l'un de l'autre.
FileStream operation;
try { operation = File.Open(Path.Combine(state, "transaction.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
catch (IOException) { return; }
using var operationLifetime = operation;
File.WriteAllText(Path.Combine(state, "helper-ready"), "ready");
Process? restarted = null;
FileStream? exclusive = null;
using var gate = new Mutex(false, UpdateFiles.LockName(installation));
try
{
    // Le PID et son heure de création évitent d'attendre un PID réutilisé.
    try
    {
        using var parent = Process.GetProcessById(pid);
        if (parent.StartTime.ToUniversalTime().Ticks != startTicks) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await parent.WaitForExitAsync(timeout.Token);
    }
    catch (ArgumentException) { }
    catch (OperationCanceledException)
    {
        UpdateFiles.Log(state, "Installation annulée : le processus initial ne s'est pas fermé.");
        return;
    }
    var acquired = false;
    try { acquired = gate.WaitOne(TimeSpan.FromSeconds(15)); } catch (AbandonedMutexException) { acquired = true; }
    if (!acquired) throw new IOException("Le jeu est encore ouvert.");
    try
    {
        exclusive = OpenExclusive();
        if (version != "--recover" && !staged.StartsWith(state + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Dossier temporaire invalide.");
        if (version == "--recover")
        {
            UpdateFiles.Recover(installation, state);
            File.WriteAllText(Path.Combine(state, "failure.txt"), "Une installation interrompue a été restaurée.");
        }
        else
        {
            // Le mutex est lié au thread : pas de reprise asynchrone sous ce verrou.
            UpdateFiles.VerifyPackage(staged, UpdateFiles.ParseVersion(version)).GetAwaiter().GetResult();
            UpdateFiles.Recover(installation, state);
            File.WriteAllText(Path.Combine(state, "failed-version.txt"), version);
            UpdateFiles.Install(installation, state, staged);
            File.Delete(Path.Combine(state, "healthy"));
        }
    }
    finally { exclusive?.Dispose(); exclusive = null; gate.ReleaseMutex(); }
    if (version == "--recover") { StartGame("--update-recovered"); return; }
    restarted = StartGame("--update-health");
    var deadline = DateTime.UtcNow.AddSeconds(45);
    while (DateTime.UtcNow < deadline && !File.Exists(Path.Combine(state, "healthy")) && !restarted.HasExited) await Task.Delay(250);
    if (!File.Exists(Path.Combine(state, "healthy"))) throw new IOException("La nouvelle version n'a pas confirmé son démarrage.");
    File.Delete(Path.Combine(state, "journal.json"));
    File.Delete(Path.Combine(state, "failed-version.txt"));
    UpdateFiles.Log(state, "Mise à jour installée et démarrage confirmé : " + version);
}
catch (Exception error)
{
    UpdateFiles.Log(state, "Échec auxiliaire : " + error.GetType().Name);
    if (restarted is not null && !restarted.HasExited)
    {
        // Uniquement le processus que cet auxiliaire vient de lancer.
        restarted.Kill();
        await restarted.WaitForExitAsync();
    }
    // Un refus avant la publication du journal n'a changé aucun fichier.
    // Relancer l'ancienne version reste possible même si un autre client joue encore.
    if (!File.Exists(Path.Combine(state, "journal.json")))
    {
        File.WriteAllText(Path.Combine(state, "failure.txt"), "La mise à jour n'a pas pu être installée. La version actuelle reste disponible. Fermez les autres clients avant de réessayer.");
        StartGame("--update-recovered");
        return;
    }
    var acquired = false;
    try
    {
        try { acquired = gate.WaitOne(TimeSpan.FromSeconds(15)); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) return;
        exclusive = OpenExclusive();
        UpdateFiles.Recover(installation, state);
        File.WriteAllText(Path.Combine(state, "failure.txt"), "La mise à jour a échoué. La version précédente a été restaurée.");
    }
    catch (Exception recoveryError)
    {
        UpdateFiles.Log(state, "Restauration à reprendre : " + recoveryError.GetType().Name);
        NativeMessage.Show("La restauration est en attente. Fermez les autres clients et les programmes qui verrouillent le dossier, puis relancez TheGang.exe. Sauvegarde et journal : " + state);
        return;
    }
    finally { exclusive?.Dispose(); if (acquired) gate.ReleaseMutex(); }
    StartGame("--update-recovered");
}
Process StartGame(string argument) => Process.Start(new ProcessStartInfo(Path.Combine(installation, "TheGang.exe"))
{ UseShellExecute = false, WorkingDirectory = installation, ArgumentList = { argument } }) ?? throw new IOException("Redémarrage impossible.");


// Attendre les autres clients sans arrêter leurs processus ni leur partie.
FileStream OpenExclusive()
{
    for (var attempt = 0; ; attempt++)
    {
        try { return File.Open(Path.Combine(state, "installation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) when (attempt < 150) { Thread.Sleep(100); }
    }
}
