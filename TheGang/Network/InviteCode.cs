using System.Text.RegularExpressions;

namespace Network;

/// <summary>Invitation contenant le code du lobby, son tunnel et un secret de 128 bits.</summary>
public static partial class InviteCode
{
    /// <summary>Réunit les trois éléments nécessaires à une invitation complète.</summary>
    public static string Create(string lobbyCode, Uri publicAddress, string invitationSecret)
    {
        if (!LobbyCodePattern().IsMatch(lobbyCode))
            throw new ArgumentException("Code de lobby invalide.", nameof(lobbyCode));
        if (publicAddress.Scheme != Uri.UriSchemeHttps ||
            !publicAddress.Host.EndsWith(".trycloudflare.com", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Une adresse HTTPS trycloudflare.com est attendue.", nameof(publicAddress));
        var tunnelName = publicAddress.Host[..^".trycloudflare.com".Length];
        if (!TunnelNamePattern().IsMatch(tunnelName))
            throw new ArgumentException("Nom de tunnel invalide.", nameof(publicAddress));
        if (!SecretPattern().IsMatch(invitationSecret))
            throw new ArgumentException("Secret d'invitation invalide.", nameof(invitationSecret));
        return $"{lobbyCode.ToUpperInvariant()}@{tunnelName}#{invitationSecret.ToUpperInvariant()}";
    }

    /// <summary>Reconstruit le code, l'adresse HTTPS et le secret sans annuaire.</summary>
    public static (string Code, Uri Address, string Secret) Parse(string invitation)
    {
        if (string.IsNullOrWhiteSpace(invitation))
            throw new ArgumentException("Invitation manquante.", nameof(invitation));
        var parts = invitation.Trim().Split('@', '#');
        if (parts.Length != 3 || !LobbyCodePattern().IsMatch(parts[0]) ||
            !TunnelNamePattern().IsMatch(parts[1]) || !SecretPattern().IsMatch(parts[2]))
            throw new ArgumentException("Invitation invalide.", nameof(invitation));
        return (parts[0].ToUpperInvariant(),
            new Uri($"https://{parts[1]}.trycloudflare.com"), parts[2].ToUpperInvariant());
    }

    [GeneratedRegex("^[A-HJ-NP-Z2-9]{6}$", RegexOptions.IgnoreCase)]
    private static partial Regex LobbyCodePattern();
    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.IgnoreCase)]
    private static partial Regex TunnelNamePattern();
    [GeneratedRegex("^[A-F0-9]{32}$", RegexOptions.IgnoreCase)]
    private static partial Regex SecretPattern();
}
