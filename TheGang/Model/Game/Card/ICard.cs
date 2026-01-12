using Model.Game.Card.CardEnum;

namespace Model.Game.Card;

public interface ICard
{
    /// <summary>
    /// Card suit (color)
    /// </summary>
    public CardColors Suit { get; set; }

    /// <summary>
    /// Card rank (value)
    /// </summary>
    public int Rank { get; set; }

    public string ToString();
}