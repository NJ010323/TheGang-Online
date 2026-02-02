using Model.Game.Card.CardEnum;

namespace Model.Game.Cards.Interfaces;

public interface ICard
{
    /// <summary>
    /// Cards suit (color)
    /// </summary>
    public CardColors Suit { get; set; }

    /// <summary>
    /// Cards rank (value)
    /// </summary>
    public int Rank { get; set; }

    public string ToString();
}