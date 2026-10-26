using Model.Game;
using Model.Lobby;
using Network;

namespace UnitTests;

public sealed class LobbyDepartureTests
{
    /// <summary>Un départ avant le démarrage libère le nom et la place sans réservation ni reprise possible.</summary>
    [Fact]
    public async Task GuestLeavingAnOrdinaryLobbyIsRemovedImmediately()
    {
        await using var server = await LobbyHttpServer.StartAsync("Alice", 3);
        using var host = new LobbyHttpClient(server.LocalAddress);
        using var bob = new LobbyHttpClient(server.LocalAddress);
        using var chloe = new LobbyHttpClient(server.LocalAddress);
        host.SetHostIdentity(server.Lobby.HostIdentity);
        await bob.JoinAsync("Bob", server.Lobby.InvitationSecret);
        await chloe.JoinAsync("Chloe", server.Lobby.InvitationSecret);
        var departedId = bob.Identity!.PlayerId;
        await bob.LeaveAsync();
        foreach (var client in new[] { host, chloe })
        {
            var lobby = await client.GetLobbyAsync();
            Assert.Equal(LobbyStatus.Open, lobby.Status);
            Assert.Equal(2, lobby.Players.Count);
            Assert.DoesNotContain(lobby.Players, player => player.Id == departedId);
            Assert.Empty(lobby.AbsentPlayers);
            Assert.Null(lobby.ExpectedTransferredPlayers);
        }
        using var replacement = new LobbyHttpClient(server.LocalAddress);
        await replacement.JoinAsync("Bob", server.Lobby.InvitationSecret);
        Assert.NotEqual(departedId, replacement.Identity!.PlayerId);
        Assert.Equal(3, (await host.StartAsync()).Players.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DepartedGuestIsRemovedFromTheExpectedTransferredTeam(bool wasStarted)
    {
        var server = new HostedLobby("Bob", 3);
        server.ConfigureHostMigration(server.Code, server.HostIdentity.SessionToken,
            new HostMigrationSetup(["Bob", "Chloe", "Dora"], wasStarted,
                wasStarted ? 2 : 0, wasStarted ? 1 : 0, 0, null));
        var chloe = server.Join("Chloe", server.InvitationSecret);
        server.Leave(server.Code, chloe.SessionToken);
        var waiting = server.GetLobby(server.Code, server.HostIdentity.SessionToken);
        Assert.Equal(LobbyStatus.Open, waiting.Status);
        Assert.Single(waiting.Players);
        Assert.Empty(waiting.AbsentPlayers);
        Assert.Equal(2, waiting.ExpectedTransferredPlayers);
        Assert.False(waiting.AutoResumeTransferredGame);
        Assert.Throws<UnauthorizedAccessException>(() => server.Resume(server.Code, chloe.SessionToken));
        server.Join("Dora", server.InvitationSecret);
        server.Join("Emma", server.InvitationSecret);
        var freshGame = server.Start(server.Code, server.HostIdentity.SessionToken);
        Assert.Equal(3, freshGame.Players.Count);
        Assert.Equal(1, freshGame.HeistNumber);
        Assert.Equal(0, freshGame.SuccessfulHeists);
        Assert.Equal(0, freshGame.FailedHeists);
    }

    [Fact]
    public void DepartedTransferredGuestDoesNotBlockCapacityOrTheThreeRemainingPlayers()
    {
        var server = new HostedLobby("Bob", 4);
        server.ConfigureHostMigration(server.Code, server.HostIdentity.SessionToken,
            new HostMigrationSetup(["Bob", "Chloe", "Dora", "Emma"], true, 2, 1, 0, null));
        var chloe = server.Join("Chloe", server.InvitationSecret);
        server.Leave(server.Code, chloe.SessionToken);
        var waiting = server.GetLobby(server.Code, server.HostIdentity.SessionToken);
        Assert.Empty(waiting.AbsentPlayers);
        Assert.Equal(3, waiting.ExpectedTransferredPlayers);
        Assert.True(waiting.AutoResumeTransferredGame);
        server.ChangeCapacity(server.Code, server.HostIdentity.SessionToken, 3);
        server.Join("Dora", server.InvitationSecret);
        server.Join("Emma", server.InvitationSecret);
        var continued = server.GetGame(server.Code, server.HostIdentity.SessionToken);
        Assert.Equal(GamePhase.PreFlop, continued.Phase);
        Assert.Equal(new[] { "Bob", "Dora", "Emma" }, continued.Players.Select(player => player.Name));
        Assert.Equal(2, continued.HeistNumber);
        Assert.Equal(1, continued.SuccessfulHeists);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoneSuccessorCanInviteNewGuestsWithoutWaitingForFormerHost(bool wasStarted)
    {
        var server = new HostedLobby("Bob", 3);
        server.ConfigureHostMigration(server.Code, server.HostIdentity.SessionToken,
            new HostMigrationSetup(["Bob"], wasStarted, wasStarted ? 1 : 0, 0, 0, null));
        var lobby = server.GetLobby(server.Code, server.HostIdentity.SessionToken);
        Assert.Single(lobby.Players);
        Assert.Empty(lobby.AbsentPlayers);
        Assert.Null(lobby.ExpectedTransferredPlayers);
        Assert.False(lobby.AutoResumeTransferredGame);
        server.Join("Chloe", server.InvitationSecret);
        server.Join("Dora", server.InvitationSecret);
        Assert.Equal(3, server.Start(server.Code, server.HostIdentity.SessionToken).Players.Count);
    }
}
