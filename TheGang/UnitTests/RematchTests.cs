using System.Net;
using Model.Game;
using Model.Lobby;
using Network;

namespace UnitTests;

public sealed class RematchTests
{
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.Parse("2026-10-28T12:00:00Z");
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(int seconds) => now = now.AddSeconds(seconds);
    }

    private static (HostedLobby Lobby, LobbyIdentity[] Players) Create(Clock? clock = null, int count = 3)
    {
        var lobby = new HostedLobby("Alice", count, clock);
        var players = new[] { lobby.HostIdentity }.Concat(Enumerable.Range(1, count - 1)
            .Select(index => lobby.Join($"Joueur {index}", lobby.InvitationSecret))).ToArray();
        lobby.Start(lobby.Code, players[0].SessionToken);
        return (lobby, players);
    }

    private static GameSnapshot Finish(HostedLobby lobby, LobbyIdentity[] players)
    {
        GameSnapshot Apply(GameAction action) => lobby.Apply(lobby.Code, players[0].SessionToken, new(action));
        while (true)
        {
            for (var round = 0; round < 4; round++)
            {
                for (var i = 0; i < players.Length; i++)
                    lobby.Apply(lobby.Code, players[i].SessionToken, new(GameAction.TakeToken, i + 1));
                foreach (var player in players)
                    lobby.Apply(lobby.Code, player.SessionToken, new(GameAction.ConfirmChoice));
                Apply(GameAction.AdvanceRound);
            }
            foreach (var _ in players) Apply(GameAction.RevealNextHand);
            var result = Apply(GameAction.ResolveHeist);
            if (result.Phase == GamePhase.Finished) return result;
            Apply(GameAction.StartNextHeist);
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    public void RequiresEveryVoteThenResetsGameWhilePreservingSessions(int count)
    {
        var (lobby, players) = Create(new Clock(), count);
        var finished = Finish(lobby, players);
        var vote = Assert.IsType<RematchSnapshot>(finished.Rematch);
        Assert.Equal(HostedLobby.RematchDuration, vote.ExpiresAtUtc - vote.ServerNowUtc);
        Assert.Empty(vote.AcceptedPlayers);
        for (var i = 0; i < players.Length - 1; i++)
        {
            lobby.DecideRematch(lobby.Code, players[i].SessionToken, new(vote.Id, true));
            // Les répétitions réseau ne comptent jamais deux fois.
            lobby.DecideRematch(lobby.Code, players[i].SessionToken, new(vote.Id, true));
            var waiting = lobby.GetGame(lobby.Code, players[0].SessionToken);
            Assert.Equal(GamePhase.Finished, waiting.Phase);
            Assert.Equal(i + 1, waiting.Rematch!.AcceptedPlayers.Count);
        }
        lobby.DecideRematch(lobby.Code, players[^1].SessionToken, new(vote.Id, true));
        foreach (var player in players)
        {
            var game = lobby.GetGame(lobby.Code, player.SessionToken);
            Assert.NotEqual(finished.GameId, game.GameId);
            Assert.Equal(GamePhase.PreFlop, game.Phase);
            Assert.Equal(GameResult.InProgress, game.Result);
            Assert.Equal(1, game.HeistNumber);
            Assert.Equal(0, game.SuccessfulHeists);
            Assert.Equal(0, game.FailedHeists);
            Assert.Null(game.Rematch);
            Assert.Empty(game.CommunityCards);
            Assert.Empty(game.RevealedHands);
            Assert.Equal(count, game.AvailableTokens.Count);
            Assert.Equal(players.Select(p => p.PlayerId), game.Players.Select(p => p.Id));
            Assert.All(game.Players, p => { Assert.Empty(p.Signals); Assert.Null(p.CurrentToken); Assert.False(p.IsChoiceConfirmed); });
            Assert.Equal(2, game.Players.Single(p => p.Id == player.PlayerId).Hand.Count);
            Assert.All(game.Players.Where(p => p.Id != player.PlayerId), p => Assert.Empty(p.Hand));
        }
        Assert.Throws<InvalidOperationException>(() => lobby.DecideRematch(lobby.Code,
            players[0].SessionToken, new(vote.Id, true)));
    }

    [Fact]
    public async Task ConcurrentVotesStartExactlyOneNewGame()
    {
        var (lobby, players) = Create(new Clock(), 6);
        var finished = Finish(lobby, players);
        var vote = finished.Rematch!;
        await Task.WhenAll(players.Select(player => Task.Run(() =>
            lobby.DecideRematch(lobby.Code, player.SessionToken, new(vote.Id, true)))));
        var games = players.Select(player => lobby.GetGame(lobby.Code, player.SessionToken)).ToArray();
        Assert.Single(games.Select(game => game.GameId).Distinct());
        Assert.NotEqual(finished.GameId, games[0].GameId);
        Assert.All(games, game => Assert.Equal(GamePhase.PreFlop, game.Phase));
    }

    [Fact]
    public void OldVoteCannotAcceptOrCancelTheNextGamesRematch()
    {
        var (lobby, players) = Create(new Clock());
        var oldVote = Finish(lobby, players).Rematch!;
        foreach (var player in players) lobby.DecideRematch(lobby.Code, player.SessionToken, new(oldVote.Id, true));
        var newVote = Finish(lobby, players).Rematch!;
        Assert.NotEqual(oldVote.Id, newVote.Id);
        Assert.Throws<InvalidOperationException>(() => lobby.DecideRematch(lobby.Code, players[0].SessionToken, new(oldVote.Id, false)));
        Assert.Throws<InvalidOperationException>(() => lobby.DecideRematch(lobby.Code, players[0].SessionToken, new(oldVote.Id, true)));
        Assert.Empty(lobby.GetGame(lobby.Code, players[0].SessionToken).Rematch!.AcceptedPlayers);
    }

    [Fact]
    public void GuestCanDeclineAfterAcceptingAndEveryoneReturnsToSameLobby()
    {
        var (lobby, players) = Create();
        var vote = Finish(lobby, players).Rematch!;
        lobby.DecideRematch(lobby.Code, players[1].SessionToken, new(vote.Id, true));
        var result = lobby.DecideRematch(lobby.Code, players[1].SessionToken, new(vote.Id, false));
        Assert.Equal(LobbyStatus.Open, result.Status);
        Assert.Contains(players[1].Lobby.Players.Last().Name, result.GameReturnReason!);
        Assert.Equal(players.Select(p => p.PlayerId), result.Players.Select(p => p.Id));
        Assert.Throws<InvalidOperationException>(() => lobby.GetGame(lobby.Code, players[0].SessionToken));
        Assert.Throws<InvalidOperationException>(() => lobby.DecideRematch(lobby.Code, players[2].SessionToken, new(vote.Id, true)));
        Assert.Equal(GamePhase.PreFlop, lobby.Start(lobby.Code, players[0].SessionToken).Phase);
    }

    [Fact]
    public void DeadlineRejectsLastVoteAndKeepsPresentPlayersInLobby()
    {
        var clock = new Clock();
        var (lobby, players) = Create(clock);
        var vote = Finish(lobby, players).Rematch!;
        foreach (var player in players[..^1]) lobby.DecideRematch(lobby.Code, player.SessionToken, new(vote.Id, true));
        for (var step = 0; step < 12; step++)
        {
            clock.Advance(5);
            foreach (var player in players) lobby.GetLobby(lobby.Code, player.SessionToken);
        }
        Assert.Throws<InvalidOperationException>(() => lobby.DecideRematch(lobby.Code, players[^1].SessionToken, new(vote.Id, true)));
        var result = lobby.GetLobby(lobby.Code, players[0].SessionToken);
        Assert.Equal(LobbyStatus.Open, result.Status);
        Assert.Equal(3, result.Players.Count);
        Assert.Contains("délai", result.GameReturnReason!);
    }

    [Fact]
    public void AbsentPlayerCannotBeCountedAndTimeoutRemovesTheirReservedPlace()
    {
        var clock = new Clock();
        var (lobby, players) = Create(clock);
        var vote = Finish(lobby, players).Rematch!;
        lobby.DecideRematch(lobby.Code, players[2].SessionToken, new(vote.Id, true));
        clock.Advance(9);
        lobby.GetLobby(lobby.Code, players[1].SessionToken);
        Assert.Throws<InvalidOperationException>(() => lobby.DecideRematch(lobby.Code, players[0].SessionToken, new(vote.Id, true)));
        clock.Advance(51);
        var result = lobby.GetLobby(lobby.Code, players[0].SessionToken);
        Assert.Equal(LobbyStatus.Open, result.Status);
        Assert.Single(result.Players);
        Assert.Empty(result.AbsentPlayers);
    }

    [Fact]
    public void DepartureDuringVoteReopensLobbyAndFreesPlace()
    {
        var (lobby, players) = Create(count: 4);
        Finish(lobby, players);
        lobby.Leave(lobby.Code, players[^1].SessionToken);
        var result = lobby.GetLobby(lobby.Code, players[0].SessionToken);
        Assert.Equal(LobbyStatus.Open, result.Status);
        Assert.Equal(3, result.Players.Count);
        Assert.Empty(result.AbsentPlayers);
        Assert.Equal("Nouveau", lobby.Join("Nouveau", lobby.InvitationSecret).Lobby.Players.Last().Name);
    }

    [Fact]
    public void RejectsVotesDuringGameAndStaleOrUnauthenticatedDecisions()
    {
        var (lobby, players) = Create();
        Assert.Throws<InvalidOperationException>(() => lobby.DecideRematch(lobby.Code, players[0].SessionToken, new(Guid.NewGuid(), true)));
        Finish(lobby, players);
        Assert.Throws<InvalidOperationException>(() => lobby.DecideRematch(lobby.Code, players[0].SessionToken, new(Guid.NewGuid(), false)));
        Assert.Throws<UnauthorizedAccessException>(() => lobby.DecideRematch(lobby.Code, "unknown", new(Guid.NewGuid(), true)));
        Assert.Equal(GamePhase.Finished, lobby.GetGame(lobby.Code, players[0].SessionToken).Phase);
    }

    [Fact]
    public async Task HttpEndpointUsesAuthenticatedGuestVoteAndCanReturnLobby()
    {
        await using var server = await LobbyHttpServer.StartAsync("Alice", 3);
        using var host = new LobbyHttpClient(server.LocalAddress);
        using var guest = new LobbyHttpClient(server.LocalAddress);
        host.SetHostIdentity(server.Lobby.HostIdentity);
        var bob = await guest.JoinAsync("Bob", server.Lobby.InvitationSecret);
        var chloe = server.Lobby.Join("Chloe", server.Lobby.InvitationSecret);
        await host.StartAsync();
        var vote = Finish(server.Lobby, [server.Lobby.HostIdentity, bob, chloe]).Rematch!;
        using var anonymous = new HttpClient { BaseAddress = server.LocalAddress };
        using var unauthorized = await anonymous.PostAsync($"lobbies/{server.Lobby.Code}/game/rematch",
            System.Net.Http.Json.JsonContent.Create(new RematchDecision(vote.Id, true)));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        await guest.DecideRematchAsync(new(vote.Id, true));
        Assert.Equal(new[] { bob.PlayerId }, (await host.GetGameAsync()).Rematch!.AcceptedPlayers);
        var result = await guest.DecideRematchAsync(new(vote.Id, false));
        Assert.Equal(LobbyStatus.Open, result.Status);
        Assert.Equal(3, (await host.GetLobbyAsync()).Players.Count);
    }
}
