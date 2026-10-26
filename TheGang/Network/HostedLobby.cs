using System.Security.Cryptography;
using System.Security;
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
    private readonly Dictionary<Guid, DateTimeOffset> _lastSeenGuests = [];
    private readonly Dictionary<Guid, DateTimeOffset> _absentUntil = [];
    private readonly TimeProvider _clock;
    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ReconnectGrace = TimeSpan.FromMinutes(2);
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
    // La relève gèle les commandes du serveur sortant jusqu'à la redirection des joueurs.
    private HostMigrationSnapshot? _migration;
    private readonly HashSet<Guid> _migrationAcknowledgements = [];
    // Les anciens joueurs entrent d'abord ; un lobby encore ouvert peut ensuite accueillir des amis.
    private HostMigrationSetup? _migrationSetup;

    /// <summary>Crée le lobby et l'identité initiale de l'hôte.</summary>
    public HostedLobby(string hostNickname, int maxPlayers, TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
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
            if (_migrationSetup is { } setup && !setup.ExpectedPlayers.Contains(nickname.Trim(),
                    StringComparer.OrdinalIgnoreCase) &&
                (setup.WasStarted || _lobby.Players.Count < setup.ExpectedPlayers.Count))
                throw new UnauthorizedAccessException("Ce joueur ne fait pas partie de la table transférée.");
            var player = _manager.JoinLobby(Code, nickname);
            var identity = NewIdentity(player.Id);
            _lastSeenGuests[player.Id] = _clock.GetUtcNow();
            StartTransferredGameWhenReady();
            return identity;
        }
    }

    /// <summary>Propose la relève au premier invité inscrit dans le lobby.</summary>
    public HostMigrationSnapshot BeginHostMigration(string code, string sessionToken)
    {
        lock (_sync)
        {
            var requesterId = EnsureMember(code, sessionToken);
            EnsureHost(requesterId);
            EnsureAllPlayersPresent();
            if (_migration is { Stage: HostMigrationStage.Offered or HostMigrationStage.Preparing or
                HostMigrationStage.Redirecting })
                throw new InvalidOperationException("Une relève est déjà en cours.");
            if (_lobby.Players.Count < 2)
                throw new InvalidOperationException("Aucun invité ne peut reprendre le rôle d'hôte.");
            if (_game?.Result is GameResult.Won or GameResult.Lost)
                throw new InvalidOperationException("Cette partie est déjà terminée.");

            var successor = _lobby.Players[1];
            var heistNumber = _game is null ? 0 : _game.Phase == GamePhase.HeistComplete
                ? _game.HeistNumber + 1 : _game.HeistNumber;
            _migrationAcknowledgements.Clear();
            _migration = new HostMigrationSnapshot(Guid.NewGuid(), HostMigrationStage.Offered,
                successor.Id, successor.Name, _clock.GetUtcNow().AddMinutes(2), null,
                _lobby.Players.Skip(1).Select(player => player.Name).ToArray(), _lobby.MaxPlayers,
                _game is not null, heistNumber, _game?.SuccessfulHeists ?? 0,
                _game?.FailedHeists ?? 0, 0, _lobby.Players.Count);
            CompleteAllGestures();
            return _migration;
        }
    }

    /// <summary>Le successeur accepte ou refuse la responsabilité du nouveau serveur.</summary>
    public HostMigrationSnapshot DecideHostMigration(string code, string sessionToken,
        HostMigrationDecision decision)
    {
        lock (_sync)
        {
            var playerId = EnsureMember(code, sessionToken);
            var migration = _migration ?? throw new InvalidOperationException("Aucune relève en cours.");
            if (migration.Id != decision.Id || migration.Stage is not
                (HostMigrationStage.Offered or HostMigrationStage.Preparing) ||
                decision.Accepted && migration.Stage != HostMigrationStage.Offered)
                throw new InvalidOperationException("Cette proposition de relève n'est plus valide.");
            if (playerId != migration.SuccessorId)
                throw new SecurityException("Seul le successeur désigné peut répondre.");
            _migration = migration with
            {
                Stage = decision.Accepted ? HostMigrationStage.Preparing : HostMigrationStage.Declined,
                // L'ouverture d'un tunnel peut prendre plus longtemps que la réponse à l'offre.
                // Le successeur dispose donc d'un délai complet pour préparer sa table.
                ExpiresAtUtc = decision.Accepted ? _clock.GetUtcNow().AddMinutes(2) : migration.ExpiresAtUtc
            };
            return _migration;
        }
    }

    /// <summary>Diffuse une invitation réelle uniquement après démarrage du tunnel du successeur.</summary>
    public HostMigrationSnapshot PublishHostMigration(string code, string sessionToken,
        HostMigrationDestination destination)
    {
        lock (_sync)
        {
            var playerId = EnsureMember(code, sessionToken);
            var migration = RequireMigration(destination.Id, HostMigrationStage.Preparing);
            if (playerId != migration.SuccessorId)
                throw new SecurityException("Seul le successeur désigné peut publier sa table.");
            InviteCode.Parse(destination.Invitation);
            _migration = migration with
            {
                Stage = HostMigrationStage.Redirecting, Invitation = destination.Invitation.Trim()
            };
            return _migration;
        }
    }

    /// <summary>Un joueur confirme avoir rejoint le nouveau serveur avant l'arrêt de l'ancien.</summary>
    public HostMigrationSnapshot AcknowledgeHostMigration(string code, string sessionToken, Guid migrationId)
    {
        lock (_sync)
        {
            var playerId = EnsureMember(code, sessionToken);
            var migration = RequireMigration(migrationId, HostMigrationStage.Redirecting);
            _migrationAcknowledgements.Add(playerId);
            return migration with { AcknowledgedPlayers = _migrationAcknowledgements.Count };
        }
    }

    /// <summary>Annule une proposition tant que personne n'a reçu l'adresse de relève.</summary>
    public bool CancelHostMigration(string code, string sessionToken, Guid migrationId)
    {
        lock (_sync)
        {
            EnsureHost(EnsureMember(code, sessionToken));
            var migration = _migration ?? throw new InvalidOperationException("Aucune relève en cours.");
            if (migration.Id != migrationId || migration.Stage == HostMigrationStage.Redirecting)
                throw new InvalidOperationException("La redirection a déjà commencé.");
            _migration = migration with { Stage = HostMigrationStage.Expired };
            return true;
        }
    }

    /// <summary>Installe sur le nouvel hôte une liste fermée et la progression déjà gagnée.</summary>
    public bool ConfigureHostMigration(string code, string sessionToken, HostMigrationSetup setup)
    {
        lock (_sync)
        {
            EnsureHost(EnsureMember(code, sessionToken));
            if (_lobby.Status != LobbyStatus.Open || _lobby.Players.Count != 1 || _migrationSetup is not null)
                throw new InvalidOperationException("Cette table ne peut plus être préparée pour la relève.");
            if (setup.ExpectedPlayers is null || setup.ExpectedPlayers.Count is < 1 or > 6 ||
                setup.ExpectedPlayers.Count > _lobby.MaxPlayers ||
                !string.Equals(setup.ExpectedPlayers[0], _lobby.Players[0].Name,
                    StringComparison.OrdinalIgnoreCase) ||
                setup.ExpectedPlayers.Any(string.IsNullOrWhiteSpace) ||
                setup.ExpectedPlayers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != setup.ExpectedPlayers.Count ||
                setup.WasStarted && (setup.HeistNumber != setup.SuccessfulHeists + setup.FailedHeists + 1 ||
                    setup.SuccessfulHeists is < 0 or > 2 || setup.FailedHeists is < 0 or > 2) ||
                !setup.WasStarted && (setup.HeistNumber != 0 || setup.SuccessfulHeists != 0 ||
                    setup.FailedHeists != 0) ||
                setup.ReturningHostName is not null && !setup.ExpectedPlayers.Contains(
                    setup.ReturningHostName, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("Données de relève invalides.", nameof(setup));
            _migrationSetup = setup with { ExpectedPlayers = setup.ExpectedPlayers.ToArray() };
            // Une relève à moins de trois prépare un salon ouvert, jamais une partie à deux.
            // Les nouveaux invités pourront entrer une fois les joueurs transférés arrivés.
            if (setup.WasStarted && setup.ExpectedPlayers.Count < 3)
                _migrationSetup = _migrationSetup with
                { WasStarted = false, HeistNumber = 0, SuccessfulHeists = 0, FailedHeists = 0 };
            return true;
        }
    }

    /// <summary>Redistribue le braquage une fois toute la nouvelle équipe arrivée.</summary>
    private void StartTransferredGameWhenReady()
    {
        if (_migrationSetup is not { WasStarted: true } setup ||
            setup.ExpectedPlayers.Count < 3 ||
            _lobby.Players.Count != setup.ExpectedPlayers.Count) return;
        _game = _manager.ContinueAfterHostMigration(Code, _lobby.HostId, setup.HeistNumber,
            setup.SuccessfulHeists, setup.FailedHeists);
        _events.Clear();
        _nextEventSequence = 0;
        RecordEvent(GameEventKind.HeistStarted, _lobby.HostId, _game);
    }

    /// <summary>Vérifie qu'une ancienne offre est toujours valide et à l'étape indiquée.</summary>
    private HostMigrationSnapshot RequireMigration(Guid id, HostMigrationStage stage)
    {
        var migration = _migration ?? throw new InvalidOperationException("Aucune relève en cours.");
        if (migration.Id != id || migration.Stage != stage)
            throw new InvalidOperationException("Cette proposition de relève n'est plus valide.");
        return migration;
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

    /// <summary>Réserve davantage de places, ou réduit la table sans retirer de joueur attendu.</summary>
    public LobbySnapshot ChangeCapacity(string code, string sessionToken, int maxPlayers)
    {
        lock (_sync)
        {
            var playerId = EnsureMember(code, sessionToken);
            EnsureHost(playerId);
            if (_migration is { Stage: HostMigrationStage.Offered or HostMigrationStage.Preparing or
                HostMigrationStage.Redirecting })
                throw new InvalidOperationException("La capacité est figée pendant la relève de l'hôte.");
            if (maxPlayers >= 3 && _migrationSetup is { } setup &&
                maxPlayers < setup.ExpectedPlayers.Count)
                throw new InvalidOperationException("La capacité doit garder une place pour chaque joueur transféré.");
            _manager.ChangeLobbyCapacity(Code, playerId, maxPlayers);
            return Snapshot();
        }
    }

    /// <summary>Libère une place avant le départ ou la réserve pendant la partie pour une reprise.</summary>
    public void Leave(string code, string sessionToken)
    {
        lock (_sync)
        {
            var playerId = EnsureMember(code, sessionToken, markPresent: false);
            if (playerId == _lobby.HostId)
            {
                _manager.CloseLobby(Code, _lobby.HostId);
                _sessions.Clear();
            }
            else if (_lobby.Status == LobbyStatus.Started)
            {
                // Le moteur conserve la main et les jetons du joueur pendant sa courte absence.
                MarkAbsent(playerId, _clock.GetUtcNow());
                if (_lobby.Status == LobbyStatus.Started && _lobby.Players.Count - _absentUntil.Count < 3)
                    ReturnToLobbyAfterDeparture();
            }
            else
            {
                if (_migration is { Stage: HostMigrationStage.Offered or HostMigrationStage.Preparing } migration)
                    _migration = migration with { Stage = HostMigrationStage.Expired };
                var name = _lobby.Players.Single(player => player.Id == playerId).Name;
                _manager.LeaveLobby(Code, playerId);
                _sessions.Remove(sessionToken);
                _lastSeenGuests.Remove(playerId);
                _absentUntil.Remove(playerId);
                // Un départ du salon est définitif, même après une relève : son nom
                // ne doit plus bloquer les invitations, la capacité ou le démarrage.
                if (_migrationSetup is { } setup)
                {
                    _migrationSetup = setup with
                    {
                        ExpectedPlayers = setup.ExpectedPlayers.Where(expected =>
                            !string.Equals(expected, name, StringComparison.OrdinalIgnoreCase)).ToArray(),
                        ReturningHostName = string.Equals(setup.ReturningHostName, name,
                            StringComparison.OrdinalIgnoreCase) ? null : setup.ReturningHostName
                    };
                    if (_migrationSetup.WasStarted && _migrationSetup.ExpectedPlayers.Count < 3)
                        _migrationSetup = _migrationSetup with
                        { WasStarted = false, HeistNumber = 0, SuccessfulHeists = 0, FailedHeists = 0 };
                }
            }
        }
    }

    /// <summary>Un départ volontaire sous le seuil de trois libère les places absentes et arrête le braquage.</summary>
    private void ReturnToLobbyAfterDeparture()
    {
        var departed = _absentUntil.Keys.ToHashSet();
        _manager.ReturnToLobby(Code, _lobby.HostId, departed);
        foreach (var token in _sessions.Where(pair => departed.Contains(pair.Value)).Select(pair => pair.Key).ToArray())
            _sessions.Remove(token);
        foreach (var playerId in departed) _lastSeenGuests.Remove(playerId);
        _absentUntil.Clear();
        _game = null;
        _migrationSetup = null;
        _events.Clear();
        _nextEventSequence = 0;
        _activeDrags.Clear();
        _completedDrags.Clear();
        _completedDragOrder.Clear();
    }

    /// <summary>Permet au seul hôte de lancer la partie quand 3 à 6 joueurs sont présents.</summary>
    public GameSnapshot Start(string code, string sessionToken)
    {
        lock (_sync)
        {
            var playerId = EnsureMember(code, sessionToken);
            EnsureHost(playerId);
            EnsureAllPlayersPresent();
            if (_migrationSetup is { } setup && _lobby.Players.Count < setup.ExpectedPlayers.Count)
                throw new InvalidOperationException("Tous les joueurs transférés ne sont pas encore arrivés.");
            _game = _manager.StartGame(Code, playerId);
            _events.Clear();
            _nextEventSequence = 0;
            _activeDrags.Clear();
            _completedDrags.Clear();
            _completedDragOrder.Clear();
            RecordEvent(GameEventKind.HeistStarted, playerId, _game);
            RefreshPresence(_clock.GetUtcNow());
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
            EnsureAllPlayersPresent();
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
            EnsureAllPlayersPresent();
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
    private Guid EnsureMember(string code, string sessionToken, bool markPresent = true)
    {
        var now = _clock.GetUtcNow();
        RefreshPresence(now);
        EnsureCode(code);
        if (!_sessions.TryGetValue(sessionToken, out var playerId))
            throw new UnauthorizedAccessException("Session de joueur inconnue.");
        if (markPresent && playerId != _lobby.HostId)
        {
            _lastSeenGuests[playerId] = now;
            _absentUntil.Remove(playerId);
        }
        return playerId;
    }

    /// <summary>Rend son identité à une session existante et retire sa marque d'absence.</summary>
    public LobbyIdentity Resume(string code, string sessionToken)
    {
        lock (_sync)
        {
            var playerId = EnsureMember(code, sessionToken);
            return new LobbyIdentity(Code, playerId, sessionToken, Snapshot());
        }
    }

    /// <summary>Détecte les invités silencieux et ferme la partie à la fin de leur délai.</summary>
    private void RefreshPresence(DateTimeOffset now)
    {
        if (_migration is { Stage: HostMigrationStage.Offered or HostMigrationStage.Preparing } migration)
        {
            if (now >= migration.ExpiresAtUtc)
                _migration = migration with { Stage = HostMigrationStage.Expired };
        }
        // Après publication, les clients cessent de sonder l'ancien serveur en rejoignant
        // le nouveau. Leur départ ne doit pas fermer l'ancienne table avant les accusés.
        if (_migration is { Stage: HostMigrationStage.Redirecting }) return;
        // Le successeur ne sonde plus l'ancien serveur lorsqu'il crée son tunnel.
        // Les autres invités doivent continuer de répondre : leur départ annule la relève.
        Guid? preparingSuccessor = _migration is { Stage: HostMigrationStage.Preparing } preparing
            ? preparing.SuccessorId : null;
        if (_lobby.Status == LobbyStatus.Started)
            foreach (var (playerId, lastSeen) in _lastSeenGuests)
                if (playerId != preparingSuccessor && !_absentUntil.ContainsKey(playerId) &&
                    now - lastSeen >= HeartbeatTimeout)
                    MarkAbsent(playerId, lastSeen + HeartbeatTimeout);
        if (_absentUntil.Values.Any(until => now >= until))
        {
            _manager.CloseLobby(Code, _lobby.HostId);
            _sessions.Clear();
            _absentUntil.Clear();
        }
    }

    /// <summary>Interrompt les gestes en cours et réserve la place de l'invité absent.</summary>
    private void MarkAbsent(Guid playerId, DateTimeOffset since)
    {
        _absentUntil.TryAdd(playerId, since + ReconnectGrace);
        CompletePlayerGesture(playerId);
        // Une équipe incomplète ne doit pas être copiée vers le nouveau serveur.
        // Une fois l'adresse publiée, l'ancien serveur attend plutôt les accusés de réception.
        if (_migration is { Stage: HostMigrationStage.Offered or HostMigrationStage.Preparing } migration)
            _migration = migration with { Stage = HostMigrationStage.Expired };
    }

    /// <summary>Empêche toute modification du moteur tant qu'une place est inoccupée.</summary>
    private void EnsureAllPlayersPresent()
    {
        if (_absentUntil.Count != 0)
            throw new InvalidOperationException("La partie est en pause : un joueur est absent.");
        if (_migration is { Stage: HostMigrationStage.Offered or HostMigrationStage.Preparing or
            HostMigrationStage.Redirecting })
            throw new InvalidOperationException("La table est en pause pendant la relève de l'hôte.");
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
            throw new SecurityException("Seul l'hôte peut effectuer cette action.");
    }

    /// <summary>Construit une copie de la liste publique des membres.</summary>
    private LobbySnapshot Snapshot() =>
        new(Code, _lobby.HostId, _lobby.MaxPlayers, _lobby.Status, _lobby.Players)
        {
            AbsentPlayers = _absentUntil.Select(entry => new AbsentPlayerSnapshot(entry.Key,
                _lobby.Players.First(player => player.Id == entry.Key).Name, entry.Value)).ToArray(),
            HostMigration = _migration is null ? null : _migration with
            {
                AcknowledgedPlayers = _migrationAcknowledgements.Count
            },
            ExpectedTransferredPlayers = _lobby.Status == LobbyStatus.Open &&
                _migrationSetup is { } setup && _lobby.Players.Count < setup.ExpectedPlayers.Count
                ? setup.ExpectedPlayers.Count : null,
            AutoResumeTransferredGame = _lobby.Status == LobbyStatus.Open &&
                _migrationSetup?.WasStarted == true
        };

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
                .Select(active => active.Drag).ToArray())
        {
            RevealedHands = game.RevealedHands.Select(hand => new RevealedHandSnapshot(hand.PlayerId,
                hand.TokenValue, hand.Score.Rank, hand.Score.Tiebreakers.ToArray())).ToArray()
        };
    }

    /// <summary>Supprime toute référence à la carte métier dans la réponse réseau.</summary>
    private static CardSnapshot CardSnapshotFor(ICard card) => new(card.Suit, card.Rank);
}
