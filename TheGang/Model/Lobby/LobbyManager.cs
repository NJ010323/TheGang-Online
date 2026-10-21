using System.Security.Cryptography;

namespace Model.Lobby;

/// <summary>
/// Registre de lobbies dans un seul processus. Il pourra être hébergé par un
/// service public et appelé depuis les clients à travers une couche réseau.
/// </summary>
public sealed class LobbyManager
{
    // Six caractères donnent un code facile à transmettre oralement.
    private const int CodeLength = 6;
    // Les caractères visuellement ambigus (I, O, 0, 1) sont exclus.
    private const string CodeCharacters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    // Le verrou rend l'allocation des codes et la fermeture atomiques.
    private readonly object _sync = new();
    // Comparaison insensible à la casse pour faciliter la saisie d'un code reçu d'un ami.
    private readonly Dictionary<string, Lobby> _lobbies = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Crée un lobby avec un code aléatoire et inscrit son hôte.</summary>
    public Lobby CreateLobby(string hostName, int maxPlayers)
    {
        if (maxPlayers is < 3 or > 6)
            throw new ArgumentOutOfRangeException(nameof(maxPlayers), "Un lobby accepte entre trois et six joueurs.");

        lock (_sync)
        {
            string code;
            do
            {
                code = GenerateCode();
            } while (_lobbies.ContainsKey(code));

            var lobby = new Lobby(code, hostName, maxPlayers);
            _lobbies.Add(code, lobby);
            return lobby;
        }
    }

    /// <summary>Retrouve un lobby encore enregistré à partir de son code d'invitation.</summary>
    public Lobby GetLobby(string code)
    {
        var normalizedCode = NormalizeCode(code);
        lock (_sync)
            return _lobbies.TryGetValue(normalizedCode, out var lobby)
                ? lobby
                : throw new KeyNotFoundException("Aucun lobby ne correspond à ce code.");
    }

    /// <summary>Rejoint le lobby désigné par son code et retourne l'identité du joueur.</summary>
    public LobbyParticipant JoinLobby(string code, string playerName) =>
        GetLobby(code).Join(playerName);

    /// <summary>Retire un invité du lobby avant le début de la partie.</summary>
    public void LeaveLobby(string code, Guid playerId) => GetLobby(code).Leave(playerId);

    /// <summary>Lance une partie seulement lorsque l'identifiant de l'hôte est fourni.</summary>
    public Model.Game.Game StartGame(string code, Guid requesterId) =>
        GetLobby(code).Start(requesterId);

    /// <summary>Ferme un lobby à la demande de son hôte et libère son code.</summary>
    public void CloseLobby(string code, Guid requesterId)
    {
        var normalizedCode = NormalizeCode(code);
        lock (_sync)
        {
            if (!_lobbies.TryGetValue(normalizedCode, out var lobby))
                throw new KeyNotFoundException("Aucun lobby ne correspond à ce code.");

            lobby.Close(requesterId);
            _lobbies.Remove(normalizedCode);
        }
    }

    /// <summary>Génère six caractères sans symboles facilement confondus.</summary>
    private static string GenerateCode()
    {
        return new string(Enumerable.Range(0, CodeLength)
            .Select(_ => CodeCharacters[RandomNumberGenerator.GetInt32(CodeCharacters.Length)])
            .ToArray());
    }

    /// <summary>Supprime les espaces autour du code et vérifie qu'il est fourni.</summary>
    private static string NormalizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Le code du lobby est obligatoire.", nameof(code));
        return code.Trim();
    }
}
