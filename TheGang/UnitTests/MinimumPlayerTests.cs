using System.Net;
using Model.Game;
using Model.Lobby;
using Network;

namespace UnitTests;

public sealed class MinimumPlayerTests
{
    /// <summary>Le départ explicite du troisième joueur rouvre le même salon pour les deux clients restants.</summary>
    [Fact]
    public async Task GuestDepartureBelowThreeReopensLobbyForBothRemainingClients()
    {
        await using var server = await LobbyHttpServer.StartAsync("Alice", 3);
        using var host = new LobbyHttpClient(server.LocalAddress);
        using var bob = new LobbyHttpClient(server.LocalAddress);
        using var chloe = new LobbyHttpClient(server.LocalAddress);
        host.SetHostIdentity(server.Lobby.HostIdentity);
        await bob.JoinAsync("Bob", server.Lobby.InvitationSecret);
        await chloe.JoinAsync("Chloe", server.Lobby.InvitationSecret);
        await host.StartAsync();
        await host.ApplyAsync(new GameActionRequest(GameAction.TakeToken, 1));
        var aliceId = host.Identity!.PlayerId;
        var bobId = bob.Identity!.PlayerId;
        await chloe.LeaveAsync();

        foreach (var client in new[] { host, bob })
        {
            var lobby = await client.GetLobbyAsync();
            Assert.Equal(LobbyStatus.Open, lobby.Status);
            Assert.Equal(server.Lobby.Code, lobby.Code);
            Assert.Equal(new[] { aliceId, bobId }, lobby.Players.Select(p => p.Id));
            Assert.Empty(lobby.AbsentPlayers);
            var unavailable = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetGameAsync());
            Assert.Equal(HttpStatusCode.Conflict, unavailable.StatusCode);
        }
        await Assert.ThrowsAsync<HttpRequestException>(() => host.StartAsync());
        // Même adresse et même invitation : un nouvel invité peut relancer une partie normale.
        using var dora = new LobbyHttpClient(server.LocalAddress);
        await dora.JoinAsync("Dora", server.Lobby.InvitationSecret);
        var restarted = await host.StartAsync();
        Assert.Equal(1, restarted.HeistNumber);
        Assert.Equal(0, restarted.SuccessfulHeists);
        Assert.Equal(0, restarted.FailedHeists);
        Assert.Equal(3, restarted.AvailableTokens.Count);
        Assert.All(restarted.Players, player => Assert.Null(player.CurrentToken));
        Assert.Equal(2, (await bob.GetGameAsync()).Players.Single(p => p.Id == bobId).Hand.Count);
    }

    [Fact]
    public void SecondDepartureAlsoRemovesPreviouslyAbsentPlayersWhenLobbyReopens()
    {
        var server = new HostedLobby("Alice", 4);
        var bob = server.Join("Bob", server.InvitationSecret);
        var chloe = server.Join("Chloe", server.InvitationSecret);
        var dora = server.Join("Dora", server.InvitationSecret);
        server.Start(server.Code, server.HostIdentity.SessionToken);
        server.Leave(server.Code, bob.SessionToken);
        Assert.Equal(LobbyStatus.Started, server.GetLobby(server.Code, dora.SessionToken).Status);
        server.Leave(server.Code, chloe.SessionToken);
        var lobby = server.GetLobby(server.Code, dora.SessionToken);
        Assert.Equal(LobbyStatus.Open, lobby.Status);
        Assert.Equal(new[] { "Alice", "Dora" }, lobby.Players.Select(p => p.Name));
        Assert.Empty(lobby.AbsentPlayers);
        Assert.Throws<UnauthorizedAccessException>(() => server.Resume(server.Code, bob.SessionToken));
        Assert.Throws<UnauthorizedAccessException>(() => server.Resume(server.Code, chloe.SessionToken));
        server.Join("Emma", server.InvitationSecret);
        Assert.Equal(3, server.Start(server.Code, server.HostIdentity.SessionToken).Players.Count);
    }

    [Fact]
    public void GameResetPreservesIdentityButClearsCardsTokensAndProgress()
    {
        var game = new Game(new Random(1));
        game.AddPlayer("Alice"); game.AddPlayer("Bob"); game.AddPlayer("Chloe");
        var ids = game.Players.Select(p => p.Id).ToArray();
        game.StartGame();
        game.TakeToken(ids[0], 1);
        game.ReturnToLobby();
        Assert.Equal(GamePhase.WaitingForPlayers, game.Phase);
        Assert.Equal(ids, game.Players.Select(p => p.Id));
        Assert.Empty(game.CommunityCards);
        Assert.Empty(game.RoundTokens);
        Assert.All(game.Players, player =>
        {
            Assert.Empty(player.Hand);
            Assert.Empty(player.Signals);
            Assert.Null(player.SignalToken);
            Assert.False(player.IsChoiceConfirmed);
        });
        game.RemovePlayer(ids[2]);
        Assert.Throws<InvalidOperationException>(() => game.ContinueAfterHostMigration(1, 0, 0));
        Assert.Throws<InvalidOperationException>(() => game.StartGame());
        game.AddPlayer("Dora");
        game.StartGame();
        Assert.Equal(1, game.HeistNumber);
        Assert.Equal(0, game.SuccessfulHeists);
        Assert.Equal(0, game.FailedHeists);
    }
}
