using Model.Game;
using Model.Lobby;

namespace UnitTests;

public class LobbyTests
{
    [Fact]
    public void HostCreatesLobbyWithCodeAndConfigurableCapacity()
    {
        var manager = new LobbyManager();

        var lobby = manager.CreateLobby(" Alice ", 4);

        Assert.Matches("^[A-HJ-NP-Z2-9]{6}$", lobby.Code);
        Assert.Equal(4, lobby.MaxPlayers);
        Assert.Equal(LobbyStatus.Open, lobby.Status);
        Assert.Single(lobby.Players);
        Assert.Equal("Alice", lobby.Players[0].Name);
        Assert.Equal(lobby.Players[0].Id, lobby.HostId);
        Assert.Same(lobby, manager.GetLobby($" {lobby.Code.ToLowerInvariant()} "));
        Assert.Throws<ArgumentOutOfRangeException>(() => manager.CreateLobby("Host", 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => manager.CreateLobby("Host", 7));
        Assert.Throws<ArgumentException>(() => manager.CreateLobby(" ", 3));
        Assert.Throws<ArgumentException>(() => manager.CreateLobby(new string('A', 25), 3));
    }

    [Fact]
    public void JoinRespectsCapacityAndLeavingFreesAPlace()
    {
        var manager = new LobbyManager();
        var lobby = manager.CreateLobby("Host", 3);
        var firstGuest = manager.JoinLobby(lobby.Code, "Bob");
        manager.JoinLobby(lobby.Code, "Chloe");

        Assert.Equal(3, lobby.Players.Count);
        Assert.Throws<InvalidOperationException>(() => manager.JoinLobby(lobby.Code, "Dana"));
        Assert.Throws<InvalidOperationException>(() => manager.LeaveLobby(lobby.Code, lobby.HostId));

        manager.LeaveLobby(lobby.Code, firstGuest.Id);
        Assert.DoesNotContain(lobby.Players, player => player.Id == firstGuest.Id);
        Assert.Throws<InvalidOperationException>(() => manager.JoinLobby(lobby.Code, "chloe"));
        Assert.Throws<ArgumentException>(() => manager.JoinLobby(lobby.Code, "Bad\nName"));
        Assert.Equal("Dana", manager.JoinLobby(lobby.Code, "Dana").Name);
    }

    [Fact]
    public void OnlyHostCanStartAfterThreePlayersHaveJoined()
    {
        var manager = new LobbyManager();
        var lobby = manager.CreateLobby("Alice", 6);
        var guest = manager.JoinLobby(lobby.Code, "Bob");

        Assert.Throws<InvalidOperationException>(() => manager.StartGame(lobby.Code, guest.Id));
        Assert.Throws<InvalidOperationException>(() => manager.StartGame(lobby.Code, lobby.HostId));
        manager.JoinLobby(lobby.Code, "Chloe");

        var game = manager.StartGame(lobby.Code, lobby.HostId);

        Assert.Equal(LobbyStatus.Started, lobby.Status);
        Assert.Equal(GamePhase.PreFlop, game.Phase);
        Assert.Equal(lobby.Players.Select(player => player.Id), game.Players.Select(player => player.Id));
        Assert.Throws<InvalidOperationException>(() => manager.JoinLobby(lobby.Code, "Dana"));
        Assert.Throws<InvalidOperationException>(() => manager.LeaveLobby(lobby.Code, guest.Id));
        Assert.Throws<InvalidOperationException>(() => manager.StartGame(lobby.Code, lobby.HostId));
    }

    [Fact]
    public void HostCanCloseLobbyAndReleaseItsCode()
    {
        var manager = new LobbyManager();
        var lobby = manager.CreateLobby("Alice", 3);
        var guest = manager.JoinLobby(lobby.Code, "Bob");

        Assert.Throws<InvalidOperationException>(() => manager.CloseLobby(lobby.Code, guest.Id));
        manager.CloseLobby(lobby.Code, lobby.HostId);

        Assert.Equal(LobbyStatus.Closed, lobby.Status);
        Assert.Throws<KeyNotFoundException>(() => manager.GetLobby(lobby.Code));
        Assert.Throws<KeyNotFoundException>(() => manager.JoinLobby(lobby.Code, "Chloe"));
    }
}
