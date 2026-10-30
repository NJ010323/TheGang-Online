using Model.Game;

namespace Network;

public sealed partial class HostedLobby
{
    public static readonly TimeSpan RematchDuration = TimeSpan.FromSeconds(60);
    private Guid _gameId = Guid.NewGuid();
    private RematchSnapshot? _rematch;
    private readonly HashSet<Guid> _rematchAccepted = [];
    private string? _gameReturnReason;

    private void BeginRematchVote()
    {
        _rematchAccepted.Clear();
        _rematchAccepted.UnionWith(_bots.Keys);
        var now = _clock.GetUtcNow();
        _rematch = new RematchSnapshot(Guid.NewGuid(), now + RematchDuration, now, []);
    }

    /// <summary>Chaque session vote pour elle-même ; seul l'accord de tous relance la partie.</summary>
    public LobbySnapshot DecideRematch(string code, string sessionToken, RematchDecision decision)
    {
        lock (_sync)
        {
            var playerId = EnsureMember(code, sessionToken);
            if (_game?.Phase != GamePhase.Finished || _rematch is not { } vote || vote.Id != decision.Id)
                throw new InvalidOperationException("Cette proposition de revanche n’est plus disponible.");
            if (!decision.Accepted)
            {
                _gameReturnReason = $"{_lobby.Players.First(player => player.Id == playerId).Name} a choisi le retour au lobby.";
                ReturnToLobbyAfterDeparture();
                return Snapshot();
            }
            EnsureAllPlayersPresent();
            _rematchAccepted.Add(playerId);
            if (_lobby.Players.All(player => _rematchAccepted.Contains(player.Id)))
            {
                _manager.ReturnToLobby(Code, _lobby.HostId, Array.Empty<Guid>());
                _game = _manager.StartGame(Code, _lobby.HostId);
                _gameId = Guid.NewGuid();
                _rematch = null;
                _rematchAccepted.Clear();
                _gameReturnReason = null;
                _migrationSetup = null;
                _events.Clear();
                _nextEventSequence = 0;
                CompleteAllGestures();
                RecordEvent(GameEventKind.HeistStarted, playerId, _game);
                RefreshPresence(_clock.GetUtcNow());
            }
            return Snapshot();
        }
    }
}
