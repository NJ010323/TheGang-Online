using Model.Game.Card;
using Model.Game.Card.CardEnum;

namespace Model.Game.Deck;

public class Deck
{
    private readonly List<ICard> _cards;
    private static readonly Random _random = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="Deck"/> class with a standard 52-card deck.
    /// </summary>
    public Deck()
    {
        _cards = new List<ICard>();
        InitializeDeck();
    }

    /// <summary>
    /// Gets the number of cards remaining in the deck.
    /// </summary>
    public int CardsRemaining => _cards.Count;

    /// <summary>
    /// Initializes the deck with 52 cards (13 ranks × 4 suits).
    /// </summary>
    private void InitializeDeck()
    {
        foreach (CardColors suit in Enum.GetValues(typeof(CardColors)))
        {
            for (int rank = 1; rank <= 13; rank++)
            {

                _cards.Add(new Card.Card(suit, rank));
            }
        }
    }

    /// <summary>
    /// Shuffles the deck using the Fisher-Yates algorithm.
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
    /// Deals a single card from the top of the deck.
    /// </summary>
    /// <returns>The top card from the deck.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the deck is empty.</exception>
    public ICard DealCard()
    {
        if (_cards.Count == 0)
        {
            throw new InvalidOperationException("Cannot deal a card from an empty deck.");
        }

        var card = _cards[0];
        _cards.RemoveAt(0);
        return card;
    }

    /// <summary>
    /// Resets the deck by clearing and reinitializing it.
    /// </summary>
    public void Reset()
    {
        _cards.Clear();
        InitializeDeck();
    }

    /// <summary>
    /// Returns a string that represents the current deck.
    /// </summary>
    /// <returns>A string that lists all cards in the deck.</returns>
    public override string ToString()
    {
        return string.Join("\n", _cards.Select(card => card.ToString()));
    }
}
