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
        Assert.False(LobbyConnectionFailure.IsSessionClosed(error));
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

        var error = await Assert.ThrowsAsync<LobbySessionClosedException>(() => guest.GetLobbyAsync());
        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
        Assert.True(LobbyConnectionFailure.IsSessionClosed(error));
        Assert.False(LobbyConnectionFailure.IsHostUnavailable(error));
    }

    [Fact]
    public async Task GuestLeavingPausesTheGameAndCanResumeTheSameHand()
    {
        await using var server = await LobbyHttpServer.StartAsync("Host", 3);
        using var host = new LobbyHttpClient(server.LocalAddress);
        using var bob = new LobbyHttpClient(server.LocalAddress);
        using var chloe = new LobbyHttpClient(server.LocalAddress);
        host.SetHostIdentity(server.Lobby.HostIdentity);
        await bob.JoinAsync("Bob", server.Lobby.InvitationSecret);
        await chloe.JoinAsync("Chloe", server.Lobby.InvitationSecret);
        await host.StartAsync();
        var identity = bob.Identity!;
        var originalHand = (await bob.GetGameAsync()).Players.Single(player => player.Id == identity.PlayerId).Hand;

        await bob.LeaveAsync();

        var paused = await host.GetLobbyAsync();
        var absent = Assert.Single(paused.AbsentPlayers);
        Assert.Equal("Bob", absent.Name);
        Assert.Equal(identity.PlayerId, absent.PlayerId);
        Assert.InRange(absent.ReconnectUntilUtc - DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(110), TimeSpan.FromMinutes(2));
        var blocked = await Assert.ThrowsAsync<HttpRequestException>(() =>
            host.ApplyAsync(new GameActionRequest(GameAction.TakeToken, 1)));
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        using var resumed = new LobbyHttpClient(server.LocalAddress);
        await resumed.ResumeAsync(identity.Code, identity.PlayerId, identity.SessionToken);
        var resumedHand = (await resumed.GetGameAsync()).Players.Single(player => player.Id == identity.PlayerId).Hand;
        Assert.Equal(originalHand, resumedHand);
        Assert.Empty((await host.GetLobbyAsync()).AbsentPlayers);
        await host.ApplyAsync(new GameActionRequest(GameAction.TakeToken, 1));

        using var wrongIdentity = new LobbyHttpClient(server.LocalAddress);
        await Assert.ThrowsAsync<LobbySessionClosedException>(() => wrongIdentity.ResumeAsync(
            identity.Code, chloe.Identity!.PlayerId, identity.SessionToken));
    }

    [Fact]
    public void SilentGuestIsDetectedAndGracePeriodEventuallyClosesTheLobby()
    {
        var clock = new AdjustableClock(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));
        var lobby = new HostedLobby("Host", 3, clock);
        var bob = lobby.Join("Bob", lobby.InvitationSecret);
        lobby.Join("Chloe", lobby.InvitationSecret);
        lobby.Start(lobby.Code, lobby.HostIdentity.SessionToken);

        clock.Advance(TimeSpan.FromSeconds(9));
        var paused = lobby.GetLobby(lobby.Code, lobby.HostIdentity.SessionToken);
        Assert.Equal(2, paused.AbsentPlayers.Count);
        Assert.All(paused.AbsentPlayers, player => Assert.Equal(
            new DateTimeOffset(2026, 10, 23, 12, 2, 8, TimeSpan.Zero), player.ReconnectUntilUtc));
        Assert.Throws<InvalidOperationException>(() => lobby.Apply(lobby.Code,
            lobby.HostIdentity.SessionToken, new GameActionRequest(GameAction.TakeToken, 1)));

        clock.Advance(TimeSpan.FromSeconds(110));
        lobby.GetLobby(lobby.Code, bob.SessionToken);
        Assert.Single(lobby.GetLobby(lobby.Code, lobby.HostIdentity.SessionToken).AbsentPlayers);
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Throws<KeyNotFoundException>(() => lobby.GetLobby(lobby.Code,
            lobby.HostIdentity.SessionToken));
    }

    [Fact]
    public async Task TemporaryGatewayFailurePreservesTheGuestIdentityAndReturnsToTheSameGame()
    {
        await using var server = await LobbyHttpServer.StartAsync("Host", 3);
        using var host = new LobbyHttpClient(server.LocalAddress);
        using var gateway = new IntermittentGatewayHandler();
        using var guest = new LobbyHttpClient(server.LocalAddress, gateway);
        host.SetHostIdentity(server.Lobby.HostIdentity);
        await guest.JoinAsync("Guest", server.Lobby.InvitationSecret);
        var identity = guest.Identity;

        gateway.FailNextRequests = 2;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var error = await Assert.ThrowsAsync<HttpRequestException>(() => guest.GetLobbyAsync());
            Assert.True(LobbyConnectionFailure.IsHostUnavailable(error));
            Assert.False(LobbyConnectionFailure.IsSessionClosed(error));
        }

        Assert.Equal(identity, guest.Identity);
        Assert.Contains((await guest.GetLobbyAsync()).Players, player => player.Id == identity!.PlayerId);
        Assert.Equal(identity, guest.Identity);
    }

    [Fact]
    public async Task GatewayNotFoundDoesNotProveThatTheHostClosedTheLobby()
    {
        await using var server = await LobbyHttpServer.StartAsync("Host", 3);
        using var gateway = new IntermittentGatewayHandler { FailureStatus = HttpStatusCode.NotFound };
        using var guest = new LobbyHttpClient(server.LocalAddress, gateway);
        await guest.JoinAsync("Guest", server.Lobby.InvitationSecret);
        gateway.FailNextRequests = 1;

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => guest.GetLobbyAsync());
        Assert.True(LobbyConnectionFailure.IsHostUnavailable(error));
        Assert.False(LobbyConnectionFailure.IsSessionClosed(error));
        Assert.Equal(server.Lobby.Code, (await guest.GetLobbyAsync()).Code);
    }

    [Fact]
    public void ReconnectionWindowExpiresOnlyAfterContinuousFailure()
    {
        var window = new LobbyReconnectWindow(TimeSpan.FromSeconds(30));
        var start = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
        window.Start(start);
        window.Start(start.AddSeconds(20));

        Assert.True(window.IsActive);
        Assert.False(window.IsExpired(start.AddSeconds(29)));
        Assert.True(window.IsExpired(start.AddSeconds(30)));
        window.Reset();
        Assert.False(window.IsActive);
        window.Start(start.AddSeconds(45));
        Assert.False(window.IsExpired(start.AddSeconds(45)));
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
        var deniedStart = await Assert.ThrowsAsync<HttpRequestException>(() => bob.StartAsync());
        Assert.Equal(HttpStatusCode.Forbidden, deniedStart.StatusCode);
        Assert.False(LobbyConnectionFailure.IsHostUnavailable(deniedStart));
        using var extra = new LobbyHttpClient(server.LocalAddress);
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            extra.JoinAsync("Other", server.Lobby.InvitationSecret));

        var hostGame = await host.StartAsync();
        var bobGame = await bob.GetGameAsync();
        Assert.Equal(GamePhase.PreFlop, hostGame.Phase);
        Assert.Equal(2, hostGame.Players.Single(p => p.Id == host.Identity!.PlayerId).Hand.Count);
        Assert.Empty(bobGame.Players.Single(p => p.Id == host.Identity!.PlayerId).Hand);
        Assert.Empty(bobGame.RevealedHands);
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
        Assert.Equal(GameEventKind.RoundRevealed, afterFlop.RecentEvents[^1].Kind);
        Assert.Equal(GamePhase.Flop, afterFlop.RecentEvents[^1].Phase);
        Assert.Equal(afterFlop.RecentEvents[^1], (await bob.GetGameAsync()).RecentEvents[^1]);

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
        for (var index = 0; index < order.Length; index++)
        {
            var expectedPlayer = order[index];
            var before = await bob.GetGameAsync();
            Assert.Equal(expectedPlayer, before.NextPlayerToRevealId);
            Assert.Equal(index, before.RevealedHands.Count);
            Assert.DoesNotContain(before.RevealedHands, hand => hand.PlayerId == expectedPlayer);
            var after = await host.ApplyAsync(new GameActionRequest(GameAction.RevealNextHand));
            Assert.Equal(2, after.Players.Single(p => p.Id == expectedPlayer).Hand.Count);
            Assert.Equal(index + 1, after.RevealedHands.Count);
            Assert.Equal(expectedPlayer, after.RevealedHands[^1].PlayerId);
            Assert.NotEmpty(after.RevealedHands[^1].Tiebreakers);
            var guestHand = (await bob.GetGameAsync()).RevealedHands[^1];
            Assert.Equal(expectedPlayer, guestHand.PlayerId);
            Assert.Equal(after.RevealedHands[^1].Rank, guestHand.Rank);
            Assert.Equal(after.RevealedHands[^1].Tiebreakers, guestHand.Tiebreakers);
        }
        var result = await host.ApplyAsync(new GameActionRequest(GameAction.ResolveHeist));
        Assert.True(result.Phase is GamePhase.HeistComplete or GamePhase.Finished);
        Assert.Equal(3, result.RecentEvents.Count(action => action.Kind == GameEventKind.HandRevealed));
        Assert.Equal(GameEventKind.HeistResolved, result.RecentEvents[^1].Kind);
        Assert.Equal(result.LastHeistSucceeded, result.RecentEvents[^1].HeistSucceeded);
        // Chaque écran reçoit la même issue pour lancer la même animation de coffre.
        Assert.Equal(result.RecentEvents[^1], (await bob.GetGameAsync()).RecentEvents[^1]);
        Assert.Equal(result.RecentEvents[^1], (await chloe.GetGameAsync()).RecentEvents[^1]);
    }

    [Fact]
    public async Task SimultaneousClaimsOfOnePhysicalTokenAreOrderedForEveryPlayer()
    {
        await using var server = await LobbyHttpServer.StartAsync("Host", 3);
        using var host = new LobbyHttpClient(server.LocalAddress);
        using var bob = new LobbyHttpClient(server.LocalAddress);
        using var chloe = new LobbyHttpClient(server.LocalAddress);
        host.SetHostIdentity(server.Lobby.HostIdentity);
        await bob.JoinAsync("Bob", server.Lobby.InvitationSecret);
        await chloe.JoinAsync("Chloe", server.Lobby.InvitationSecret);
        await host.StartAsync();

        // Les requêtes arrivent dans un ordre indéterminé ; l'hôte les sérialise.
        await Task.WhenAll(new[] { host, bob, chloe }.Select(client =>
            client.ApplyAsync(new GameActionRequest(GameAction.TakeToken, 1))));
        var snapshot = await host.GetGameAsync();
        var movements = snapshot.RecentEvents.Where(action => action.Kind is
            GameEventKind.TokenTaken or GameEventKind.TokenStolen).ToArray();

        Assert.Equal(3, movements.Length);
        Assert.Equal(GameEventKind.TokenTaken, movements[0].Kind);
        Assert.All(movements.Skip(1), action => Assert.Equal(GameEventKind.TokenStolen, action.Kind));
        for (var index = 1; index < movements.Length; index++)
            Assert.Equal(movements[index - 1].ActorId, movements[index].FromPlayerId);
        Assert.Equal(movements.Select(action => action.Sequence).Order(),
            movements.Select(action => action.Sequence));
        Assert.Equal(movements[^1].ActorId, snapshot.Players.Single(player => player.CurrentToken == 1).Id);
        Assert.Equal(1, snapshot.Players.Count(player => player.CurrentToken == 1));
        Assert.False(snapshot.AllPlayersHaveTokens);
    }

    [Fact]
    public async Task LiveDragIsVisibleWithoutTakingTheTokenAndCannotReturnAfterDrop()
    {
        await using var server = await LobbyHttpServer.StartAsync("Host", 3);
        using var host = new LobbyHttpClient(server.LocalAddress);
        using var bob = new LobbyHttpClient(server.LocalAddress);
        using var chloe = new LobbyHttpClient(server.LocalAddress);
        host.SetHostIdentity(server.Lobby.HostIdentity);
        var bobId = (await bob.JoinAsync("Bob", server.Lobby.InvitationSecret)).PlayerId;
        await chloe.JoinAsync("Chloe", server.Lobby.InvitationSecret);
        await host.StartAsync();

        var gestureId = Guid.NewGuid();
        await bob.UpdateDragAsync(new GameDragRequest(gestureId, 2, 1, 530, 520, true));
        var moving = await host.GetGameAsync();
        var drag = Assert.Single(moving.ActiveDrags);
        Assert.Equal(bobId, drag.PlayerId);
        Assert.Equal(2, drag.TokenValue);
        Assert.Equal(530, drag.X);
        Assert.Contains(2, moving.AvailableTokens);
        Assert.All(moving.Players, player => Assert.Null(player.CurrentToken));

        await bob.UpdateDragAsync(new GameDragRequest(gestureId, 2, 2, 600, 580, true));
        await bob.UpdateDragAsync(new GameDragRequest(gestureId, 2, 1, 400, 440, true));
        Assert.Equal(600, Assert.Single((await host.GetGameAsync()).ActiveDrags).X);

        await bob.ApplyAsync(new GameActionRequest(GameAction.TakeToken, 2, gestureId));
        await bob.UpdateDragAsync(new GameDragRequest(gestureId, 2, 3, 700, 650, true));
        var dropped = await host.GetGameAsync();
        Assert.Empty(dropped.ActiveDrags);
        Assert.Equal(2, dropped.Players.Single(player => player.Id == bobId).CurrentToken);

        var cancelledGesture = Guid.NewGuid();
        await bob.UpdateDragAsync(new GameDragRequest(cancelledGesture, 1, 1, 520, 520, true));
        await bob.UpdateDragAsync(new GameDragRequest(cancelledGesture, 1, 2, 520, 520, false));
        await bob.UpdateDragAsync(new GameDragRequest(cancelledGesture, 1, 3, 650, 550, true));
        Assert.Empty((await host.GetGameAsync()).ActiveDrags);
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

    /// <summary>Contrôle le délai de reprise sans attendre réellement deux minutes dans les tests.</summary>
    private sealed class AdjustableClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
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

    /// <summary>Interrompt quelques requêtes puis transmet les suivantes au même serveur local.</summary>
    private sealed class IntermittentGatewayHandler : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _upstream = new(new HttpClientHandler());

        public int FailNextRequests { get; set; }
        public HttpStatusCode FailureStatus { get; set; } = (HttpStatusCode)530;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (FailNextRequests-- > 0)
                return Task.FromResult(new HttpResponseMessage(FailureStatus)
                {
                    Content = new StringContent("<html>Passerelle indisponible</html>")
                });
            return _upstream.SendAsync(request, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _upstream.Dispose();
            base.Dispose(disposing);
        }
    }
}
