namespace Model.Lobby;

/// <summary>IdentitÃ© publique d'un joueur inscrit dans un lobby.</summary>
/// <param name="Id">Identifiant rÃ©utilisÃ© par le moteur de partie.</param>
/// <param name="Name">Nom affichÃ© aux autres joueurs.</param>
public sealed record LobbyParticipant(Guid Id, string Name)
{ public bool IsBot { get; init; } }
