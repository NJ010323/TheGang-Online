using System.Net;

namespace Network;

/// <summary>Reconnaît les erreurs indiquant qu'une session de lobby n'est plus joignable.</summary>
public static class LobbyConnectionFailure
{
    /// <summary>Reconnaît un refus définitif émis par le serveur d'origine du jeu.</summary>
    public static bool IsSessionClosed(Exception error) => error is LobbySessionClosedException;

    /// <summary>Reconnaît une coupure réseau qui peut se résoudre sans rejoindre à nouveau.</summary>
    public static bool IsHostUnavailable(Exception error)
    {
        if (IsSessionClosed(error)) return false;
        if (error is TaskCanceledException) return true; // Délai HTTP dépassé.
        if (error is not HttpRequestException requestError) return false;
        if (requestError.StatusCode is null) return true; // Transport interrompu.

        return requestError.StatusCode is HttpStatusCode.Unauthorized or
            HttpStatusCode.NotFound or HttpStatusCode.Gone ||
            (int)requestError.StatusCode >= 500;
    }
}
