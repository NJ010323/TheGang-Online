using Model.Game.Card.CardEnum;

namespace Model.Game.Card;

public class Card : ICard
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Card"/> class.
    /// </summary>
    /// <param name="suit">The suit of the card.</param>
    /// <param name="rank">The rank of the card (1-13, where 1 is Ace, 11 is Jack, 12 is Queen, 13 is King).</param>
    public Card(CardColors suit, int rank)
    {
        Suit = suit;
        Rank = rank;
    }
    
    public CardColors Suit { get; set; }
    
    public int Rank { get; set; }
    
    public override string ToString()
    {
        string rankStr = Rank switch
        {
            1 => "As",
            11 => "Valet",
            12 => "Dame",
            13 => "Roi",
            _ => Rank.ToString()
        };

        return $"{rankStr} de {Suit}";
    }
}
