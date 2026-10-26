using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Updates;

namespace View;

public partial class App : Application
{
    private Mutex? _installationLock;
    private bool _ownsLock;
    private FileStream? _installationReader;
    private FileStream? _checkLock;
    private readonly CancellationTokenSource _shutdown = new();
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var installation = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var state = UpdateFiles.StateDirectory(installation);
        try
        {
            _installationLock = new Mutex(false, UpdateFiles.LockName(installation));
            try { _ownsLock = _installationLock.WaitOne(0); } catch (AbandonedMutexException) { _ownsLock = true; }
            if (!_ownsLock) { MessageBox.Show("Le jeu ou sa mise à jour est déjà ouvert.", "The Gang"); Shutdown(); return; }
            // Plusieurs clients peuvent jouer depuis le même dossier. Seul l'installateur
            // exige un accès exclusif ; il attend la fermeture de tous ces lecteurs.
            Directory.CreateDirectory(state);
            if (!e.Args.Contains("--update-health") && !e.Args.Contains("--update-recovered"))
            {
                try
                {
                    using var transaction = File.Open(Path.Combine(state, "transaction.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
                }
                catch (IOException)
                {
                    // Ne pas retenir le verrou de démarrage pendant le message :
                    // le processus de validation doit pouvoir ouvrir sa fenêtre.
                    _installationLock.ReleaseMutex();
                    _ownsLock = false;
                    MessageBox.Show("Une mise à jour est en cours. Le jeu va redémarrer automatiquement.", "The Gang");
                    Shutdown(); return;
                }
            }
            var lockPath = Path.Combine(state, "installation.lock");
            if (!File.Exists(lockPath)) { using var created = File.Open(lockPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite); }
            _installationReader = File.Open(lockPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            _installationLock.ReleaseMutex();
            _ownsLock = false;
            try { _checkLock = File.Open(Path.Combine(state, "check.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { /* Un autre client effectue déjà la vérification. */ }
            var window = new MainWindow { StartupUpdateInProgress = true };
            MainWindow = window;
            // L'interface habituelle reste visible, mais aucune partie ne peut démarrer.
            var content = (UIElement)window.Content;
            content.IsEnabled = false;
            var status = new TextBlock { Foreground = Brushes.Wheat, FontSize = 17, Margin = new Thickness(18), Text = "Recherche de mise à jour…" };
            var bar = new ProgressBar { Height = 4, IsIndeterminate = true, Margin = new Thickness(18, 0, 18, 14) };
            var panel = new StackPanel { Background = new SolidColorBrush(Color.FromRgb(25, 16, 16)), VerticalAlignment = VerticalAlignment.Top };
            panel.Children.Add(status); panel.Children.Add(bar);
            window.Content = null;
            var grid = new Grid(); grid.Children.Add(content); grid.Children.Add(panel); window.Content = grid;
            window.Closed += (_, _) => { _shutdown.Cancel(); Shutdown(); };
            window.Show();
            var health = e.Args.Contains("--update-health");
            if (File.Exists(Path.Combine(state, "journal.json")) && !health)
            {
                status.Text = "Restauration de la version précédente…";
                await Handoff(installation, state, state, "--recover", _shutdown.Token);
                Shutdown(); return;
            }
            if (health)
            {
                UpdateFiles.Write(Path.Combine(state, "healthy"), new { Ready = true });
            }
            else if (_checkLock is not null && !e.Args.Contains("--update-recovered") && File.Exists(Path.Combine(installation, "update-settings.json")))
            {
                try
                {
                    using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
                    var progress = new Progress<(string Text, double? Percent)>(p =>
                    { status.Text = p.Text; bar.IsIndeterminate = p.Percent is null; bar.Value = p.Percent ?? 0; });
                    var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version!;
                    var current = new Version(assemblyVersion.Major, assemblyVersion.Minor, assemblyVersion.Build);
                    var update = await new UpdateClient(http).Prepare(installation, state, current, progress, _shutdown.Token);
                    if (update is not null)
                    {
                        status.Text = "Installation et redémarrage…";
                        await Handoff(installation, state, update.Directory, update.Version, _shutdown.Token);
                        Shutdown(); return;
                    }
                }
                catch (Exception error) when (!_shutdown.IsCancellationRequested)
                {
                    UpdateFiles.Log(state, "Vérification/téléchargement : " + error.GetType().Name);
                    MessageBox.Show(window, "La mise à jour n'a pas pu être effectuée. Vous pouvez jouer avec la version actuelle.", "The Gang", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            if (_shutdown.IsCancellationRequested) return;
            var failure = Path.Combine(state, "failure.txt");
            if (File.Exists(failure))
            { MessageBox.Show(window, File.ReadAllText(failure), "The Gang", MessageBoxButton.OK, MessageBoxImage.Information); File.Delete(failure); }
            grid.Children.Remove(panel);
            content.IsEnabled = true;
            window.StartupUpdateInProgress = false;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception error)
        {
            UpdateFiles.Log(state, "Démarrage : " + error.GetType().Name);
            MessageBox.Show("Le démarrage n'a pas abouti. Consultez le journal dans %LOCALAPPDATA%\\TheGang\\Updates. Si une restauration est en attente, relancez le jeu.", "The Gang");
            Shutdown();
        }
    }
    private static async Task Handoff(string installation, string state, string staged, string version, CancellationToken token)
    {
        var runner = Path.Combine(state, "runner");
        Directory.CreateDirectory(runner);
        var helper = Path.Combine(runner, "TheGang.Updater.exe");
        if (version != "--recover") File.Copy(Path.Combine(installation, "TheGang.Updater.exe"), helper, true);
        if (!File.Exists(helper)) throw new IOException("Auxiliaire de restauration absent.");
        var ready = Path.Combine(state, "helper-ready");
        File.Delete(ready);
        using var self = Process.GetCurrentProcess();
        var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = runner };
        foreach (var argument in new[] { installation, staged, version, self.Id.ToString(), self.StartTime.ToUniversalTime().Ticks.ToString() }) start.ArgumentList.Add(argument);
        using var child = Process.Start(start) ?? throw new IOException("Auxiliaire indisponible.");
        for (var i = 0; i < 100; i++)
        {
            token.ThrowIfCancellationRequested();
            if (File.Exists(ready)) return;
            if (child.HasExited) break;
            await Task.Delay(100, token);
        }
        // Le parent reste ouvert : l'auxiliaire expirera sans toucher aux fichiers.
        throw new IOException("L'auxiliaire ne répond pas.");
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _shutdown.Cancel();
        if (_ownsLock) _installationLock?.ReleaseMutex();
        _installationReader?.Dispose();
        _checkLock?.Dispose();
        _installationLock?.Dispose();
        base.OnExit(e);
    }
}
