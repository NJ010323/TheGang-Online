using Model.Game;
using Model.Game.Card.CardEnum;
using Model.Game.Tokens.TokenEnum;
using Model.Lobby;

namespace Network;

/// <summary>Données reçues lorsqu'un invité rejoint le lobby.</summary>
public sealed record JoinLobbyRequest(string Nickname, string InvitationSecret);
/// <summary>Identité secrète à conserver dans le seul client concerné.</summary>
public sealed record LobbyIdentity(string Code, Guid PlayerId, string SessionToken, LobbySnapshot Lobby);
/// <summary>Vue publique du lobby, sans accès au moteur de jeu.</summary>
public sealed record LobbySnapshot(string Code, Guid HostId, int MaxPlayers, LobbyStatus Status,
    IReadOnlyList<LobbyParticipant> Players);
/// <summary>Carte transportée sous une forme sérialisable, sans interface métier.</summary>
public sealed record CardSnapshot(CardColors Suit, int Rank);
/// <summary>Jeton d'un joueur pendant le braquage actuel.</summary>
public sealed record SignalSnapshot(TokenColors Color, int Value);
/// <summary>Vue d'un joueur ; seules ses propres cartes ou des cartes dévoilées sont présentes.</summary>
public sealed record PlayerSnapshot(Guid Id, string Name, IReadOnlyList<CardSnapshot> Hand,
    IReadOnlyList<SignalSnapshot> Signals, int? CurrentToken, bool IsChoiceConfirmed);
/// <summary>Vue autorisée de la partie pour une seule connexion.</summary>
public sealed record GameSnapshot(GamePhase Phase, GameResult Result, int HeistNumber,
    int SuccessfulHeists, int FailedHeists, bool? LastHeistSucceeded,
    IReadOnlyList<CardSnapshot> CommunityCards, IReadOnlyList<PlayerSnapshot> Players,
    IReadOnlyList<int> AvailableTokens, bool AllPlayersHaveTokens, bool AllChoicesConfirmed,
    Guid? NextPlayerToRevealId);

/// <summary>Actions acceptées par le serveur de partie.</summary>
public enum GameAction
{
    TakeToken, ReturnToken, ConfirmChoice, WithdrawConfirmation,
    AdvanceRound, RevealNextHand, ResolveHeist, StartNextHeist
}

/// <summary>Commande de jeu ; le serveur déduit le joueur de son jeton de session.</summary>
public sealed record GameActionRequest(GameAction Action, int? TokenValue = null);
