using Model.Game.Card;
using Model.Game.Card.CardEnum;
using Model.Game.Cards.Interfaces;

namespace Model.Game.Cards;

public class Card : ICard
{
    /// <summary>
    /// Initialise une nouvelle carte.
    /// </summary>
    /// <param name="suit">Couleur de la carte.</param>
    /// <param name="rank">Valeur de la carte (1-13, où 1 est As, 11 Valet, 12 Dame, 13 Roi).</param>
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
