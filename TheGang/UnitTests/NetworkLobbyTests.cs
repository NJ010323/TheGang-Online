using System.Net;
using System.Net.Http.Json;
using Model.Game;
using Network;

namespace UnitTests;

public class NetworkLobbyTests
{
    [Fact]
    public async Task MissingTunnelBinaryReportsItsRealCause()
    {
        var absentBinary = Path.Combine(Path.GetTempPath(), $"cloudflared-absent-{Guid.NewGuid():N}.exe");
        var error = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            CloudflareQuickTunnel.StartAsync(new Uri("http://127.0.0.1:54321"), absentBinary));
        Assert.Contains("cloudflared.exe est introuvable", error.Message);
    }

    [Fact]
    public void InvitationContainsDestinationAndUnforgeableSecret()
    {
        const string secret = "0123456789ABCDEF0123456789ABCDEF";
        var invitation = InviteCode.Create("ABC234", new Uri("https://three-random-words.trycloudflare.com"), secret);

        Assert.Equal($"ABC234@three-random-words#{secret}", invitation);
        var parsed = InviteCode.Parse(invitation);
        Assert.Equal("ABC234", parsed.Code);
        Assert.Equal("https://three-random-words.trycloudflare.com/", parsed.Address.ToString());
        Assert.Equal(secret, parsed.Secret);
        Assert.Throws<ArgumentException>(() => InviteCode.Parse("ABC234"));
        Assert.Throws<ArgumentException>(() => InviteCode.Parse("ABC234@localhost.com#0123456789ABCDEF0123456789ABCDEF"));
    }

    [Fact]
    public async Task TunnelHtmlErrorIsShortAndMeansTheHostIsUnavailable()
    {
        using var client = new LobbyHttpClient(new Uri("https://example.trycloudflare.com"),
            new HtmlTunnelErrorHandler());

        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.JoinAsync("Guest", "0123456789ABCDEF0123456789ABCDEF"));

        Assert.Equal((HttpStatusCode)530, error.StatusCode);
        Assert.DoesNotContain("<html>", error.Message);
        Assert.True(LobbyConnectionFailure.IsHostUnavailable(error));
        Assert.False(LobbyConnectionFailure.IsHostUnavailable(
            new HttpRequestException("Commande impossible", null, HttpStatusCode.Conflict)));
    }

    [Fact]
    public async Task GuestDetectsHostLeavingTheLobby()
    {
        await using var server = await LobbyHttpServer.StartAsync("Host", 3);
        using var host = new LobbyHttpClient(server.LocalAddress);
        using var guest = new LobbyHttpClient(server.LocalAddress);
        host.SetHostIdentity(server.Lobby.HostIdentity);
        await guest.JoinAsync("Guest", server.Lobby.InvitationSecret);

        await host.LeaveAsync();

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => guest.GetLobbyAsync());
        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
        Assert.True(LobbyConnectionFailure.IsHostUnavailable(error));
    }

    [Fact]
    public async Task LeavingStartedGameClosesItForTheOtherPlayers()
    {
        await using var server = await LobbyHttpServer.StartAsync("Host", 3);
        using var host = new LobbyHttpClient(server.LocalAddress);
        using var bob = new LobbyHttpClient(server.LocalAddress);
        using var chloe = new LobbyHttpClient(server.LocalAddress);
        host.SetHostIdentity(server.Lobby.HostIdentity);
        await bob.JoinAsync("Bob", server.Lobby.InvitationSecret);
        await chloe.JoinAsync("Chloe", server.Lobby.InvitationSecret);
        await host.StartAsync();

        await bob.LeaveAsync();

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => host.GetLobbyAsync());
        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
    }

    [Fact]
    public async Task ThreeClientsCanJoinAndPlayWithPrivateHandsAndHostAuthorization()
    {
        await using var server = await LobbyHttpServer.StartAsync("Alice", 3);
        using var host = new LobbyHttpClient(server.LocalAddress);
        using var bob = new LobbyHttpClient(server.LocalAddress);
        using var chloe = new LobbyHttpClient(server.LocalAddress);
        using var intruder = new HttpClient { BaseAddress = server.LocalAddress };
        host.SetHostIdentity(server.Lobby.HostIdentity);
        var code = server.Lobby.Code;

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await intruder.GetAsync($"lobbies/{code}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await intruder.PostAsJsonAsync("join",
                new JoinLobbyRequest("Mallory", "wrong"))).StatusCode);
        await bob.JoinAsync("Bob", server.Lobby.InvitationSecret);
        await chloe.JoinAsync("Chloe", server.Lobby.InvitationSecret);
        Assert.Equal(3, (await host.GetLobbyAsync()).Players.Count);
        await Assert.ThrowsAsync<HttpRequestException>(() => bob.StartAsync());
        using var extra = new LobbyHttpClient(server.LocalAddress);
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            extra.JoinAsync("Other", server.Lobby.InvitationSecret));

        var hostGame = await host.StartAsync();
        var bobGame = await bob.GetGameAsync();
        Assert.Equal(GamePhase.PreFlop, hostGame.Phase);
        Assert.Equal(2, hostGame.Players.Single(p => p.Id == host.Identity!.PlayerId).Hand.Count);
        Assert.Empty(bobGame.Players.Single(p => p.Id == host.Identity!.PlayerId).Hand);
        Assert.Equal(2, bobGame.Players.Single(p => p.Id == bob.Identity!.PlayerId).Hand.Count);
        Assert.Empty(bobGame.Players.Single(p => p.Id == chloe.Identity!.PlayerId).Hand);
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            bob.ApplyAsync(new GameActionRequest(GameAction.AdvanceRound)));

        await PlayRoundAsync(host, bob, chloe);
        Assert.Equal(GamePhase.Flop,
            (await host.ApplyAsync(new GameActionRequest(GameAction.AdvanceRound))).Phase);
        var afterFlop = await chloe.GetGameAsync();
        Assert.Equal(3, afterFlop.CommunityCards.Count);
        Assert.All(afterFlop.Players, p => Assert.Contains(p.Signals, s => s.Value is >= 1 and <= 3));

        await PlayRoundAsync(host, bob, chloe);
        Assert.Equal(GamePhase.Turn,
            (await host.ApplyAsync(new GameActionRequest(GameAction.AdvanceRound))).Phase);
        await PlayRoundAsync(host, bob, chloe);
        Assert.Equal(GamePhase.River,
            (await host.ApplyAsync(new GameActionRequest(GameAction.AdvanceRound))).Phase);
        await PlayRoundAsync(host, bob, chloe);
        Assert.Equal(GamePhase.Showdown,
            (await host.ApplyAsync(new GameActionRequest(GameAction.AdvanceRound))).Phase);

        var showdown = await host.GetGameAsync();
        var order = showdown.Players
            .Select(player => (PlayerId: player.Id, Token: player.CurrentToken!.Value))
            .OrderBy(player => player.Token).Select(player => player.PlayerId).ToArray();
        foreach (var expectedPlayer in order)
        {
            var before = await bob.GetGameAsync();
            Assert.Equal(expectedPlayer, before.NextPlayerToRevealId);
            var after = await host.ApplyAsync(new GameActionRequest(GameAction.RevealNextHand));
            Assert.Equal(2, after.Players.Single(p => p.Id == expectedPlayer).Hand.Count);
        }
        var result = await host.ApplyAsync(new GameActionRequest(GameAction.ResolveHeist));
        Assert.True(result.Phase is GamePhase.HeistComplete or GamePhase.Finished);
    }

    [Fact]
    public async Task TokenChangesResetConfirmationsAndGuestsCanLeave()
    {
        await using var server = await LobbyHttpServer.StartAsync("Host", 3);
        using var host = new LobbyHttpClient(server.LocalAddress);
        using var bob = new LobbyHttpClient(server.LocalAddress);
        using var chloe = new LobbyHttpClient(server.LocalAddress);
        host.SetHostIdentity(server.Lobby.HostIdentity);
        await bob.JoinAsync("Bob", server.Lobby.InvitationSecret);
        await chloe.JoinAsync("Chloe", server.Lobby.InvitationSecret);
        await host.StartAsync();

        await host.ApplyAsync(new GameActionRequest(GameAction.TakeToken, 1));
        await bob.ApplyAsync(new GameActionRequest(GameAction.TakeToken, 2));
        await chloe.ApplyAsync(new GameActionRequest(GameAction.TakeToken, 3));
        await bob.ApplyAsync(new GameActionRequest(GameAction.ConfirmChoice));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            host.ApplyAsync(new GameActionRequest(GameAction.AdvanceRound)));
        var snapshot = await host.ApplyAsync(new GameActionRequest(GameAction.TakeToken, 2));
        Assert.False(snapshot.AllPlayersHaveTokens);
        Assert.All(snapshot.Players, player => Assert.False(player.IsChoiceConfirmed));

        await using var waitingServer = await LobbyHttpServer.StartAsync("NewHost", 3);
        using var guest = new LobbyHttpClient(waitingServer.LocalAddress);
        await guest.JoinAsync("Guest", waitingServer.Lobby.InvitationSecret);
        await guest.LeaveAsync();
        Assert.Single(waitingServer.Lobby.GetLobby(waitingServer.Lobby.Code,
            waitingServer.Lobby.HostIdentity.SessionToken).Players);
    }

    [Fact]
    public async Task AnonymousJoinAttemptsAreLimited()
    {
        await using var server = await LobbyHttpServer.StartAsync("Host", 3);
        using var anonymous = new HttpClient { BaseAddress = server.LocalAddress };
        for (var attempt = 0; attempt < 12; attempt++)
        {
            using var response = await anonymous.PostAsJsonAsync("join",
                new JoinLobbyRequest("Intruder", "wrong"));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using var rejected = await anonymous.PostAsJsonAsync("join",
            new JoinLobbyRequest("Intruder", "wrong"));
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [Fact]
    public async Task RemoteCommandsCanFinishAnEntireGame()
    {
        await using var server = await LobbyHttpServer.StartAsync("Host", 3);
        using var host = new LobbyHttpClient(server.LocalAddress);
        using var bob = new LobbyHttpClient(server.LocalAddress);
        using var chloe = new LobbyHttpClient(server.LocalAddress);
        host.SetHostIdentity(server.Lobby.HostIdentity);
        await bob.JoinAsync("Bob", server.Lobby.InvitationSecret);
        await chloe.JoinAsync("Chloe", server.Lobby.InvitationSecret);
        var game = await host.StartAsync();

        while (game.Phase != GamePhase.Finished)
        {
            foreach (var _ in Enumerable.Range(0, 4))
            {
                await PlayRoundAsync(host, bob, chloe);
                game = await host.ApplyAsync(new GameActionRequest(GameAction.AdvanceRound));
            }
            Assert.Equal(GamePhase.Showdown, game.Phase);
            for (var player = 0; player < 3; player++)
                game = await host.ApplyAsync(new GameActionRequest(GameAction.RevealNextHand));
            game = await host.ApplyAsync(new GameActionRequest(GameAction.ResolveHeist));
            if (game.Phase == GamePhase.HeistComplete)
                game = await host.ApplyAsync(new GameActionRequest(GameAction.StartNextHeist));
        }

        Assert.InRange(game.HeistNumber, 3, 5);
        Assert.True(game.SuccessfulHeists == 3 || game.FailedHeists == 3);
        Assert.True(game.Result is GameResult.Won or GameResult.Lost);
    }

    [Fact]
    [Trait("Category", "External")]
    public async Task QuickTunnelAllowsJoiningFromItsPublicAddress()
    {
        // Test optionnel lancé explicitement avec le binaire livré ; il dépend du réseau Cloudflare.
        var binary = Environment.GetEnvironmentVariable("THEGANG_CLOUDFLARED");
        if (string.IsNullOrWhiteSpace(binary)) return;

        await using var server = await LobbyHttpServer.StartAsync("Host", 3);
        await using var tunnel = await CloudflareQuickTunnel.StartAsync(server.LocalAddress, binary);
        var invitation = InviteCode.Create(server.Lobby.Code, tunnel.PublicAddress, server.Lobby.InvitationSecret);
        await using var remote = await DesktopLobbySession.JoinAsync(invitation, "Remote");
        var guest = remote.Client.Identity!;
        Assert.Equal("Remote", guest.Lobby.Players.Single(p => p.Id == guest.PlayerId).Name);
    }

    /// <summary>Distribue trois jetons et obtient les confirmations via trois clients HTTP.</summary>
    private static async Task PlayRoundAsync(params LobbyHttpClient[] clients)
    {
        for (var index = 0; index < clients.Length; index++)
            await clients[index].ApplyAsync(new GameActionRequest(GameAction.TakeToken, index + 1));
        foreach (var client in clients)
            await client.ApplyAsync(new GameActionRequest(GameAction.ConfirmChoice));
        Assert.True((await clients[0].GetGameAsync()).AllChoicesConfirmed);
    }

    /// <summary>Reproduit la page HTML renvoyée par Cloudflare après l'arrêt du tunnel.</summary>
    private sealed class HtmlTunnelErrorHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)530)
        {
            Content = new StringContent("<html>Une très longue page d'erreur du tunnel</html>")
        });
    }
}
