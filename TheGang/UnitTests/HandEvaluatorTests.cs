using Model.Game.Card;
using Model.Game.Card.CardEnum;
using Model.Game.Cards;
using Model.Game.Hand;

namespace UnitTests;

/// <summary>
/// Tests unitaires pour l'évaluation des mains.
/// </summary>
public class HandEvaluatorTests
{
    [Fact]
    public void Evaluate_ShouldDetectStraightFlush()
    {
        // Arrange
        var hand = new[]
        {
            new Card(CardColors.Coeur, 10),
            new Card(CardColors.Coeur, 11)
        };

        var community = new[]
        {
            new Card(CardColors.Coeur, 12),
            new Card(CardColors.Coeur, 13),
            new Card(CardColors.Coeur, 1),
            new Card(CardColors.Pique, 2),
            new Card(CardColors.Trefle, 3)
        };

        // Act
        var score = PokerHandEvaluator.Evaluate(hand, community);

        // Assert
        Assert.Equal(HandRank.StraightFlush, score.Rank);
    }

    [Fact]
    public void Evaluate_ShouldDetectFullHouse()
    {
        // Arrange
        var hand = new[]
        {
            new Card(CardColors.Carreau, 4),
            new Card(CardColors.Pique, 4)
        };

        var community = new[]
        {
            new Card(CardColors.Coeur, 4),
            new Card(CardColors.Trefle, 9),
            new Card(CardColors.Carreau, 9),
            new Card(CardColors.Pique, 2),
            new Card(CardColors.Coeur, 7)
        };

        // Act
        var score = PokerHandEvaluator.Evaluate(hand, community);

        // Assert
        Assert.Equal(HandRank.FullHouse, score.Rank);
    }

    [Fact]
    public void Evaluate_ShouldDetectTwoPair()
    {
        // Arrange
        var hand = new[]
        {
            new Card(CardColors.Carreau, 8),
            new Card(CardColors.Pique, 12)
        };

        var community = new[]
        {
            new Card(CardColors.Coeur, 8),
            new Card(CardColors.Trefle, 12),
            new Card(CardColors.Carreau, 3),
            new Card(CardColors.Pique, 5),
            new Card(CardColors.Coeur, 6)
        };

        // Act
        var score = PokerHandEvaluator.Evaluate(hand, community);

        // Assert
        Assert.Equal(HandRank.TwoPair, score.Rank);
        Assert.Equal(new[] { 12, 8, 6 }, score.Tiebreakers);
    }
}
