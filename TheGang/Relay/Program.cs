using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

// Relais TCP simple pour connecter un hôte et des invités via un serveur central.
// Protocole:
// - HOST <code> : l'hôte s'enregistre.
// - JOIN <code> : un invité demande une connexion.
// - DATA <token> : l'hôte ouvre un tunnel pour un invité spécifique.
var port = 9000;
if (args.Length > 0 && int.TryParse(args[0], out var parsedPort))
{
    port = parsedPort;
}

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var server = new RelayServer();
await server.RunAsync(port, cts.Token).ConfigureAwait(false);

internal sealed class RelayServer
{
    private readonly ConcurrentDictionary<string, HostSession> _hosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PendingJoin> _pending = new();

    public async Task RunAsync(int port, CancellationToken token)
    {
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        Console.WriteLine($"Relay prêt sur 0.0.0.0:{port}.");

        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                _ = Task.Run(() => HandleConnectionAsync(client, token), token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken token)
    {
        try
        {
            var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            await using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

            var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
            if (line is null)
            {
                client.Close();
                return;
            }

            // Première commande envoyée par le client.
            if (line.StartsWith("HOST ", StringComparison.OrdinalIgnoreCase))
            {
                await HandleHostAsync(client, writer, reader, line["HOST ".Length..].Trim(), token).ConfigureAwait(false);
                return;
            }

            if (line.StartsWith("JOIN ", StringComparison.OrdinalIgnoreCase))
            {
                await HandleJoinAsync(client, writer, line["JOIN ".Length..].Trim(), token).ConfigureAwait(false);
                return;
            }

            if (line.StartsWith("DATA ", StringComparison.OrdinalIgnoreCase))
            {
                await HandleDataAsync(client, writer, line["DATA ".Length..].Trim(), token).ConfigureAwait(false);
                return;
            }

            await writer.WriteLineAsync("ERROR:Commande inconnue").ConfigureAwait(false);
            client.Close();
        }
        catch (OperationCanceledException)
        {
            client.Close();
        }
        catch (IOException)
        {
            client.Close();
        }
    }

    private async Task HandleHostAsync(TcpClient client, StreamWriter writer, StreamReader reader, string code, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            await writer.WriteLineAsync("ERROR:Code manquant").ConfigureAwait(false);
            client.Close();
            return;
        }

        var session = new HostSession(code, client, writer);
        if (!_hosts.TryAdd(code, session))
        {
            await writer.WriteLineAsync("ERROR:Session déjà hébergée").ConfigureAwait(false);
            client.Close();
            return;
        }

        await writer.WriteLineAsync("OK").ConfigureAwait(false);

        try
        {
            while (!token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _hosts.TryRemove(code, out _);
            client.Close();
        }
    }

    private async Task HandleJoinAsync(TcpClient client, StreamWriter writer, string code, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            await writer.WriteLineAsync("ERROR:Code manquant").ConfigureAwait(false);
            client.Close();
            return;
        }

        if (!_hosts.TryGetValue(code, out var host))
        {
            await writer.WriteLineAsync("ERROR:Session introuvable").ConfigureAwait(false);
            client.Close();
            return;
        }

        // Génère un identifiant temporaire pour relier l'invité et l'hôte.
        var tokenValue = Guid.NewGuid().ToString("N");
        _pending[tokenValue] = new PendingJoin(client, code);
        await writer.WriteLineAsync("WAIT").ConfigureAwait(false);

        await host.SendAsync($"CONNECT {tokenValue}").ConfigureAwait(false);
    }

    private async Task HandleDataAsync(TcpClient client, StreamWriter writer, string tokenValue, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(tokenValue))
        {
            await writer.WriteLineAsync("ERROR:Token manquant").ConfigureAwait(false);
            client.Close();
            return;
        }

        if (!_pending.TryRemove(tokenValue, out var pending))
        {
            await writer.WriteLineAsync("ERROR:Session introuvable").ConfigureAwait(false);
            client.Close();
            return;
        }

        try
        {
            var joinerStream = pending.Client.GetStream();
            await using var joinerWriter = new StreamWriter(joinerStream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            // Signale à l'invité que le tunnel est prêt.
            await joinerWriter.WriteLineAsync("READY").ConfigureAwait(false);

            var hostStream = client.GetStream();
            // Pont bidirectionnel entre l'hôte et l'invité.
            var joinerToHost = PumpAsync(joinerStream, hostStream, token);
            var hostToJoiner = PumpAsync(hostStream, joinerStream, token);

            await Task.WhenAny(joinerToHost, hostToJoiner).ConfigureAwait(false);
        }
        catch (IOException)
        {
        }
        finally
        {
            pending.Client.Close();
            client.Close();
        }
    }

    private static async Task PumpAsync(NetworkStream source, NetworkStream destination, CancellationToken token)
    {
        var buffer = new byte[8192];
        while (!token.IsCancellationRequested)
        {
            var read = await source.ReadAsync(buffer, token).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
        }
    }

    private sealed class HostSession
    {
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly StreamWriter _writer;

        public HostSession(string code, TcpClient client, StreamWriter writer)
        {
            Code = code;
            Client = client;
            _writer = writer;
        }

        public string Code { get; }

        public TcpClient Client { get; }

        public async Task SendAsync(string message)
        {
            await _sendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await _writer.WriteLineAsync(message).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }
    }

    private sealed record PendingJoin(TcpClient Client, string Code);
}
