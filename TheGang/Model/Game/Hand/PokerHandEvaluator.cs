using Model.Game.Card;
using Model.Game.Cards.Interfaces;

namespace Model.Game.Hand;

/// <summary>
/// Évalue des mains de poker Texas Hold'em.
/// </summary>
public static class PokerHandEvaluator
{
    /// <summary>
    /// Compare toutes les combinaisons de cinq cartes et retient la plus forte.
    /// </summary>
    public static HandScore Evaluate(IEnumerable<ICard> holeCards, IEnumerable<ICard> communityCards)
    {
        var allCards = holeCards.Concat(communityCards).ToList();
        if (allCards.Count < 5)
        {
            throw new ArgumentException("Il faut au moins 5 cartes pour évaluer une main.");
        }

        HandScore? bestScore = null;
        foreach (var combo in GetFiveCardCombinations(allCards))
        {
            var score = EvaluateFiveCards(combo);
            if (bestScore is null || score.CompareTo(bestScore) > 0)
            {
                bestScore = score;
            }
        }

        return bestScore ?? throw new InvalidOperationException("Impossible d'évaluer la main.");
    }

    /// <summary>Énumère chaque combinaison de cinq cartes sans répétition.</summary>
    private static IEnumerable<IReadOnlyList<ICard>> GetFiveCardCombinations(IReadOnlyList<ICard> cards)
    {
        for (int i = 0; i < cards.Count - 4; i++)
        {
            for (int j = i + 1; j < cards.Count - 3; j++)
            {
                for (int k = j + 1; k < cards.Count - 2; k++)
                {
                    for (int l = k + 1; l < cards.Count - 1; l++)
                    {
                        for (int m = l + 1; m < cards.Count; m++)
                        {
                            yield return new List<ICard>
                            {
                                cards[i],
                                cards[j],
                                cards[k],
                                cards[l],
                                cards[m]
                            };
                        }
                    }
                }
            }
        }
    }

    /// <summary>Classe cinq cartes et prépare les valeurs de départage.</summary>
    private static HandScore EvaluateFiveCards(IReadOnlyList<ICard> cards)
    {
        var ranks = cards.Select(card => NormalizeRank(card.Rank)).OrderByDescending(rank => rank).ToList();
        var suits = cards.Select(card => card.Suit).ToList();

        bool isFlush = suits.All(suit => Enum.IsDefined(suit)) && suits.Distinct().Count() == 1;
        int? straightHigh = GetStraightHigh(ranks);

        var groupedRanks = ranks
            .GroupBy(rank => rank)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Key)
            .ToList();
        if (groupedRanks[0].Count() == 5)
            return new HandScore(HandRank.FourOfKind, new[] { groupedRanks[0].Key, groupedRanks[0].Key });

        if (straightHigh.HasValue && isFlush)
        {
            var rank = straightHigh.Value == 14 ? HandRank.RoyalFlush : HandRank.StraightFlush;
            return new HandScore(rank, new List<int> { straightHigh.Value });
        }

        if (groupedRanks[0].Count() == 4)
        {
            int quadRank = groupedRanks[0].Key;
            int kicker = groupedRanks[1].Key;
            return new HandScore(HandRank.FourOfKind, new List<int> { quadRank, kicker });
        }

        if (groupedRanks[0].Count() == 3 && groupedRanks[1].Count() == 2)
        {
            return new HandScore(HandRank.FullHouse, new List<int> { groupedRanks[0].Key, groupedRanks[1].Key });
        }

        if (isFlush)
        {
            return new HandScore(HandRank.Flush, ranks);
        }

        if (straightHigh.HasValue)
        {
            return new HandScore(HandRank.Straight, new List<int> { straightHigh.Value });
        }

        if (groupedRanks[0].Count() == 3)
        {
            var kickers = groupedRanks.Skip(1).Select(group => group.Key).OrderByDescending(rank => rank).ToList();
            var tiebreakers = new List<int> { groupedRanks[0].Key };
            tiebreakers.AddRange(kickers);
            return new HandScore(HandRank.ThreeOfKind, tiebreakers);
        }

        if (groupedRanks[0].Count() == 2 && groupedRanks[1].Count() == 2)
        {
            int highPair = Math.Max(groupedRanks[0].Key, groupedRanks[1].Key);
            int lowPair = Math.Min(groupedRanks[0].Key, groupedRanks[1].Key);
            int kicker = groupedRanks[2].Key;
            return new HandScore(HandRank.TwoPair, new List<int> { highPair, lowPair, kicker });
        }

        if (groupedRanks[0].Count() == 2)
        {
            var kickers = groupedRanks.Skip(1).Select(group => group.Key).OrderByDescending(rank => rank).ToList();
            var tiebreakers = new List<int> { groupedRanks[0].Key };
            tiebreakers.AddRange(kickers);
            return new HandScore(HandRank.OnePair, tiebreakers);
        }

        return new HandScore(HandRank.HighCard, ranks);
    }

    /// <summary>Convertit l'as, stocké comme 1, en valeur haute pour les comparaisons.</summary>
    private static int NormalizeRank(int rank)
    {
        return rank == 1 ? 14 : rank;
    }

    /// <summary>Retourne la carte haute d'une quinte, avec le cas spécial A-2-3-4-5.</summary>
    private static int? GetStraightHigh(IReadOnlyList<int> ranks)
    {
        var distinctRanks = ranks.Distinct().OrderByDescending(rank => rank).ToList();
        if (distinctRanks.Count < 5)
        {
            return null;
        }

        for (int i = 0; i <= distinctRanks.Count - 5; i++)
        {
            int start = distinctRanks[i];
            bool isStraight = true;
            for (int offset = 1; offset < 5; offset++)
            {
                if (!distinctRanks.Contains(start - offset))
                {
                    isStraight = false;
                    break;
                }
            }

            if (isStraight)
            {
                return start;
            }
        }

        if (distinctRanks.Contains(14) &&
            distinctRanks.Contains(5) &&
            distinctRanks.Contains(4) &&
            distinctRanks.Contains(3) &&
            distinctRanks.Contains(2))
        {
            return 5;
        }

        return null;
    }
}
