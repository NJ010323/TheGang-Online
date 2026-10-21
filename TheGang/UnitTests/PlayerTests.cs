using Model.Game;
using Model.Game.Cards.Interfaces;
using Model.Game.Tokens.TokenEnum;

namespace UnitTests;

public class PlayerTests
{
    [Fact]
    public void GameExposesReadOnlyPlayerHandsAndCommunityCards()
    {
        var game = new Game(new Random(7));
        game.AddPlayer("Alice");
        game.AddPlayer("Bob");
        game.AddPlayer("Chloe");
        game.StartGame();

        Assert.Throws<NotSupportedException>(() =>
            ((IList<ICard>)game.Players[0].Hand).Clear());
        Assert.Throws<NotSupportedException>(() =>
            ((IList<ICard>)game.CommunityCards).Clear());
        Assert.Throws<NotSupportedException>(() =>
            ((IList<Model.Players.Player>)game.Players).Clear());
    }

    [Fact]
    public void PreviousRoundSignalsRemainAvailable()
    {
        var game = new Game(new Random(7));
        game.AddPlayer("Alice");
        game.AddPlayer("Bob");
        game.AddPlayer("Chloe");
        game.StartGame();
        foreach (var (player, index) in game.Players.Select((player, index) => (player, index)))
            game.TakeToken(player.Id, index + 1);
        foreach (var player in game.Players)
            game.ConfirmChoice(player.Id);

        game.RevealFlop();

        Assert.All(game.Players, player =>
        {
            Assert.True(player.Signals.ContainsKey(TokenColors.White));
            Assert.Null(player.SignalToken);
        });
    }
}
