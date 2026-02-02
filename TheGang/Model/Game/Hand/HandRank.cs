namespace Model.Game.Hand;

/// <summary>
/// Représente la force d'une main de poker.
/// </summary>
public enum HandRank
{
    HighCard = 1,
    OnePair = 2,
    TwoPair = 3,
    ThreeOfKind = 4,
    Straight = 5,
    Flush = 6,
    FullHouse = 7,
    FourOfKind = 8,
    StraightFlush = 9
}
