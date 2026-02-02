using Model.Game.Card;
using Model.Game.Card.CardEnum;

namespace Model.Game.Deck;

public class Deck
{
    private readonly List<ICard> _cards;
    private static readonly Random _random = new();

    /// <summary>
    /// Initialise un nouveau deck standard de 52 cartes.
    /// </summary>
    public Deck()
    {
        _cards = new List<ICard>();
        InitializeDeck();
    }

    /// <summary>
    /// Retourne le nombre de cartes restantes dans le deck.
    /// </summary>
    public int CardsRemaining => _cards.Count;

    /// <summary>
    /// Initialise le deck avec 52 cartes (13 valeurs × 4 couleurs).
    /// </summary>
    private void InitializeDeck()
    {
        foreach (CardColors suit in Enum.GetValues(typeof(CardColors)))
        {
            for (int rank = 1; rank <= 13; rank++)
            {

                _cards.Add(new Card(suit, rank));
            }
        }
    }

    /// <summary>
    /// Mélange le deck avec l'algorithme de Fisher-Yates.
    /// </summary>
    public void Shuffle()
    {
        int n = _cards.Count;
        while (n > 1)
        {
            n--;
            int k = _random.Next(n + 1);
            (_cards[k], _cards[n]) = (_cards[n], _cards[k]);
        }
    }

    /// <summary>
    /// Distribue une carte depuis le haut du deck.
    /// </summary>
    /// <returns>La carte distribuée.</returns>
    /// <exception cref="InvalidOperationException">Le deck est vide.</exception>
    public ICard DealCard()
    {
        if (_cards.Count == 0)
        {
            throw new InvalidOperationException("Impossible de distribuer une carte depuis un deck vide.");
        }

        var card = _cards[0];
        _cards.RemoveAt(0);
        return card;
    }

    /// <summary>
    /// Réinitialise le deck en le remplissant de nouveau.
    /// </summary>
    public void Reset()
    {
        _cards.Clear();
        InitializeDeck();
    }

    /// <summary>
    /// Retourne une chaîne représentant le deck courant.
    /// </summary>
    /// <returns>Une chaîne listant toutes les cartes.</returns>
    public override string ToString()
    {
        return string.Join("\n", _cards.Select(card => card.ToString()));
    }
}
