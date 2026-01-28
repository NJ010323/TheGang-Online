namespace Model.Networking.Interfaces;

/// <summary>
/// Contrat pour une session peer-to-peer.
/// </summary>
public interface IPeerToPeerSession
{
    bool IsOpen { get; }

    IPeerInfo Host { get; }

    IReadOnlyList<IPeerInfo> Peers { get; }

    void Open();

    void Close();

    bool TryJoin(string displayName, out IPeerInfo? newPeer, out string? errorMessage);

    bool TryLeave(Guid peerId, out string? errorMessage);
}