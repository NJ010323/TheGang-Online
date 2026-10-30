using Model.Lobby;
using Network.Bots;

namespace Network;

public sealed partial class HostedLobby
{
    private sealed class BotSeat
    {
        public BotDecision Decision { get; } = new();
        public DateTimeOffset Due { get; set; }
        public string Scope { get; set; } = "";
    }
    private readonly Dictionary<Guid, BotSeat> _bots = [];

    /// <summary>Seul l'hôte peut modifier les places automatiques d'un salon ouvert.</summary>
    public LobbySnapshot ChangeBotCount(string code, string sessionToken, int count)
    {
        lock (_sync)
        {
            EnsureHost(EnsureMember(code, sessionToken));
            EnsureAllPlayersPresent();
            if (_lobby.Status != LobbyStatus.Open || _migrationSetup is not null)
                throw new InvalidOperationException("Les bots se choisissent avant le départ, hors relève.");
            if (count < 0 || count + _lobby.Players.Count - _bots.Count > _lobby.MaxPlayers)
                throw new ArgumentOutOfRangeException(nameof(count), "Les humains et bots doivent tenir dans les places de la table.");
            while (_bots.Count > count)
            {
                var id = _bots.Keys.Last();
                _manager.LeaveLobby(Code, id);
                _bots.Remove(id);
            }
            while (_bots.Count < count)
            {
                var index = 1;
                while (_lobby.Players.Any(p => p.Name.Equals($"Complice {index}", StringComparison.OrdinalIgnoreCase))) index++;
                AddBot($"Complice {index}");
            }
            return Snapshot();
        }
    }

    private void AddBot(string name)
    {
        var player = _manager.JoinLobby(Code, name);
        // Pas de session réseau : aucun client ne peut usurper la place d'un bot.
        _bots.Add(player.Id, new BotSeat());
    }

    /// <summary>Appelé uniquement par le serveur. Aucune commande différée n'est conservée :
    /// la décision est prise sur une projection fraîche après le délai de réflexion.</summary>
    public void TickBots()
    {
        lock (_sync)
        {
            var now = _clock.GetUtcNow();
            RefreshPresence(now);
            if (_lobby.Status != LobbyStatus.Started || _game is null || _absentUntil.Count != 0 ||
                _migration is { Stage: HostMigrationStage.Offered or HostMigrationStage.Preparing or HostMigrationStage.Redirecting })
            {
                foreach (var seat in _bots.Values) seat.Scope = "";
                return;
            }
            foreach (var (id, seat) in _bots)
            {
                var view = GameSnapshotFor(id);
                var scope = $"{view.GameId}/{view.HeistNumber}/{view.Phase}/{view.Special?.SetupKind}/{view.Special?.DiscardPlayer}/{view.Special?.Proposal?.Id}/{view.NextPlayerToRevealId}";
                if (seat.Scope != scope)
                {
                    seat.Scope = scope;
                    seat.Due = now.AddMilliseconds(Random.Shared.Next(1000, 1900));
                    continue;
                }
                if (now < seat.Due) continue;
                seat.Due = now.AddMilliseconds(Random.Shared.Next(1100, 2100));
                var observation = BotObservation.FromSnapshot(view, id);
                var action = seat.Decision.Decide(observation);
                if (action is not null) ApplyAs(id, action);
                // Une seule action par tick rend les mouvements visibles et sérialise les décisions.
                if (action is not null) break;
            }
        }
    }
}
