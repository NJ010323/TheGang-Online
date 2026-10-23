using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Network;

/// <summary>Lance un tunnel temporaire vers le serveur local, sans compte pour l'hôte.</summary>
public sealed partial class CloudflareQuickTunnel : IAsyncDisposable
{
    private readonly Process _process;

    private CloudflareQuickTunnel(Process process, Uri publicAddress)
    {
        _process = process;
        PublicAddress = publicAddress;
    }

    /// <summary>Adresse HTTPS fournie par Cloudflare pour la durée du processus.</summary>
    public Uri PublicAddress { get; }

    /// <summary>Indique si le tunnel est encore en cours d'exécution.</summary>
    public bool IsRunning => !_process.HasExited;

    /// <summary>Démarre cloudflared livré avec le jeu et attend son adresse temporaire.</summary>
    public static async Task<CloudflareQuickTunnel> StartAsync(Uri localAddress,
        string? executablePath = null, CancellationToken cancellationToken = default)
    {
        if (!localAddress.IsLoopback || localAddress.Scheme != Uri.UriSchemeHttp)
            throw new ArgumentException("Le tunnel doit viser un serveur HTTP local.", nameof(localAddress));

        var bundledPath = Path.Combine(AppContext.BaseDirectory, "cloudflared.exe");
        var binary = executablePath ?? (File.Exists(bundledPath) ? bundledPath : "cloudflared.exe");
        var process = new Process
        {
            StartInfo = new ProcessStartInfo(binary)
            {
                // HTTP/2 passe par TCP et évite les réseaux qui bloquent QUIC/UDP.
                Arguments = $"tunnel --no-autoupdate --protocol http2 --url {localAddress}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            },
            EnableRaisingEvents = true
        };
        var address = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void ReadLine(object sender, DataReceivedEventArgs eventArgs)
        {
            if (eventArgs.Data is null) return;
            var match = UrlPattern().Match(eventArgs.Data);
            if (match.Success && Uri.TryCreate(match.Value, UriKind.Absolute, out var uri))
                address.TrySetResult(uri);
            if (eventArgs.Data.Contains("Registered tunnel connection", StringComparison.OrdinalIgnoreCase))
                connected.TrySetResult();
        }
        process.OutputDataReceived += ReadLine;
        process.ErrorDataReceived += ReadLine;
        process.Exited += (_, _) =>
        {
            var error = new InvalidOperationException($"Le tunnel s'est arrêté (code {process.ExitCode}).");
            address.TrySetException(error);
            connected.TrySetException(error);
        };
        var started = false;
        try
        {
            started = process.Start();
            if (!started)
                throw new InvalidOperationException("Impossible de démarrer cloudflared.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            var publicAddress = await address.Task.WaitAsync(timeout.Token);
            await connected.Task.WaitAsync(timeout.Token);
            return new CloudflareQuickTunnel(process, publicAddress);
        }
        catch (Exception error)
        {
            // HasExited lève une autre exception si Start a échoué : conserver l'erreur d'origine.
            if (started && !process.HasExited) process.Kill(entireProcessTree: true);
            process.Dispose();
            if (error is Win32Exception startError && startError.NativeErrorCode is 2 or 3)
                throw new FileNotFoundException(
                    "cloudflared.exe est introuvable. Lancez TheGang.exe depuis le dossier publié complet.",
                    binary, startError);
            if (error is OperationCanceledException && !cancellationToken.IsCancellationRequested)
                throw new TimeoutException("Le tunnel Cloudflare n'a pas pu démarrer dans le délai prévu.", error);
            throw;
        }
    }

    /// <summary>Ferme le tunnel avec le lobby de l'hôte.</summary>
    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync().ConfigureAwait(false);
        }
        _process.Dispose();
    }

    [GeneratedRegex(@"https://[a-z0-9-]+\.trycloudflare\.com", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();
}
