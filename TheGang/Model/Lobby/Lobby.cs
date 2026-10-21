namespace Model.Lobby;

/// <summary>
/// Prépare une partie locale. Un futur serveur pourra associer chaque connexion
/// à un joueur avant d'appeler ces méthodes avec son identifiant.
/// </summary>
public sealed class Lobby
{
    // Protège les inscriptions et le démarrage lorsque plusieurs clients sollicitent le serveur.
    private readonly object _sync = new();
    // Le moteur de jeu crée les identifiants utilisés ensuite dans la partie.
    private readonly Model.Game.Game _game = new();
    // Le statut empêche les inscriptions après le départ ou la fermeture.
    private LobbyStatus _status = LobbyStatus.Open;

    /// <summary>Crée un lobby et inscrit l'hôte comme premier joueur.</summary>
    internal Lobby(string code, string hostName, int maxPlayers)
    {
        if (maxPlayers is < 3 or > 6)
            throw new ArgumentOutOfRangeException(nameof(maxPlayers), "Un lobby accepte entre trois et six joueurs.");

        Code = code;
        MaxPlayers = maxPlayers;
        _game.AddPlayer(hostName);
        HostId = _game.Players[0].Id;
    }

    /// <summary>Code d'invitation de six caractères, unique dans le registre local.</summary>
    public string Code { get; }

    /// <summary>Identifiant du joueur qui peut lancer ou fermer le lobby.</summary>
    public Guid HostId { get; }

    /// <summary>Nombre maximal de joueurs, hôte compris.</summary>
    public int MaxPlayers { get; }

    /// <summary>État actuel du lobby.</summary>
    public LobbyStatus Status
    {
        get { lock (_sync) return _status; }
    }

    /// <summary>Liste publique des joueurs, sans leurs cartes privées.</summary>
    public IReadOnlyList<LobbyParticipant> Players
    {
        get
        {
            lock (_sync)
                return _game.Players
                    .Select(player => new LobbyParticipant(player.Id, player.Name))
                    .ToList().AsReadOnly();
        }
    }

    /// <summary>Inscrit un joueur tant que le lobby est ouvert et possède une place libre.</summary>
    internal LobbyParticipant Join(string name)
    {
        lock (_sync)
        {
            EnsureOpen();
            if (_game.Players.Count >= MaxPlayers)
                throw new InvalidOperationException("Le lobby est complet.");

            _game.AddPlayer(name);
            var player = _game.Players[^1];
            return new LobbyParticipant(player.Id, player.Name);
        }
    }

    /// <summary>Retire un invité avant le début ; l'hôte ferme le lobby séparément.</summary>
    internal void Leave(Guid playerId)
    {
        lock (_sync)
        {
            EnsureOpen();
            if (playerId == HostId)
                throw new InvalidOperationException("L'hôte doit fermer le lobby plutôt que le quitter.");

            _game.RemovePlayer(playerId);
        }
    }

    /// <summary>Lance la partie si l'hôte le demande et si au moins trois joueurs sont présents.</summary>
    internal Model.Game.Game Start(Guid requesterId)
    {
        lock (_sync)
        {
            EnsureOpen();
            EnsureHost(requesterId);
            _game.StartGame();
            _status = LobbyStatus.Started;
            return _game;
        }
    }

    /// <summary>Ferme le lobby et empêche toute nouvelle entrée ou partie.</summary>
    internal void Close(Guid requesterId)
    {
        lock (_sync)
        {
            EnsureHost(requesterId);
            if (_status == LobbyStatus.Closed)
                throw new InvalidOperationException("Le lobby est déjà fermé.");
            _status = LobbyStatus.Closed;
        }
    }

    /// <summary>Vérifie que les inscriptions sont encore autorisées.</summary>
    private void EnsureOpen()
    {
        if (_status != LobbyStatus.Open)
            throw new InvalidOperationException("Ce lobby n'accepte plus de joueurs ni de démarrage.");
    }

    /// <summary>Vérifie l'identité déclarée de l'hôte.</summary>
    private void EnsureHost(Guid requesterId)
    {
        if (requesterId != HostId)
            throw new InvalidOperationException("Seul l'hôte peut effectuer cette action.");
    }
}
