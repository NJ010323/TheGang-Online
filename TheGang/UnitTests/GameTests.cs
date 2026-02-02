using Model.Game;
using Model.Game.Card;
using Model.Game.Card.CardEnum;

namespace UnitTests;

/// <summary>
/// Tests unitaires pour la logique principale du jeu.
/// </summary>
public class GameTests
{
    [Fact]
    public void StartGame_ShouldDealCardsAndSetPhase()
    {
        // Arrange
        var game = new Game();
        game.AddPlayer("Alice");
        game.AddPlayer("Bob");
        game.AddPlayer("Chloe");

        // Act
        game.StartGame();

        // Assert
        Assert.Equal(GamePhase.PreFlop, game.Phase);
        Assert.All(game.Players, player => Assert.Equal(2, player.Hand.Count));
        Assert.Empty(game.CommunityCards);
    }

    [Fact]
    public void RevealSequence_ShouldAddCommunityCards()
    {
        // Arrange
        var game = new Game();
        game.AddPlayer("Alice");
        game.AddPlayer("Bob");
        game.AddPlayer("Chloe");
        game.StartGame();

        // Act
        game.RevealFlop();
        game.RevealTurn();
        game.RevealRiver();

        // Assert
        Assert.Equal(5, game.CommunityCards.Count);
        Assert.Equal(GamePhase.River, game.Phase);
    }

    [Fact]
    public void GetRanking_ShouldReturnPlayersOrderedByScore()
    {
        // Arrange
        var game = new Game();
        game.AddPlayer("Alice");
        game.AddPlayer("Bob");
        game.AddPlayer("Chloe");
        game.StartGame();
        game.RevealFlop();
        game.RevealTurn();
        game.RevealRiver();

        // Forcer les mains et les cartes communes
        game.Players[0].ResetHand();
        game.Players[1].ResetHand();

        game.Players[0].ReceiveCard(new Card(CardColors.Coeur, 1));
        game.Players[0].ReceiveCard(new Card(CardColors.Coeur, 13));

        game.Players[1].ReceiveCard(new Card(CardColors.Pique, 2));
        game.Players[1].ReceiveCard(new Card(CardColors.Trefle, 2));

        game.CommunityCards.Clear();
        game.CommunityCards.AddRange(new[]
        {
            new Card(CardColors.Coeur, 10),
            new Card(CardColors.Coeur, 11),
            new Card(CardColors.Coeur, 12),
            new Card(CardColors.Carreau, 3),
            new Card(CardColors.Pique, 4)
        });

        game.GoToShowdown();

        // Act
        var ranking = game.GetRanking();

        // Assert
        Assert.Equal("Alice", ranking[0].Name);
    }
}
