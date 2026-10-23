using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Model.Lobby;

namespace Network;

/// <summary>Client HTTP de l'application bureau pour rejoindre et piloter une partie distante.</summary>
public sealed class LobbyHttpClient : IDisposable
{
    private readonly HttpClient _http;
    private LobbyIdentity? _identity;

    /// <summary>Utilise une adresse HTTPS distante ou une adresse locale réservée aux tests.</summary>
    public LobbyHttpClient(Uri address, HttpMessageHandler? handler = null)
    {
        if (address.Scheme != Uri.UriSchemeHttps &&
            !(address.Scheme == Uri.UriSchemeHttp && address.IsLoopback))
            throw new ArgumentException("Connexion non sécurisée interdite.", nameof(address));
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = address;
        _http.Timeout = TimeSpan.FromSeconds(15);
    }

    /// <summary>Identité du joueur après création locale ou inscription distante.</summary>
    public LobbyIdentity? Identity => _identity;

    /// <summary>Installe l'identité créée localement par le serveur de l'hôte.</summary>
    public void SetHostIdentity(LobbyIdentity identity) => SetIdentity(identity);

    /// <summary>Rejoint le lobby avec un pseudo que le serveur valide et rend unique.</summary>
    public async Task<LobbyIdentity> JoinAsync(string nickname, string invitationSecret,
        CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsJsonAsync("join",
            new JoinLobbyRequest(nickname, invitationSecret), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var identity = await response.Content.ReadFromJsonAsync<LobbyIdentity>(cancellationToken);
        SetIdentity(identity ?? throw new InvalidOperationException("Réponse de lobby vide."));
        return identity;
    }

    /// <summary>Réutilise la session d'un invité pour reprendre sa place et sa main sans nouvelle inscription.</summary>
    public async Task<LobbyIdentity> ResumeAsync(string code, Guid playerId, string sessionToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code) || playerId == Guid.Empty ||
            string.IsNullOrWhiteSpace(sessionToken))
            throw new ArgumentException("Session de reprise incomplète.");
        var placeholder = new LobbySnapshot(code, Guid.Empty, 0, LobbyStatus.Open, []);
        SetIdentity(new LobbyIdentity(code, playerId, sessionToken, placeholder));
        try
        {
            var identity = await GetAsync<LobbyIdentity>($"lobbies/{Code}/session", cancellationToken);
            if (identity.PlayerId != playerId ||
                !string.Equals(identity.Code, code, StringComparison.OrdinalIgnoreCase) ||
                identity.Lobby.Status == LobbyStatus.Closed)
                throw new LobbySessionClosedException(HttpStatusCode.Gone);
            SetIdentity(identity);
            return _identity!;
        }
        catch
        {
            _identity = null;
            _http.DefaultRequestHeaders.Authorization = null;
            throw;
        }
    }

    /// <summary>Actualise la liste publique des membres.</summary>
    public async Task<LobbySnapshot> GetLobbyAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<LobbySnapshot>($"lobbies/{Code}", cancellationToken);

    /// <summary>L'hôte lance la partie après l'arrivée de trois joueurs ou plus.</summary>
    public async Task<GameSnapshot> StartAsync(CancellationToken cancellationToken = default) =>
        await PostAsync<GameSnapshot>($"lobbies/{Code}/start", cancellationToken);

    /// <summary>Récupère uniquement l'état visible par le joueur identifié.</summary>
    public async Task<GameSnapshot> GetGameAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<GameSnapshot>($"lobbies/{Code}/game", cancellationToken);

    /// <summary>Transmet une commande ; le serveur déduit son auteur de la session.</summary>
    public async Task<GameSnapshot> ApplyAsync(GameActionRequest action,
        CancellationToken cancellationToken = default) =>
        await PostAsync<GameSnapshot>($"lobbies/{Code}/game/actions", cancellationToken, action);

    /// <summary>Publie la position du jeton tenu sans modifier la partie.</summary>
    public async Task UpdateDragAsync(GameDragRequest drag, CancellationToken cancellationToken = default) =>
        await PostAsync<bool>($"lobbies/{Code}/game/drag", cancellationToken, drag);

    /// <summary>Quitte le lobby ; un invité en partie garde temporairement sa place.</summary>
    public async Task LeaveAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsync($"lobbies/{Code}/leave", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        _identity = null;
        _http.DefaultRequestHeaders.Authorization = null;
    }

    /// <summary>Libère les connexions HTTP de ce client.</summary>
    public void Dispose() => _http.Dispose();

    private string Code => _identity?.Code ?? throw new InvalidOperationException("Aucun lobby rejoint.");

    /// <summary>Conserve le secret dans l'en-tête des requêtes suivantes.</summary>
    private void SetIdentity(LobbyIdentity identity)
    {
        _identity = identity;
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", identity.SessionToken);
    }

    /// <summary>Lit une réponse GET typée.</summary>
    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(path, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    /// <summary>Envoie une commande sans ou avec corps JSON.</summary>
    private async Task<T> PostAsync<T>(string path, CancellationToken cancellationToken, object? body = null)
    {
        using var response = body is null
            ? await _http.PostAsync(path, null, cancellationToken)
            : await _http.PostAsJsonAsync(path, body, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    /// <summary>Traduit une erreur HTTP en exception lisible.</summary>
    private async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new InvalidOperationException("Réponse du serveur vide.");
    }

    /// <summary>Inclut le message métier envoyé par le serveur en cas d'échec.</summary>
    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        // Seule une réponse authentifiée du serveur du jeu peut prouver la fin de cette session.
        // Une passerelle peut aussi retourner 404 ou 5xx lors d'une coupure passagère.
        var sessionRejected = response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone ||
            response.StatusCode == HttpStatusCode.Unauthorized &&
            response.RequestMessage?.Method == HttpMethod.Get;
        if (_identity is not null && sessionRejected &&
            response.Headers.TryGetValues("X-TheGang-Server", out var origins) &&
            origins.Contains("1"))
            throw new LobbySessionClosedException(response.StatusCode);
        // Les passerelles de tunnel renvoient parfois une page HTML complète.
        // Elle n'est pas un message métier et ne doit jamais remplir l'interface.
        if ((int)response.StatusCode >= 500 || response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            throw new HttpRequestException("Le lobby n'est plus disponible.", null, response.StatusCode);
        var message = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.Content.Headers.ContentType?.MediaType == "text/html")
            message = "Le serveur n'a pas répondu correctement.";
        else if (message.Length > 300)
            message = message[..300];
        throw new HttpRequestException($"{(int)response.StatusCode} : {message}", null, response.StatusCode);
    }
}
