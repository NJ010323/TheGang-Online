namespace Network;

/// <summary>Accorde un délai aux coupures passagères sans prolonger indéfiniment une partie perdue.</summary>
public sealed class LobbyReconnectWindow
{
    private readonly TimeSpan _duration;
    private DateTimeOffset? _startedAt;

    public LobbyReconnectWindow(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        _duration = duration;
    }

    /// <summary>Indique si une série de tentatives est en cours.</summary>
    public bool IsActive => _startedAt is not null;

    /// <summary>Démarre une seule fenêtre de reprise, même si plusieurs requêtes échouent.</summary>
    public void Start(DateTimeOffset now) => _startedAt ??= now;

    /// <summary>Indique si l'hôte est resté inaccessible au-delà du délai accordé.</summary>
    public bool IsExpired(DateTimeOffset now) => _startedAt is { } start && now - start >= _duration;

    /// <summary>Oublie la coupure dès qu'un état de partie complet a été reçu.</summary>
    public void Reset() => _startedAt = null;
}
