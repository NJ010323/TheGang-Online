using Model.Game;
using Model.Game.Card.CardEnum;
using Model.Game.Hand;
using Model.Game.Tokens.TokenEnum;
using Model.Lobby;

namespace Network;

/// <summary>Données reçues lorsqu'un invité rejoint le lobby.</summary>
public sealed record JoinLobbyRequest(string Nickname, string InvitationSecret);
/// <summary>Nouvelle taille demandée par l'hôte d'un lobby encore ouvert.</summary>
public sealed record ChangeLobbyCapacityRequest(int MaxPlayers);
/// <summary>Identité secrète à conserver dans le seul client concerné.</summary>
public sealed record LobbyIdentity(string Code, Guid PlayerId, string SessionToken, LobbySnapshot Lobby);
/// <summary>Vue publique du lobby, sans accès au moteur de jeu.</summary>
public sealed record LobbySnapshot(string Code, Guid HostId, int MaxPlayers, LobbyStatus Status,
    IReadOnlyList<LobbyParticipant> Players)
{
    /// <summary>Invités temporairement absents ; leur place reste réservée jusqu'à cette échéance.</summary>
    public IReadOnlyList<AbsentPlayerSnapshot> AbsentPlayers { get; init; } = [];
    /// <summary>Relève proposée au premier invité, ou adresse du nouvel hôte après son accord.</summary>
    public HostMigrationSnapshot? HostMigration { get; init; }
    /// <summary>Effectif attendu sur le nouveau serveur avant la reprise automatique.</summary>
    public int? ExpectedTransferredPlayers { get; init; }
    /// <summary>La partie transférée redémarre dès que tous ces joueurs sont présents.</summary>
    public bool AutoResumeTransferredGame { get; init; }
}
/// <summary>Identité publique d'un invité attendu avant la fermeture de la partie.</summary>
public sealed record AbsentPlayerSnapshot(Guid PlayerId, string Name, DateTimeOffset ReconnectUntilUtc);
/// <summary>Étape de la relève visible des seuls membres du lobby.</summary>
public enum HostMigrationStage { Offered, Preparing, Redirecting, Declined, Expired }
/// <summary>Transfert public du score et de l'ordre d'arrivée, sans cartes privées ni jetons de session.</summary>
public sealed record HostMigrationSnapshot(Guid Id, HostMigrationStage Stage, Guid SuccessorId,
    string SuccessorName, DateTimeOffset ExpiresAtUtc, string? Invitation,
    IReadOnlyList<string> ExpectedPlayers, int MaxPlayers, bool WasStarted,
    int HeistNumber, int SuccessfulHeists, int FailedHeists, int AcknowledgedPlayers,
    int PlayersToRedirect);
/// <summary>Réponse du successeur à la proposition de relève.</summary>
public sealed record HostMigrationDecision(Guid Id, bool Accepted);
/// <summary>Invitation publiée seulement par le successeur qui a accepté la relève.</summary>
public sealed record HostMigrationDestination(Guid Id, string Invitation);
/// <summary>Identifie une relève pour un accusé de réception ou une annulation.</summary>
public sealed record HostMigrationReference(Guid Id);
/// <summary>Joueurs attendus et résultats déjà acquis, installés sur le nouveau serveur.</summary>
public sealed record HostMigrationSetup(IReadOnlyList<string> ExpectedPlayers, bool WasStarted,
    int HeistNumber, int SuccessfulHeists, int FailedHeists, string? ReturningHostName);
/// <summary>Carte transportée sous une forme sérialisable, sans interface métier.</summary>
public sealed record CardSnapshot(CardColors Suit, int Rank);
/// <summary>Jeton d'un joueur pendant le braquage actuel.</summary>
public sealed record SignalSnapshot(TokenColors Color, int Value);
/// <summary>Vue d'un joueur ; seules ses propres cartes ou des cartes dévoilées sont présentes.</summary>
public sealed record PlayerSnapshot(Guid Id, string Name, IReadOnlyList<CardSnapshot> Hand,
    IReadOnlyList<SignalSnapshot> Signals, int? CurrentToken, bool IsChoiceConfirmed);
/// <summary>Meilleure combinaison d'un joueur dont la main est déjà publique.</summary>
public sealed record RevealedHandSnapshot(Guid PlayerId, int TokenValue, HandRank Rank,
    IReadOnlyList<int> Tiebreakers);
/// <summary>Type d'action publique à afficher et animer sur tous les clients.</summary>
public enum GameEventKind
{
    HeistStarted, TokenTaken, TokenStolen, TokenReturned, ChoiceConfirmed,
    ConfirmationWithdrawn, RoundRevealed, HandRevealed, HeistResolved
}
/// <summary>Événement ordonné du serveur ; il ne contient ni main cachée ni secret de session.</summary>
public sealed record GameEventSnapshot(long Sequence, GameEventKind Kind, Guid ActorId,
    string ActorName, GamePhase Phase, int HeistNumber, int? TokenValue = null, Guid? FromPlayerId = null,
    string? FromPlayerName = null, bool? HeistSucceeded = null);
/// <summary>Position temporaire d'un jeton pendant le geste d'un joueur, en coordonnées du tapis.</summary>
public sealed record GameDragSnapshot(Guid PlayerId, string PlayerName, Guid GestureId,
    int TokenValue, long Revision, double X, double Y);
/// <summary>Mise à jour éphémère du glissement ; seul le dépôt change le propriétaire du jeton.</summary>
public sealed record GameDragRequest(Guid GestureId, int TokenValue, long Revision,
    double X, double Y, bool IsActive);
/// <summary>Vue autorisée de la partie pour une seule connexion.</summary>
public sealed record GameSnapshot(GamePhase Phase, GameResult Result, int HeistNumber,
    int SuccessfulHeists, int FailedHeists, bool? LastHeistSucceeded,
    IReadOnlyList<CardSnapshot> CommunityCards, IReadOnlyList<PlayerSnapshot> Players,
    IReadOnlyList<int> AvailableTokens, bool AllPlayersHaveTokens, bool AllChoicesConfirmed,
    Guid? NextPlayerToRevealId, IReadOnlyList<GameEventSnapshot> RecentEvents,
    IReadOnlyList<GameDragSnapshot> ActiveDrags)
{
    /// <summary>Mains dévoilées uniquement ; vide si un ancien hôte ne fournit pas encore ce champ.</summary>
    public IReadOnlyList<RevealedHandSnapshot> RevealedHands { get; init; } = [];
}

/// <summary>Actions acceptées par le serveur de partie.</summary>
public enum GameAction
{
    TakeToken, ReturnToken, ConfirmChoice, WithdrawConfirmation,
    AdvanceRound, RevealNextHand, ResolveHeist, StartNextHeist
}

/// <summary>Commande de jeu ; le serveur déduit le joueur de son jeton de session.</summary>
public sealed record GameActionRequest(GameAction Action, int? TokenValue = null, Guid? GestureId = null);
