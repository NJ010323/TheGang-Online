using System.Net;
using System.Net.Http.Json;
using Model.Game;
using Model.Lobby;
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
    public async Task GuestLeavingWithThreeOthersPresentCanResumeTheSameHand()
    {
        await using var server = await LobbyHttpServer.StartAsync("Host", 4);
        using var host = new LobbyHttpClient(server.LocalAddress);
        using var bob = new LobbyHttpClient(server.LocalAddress);
        using var chloe = new LobbyHttpClient(server.LocalAddress);
        host.SetHostIdentity(server.Lobby.HostIdentity);
        await bob.JoinAsync("Bob", server.Lobby.InvitationSecret);
        await chloe.JoinAsync("Chloe", server.Lobby.InvitationSecret);
        server.Lobby.Join("Dora", server.Lobby.InvitationSecret);
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
    public async Task HostCanIncreaseCapacityAndGuestsSeeTheNewPlace()
    {
        await using var server = await LobbyHttpServer.StartAsync("Alice", 3);
        using var host = new LobbyHttpClient(server.LocalAddress);
        using var bob = new LobbyHttpClient(server.LocalAddress);
        using var chloe = new LobbyHttpClient(server.LocalAddress);
        using var dora = new LobbyHttpClient(server.LocalAddress);
        host.SetHostIdentity(server.Lobby.HostIdentity);
        await bob.JoinAsync("Bob", server.Lobby.InvitationSecret);
        await chloe.JoinAsync("Chloe", server.Lobby.InvitationSecret);
        var full = await Assert.ThrowsAsync<HttpRequestException>(() =>
            dora.JoinAsync("Dora", server.Lobby.InvitationSecret));
        Assert.Equal(HttpStatusCode.Conflict, full.StatusCode);

        var forbidden = await Assert.ThrowsAsync<HttpRequestException>(() => bob.ChangeCapacityAsync(4));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var invalid = await Assert.ThrowsAsync<HttpRequestException>(() => host.ChangeCapacityAsync(7));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(4, (await host.ChangeCapacityAsync(4)).MaxPlayers);
        Assert.Equal(4, (await bob.GetLobbyAsync()).MaxPlayers);
        await dora.JoinAsync("Dora", server.Lobby.InvitationSecret);
        var tooSmall = await Assert.ThrowsAsync<HttpRequestException>(() => host.ChangeCapacityAsync(3));
        Assert.Equal(HttpStatusCode.Conflict, tooSmall.StatusCode);
        Assert.Equal(4, (await host.GetLobbyAsync()).MaxPlayers);

        await host.StartAsync();
        var started = await Assert.ThrowsAsync<HttpRequestException>(() => host.ChangeCapacityAsync(5));
        Assert.Equal(HttpStatusCode.Conflict, started.StatusCode);
        Assert.Equal(4, (await host.BeginHostMigrationAsync()).MaxPlayers);
    }

    [Fact]
    public void CapacityCannotDropBelowPlayersExpectedDuringHostMigration()
    {
        var next = new HostedLobby("Bob", 4);
        next.ConfigureHostMigration(next.Code, next.HostIdentity.SessionToken,
            new HostMigrationSetup(["Bob", "Chloe", "Dora", "Emma"], false, 0, 0, 0, null));
        Assert.Throws<InvalidOperationException>(() =>
            next.ChangeCapacity(next.Code, next.HostIdentity.SessionToken, 3));
        Assert.Equal(4, next.GetLobby(next.Code, next.HostIdentity.SessionToken).MaxPlayers);
    }

    [Fact]
    public async Task ServerConflictDisplaysItsMessageWithoutRawJson()
    {
        using var client = new LobbyHttpClient(new Uri("https://example.trycloudflare.com"),
            new JsonConflictHandler());

        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.JoinAsync("Guest", "0123456789ABCDEF0123456789ABCDEF"));

        Assert.Equal(HttpStatusCode.Conflict, error.StatusCode);
        Assert.Contains("Cette proposition de relève n'est plus valide.", error.Message);
        Assert.DoesNotContain("{\"error\"", error.Message);
    }

    [Fact]
    public async Task FirstGuestCanAcceptHostMigrationAndThreeRemainingPlayersRestartTheHeist()
    {
        await using var oldServer = await LobbyHttpServer.StartAsync("Alice", 4);
        using var oldHost = new LobbyHttpClient(oldServer.LocalAddress);
        using var successor = new LobbyHttpClient(oldServer.LocalAddress);
        using var third = new LobbyHttpClient(oldServer.LocalAddress);
        using var fourth = new LobbyHttpClient(oldServer.LocalAddress);
        oldHost.SetHostIdentity(oldServer.Lobby.HostIdentity);
        await successor.JoinAsync("Bob", oldServer.Lobby.InvitationSecret);
        await third.JoinAsync("Chloe", oldServer.Lobby.InvitationSecret);
        await fourth.JoinAsync("Dora", oldServer.Lobby.InvitationSecret);
        await oldHost.StartAsync();

        var migration = await oldHost.BeginHostMigrationAsync();
        Assert.Equal(successor.Identity!.PlayerId, migration.SuccessorId);
        Assert.Equal(new[] { "Bob", "Chloe", "Dora" }, migration.ExpectedPlayers);
        Assert.Equal(1, migration.HeistNumber);
        Assert.Equal(HostMigrationStage.Offered, migration.Stage);
        var frozen = await Assert.ThrowsAsync<HttpRequestException>(() =>
            third.ApplyAsync(new GameActionRequest(GameAction.TakeToken, 1)));
        Assert.Equal(HttpStatusCode.Conflict, frozen.StatusCode);
        var forbidden = await Assert.ThrowsAsync<HttpRequestException>(() =>
            third.DecideHostMigrationAsync(new HostMigrationDecision(migration.Id, true)));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        await successor.DecideHostMigrationAsync(new HostMigrationDecision(migration.Id, true));
        await using var newServer = await LobbyHttpServer.StartAsync("Bob", 4);
        using var newHost = new LobbyHttpClient(newServer.LocalAddress);
        using var migratedThird = new LobbyHttpClient(newServer.LocalAddress);
        using var migratedFourth = new LobbyHttpClient(newServer.LocalAddress);
        newHost.SetHostIdentity(newServer.Lobby.HostIdentity);
        await newHost.ConfigureHostMigrationAsync(new HostMigrationSetup(migration.ExpectedPlayers,
            true, migration.HeistNumber, migration.SuccessfulHeists, migration.FailedHeists, null));
        var fakeInvitation = InviteCode.Create(newServer.Lobby.Code,
            new Uri("https://fresh-table.trycloudflare.com"), newServer.Lobby.InvitationSecret);
        await successor.PublishHostMigrationAsync(new HostMigrationDestination(migration.Id, fakeInvitation));
        Assert.Equal(fakeInvitation, (await third.GetLobbyAsync()).HostMigration!.Invitation);
        await migratedThird.JoinAsync("Chloe", newServer.Lobby.InvitationSecret);
        await migratedFourth.JoinAsync("Dora", newServer.Lobby.InvitationSecret);
        var continued = await newHost.GetGameAsync();
        Assert.Equal(GamePhase.PreFlop, continued.Phase);
        Assert.Equal(3, continued.Players.Count);
        Assert.Equal(3, continued.AvailableTokens.Count);
        Assert.Equal(1, continued.HeistNumber);
        Assert.DoesNotContain(continued.Players, player => player.Name == "Alice");
        Assert.Equal(2, continued.Players.Single(player => player.Id == newHost.Identity!.PlayerId).Hand.Count);
        await PlayRoundAsync(newHost, migratedThird, migratedFourth);

        await successor.AcknowledgeHostMigrationAsync(migration.Id);
        await third.AcknowledgeHostMigrationAsync(migration.Id);
        await fourth.AcknowledgeHostMigrationAsync(migration.Id);
        var acknowledged = await oldHost.AcknowledgeHostMigrationAsync(migration.Id);
        Assert.Equal(4, acknowledged.AcknowledgedPlayers);
        await oldHost.LeaveAsync();
        Assert.Equal(GamePhase.PreFlop, (await newHost.GetGameAsync()).Phase);
    }

    [Fact]
    public void FormerHostLeavingAnOpenTransferredLobbyIsNotExpectedToReturn()
    {
        var clock = new AdjustableClock(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));
        var old = new HostedLobby("Alice", 3, clock);
        var bob = old.Join("Bob", old.InvitationSecret);
        var offer = old.BeginHostMigration(old.Code, old.HostIdentity.SessionToken);
        Assert.Equal(bob.PlayerId, offer.SuccessorId);
        old.DecideHostMigration(old.Code, bob.SessionToken, new HostMigrationDecision(offer.Id, true));

        var next = new HostedLobby("Bob", 3, clock);
        next.ConfigureHostMigration(next.Code, next.HostIdentity.SessionToken,
            new HostMigrationSetup(["Bob", "Alice"], false, 0, 0, 0, "Alice"));
        var returningHost = next.Join("Alice", next.InvitationSecret);
        next.Leave(next.Code, returningHost.SessionToken);
        var paused = next.GetLobby(next.Code, next.HostIdentity.SessionToken);
        Assert.Equal(LobbyStatus.Open, paused.Status);
        Assert.Equal("Bob", Assert.Single(paused.Players).Name);
        Assert.Empty(paused.AbsentPlayers);
        Assert.Null(paused.ExpectedTransferredPlayers);
        Assert.False(paused.AutoResumeTransferredGame);
        Assert.Throws<UnauthorizedAccessException>(() => next.Resume(next.Code, returningHost.SessionToken));
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(LobbyStatus.Open, next.GetLobby(next.Code, next.HostIdentity.SessionToken).Status);
        next.Join("Chloe", next.InvitationSecret);
        next.Join("Dora", next.InvitationSecret);
        Assert.Equal(3, next.Start(next.Code, next.HostIdentity.SessionToken).Players.Count);
    }

    [Fact]
    public void TransferredPairWaitsInLobbyAndCanInviteAThirdPlayer()
    {
        var next = new HostedLobby("Bob", 3);
        next.ConfigureHostMigration(next.Code, next.HostIdentity.SessionToken,
            new HostMigrationSetup(["Bob", "Chloe"], true, 2, 1, 0, null));
        var chloe = next.Join("Chloe", next.InvitationSecret);
        foreach (var viewer in new[] { next.HostIdentity, chloe })
        {
            var lobby = next.GetLobby(next.Code, viewer.SessionToken);
            Assert.Equal(LobbyStatus.Open, lobby.Status);
            Assert.Equal(2, lobby.Players.Count);
            Assert.False(lobby.AutoResumeTransferredGame);
            Assert.Throws<InvalidOperationException>(() => next.GetGame(next.Code, viewer.SessionToken));
        }
        Assert.Throws<InvalidOperationException>(() => next.Start(next.Code, next.HostIdentity.SessionToken));
        next.Join("Dora", next.InvitationSecret);
        Assert.Equal(LobbyStatus.Open, next.GetLobby(next.Code, chloe.SessionToken).Status);
        var game = next.Start(next.Code, next.HostIdentity.SessionToken);
        Assert.Equal(1, game.HeistNumber);
        Assert.Equal(0, game.SuccessfulHeists);
        Assert.Equal(0, game.FailedHeists);
        Assert.Equal(3, game.AvailableTokens.Count);
        Assert.Equal(GamePhase.PreFlop, game.Phase);
    }

    [Fact]
    public async Task CompletedHeistScoreSurvivesMigrationWhileCurrentHeistIsRedealt()
    {
        await using var oldServer = await LobbyHttpServer.StartAsync("Alice", 4);
        using var alice = new LobbyHttpClient(oldServer.LocalAddress);
        using var bob = new LobbyHttpClient(oldServer.LocalAddress);
        using var chloe = new LobbyHttpClient(oldServer.LocalAddress);
        using var dora = new LobbyHttpClient(oldServer.LocalAddress);
        alice.SetHostIdentity(oldServer.Lobby.HostIdentity);
        await bob.JoinAsync("Bob", oldServer.Lobby.InvitationSecret);
        await chloe.JoinAsync("Chloe", oldServer.Lobby.InvitationSecret);
        await dora.JoinAsync("Dora", oldServer.Lobby.InvitationSecret);
        await alice.StartAsync();
        for (var round = 0; round < 4; round++)
        {
            await PlayRoundAsync(alice, bob, chloe, dora);
            await alice.ApplyAsync(new GameActionRequest(GameAction.AdvanceRound));
        }
        for (var player = 0; player < 4; player++)
            await alice.ApplyAsync(new GameActionRequest(GameAction.RevealNextHand));
        var resolved = await alice.ApplyAsync(new GameActionRequest(GameAction.ResolveHeist));
        Assert.Equal(GamePhase.HeistComplete, resolved.Phase);

        var offer = await alice.BeginHostMigrationAsync();
        Assert.Equal(2, offer.HeistNumber);
        Assert.Equal(1, offer.SuccessfulHeists + offer.FailedHeists);
        var next = new HostedLobby("Bob", 4);
        next.ConfigureHostMigration(next.Code, next.HostIdentity.SessionToken,
            new HostMigrationSetup(offer.ExpectedPlayers, offer.WasStarted, offer.HeistNumber,
                offer.SuccessfulHeists, offer.FailedHeists, null));
        next.Join("Chloe", next.InvitationSecret);
        next.Join("Dora", next.InvitationSecret);
        var continued = next.GetGame(next.Code, next.HostIdentity.SessionToken);
        Assert.Equal(offer.HeistNumber, continued.HeistNumber);
        Assert.Equal(offer.SuccessfulHeists, continued.SuccessfulHeists);
        Assert.Equal(offer.FailedHeists, continued.FailedHeists);
        Assert.Equal(3, continued.Players.Count);
        Assert.Empty(continued.CommunityCards);
        Assert.All(continued.Players, player => Assert.Empty(player.Signals));
    }

    [Fact]
    public void MigrationRefusalAndExpiryRestoreTheOriginalTable()
    {
        var clock = new AdjustableClock(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));
        var lobby = new HostedLobby("Alice", 3, clock);
        var bob = lobby.Join("Bob", lobby.InvitationSecret);
        lobby.Join("Chloe", lobby.InvitationSecret);
        var offer = lobby.BeginHostMigration(lobby.Code, lobby.HostIdentity.SessionToken);
        Assert.Throws<InvalidOperationException>(() =>
            lobby.Start(lobby.Code, lobby.HostIdentity.SessionToken));
        lobby.DecideHostMigration(lobby.Code, bob.SessionToken,
            new HostMigrationDecision(offer.Id, false));
        var second = lobby.BeginHostMigration(lobby.Code, lobby.HostIdentity.SessionToken);
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(HostMigrationStage.Expired,
            lobby.GetLobby(lobby.Code, lobby.HostIdentity.SessionToken).HostMigration!.Stage);
        Assert.Throws<InvalidOperationException>(() => lobby.DecideHostMigration(lobby.Code,
            bob.SessionToken, new HostMigrationDecision(second.Id, true)));
        Assert.Equal(GamePhase.PreFlop,
            lobby.Start(lobby.Code, lobby.HostIdentity.SessionToken).Phase);
    }

    [Fact]
    public void PreparingMigrationSurvivesMissedHeartbeatsAndGetsItsOwnDeadline()
    {
        var clock = new AdjustableClock(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));
        var lobby = new HostedLobby("Alice", 4, clock);
        var bob = lobby.Join("Bob", lobby.InvitationSecret);
        var chloe = lobby.Join("Chloe", lobby.InvitationSecret);
        var dora = lobby.Join("Dora", lobby.InvitationSecret);
        lobby.Start(lobby.Code, lobby.HostIdentity.SessionToken);

        var offer = lobby.BeginHostMigration(lobby.Code, lobby.HostIdentity.SessionToken);
        for (var index = 0; index < 18; index++)
        {
            clock.Advance(TimeSpan.FromSeconds(5));
            lobby.GetLobby(lobby.Code, bob.SessionToken);
            lobby.GetLobby(lobby.Code, chloe.SessionToken);
            lobby.GetLobby(lobby.Code, dora.SessionToken);
        }
        var pending = lobby.GetLobby(lobby.Code, lobby.HostIdentity.SessionToken);
        Assert.Equal(HostMigrationStage.Offered, pending.HostMigration!.Stage);
        Assert.Empty(pending.AbsentPlayers);

        var preparing = lobby.DecideHostMigration(lobby.Code, bob.SessionToken,
            new HostMigrationDecision(offer.Id, true));
        Assert.Equal(clock.GetUtcNow().AddMinutes(2), preparing.ExpiresAtUtc);
        for (var index = 0; index < 7; index++)
        {
            clock.Advance(TimeSpan.FromSeconds(5));
            lobby.GetLobby(lobby.Code, lobby.HostIdentity.SessionToken);
            lobby.GetLobby(lobby.Code, chloe.SessionToken);
            lobby.GetLobby(lobby.Code, dora.SessionToken);
        }
        pending = lobby.GetLobby(lobby.Code, lobby.HostIdentity.SessionToken);
        Assert.Equal(HostMigrationStage.Preparing, pending.HostMigration!.Stage);
        Assert.Empty(pending.AbsentPlayers);

        var invitation = InviteCode.Create("ABC234",
            new Uri("https://fresh-table.trycloudflare.com"), lobby.InvitationSecret);
        var published = lobby.PublishHostMigration(lobby.Code, bob.SessionToken,
            new HostMigrationDestination(offer.Id, invitation));
        Assert.Equal(HostMigrationStage.Redirecting, published.Stage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GuestDisconnectBeforePublicationCancelsMigrationWithoutLosingTheirResumePlace(
        bool accepted)
    {
        var old = new HostedLobby("Alice", 4);
        var bob = old.Join("Bob", old.InvitationSecret);
        old.Join("Chloe", old.InvitationSecret);
        old.Join("Dora", old.InvitationSecret);
        old.Start(old.Code, old.HostIdentity.SessionToken);
        var offer = old.BeginHostMigration(old.Code, old.HostIdentity.SessionToken);
        if (accepted)
            old.DecideHostMigration(old.Code, bob.SessionToken,
                new HostMigrationDecision(offer.Id, true));
        old.Leave(old.Code, bob.SessionToken);
        Assert.Equal(HostMigrationStage.Expired,
            old.GetLobby(old.Code, old.HostIdentity.SessionToken).HostMigration!.Stage);
        Assert.Single(old.GetLobby(old.Code, old.HostIdentity.SessionToken).AbsentPlayers);
        old.Resume(old.Code, bob.SessionToken);
        var retry = old.BeginHostMigration(old.Code, old.HostIdentity.SessionToken);
        Assert.NotEqual(offer.Id, retry.Id);
    }

    [Fact]
    public void TransferredLobbyRejectsUnknownPlayersAndPausesForReturningFormerHost()
    {
        var clock = new AdjustableClock(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));
        var next = new HostedLobby("Bob", 4, clock);
        next.ConfigureHostMigration(next.Code, next.HostIdentity.SessionToken,
            new HostMigrationSetup(["Bob", "Alice", "Chloe", "Dora"], true, 1, 0, 0, "Alice"));
        Assert.Throws<UnauthorizedAccessException>(() => next.Join("Mallory", next.InvitationSecret));
        var alice = next.Join("Alice", next.InvitationSecret);
        next.Join("Chloe", next.InvitationSecret);
        next.Join("Dora", next.InvitationSecret);
        var oldHand = next.GetGame(next.Code, alice.SessionToken).Players
            .Single(player => player.Id == alice.PlayerId).Hand;
        next.Leave(next.Code, alice.SessionToken);
        Assert.Single(next.GetLobby(next.Code, next.HostIdentity.SessionToken).AbsentPlayers);
        Assert.Throws<InvalidOperationException>(() => next.Apply(next.Code,
            next.HostIdentity.SessionToken, new GameActionRequest(GameAction.TakeToken, 1)));
        next.Resume(next.Code, alice.SessionToken);
        var resumedHand = next.GetGame(next.Code, alice.SessionToken).Players
            .Single(player => player.Id == alice.PlayerId).Hand;
        Assert.Equal(oldHand, resumedHand);
    }

    [Fact]
    public void PregameTransferredLobbyCanInviteNewPlayersAfterOriginalGuestsArrive()
    {
        var next = new HostedLobby("Bob", 4);
        next.ConfigureHostMigration(next.Code, next.HostIdentity.SessionToken,
            new HostMigrationSetup(["Bob", "Chloe"], false, 0, 0, 0, null));
        Assert.Throws<UnauthorizedAccessException>(() => next.Join("Dora", next.InvitationSecret));
        next.Join("Chloe", next.InvitationSecret);
        next.Join("Dora", next.InvitationSecret);
        Assert.Equal(GamePhase.PreFlop,
            next.Start(next.Code, next.HostIdentity.SessionToken).Phase);
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
    public async Task TokenReturnAndReplacementAreRejectedAndGuestsCanLeave()
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
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            host.ApplyAsync(new GameActionRequest(GameAction.TakeToken, 2)));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            bob.ApplyAsync(new GameActionRequest(GameAction.ReturnToken)));
        var snapshot = await host.GetGameAsync();
        Assert.True(snapshot.AllPlayersHaveTokens);
        Assert.True(snapshot.Players.Single(player => player.Name == "Bob").IsChoiceConfirmed);

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

    /// <summary>Reproduit une erreur métier JSON pour vérifier le texte montré aux joueurs.</summary>
    private sealed class JsonConflictHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent("{\"error\":\"Cette proposition de relève n'est plus valide.\"}",
                System.Text.Encoding.UTF8, "application/json")
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
