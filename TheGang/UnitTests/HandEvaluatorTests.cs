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
    public void Evaluer_DevraitReconnaîtreLaQuinteBasseAvecAs()
    {
        var score = PokerHandEvaluator.Evaluate(
            new[] { new Card(CardColors.Coeur, 1), new Card(CardColors.Pique, 2) },
            new[]
            {
                new Card(CardColors.Carreau, 3), new Card(CardColors.Trefle, 4),
                new Card(CardColors.Coeur, 5), new Card(CardColors.Pique, 9),
                new Card(CardColors.Carreau, 13)
            });

        Assert.Equal(HandRank.Straight, score.Rank);
        Assert.Equal(new[] { 5 }, score.Tiebreakers);
    }

    [Fact]
    public void Evaluer_DevraitComparerLesKickersEtReconnaîtreUneEgalite()
    {
        var community = new[]
        {
            new Card(CardColors.Coeur, 1), new Card(CardColors.Pique, 1),
            new Card(CardColors.Carreau, 13), new Card(CardColors.Trefle, 12),
            new Card(CardColors.Coeur, 3)
        };
        var stronger = PokerHandEvaluator.Evaluate(
            new[] { new Card(CardColors.Pique, 11), new Card(CardColors.Carreau, 2) }, community);
        var weaker = PokerHandEvaluator.Evaluate(
            new[] { new Card(CardColors.Trefle, 10), new Card(CardColors.Coeur, 4) }, community);
        var tied = PokerHandEvaluator.Evaluate(
            new[] { new Card(CardColors.Carreau, 11), new Card(CardColors.Pique, 5) }, community);

        Assert.True(stronger.CompareTo(weaker) > 0);
        Assert.Equal(0, stronger.CompareTo(tied));
    }

    [Fact]
    public void Evaluer_DevraitChoisirLeMeilleurFullAvecDeuxBrelans()
    {
        var score = PokerHandEvaluator.Evaluate(
            new[] { new Card(CardColors.Coeur, 8), new Card(CardColors.Pique, 8) },
            new[]
            {
                new Card(CardColors.Carreau, 8), new Card(CardColors.Trefle, 5),
                new Card(CardColors.Coeur, 5), new Card(CardColors.Pique, 5),
                new Card(CardColors.Carreau, 2)
            });

        Assert.Equal(HandRank.FullHouse, score.Rank);
        Assert.Equal(new[] { 8, 5 }, score.Tiebreakers);
    }

    [Fact]
    public void Evaluer_DevraitDetecterQuinteFlushRoyale()
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
        Assert.Equal(HandRank.RoyalFlush, score.Rank);
    }

    [Fact]
    public void Evaluer_DevraitDetecterFull()
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
    public void Evaluer_DevraitDetecterDoublePaire()
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
