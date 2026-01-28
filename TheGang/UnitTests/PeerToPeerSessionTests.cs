using Model.Networking;

namespace TheGang.Tests;

/// <summary>
/// Tests unitaires pour la gestion de session peer-to-peer.
/// </summary>
public class PeerToPeerSessionTests
{
    [Fact]
    public void CreateHost_ShouldInitializeSessionWithHost()
    {
        var session = PeerToPeerSession.CreateHost("Alice");

        Assert.True(session.IsOpen);
        Assert.Equal(1, session.Peers.Count);
        Assert.True(session.Host.IsHost);
        Assert.Equal("Alice", session.Host.DisplayName);
    }

    [Fact]
    public void TryJoin_ShouldAllowUpToMaximumPeers()
    {
        const int maxPeers = 4;
        var session = PeerToPeerSession.CreateHost("Host", maxPeers);

        for (int index = 0; index < maxPeers - 1; index++)
        {
            bool joined = session.TryJoin($"Player{index}", out var peer, out var errorMessage);

            Assert.True(joined);
            Assert.NotNull(peer);
            Assert.Null(errorMessage);
        }

        bool extraJoin = session.TryJoin("ExtraPlayer", out var extraPeer, out var extraError);

        Assert.False(extraJoin);
        Assert.Null(extraPeer);
        Assert.Equal("Le nombre maximal de joueurs est atteint.", extraError);
    }
    
    [Fact]
    public void TryJoin_ShouldAllowTwoPlayersWhenMaxPeersIsTwo()
    {
        var session = PeerToPeerSession.CreateHost("Host", maxPeers: 2);

        bool joined = session.TryJoin("Guest", out var guest, out var errorMessage);

        Assert.True(joined);
        Assert.NotNull(guest);
        Assert.Null(errorMessage);

        bool extraJoin = session.TryJoin("LateGuest", out var extraGuest, out var extraError);

        Assert.False(extraJoin);
        Assert.Null(extraGuest);
        Assert.Equal("Le nombre maximal de joueurs est atteint.", extraError);
    }

    [Fact]
    public void TryJoin_ShouldRejectDuplicateDisplayNames()
    {
        var session = PeerToPeerSession.CreateHost("Host");

        bool firstJoin = session.TryJoin("Bob", out var bob, out var firstError);
        bool secondJoin = session.TryJoin("bob", out var duplicate, out var secondError);

        Assert.True(firstJoin);
        Assert.NotNull(bob);
        Assert.Null(firstError);

        Assert.False(secondJoin);
        Assert.Null(duplicate);
        Assert.Equal("Ce nom est déjà utilisé dans la session.", secondError);
    }

    [Fact]
    public void TryJoin_ShouldRejectWhenSessionClosed()
    {
        var session = PeerToPeerSession.CreateHost("Host");

        session.Close();

        bool joined = session.TryJoin("LatePlayer", out var peer, out var errorMessage);

        Assert.False(joined);
        Assert.Null(peer);
        Assert.Equal("La session est fermée.", errorMessage);
    }

    [Fact]
    public void TryLeave_ShouldPreventHostFromLeaving()
    {
        var session = PeerToPeerSession.CreateHost("Host");

        bool left = session.TryLeave(session.Host.Id, out var errorMessage);

        Assert.False(left);
        Assert.Equal("L'hôte ne peut pas quitter sans fermer la session.", errorMessage);
    }
}