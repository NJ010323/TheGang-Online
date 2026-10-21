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
    // L'hôte ordonne les actions ; chaque client peut rejouer les dernières animations après un sondage réseau.
    private readonly List<GameEventSnapshot> _events = [];
    private long _nextEventSequence;
    private const int MaxRecentEvents = 80;
    // Les gestes en cours sont publics mais ne changent jamais le propriétaire d'un jeton.
    private readonly Dictionary<Guid, (GameDragSnapshot Drag, DateTimeOffset Updated)> _activeDrags = [];
    private readonly HashSet<(Guid PlayerId, Guid GestureId)> _completedDrags = [];
    private readonly Queue<(Guid PlayerId, Guid GestureId)> _completedDragOrder = [];
    private const int MaxCompletedDrags = 256;

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
            _events.Clear();
            _nextEventSequence = 0;
            _activeDrags.Clear();
            _completedDrags.Clear();
            _completedDragOrder.Clear();
            RecordEvent(GameEventKind.HeistStarted, playerId, _game);
            return GameSnapshotFor(playerId);
        }
    }

    /// <summary>Fournit au joueur sa projection, jamais le moteur contenant toutes les mains.</summary>
    public GameSnapshot GetGame(string code, string sessionToken)
    {
        lock (_sync)
            return GameSnapshotFor(EnsureMember(code, sessionToken));
    }

    /// <summary>Partage un geste de souris authentifié sans appliquer encore la prise du jeton.</summary>
    public bool UpdateDrag(string code, string sessionToken, GameDragRequest request)
    {
        lock (_sync)
        {
            var playerId = EnsureMember(code, sessionToken);
            var game = _game ?? throw new InvalidOperationException("La partie n'a pas commencé.");
            if (request.GestureId == Guid.Empty || request.Revision < 1)
                throw new ArgumentException("Geste de jeton invalide.");
            if (!request.IsActive)
            {
                CompleteGesture(playerId, request.GestureId);
                return true;
            }
            if (game.Phase is not (GamePhase.PreFlop or GamePhase.Flop or GamePhase.Turn or GamePhase.River) ||
                game.AllChoicesConfirmed)
            {
                CompleteGesture(playerId, request.GestureId);
                return true;
            }
            if (request.TokenValue < 1 || request.TokenValue > game.Players.Count ||
                !double.IsFinite(request.X) || !double.IsFinite(request.Y) ||
                request.X < 0 || request.X > 1280 || request.Y < 0 || request.Y > 720)
                throw new ArgumentException("Position de jeton invalide.");
            if (_completedDrags.Contains((playerId, request.GestureId))) return true;
            if (_activeDrags.TryGetValue(playerId, out var previous))
            {
                if (previous.Drag.GestureId == request.GestureId && request.Revision <= previous.Drag.Revision)
                    return true;
                if (previous.Drag.GestureId != request.GestureId)
                    CompleteGesture(playerId, previous.Drag.GestureId);
            }
            var playerName = game.Players.First(player => player.Id == playerId).Name;
            _activeDrags[playerId] = (new GameDragSnapshot(playerId, playerName,
                request.GestureId, request.TokenValue, request.Revision, request.X, request.Y),
                DateTimeOffset.UtcNow);
            return true;
        }
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
                    var value = request.TokenValue ?? throw new ArgumentException("Valeur de jeton manquante.");
                    var previousOwner = game.Players.FirstOrDefault(player => player.SignalToken?.Value == value);
                    game.TakeToken(playerId, value);
                    if (previousOwner?.Id != playerId)
                        RecordEvent(previousOwner is null ? GameEventKind.TokenTaken : GameEventKind.TokenStolen,
                            playerId, game, value, previousOwner?.Id, previousOwner?.Name);
                    break;
                case GameAction.ReturnToken:
                    var returnedValue = game.Players.First(player => player.Id == playerId).SignalToken?.Value;
                    game.ReturnToken(playerId);
                    if (returnedValue is not null)
                        RecordEvent(GameEventKind.TokenReturned, playerId, game, returnedValue);
                    break;
                case GameAction.ConfirmChoice:
                    var wasConfirmed = game.Players.First(player => player.Id == playerId).IsChoiceConfirmed;
                    game.ConfirmChoice(playerId);
                    if (!wasConfirmed) RecordEvent(GameEventKind.ChoiceConfirmed, playerId, game);
                    break;
                case GameAction.WithdrawConfirmation:
                    var wasWithdrawn = !game.Players.First(player => player.Id == playerId).IsChoiceConfirmed;
                    game.WithdrawConfirmation(playerId);
                    if (!wasWithdrawn) RecordEvent(GameEventKind.ConfirmationWithdrawn, playerId, game);
                    break;
                case GameAction.AdvanceRound:
                    EnsureHost(playerId);
                    AdvanceRound(game);
                    RecordEvent(GameEventKind.RoundRevealed, playerId, game);
                    break;
                case GameAction.RevealNextHand:
                    EnsureHost(playerId);
                    var revealed = game.RevealNextHand();
                    RecordEvent(GameEventKind.HandRevealed, revealed.PlayerId, game, revealed.TokenValue);
                    break;
                case GameAction.ResolveHeist:
                    EnsureHost(playerId);
                    var succeeded = game.ResolveHeist();
                    RecordEvent(GameEventKind.HeistResolved, playerId, game, heistSucceeded: succeeded);
                    break;
                case GameAction.StartNextHeist:
                    EnsureHost(playerId);
                    game.StartNextHeist();
                    RecordEvent(GameEventKind.HeistStarted, playerId, game);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(request));
            }
            if (request.Action is GameAction.AdvanceRound or GameAction.ResolveHeist or GameAction.StartNextHeist)
                CompleteAllGestures();
            else
                CompletePlayerGesture(playerId);
            if (request.GestureId is { } gestureId) CompleteGesture(playerId, gestureId);
            return GameSnapshotFor(playerId);
        }
    }

    /// <summary>Ferme un geste pour empêcher les paquets retardés de le réafficher.</summary>
    private void CompleteGesture(Guid playerId, Guid gestureId)
    {
        var key = (playerId, gestureId);
        if (_completedDrags.Add(key))
        {
            _completedDragOrder.Enqueue(key);
            if (_completedDragOrder.Count > MaxCompletedDrags)
                _completedDrags.Remove(_completedDragOrder.Dequeue());
        }
        if (_activeDrags.TryGetValue(playerId, out var active) && active.Drag.GestureId == gestureId)
            _activeDrags.Remove(playerId);
    }

    /// <summary>Une commande achevée rend le geste de son auteur obsolète.</summary>
    private void CompletePlayerGesture(Guid playerId)
    {
        if (_activeDrags.TryGetValue(playerId, out var active))
            CompleteGesture(playerId, active.Drag.GestureId);
    }

    /// <summary>Un nouveau tour ou braquage retire tous les gestes de l'ancien.</summary>
    private void CompleteAllGestures()
    {
        foreach (var playerId in _activeDrags.Keys.ToArray()) CompletePlayerGesture(playerId);
    }

    /// <summary>Ajoute un événement public sans divulguer les cartes ni les jetons de session.</summary>
    private void RecordEvent(GameEventKind kind, Guid actorId, Game game, int? tokenValue = null,
        Guid? fromPlayerId = null, string? fromPlayerName = null, bool? heistSucceeded = null)
    {
        var actorName = game.Players.First(player => player.Id == actorId).Name;
        _events.Add(new GameEventSnapshot(++_nextEventSequence, kind, actorId, actorName,
            game.Phase, game.HeistNumber, tokenValue, fromPlayerId, fromPlayerName, heistSucceeded));
        if (_events.Count > MaxRecentEvents) _events.RemoveAt(0);
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
            game.AllPlayersHaveTokens, game.AllChoicesConfirmed, game.NextPlayerToRevealId,
            _events.ToArray(), _activeDrags.Values
                .Where(active => DateTimeOffset.UtcNow - active.Updated < TimeSpan.FromSeconds(4))
                .Select(active => active.Drag).ToArray());
    }

    /// <summary>Supprime toute référence à la carte métier dans la réponse réseau.</summary>
    private static CardSnapshot CardSnapshotFor(ICard card) => new(card.Suit, card.Rank);
}
