namespace Model.Game.Hand;

/// <summary>
/// Décrit un score de main pour comparer les joueurs.
/// </summary>
public sealed class HandScore : IComparable<HandScore>
{
    /// <summary>Famille de la main, de la carte haute à la quinte flush royale.</summary>
    public HandRank Rank { get; }

    /// <summary>Valeurs comparées dans l'ordre pour départager deux mains de même famille.</summary>
    public IReadOnlyList<int> Tiebreakers { get; }
    public bool DominatesCategory { get; }

    /// <summary>Crée un score immuable à partir d'une catégorie et de ses départages.</summary>
    public HandScore(HandRank rank, IReadOnlyList<int> tiebreakers, bool dominatesCategory = false)
    {
        Rank = rank;
        DominatesCategory = dominatesCategory;
        Tiebreakers = Array.AsReadOnly(tiebreakers.ToArray());
    }

    /// <summary>Compare d'abord la catégorie, puis les valeurs de départage.</summary>
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

        if (DominatesCategory != other.DominatesCategory) return DominatesCategory ? 1 : -1;

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

    /// <summary>Retourne la catégorie et ses valeurs de départage.</summary>
    public override string ToString()
    {
        return $"{Rank} ({string.Join(", ", Tiebreakers)})";
    }
}
