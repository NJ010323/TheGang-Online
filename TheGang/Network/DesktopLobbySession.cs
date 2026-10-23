using System.Net.Sockets;

namespace Network;

/// <summary>Réunit serveur local, tunnel et client de l'hôte pour un lobby actif.</summary>
public sealed class DesktopLobbySession : IAsyncDisposable
{
    private readonly LobbyHttpServer? _server;
    private readonly CloudflareQuickTunnel? _tunnel;

    private DesktopLobbySession(LobbyHttpClient client, string invitation,
        LobbyHttpServer? server = null, CloudflareQuickTunnel? tunnel = null)
    {
        Client = client;
        Invitation = invitation;
        _server = server;
        _tunnel = tunnel;
    }

    /// <summary>Client utilisé par l'interface pour toutes les actions du joueur.</summary>
    public LobbyHttpClient Client { get; }

    /// <summary>Code complet à copier-coller pour rejoindre depuis un autre réseau.</summary>
    public string Invitation { get; }

    /// <summary>Vrai pour l'ordinateur qui exécute le moteur et le tunnel.</summary>
    public bool IsHost => _server is not null;

    /// <summary>Crée un lobby puis publie son serveur local par un tunnel sortant.</summary>
    public static async Task<DesktopLobbySession> CreateAsync(string nickname, int maxPlayers,
        CancellationToken cancellationToken = default)
    {
        var server = await LobbyHttpServer.StartAsync(nickname, maxPlayers, cancellationToken);
        CloudflareQuickTunnel? tunnel = null;
        try
        {
            tunnel = await CloudflareQuickTunnel.StartAsync(server.LocalAddress,
                cancellationToken: cancellationToken);
            var client = new LobbyHttpClient(server.LocalAddress);
            client.SetHostIdentity(server.Lobby.HostIdentity);
            return new DesktopLobbySession(client,
                InviteCode.Create(server.Lobby.Code, tunnel.PublicAddress, server.Lobby.InvitationSecret), server, tunnel);
        }
        catch
        {
            if (tunnel is not null) await tunnel.DisposeAsync();
            await server.DisposeAsync();
            throw;
        }
    }

    /// <summary>Rejoint un hôte distant en utilisant le code copié depuis son jeu.</summary>
    public static async Task<DesktopLobbySession> JoinAsync(string invitation, string nickname,
        CancellationToken cancellationToken = default)
    {
        var (code, address, secret) = InviteCode.Parse(invitation);
        var client = new LobbyHttpClient(address);
        try
        {
            // Une URL Quick Tunnel peut être annoncée avant sa propagation DNS.
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    var identity = await client.JoinAsync(nickname, secret, cancellationToken);
                    if (!string.Equals(identity.Code, code, StringComparison.OrdinalIgnoreCase))
                    {
                        await client.LeaveAsync(cancellationToken);
                        throw new ArgumentException("Le code du lobby ne correspond pas à l'invitation.", nameof(invitation));
                    }
                    break;
                }
                catch (HttpRequestException error) when (attempt < 19 && IsDnsPending(error))
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
            }
            return new DesktopLobbySession(client, invitation.Trim());
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>Retrouve le même invité à partir de l'invitation et du jeton sauvés localement.</summary>
    public static async Task<DesktopLobbySession> ResumeAsync(string invitation, Guid playerId,
        string sessionToken, CancellationToken cancellationToken = default)
    {
        var (code, address, _) = InviteCode.Parse(invitation);
        var client = new LobbyHttpClient(address);
        try
        {
            await client.ResumeAsync(code, playerId, sessionToken, cancellationToken);
            return new DesktopLobbySession(client, invitation.Trim());
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>Ne retente que les requêtes jamais parvenues au serveur faute de DNS.</summary>
    private static bool IsDnsPending(HttpRequestException error)
    {
        for (Exception? cause = error; cause is not null; cause = cause.InnerException)
            if (cause is SocketException socket &&
                socket.SocketErrorCode is SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData)
                return true;
        return false;
    }

    /// <summary>Ferme les connexions et les processus détenus par cette application.</summary>
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        if (_tunnel is not null) await _tunnel.DisposeAsync().ConfigureAwait(false);
        if (_server is not null) await _server.DisposeAsync().ConfigureAwait(false);
    }
}
