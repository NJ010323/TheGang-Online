using System.Security;
using Model.Game;
using Model.Lobby;
using Network;
using Network.Bots;
using Model.Game.Card.CardEnum;
using System.Text.Json;

namespace UnitTests;

public class BotTests
{
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Step() => _now += TimeSpan.FromMilliseconds(500);
    }

    [Fact]
    public void OnlyHostCanManageBotsAndAllSeatsCount()
    {
        var lobby = new HostedLobby("Host", 4);
        var guest = lobby.Join("Guest", lobby.InvitationSecret);
        Assert.Throws<SecurityException>(() => lobby.ChangeBotCount(lobby.Code, guest.SessionToken, 1));
        var snapshot = lobby.ChangeBotCount(lobby.Code, lobby.HostIdentity.SessionToken, 2);
        Assert.Equal(4, snapshot.Players.Count);
        Assert.Equal(2, snapshot.Players.Count(p => p.IsBot));
        Assert.Throws<ArgumentOutOfRangeException>(() => lobby.ChangeBotCount(lobby.Code, lobby.HostIdentity.SessionToken, 3));
        Assert.Throws<InvalidOperationException>(() => lobby.Join("Overflow", lobby.InvitationSecret));
        Assert.Throws<InvalidOperationException>(() => lobby.ChangeCapacity(lobby.Code, lobby.HostIdentity.SessionToken, 3));
        lobby.ChangeBotCount(lobby.Code, lobby.HostIdentity.SessionToken, 1);
        lobby.Join("New guest", lobby.InvitationSecret);
        var game = lobby.Start(lobby.Code, lobby.HostIdentity.SessionToken);
        Assert.Single(game.Players.Where(p => p.IsBot));
        Assert.Throws<InvalidOperationException>(() => lobby.ChangeBotCount(lobby.Code, lobby.HostIdentity.SessionToken, 0));
        Assert.All(game.Players.Where(p => p.Id != lobby.HostIdentity.PlayerId), p => Assert.Empty(p.Hand));
    }

    public static IEnumerable<object[]> Configurations =>
        from count in Enumerable.Range(3, 4)
        from mode in Enum.GetValues<GameDifficulty>()
        select new object[] { count, mode };

    [Theory, MemberData(nameof(Configurations))]
    public void CompleteGamesAndRematchesWithOneHuman(int count, GameDifficulty mode)
    {
        var clock = new Clock();
        var lobby = new HostedLobby("Host", count, clock);
        lobby.ChangeBotCount(lobby.Code, lobby.HostIdentity.SessionToken, count - 1);
        lobby.ChangeDifficulty(lobby.Code, lobby.HostIdentity.SessionToken, mode);
        lobby.Start(lobby.Code, lobby.HostIdentity.SessionToken);
        RunGame(lobby, clock, new[] { lobby.HostIdentity });
        var finished = lobby.GetGame(lobby.Code, lobby.HostIdentity.SessionToken);
        Assert.NotNull(finished.Rematch);
        lobby.DecideRematch(lobby.Code, lobby.HostIdentity.SessionToken, new(finished.Rematch!.Id, true));
        var rematch = lobby.GetGame(lobby.Code, lobby.HostIdentity.SessionToken);
        Assert.NotEqual(finished.GameId, rematch.GameId);
        Assert.Equal(1, rematch.HeistNumber);
        Assert.Equal(count - 1, rematch.Players.Count(p => p.IsBot));
    }

    [Fact]
    public void MixedGroupFinishesAndBotsArePreservedOnHostMigration()
    {
        var clock = new Clock();
        var lobby = new HostedLobby("Host", 5, clock);
        // Bots precede the guest: succession must still select the human.
        lobby.ChangeBotCount(lobby.Code, lobby.HostIdentity.SessionToken, 3);
        var guest = lobby.Join("Guest", lobby.InvitationSecret);
        lobby.Start(lobby.Code, lobby.HostIdentity.SessionToken);
        var migration = lobby.BeginHostMigration(lobby.Code, lobby.HostIdentity.SessionToken);
        Assert.Equal(guest.PlayerId, migration.SuccessorId);
        Assert.Equal(2, migration.PlayersToRedirect);
        var next = new HostedLobby("Guest", 5, clock);
        next.ConfigureHostMigration(next.Code, next.HostIdentity.SessionToken,
            new(migration.ExpectedPlayers, true, 1, 0, 0, null) { BotNames = migration.BotNames });
        Assert.Equal(3, next.GetLobby(next.Code, next.HostIdentity.SessionToken).Players.Count(p => p.IsBot));
        RunGame(next, clock, new[] { next.HostIdentity });
        lobby.CancelHostMigration(lobby.Code, lobby.HostIdentity.SessionToken, migration.Id);
        RunGame(lobby, clock, new[] { lobby.HostIdentity, guest });
    }

    [Theory]
    [InlineData("E01")][InlineData("E02")][InlineData("E03")][InlineData("E04")][InlineData("E05")]
    [InlineData("E06")][InlineData("E07")][InlineData("E08")][InlineData("E09")][InlineData("E10")]
    [InlineData("C01")][InlineData("C02")][InlineData("C03")][InlineData("C04")][InlineData("C05")]
    [InlineData("C06")][InlineData("C07")][InlineData("C08")][InlineData("C09")][InlineData("C10")]
    public void EverySpecialCardProgressesWithBots(string card)
    {
        var clock = new Clock();
        var lobby = new HostedLobby("Host", 3, clock);
        lobby.ConfigureHostMigration(lobby.Code, lobby.HostIdentity.SessionToken,
            new(new[] { "Host", "Bot A", "Bot B" }, true, 1, 0, 0, null)
            {
                BotNames = new[] { "Bot A", "Bot B" },
                DifficultyState = new(GameDifficulty.Advanced, null, new[] { card },
                    Enumerable.Range(1, 10).ToArray(), Enumerable.Range(1, 10).ToArray())
            });
        if (new[] { "C01", "C02", "C04", "C05", "C07", "C10" }.Contains(card))
        {
            var game = lobby.GetGame(lobby.Code, lobby.HostIdentity.SessionToken);
            var target = card == "C01" ? lobby.HostIdentity.PlayerId : game.Players.First(p => p.IsBot).Id;
            lobby.Apply(lobby.Code, lobby.HostIdentity.SessionToken,
                new(GameAction.ProposeComplice) { ExpectedGameId = game.GameId, ExpectedHeist = 1,
                    TargetId = target, RecipientId = game.Players.First(p => p.IsBot).Id, Rank = 1 });
        }
        RunGame(lobby, clock, new[] { lobby.HostIdentity }, stopAfterHeist: true);
    }

    [Fact]
    public void ThinkingActionsAreCancelledOnLeaveAndNewGame()
    {
        var clock = new Clock();
        var lobby = new HostedLobby("Host", 3, clock);
        lobby.ChangeBotCount(lobby.Code, lobby.HostIdentity.SessionToken, 2);
        lobby.Start(lobby.Code, lobby.HostIdentity.SessionToken);
        lobby.TickBots(); // Schedule thinking, without executing any action.
        lobby.Leave(lobby.Code, lobby.HostIdentity.SessionToken);
        clock.Step(); clock.Step(); clock.Step(); clock.Step();
        lobby.TickBots(); // A closed table never executes its pending work.
        Assert.Throws<KeyNotFoundException>(() => lobby.GetGame(lobby.Code, lobby.HostIdentity.SessionToken));
    }

    [Fact]
    public void DecisionsCannotDependOnOtherPrivateHands()
    {
        var lobby = new HostedLobby("Host", 3);
        lobby.ChangeBotCount(lobby.Code, lobby.HostIdentity.SessionToken, 2);
        var snapshot = lobby.Start(lobby.Code, lobby.HostIdentity.SessionToken);
        var first = BotObservation.FromSnapshot(snapshot, lobby.HostIdentity.PlayerId);
        var changed = snapshot with { Players = snapshot.Players.Select(p => p.Id == lobby.HostIdentity.PlayerId
            ? p : p with { Hand = new[] { new CardSnapshot(CardColors.Pique, 1), new CardSnapshot(CardColors.Coeur, 13) } }).ToArray() };
        var second = BotObservation.FromSnapshot(changed, lobby.HostIdentity.PlayerId);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.Equal(new BotDecision(new Random(42)).Decide(first), new BotDecision(new Random(42)).Decide(second));
        Assert.All(snapshot.Players.Where(p => p.IsBot), p =>
            Assert.Throws<UnauthorizedAccessException>(() => lobby.GetGame(lobby.Code, p.Id.ToString())));
    }

    [Fact]
    public async Task OfflineSessionUsesLoopbackAndSupportsBotManagementOverHttp()
    {
        await using var session = await DesktopLobbySession.CreateOfflineAsync("Host", 3);
        Assert.True(session.IsOffline);
        Assert.True(session.IsHost);
        await session.Client.ChangeBotCountAsync(2);
        var game = await session.Client.StartAsync();
        Assert.Equal(2, game.Players.Count(p => p.IsBot));
        Assert.All(game.Players.Where(p => p.IsBot), p => Assert.Empty(p.Hand));
        await session.Client.LeaveAsync();
    }

    private static void RunGame(HostedLobby lobby, Clock clock, IReadOnlyList<LobbyIdentity> humans, bool stopAfterHeist = false)
    {
        var host = lobby.HostIdentity;
        for (var tick = 0; tick < 1800; tick++)
        {
            // Keep all humans present while the authoritative clock advances.
            foreach (var human in humans) lobby.GetLobby(lobby.Code, human.SessionToken);
            clock.Step();
            lobby.TickBots();
            foreach (var human in humans)
            {
                var view = lobby.GetGame(lobby.Code, human.SessionToken);
                var self = view.Players.Single(p => p.Id == human.PlayerId);
                GameActionRequest? action = null;
                var special = view.Special!;
                if (special.SetupKind is not null && !special.SetupChosen) action = new(GameAction.ChooseSpecialSetup);
                else if (special.DiscardPlayer == self.Id) action = new(GameAction.DiscardSpecial) { CardIndex = special.ForbiddenDiscard == 0 ? 1 : 0 };
                else if (special.Proposal is { } vote && !vote.Accepted.Contains(self.Id)) action = new(GameAction.VoteComplice) { ProposalId = vote.Id, Accepted = true };
                else if (special.SetupKind is null && special.DiscardPlayer is null && special.Proposal is null &&
                    view.Phase is GamePhase.PreFlop or GamePhase.Flop or GamePhase.Turn or GamePhase.River)
                {
                    if (self.CurrentToken is null) action = new(GameAction.TakeToken, view.AvailableTokens.First());
                    else if (view.AllPlayersHaveTokens && !self.IsChoiceConfirmed) action = new(GameAction.ConfirmChoice);
                }
                if (view.Phase == GamePhase.Showdown && view.NextPlayerToRevealId == special.GuessTarget && self.Id != special.GuessTarget)
                {
                    var kind = new[] { "E04", "E09" }.FirstOrDefault(k => special.ActiveCards.Contains(k) && !special.OwnGuesses.ContainsKey(k));
                    if (kind is not null) action = new(GameAction.SubmitSpecialGuess) { GuessKind = kind, Rank = 1 };
                }
                if (action is not null) lobby.Apply(lobby.Code, human.SessionToken, action with { ExpectedGameId = view.GameId, ExpectedHeist = view.HeistNumber });
            }
            var current = lobby.GetGame(lobby.Code, host.SessionToken);
            if (current.Phase == GamePhase.Finished || stopAfterHeist && current.Phase == GamePhase.HeistComplete) return;
            GameActionRequest? transition = current.AllChoicesConfirmed ? new(GameAction.AdvanceRound) :
                current.Phase == GamePhase.HeistComplete ? new(GameAction.StartNextHeist) : null;
            if (current.Phase == GamePhase.Showdown)
            {
                var special = current.Special!;
                var guessing = current.NextPlayerToRevealId == special.GuessTarget;
                var ready = new[] { "E04", "E09" }.Where(special.ActiveCards.Contains).All(k =>
                {
                    var votes = special.PublicGuesses.Where(g => g.Kind == k).ToArray();
                    return votes.Length == current.Players.Count - 1 && votes.Select(g => g.Value).Distinct().Count() == 1;
                });
                if (!guessing || ready) transition = new(current.NextPlayerToRevealId is null ? GameAction.ResolveHeist : GameAction.RevealNextHand);
            }
            if (transition is not null) lobby.Apply(lobby.Code, host.SessionToken, transition);
        }
        Assert.Fail("La partie ne progresse plus avec les bots.");
    }
}
