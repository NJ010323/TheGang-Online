using System.Security.Cryptography;
using System.Text;
using Model.Game;
using Model.Game.Cards.Interfaces;
using Model.Lobby;

namespace Network;

/// <summary>État autoritaire d'un lobby sur l'ordinateur de l'hôte.</summary>
public sealed class HostedLobby
{
    // Un seul verrou sérialise les commandes réseau et protège le moteur de partie.
    private readonly object _sync = new();
    private readonly LobbyManager _manager = new();
    // Les jetons de session ne sont jamais communiqués aux autres joueurs.
    private readonly Dictionary<string, Guid> _sessions = new(StringComparer.Ordinal);
    private readonly Lobby _lobby;
    private Game? _game;

    /// <summary>Crée le lobby et l'identité initiale de l'hôte.</summary>
    public HostedLobby(string hostNickname, int maxPlayers)
    {
        _lobby = _manager.CreateLobby(hostNickname, maxPlayers);
        InvitationSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        HostIdentity = NewIdentity(_lobby.HostId);
    }

    /// <summary>Identité secrète de l'hôte, à conserver dans son processus.</summary>
    public LobbyIdentity HostIdentity { get; }

    /// <summary>Code public de six caractères du lobby.</summary>
    public string Code => _lobby.Code;

    /// <summary>Secret de l'invitation ; le code local seul ne donne pas accès au lobby.</summary>
    public string InvitationSecret { get; }

    /// <summary>Associe un pseudo unique à une nouvelle identité de connexion.</summary>
    public LobbyIdentity Join(string nickname, string invitationSecret)
    {
        lock (_sync)
        {
            var expected = Encoding.ASCII.GetBytes(InvitationSecret);
            var provided = Encoding.ASCII.GetBytes(invitationSecret ?? string.Empty);
            if (!CryptographicOperations.FixedTimeEquals(expected, provided))
                throw new UnauthorizedAccessException("Invitation invalide.");
            var player = _manager.JoinLobby(Code, nickname);
            return NewIdentity(player.Id);
        }
    }

    /// <summary>Retourne la liste publique aux seuls membres identifiés.</summary>
    public LobbySnapshot GetLobby(string code, string sessionToken)
    {
        lock (_sync)
        {
            EnsureMember(code, sessionToken);
            return Snapshot();
        }
    }

    /// <summary>Retire un invité avant le départ ; après le départ, un abandon ferme la partie pour tous.</summary>
    public void Leave(string code, string sessionToken)
    {
        lock (_sync)
        {
            var playerId = EnsureMember(code, sessionToken);
            if (playerId == _lobby.HostId || _lobby.Status == LobbyStatus.Started)
            {
                // Le moteur garde un nombre fixe de joueurs pendant une partie.
                _manager.CloseLobby(Code, _lobby.HostId);
                _sessions.Clear();
            }
            else
            {
                _manager.LeaveLobby(Code, playerId);
                _sessions.Remove(sessionToken);
            }
        }
    }

    /// <summary>Permet au seul hôte de lancer la partie quand 3 à 6 joueurs sont présents.</summary>
    public GameSnapshot Start(string code, string sessionToken)
    {
        lock (_sync)
        {
            var playerId = EnsureMember(code, sessionToken);
            EnsureHost(playerId);
            _game = _manager.StartGame(Code, playerId);
            return GameSnapshotFor(playerId);
        }
    }

    /// <summary>Fournit au joueur sa projection, jamais le moteur contenant toutes les mains.</summary>
    public GameSnapshot GetGame(string code, string sessionToken)
    {
        lock (_sync)
            return GameSnapshotFor(EnsureMember(code, sessionToken));
    }

    /// <summary>Applique une commande au nom du membre authentifié, puis renvoie sa vue.</summary>
    public GameSnapshot Apply(string code, string sessionToken, GameActionRequest request)
    {
        lock (_sync)
        {
            var playerId = EnsureMember(code, sessionToken);
            var game = _game ?? throw new InvalidOperationException("La partie n'a pas commencé.");
            switch (request.Action)
            {
                case GameAction.TakeToken:
                    game.TakeToken(playerId, request.TokenValue ?? throw new ArgumentException("Valeur de jeton manquante."));
                    break;
                case GameAction.ReturnToken: game.ReturnToken(playerId); break;
                case GameAction.ConfirmChoice: game.ConfirmChoice(playerId); break;
                case GameAction.WithdrawConfirmation: game.WithdrawConfirmation(playerId); break;
                case GameAction.AdvanceRound:
                    EnsureHost(playerId);
                    AdvanceRound(game);
                    break;
                case GameAction.RevealNextHand:
                    EnsureHost(playerId);
                    game.RevealNextHand();
                    break;
                case GameAction.ResolveHeist:
                    EnsureHost(playerId);
                    game.ResolveHeist();
                    break;
                case GameAction.StartNextHeist:
                    EnsureHost(playerId);
                    game.StartNextHeist();
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(request));
            }
            return GameSnapshotFor(playerId);
        }
    }

    /// <summary>Crée un secret opaque indépendant de l'identifiant public du joueur.</summary>
    private LobbyIdentity NewIdentity(Guid playerId)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _sessions.Add(token, playerId);
        return new LobbyIdentity(Code, playerId, token, Snapshot());
    }

    /// <summary>Vérifie le code et le jeton avant chaque opération.</summary>
    private Guid EnsureMember(string code, string sessionToken)
    {
        EnsureCode(code);
        return _sessions.TryGetValue(sessionToken, out var playerId)
            ? playerId
            : throw new UnauthorizedAccessException("Session de joueur inconnue.");
    }

    /// <summary>Compare les codes sans distinction de casse.</summary>
    private void EnsureCode(string code)
    {
        if (!string.Equals(code?.Trim(), Code, StringComparison.OrdinalIgnoreCase) ||
            _lobby.Status == LobbyStatus.Closed)
            throw new KeyNotFoundException("Lobby introuvable.");
    }

    /// <summary>Réserve les transitions collectives à l'hôte.</summary>
    private void EnsureHost(Guid playerId)
    {
        if (playerId != _lobby.HostId)
            throw new UnauthorizedAccessException("Seul l'hôte peut effectuer cette action.");
    }

    /// <summary>Construit une copie de la liste publique des membres.</summary>
    private LobbySnapshot Snapshot() =>
        new(Code, _lobby.HostId, _lobby.MaxPlayers, _lobby.Status, _lobby.Players);

    /// <summary>Fait avancer d'une étape seulement après les confirmations du moteur.</summary>
    private static void AdvanceRound(Game game)
    {
        switch (game.Phase)
        {
            case GamePhase.PreFlop: game.RevealFlop(); break;
            case GamePhase.Flop: game.RevealTurn(); break;
            case GamePhase.Turn: game.RevealRiver(); break;
            case GamePhase.River: game.GoToShowdown(); break;
            default: throw new InvalidOperationException("Aucun tour à révéler.");
        }
    }

    /// <summary>Copie l'état visible et filtre toutes les cartes privées non dévoilées.</summary>
    private GameSnapshot GameSnapshotFor(Guid viewerId)
    {
        var game = _game ?? throw new InvalidOperationException("La partie n'a pas commencé.");
        var players = game.Players.Select(player =>
        {
            var cards = player.Id == viewerId
                ? player.Hand
                : game.RevealedHands.FirstOrDefault(hand => hand.PlayerId == player.Id)?.Cards
                    ?? Array.Empty<ICard>();
            return new PlayerSnapshot(player.Id, player.Name, cards.Select(CardSnapshotFor).ToArray(),
                player.Signals.Select(signal => new SignalSnapshot(signal.Key, signal.Value.Value)).ToArray(),
                player.SignalToken?.Value, player.IsChoiceConfirmed);
        }).ToArray();
        return new GameSnapshot(game.Phase, game.Result, game.HeistNumber,
            game.SuccessfulHeists, game.FailedHeists, game.LastHeistSucceeded,
            game.CommunityCards.Select(CardSnapshotFor).ToArray(), players,
            game.AvailableTokens.Select(token => token.Value).ToArray(),
            game.AllPlayersHaveTokens, game.AllChoicesConfirmed, game.NextPlayerToRevealId);
    }

    /// <summary>Supprime toute référence à la carte métier dans la réponse réseau.</summary>
    private static CardSnapshot CardSnapshotFor(ICard card) => new(card.Suit, card.Rank);
}
