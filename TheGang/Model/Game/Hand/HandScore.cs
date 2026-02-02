namespace Model.Game.Hand;

/// <summary>
/// Décrit un score de main pour comparer les joueurs.
/// </summary>
public sealed class HandScore : IComparable<HandScore>
{
    public HandRank Rank { get; }

    public IReadOnlyList<int> Tiebreakers { get; }

    public HandScore(HandRank rank, IReadOnlyList<int> tiebreakers)
    {
        Rank = rank;
        Tiebreakers = tiebreakers;
    }

    public int CompareTo(HandScore? other)
    {
        if (other is null)
        {
            return 1;
        }

        int rankComparison = Rank.CompareTo(other.Rank);
        if (rankComparison != 0)
        {
            return rankComparison;
        }

        int length = Math.Min(Tiebreakers.Count, other.Tiebreakers.Count);
        for (int i = 0; i < length; i++)
        {
            int comparison = Tiebreakers[i].CompareTo(other.Tiebreakers[i]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return Tiebreakers.Count.CompareTo(other.Tiebreakers.Count);
    }

    public override string ToString()
    {
        return $"{Rank} ({string.Join(", ", Tiebreakers)})";
    }
}
