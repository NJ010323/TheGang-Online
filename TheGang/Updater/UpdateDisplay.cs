using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Updates;

// Le dispatcher possède son thread STA ; la transaction conserve son propre thread et ses verrous.
internal sealed class UpdateDisplay : IDisposable
{
    private readonly Thread thread;
    private Window? window;
    private UpdateProgressPanel? panel;
    public UpdateDisplay()
    {
        using var ready = new ManualResetEventSlim();
        thread = new Thread(() =>
        {
            try
            {
                panel = new UpdateProgressPanel();
                panel.Report(new(UpdateStage.Install, "Installation de la nouvelle version…"));
                window = new Window { Title = "The Gang — Mise à jour",
                    Content = new Viewbox { Child = panel, Margin = new Thickness(22),
                        Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly },
                    Icon = BitmapFrame.Create(new Uri("pack://application:,,,/TheGang.Updater;component/Assets/app-icon.ico")),
                    Width = Math.Min(920, SystemParameters.WorkArea.Width - 32),
                    Height = Math.Min(700, SystemParameters.WorkArea.Height - 32),
                    ResizeMode = ResizeMode.NoResize,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Background = new SolidColorBrush(Color.FromRgb(18, 15, 20)) };
                // Fermer cette fenêtre ne doit pas interrompre le remplacement des fichiers.
                window.Closing += (_, e) => { e.Cancel = true; window.Hide(); };
                window.Show();

            }
            catch { ready.Set(); return; /* L'affichage ne doit jamais faire échouer une installation. */ }
            ready.Set();
            Dispatcher.Run();
        }) { IsBackground = true, Name = "Update progress" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
    }
    public void Report(UpdateProgress progress)
    {
        if (window is { } target && !target.Dispatcher.HasShutdownStarted)
            target.Dispatcher.BeginInvoke(() => panel?.Report(progress));
    }
    public void Dispose()
    {
        if (window is { } target && !target.Dispatcher.HasShutdownStarted)
            target.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
        thread.Join(TimeSpan.FromSeconds(2));
    }
}
