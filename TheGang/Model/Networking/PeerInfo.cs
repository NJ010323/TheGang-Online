using Model.Networking.Interfaces;

namespace Model.Networking;

/// <summary>
/// Représente un pair connecté à une session peer-to-peer.
/// </summary>
public class PeerInfo : IPeerInfo
{
    public Guid Id { get; }

    public string DisplayName { get; }

    public bool IsHost { get; }
    
    public PeerInfo(Guid id, string displayName, bool isHost)
    {
        Id = id;
        DisplayName = displayName;
        IsHost = isHost;
    }
}