namespace Model.Lobby;

/// <summary>Identité publique d'un joueur inscrit dans un lobby.</summary>
/// <param name="Id">Identifiant réutilisé par le moteur de partie.</param>
/// <param name="Name">Nom affiché aux autres joueurs.</param>
public sealed record LobbyParticipant(Guid Id, string Name);
