namespace Model.Networking.Interfaces;

/// <summary>
/// Contrat pour un pair connecté à une session.
/// </summary>
public interface IPeerInfo
{
    Guid Id { get; }

    string DisplayName { get; }

    bool IsHost { get; }
}
