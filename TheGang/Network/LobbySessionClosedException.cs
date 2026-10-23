using System.Net;

namespace Network;

/// <summary>Signale que le serveur du jeu a explicitement refusé une session existante.</summary>
public sealed class LobbySessionClosedException : HttpRequestException
{
    public LobbySessionClosedException(HttpStatusCode statusCode)
        : base("Le lobby ou la session n'est plus disponible.", null, statusCode) { }
}
