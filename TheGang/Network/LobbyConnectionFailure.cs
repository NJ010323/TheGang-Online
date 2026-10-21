using System.Net;

namespace Network;

/// <summary>Reconnaît les erreurs indiquant qu'une session de lobby n'est plus joignable.</summary>
public static class LobbyConnectionFailure
{
    /// <summary>Distingue une fermeture ou perte d'hôte d'une erreur de commande métier.</summary>
    public static bool IsHostUnavailable(Exception error)
    {
        if (error is TaskCanceledException) return true; // Délai HTTP dépassé.
        if (error is not HttpRequestException requestError) return false;
        if (requestError.StatusCode is null) return true; // Transport interrompu.

        return requestError.StatusCode is HttpStatusCode.Unauthorized or
            HttpStatusCode.NotFound or HttpStatusCode.Gone ||
            (int)requestError.StatusCode >= 500;
    }
}
