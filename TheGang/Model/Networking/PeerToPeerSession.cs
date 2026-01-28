using Model.Networking.Interfaces;

namespace Model.Networking;

/// <summary>
/// Gère une session peer-to-peer simple pour héberger et rejoindre une partie.
/// </summary>
public class PeerToPeerSession : IPeerToPeerSession
{
    public const int DefaultMaxPeers = 6;

    private readonly List<IPeerInfo> _peers = new();
    
    /// <summary>
    /// Pair qui héberge la session.
    /// </summary>
    public IPeerInfo Host { get; }

    /// <summary>
    /// Indique si la session accepte de nouveaux pairs.
    /// </summary>
    public bool IsOpen { get; private set; }

    /// <summary>
    /// Liste en lecture seule des pairs connectés (hébergeur inclus).
    /// </summary>
    public IReadOnlyList<IPeerInfo> Peers => _peers.AsReadOnly();
    
    /// <summary>
    /// Nombre maximal de pairs autorisés dans la session (hébergeur inclus).
    /// </summary>
    public int MaxPeers { get; }

    private PeerToPeerSession(IPeerInfo host, int maxPeers)
    {
        Host = host;
        MaxPeers = maxPeers;
        _peers.Add(host);
        IsOpen = true;
    }
    

    /// <summary>
    /// Crée une nouvelle session en tant qu'hôte.
    /// </summary>
    public static PeerToPeerSession CreateHost(string hostDisplayName, int maxPeers = DefaultMaxPeers)
    {
        if (string.IsNullOrWhiteSpace(hostDisplayName))
        {
            throw new ArgumentException("Le nom de l'hôte est requis.", nameof(hostDisplayName));
        }
        if (maxPeers < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPeers), "Le nombre maximal de joueurs doit être au moins 2.");
        }

        var host = new PeerInfo(Guid.NewGuid(), hostDisplayName.Trim(), true);
        return new PeerToPeerSession(host, maxPeers);
    }

    /// <summary>
    /// Ouvre la session afin d'autoriser de nouvelles connexions.
    /// </summary>
    public void Open() => IsOpen = true;

    /// <summary>
    /// Ferme la session afin d'empêcher de nouvelles connexions.
    /// </summary>
    public void Close() => IsOpen = false;

    /// <summary>
    /// Tente d'ajouter un nouveau pair à la session.
    /// </summary>
    public bool TryJoin(string displayName, out IPeerInfo? newPeer, out string? errorMessage)
    {
        newPeer = null;
        errorMessage = null;

        if (!IsOpen)
        {
            errorMessage = "La session est fermée.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            errorMessage = "Le nom du joueur est requis.";
            return false;
        }

        if (_peers.Count >= MaxPeers)
        {
            errorMessage = "Le nombre maximal de joueurs est atteint.";
            return false;
        }

        string trimmedName = displayName.Trim();
        if (_peers.Any(peer => peer.DisplayName.Equals(trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            errorMessage = "Ce nom est déjà utilisé dans la session.";
            return false;
        }

        newPeer = new PeerInfo(Guid.NewGuid(), trimmedName, false);
        _peers.Add(newPeer);
        return true;
    }

    /// <summary>
    /// Retire un pair existant de la session.
    /// </summary>
    public bool TryLeave(Guid peerId, out string? errorMessage)
    {
        errorMessage = null;

        var peer = _peers.FirstOrDefault(current => current.Id == peerId);
        if (peer is null)
        {
            errorMessage = "Le joueur est introuvable.";
            return false;
        }

        if (peer.IsHost)
        {
            errorMessage = "L'hôte ne peut pas quitter sans fermer la session.";
            return false;
        }

        _peers.Remove(peer);
        return true;
    }
}