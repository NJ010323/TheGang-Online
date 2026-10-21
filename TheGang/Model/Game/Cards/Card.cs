using Model.Game.Card;
using Model.Game.Card.CardEnum;
using Model.Game.Cards.Interfaces;

namespace Model.Game.Cards;

/// <summary>Carte standard immuable d'un paquet de 52 cartes.</summary>
public class Card : ICard
{
    /// <summary>
    /// Initialise une nouvelle carte.
    /// </summary>
    /// <param name="suit">Couleur de la carte.</param>
    /// <param name="rank">Valeur de la carte (1-13, où 1 est As, 11 Valet, 12 Dame, 13 Roi).</param>
    public Card(CardColors suit, int rank)
    {
        if (!Enum.IsDefined(suit))
            throw new ArgumentOutOfRangeException(nameof(suit));
        if (rank is < 1 or > 13)
            throw new ArgumentOutOfRangeException(nameof(rank));

        Suit = suit;
        Rank = rank;
    }
    
    /// <summary>Enseigne de la carte.</summary>
    public CardColors Suit { get; }
    
    /// <summary>Valeur de 1 à 13 ; l'as est représenté par 1.</summary>
    public int Rank { get; }
    
    /// <summary>Retourne un libellé lisible de la carte.</summary>
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
