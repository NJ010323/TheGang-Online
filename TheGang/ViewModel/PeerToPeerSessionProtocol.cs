using System.Linq;
using Model.Networking;

namespace ViewModel;

/// <summary>
/// Centralise le format des messages échangés entre l'hôte et les clients.
/// </summary>
internal static class PeerToPeerSessionProtocol
{
    /// <summary>
    /// Snapshot simple d'une session pour l'affichage côté client.
    /// </summary>
    internal readonly record struct SessionSnapshot(string HostName, int PeerCount, string PeerList, string[] PeerNames);

    /// <summary>
    /// Formate le nom affiché de l'hôte dans la liste.
    /// </summary>
    internal static string FormatHostDisplayName(string hostName) => $"{hostName} (hôte)";

    /// <summary>
    /// Construit un message décrivant l'état actuel de la session.
    /// </summary>
    internal static string BuildSessionInfoMessage(PeerToPeerSession session)
    {
        var names = session.Peers.Select(peer => peer.DisplayName);
        return $"OK|{session.Host.DisplayName}|{session.Peers.Count}|{string.Join(';', names)}";
    }

    /// <summary>
    /// Analyse un message d'information de session.
    /// </summary>
    internal static bool TryParseSessionInfo(string response, out SessionSnapshot sessionInfo)
    {
        sessionInfo = default;

        if (!response.StartsWith("OK|", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = response.Split('|');
        if (parts.Length < 4)
        {
            return false;
        }

        if (!int.TryParse(parts[2], out var count))
        {
            return false;
        }

        var names = parts[3].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        sessionInfo = new SessionSnapshot(parts[1], count, string.Join(", ", names), names);
        return true;
    }
}
