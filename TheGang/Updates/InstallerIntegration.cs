namespace Updates;

/// <summary>Coordonne le jeu et son auxiliaire avec l'installateur, sans fermer de processus.</summary>
public static class InstallerIntegration
{
    // Ces noms doivent correspondre aux directives AppMutex / SetupMutex de TheGang.iss.
    public const string RunningMutexName = @"Local\TheGangOnline.Running";
    public const string SetupMutexName = @"Local\TheGangOnline.Setup";

    public static bool IsSetupRunning()
    {
        if (!Mutex.TryOpenExisting(SetupMutexName, out var setup)) return false;
        setup.Dispose();
        return true;
    }
}
