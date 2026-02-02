using Model.Game.Card;
using Model.Game.Tokens;

namespace Model.Player;

/// <summary>
/// Représente un joueur dans la partie.
/// </summary>
public class Player
{
    private readonly List<ICard> _hand = new();

    public Player(string name)
    {
        Id = Guid.NewGuid();
        Name = name;
    }

    public Guid Id { get; }

    public string Name { get; }

    public IReadOnlyList<ICard> Hand => _hand;

    public Token? SignalToken { get; private set; }

    /// <summary>
    /// Ajoute une carte à la main du joueur.
    /// </summary>
    public void ReceiveCard(ICard card)
    {
        _hand.Add(card);
    }

    /// <summary>
    /// Réinitialise la main du joueur.
    /// </summary>
    public void ResetHand()
    {
        _hand.Clear();
    }

    /// <summary>
    /// Associe un jeton de signalisation au joueur.
    /// </summary>
    public void AssignSignalToken(Token token)
    {
        SignalToken = token;
    }

    /// <summary>
    /// Retire le jeton de signalisation du joueur.
    /// </summary>
    public void ClearSignalToken()
    {
        SignalToken = null;
    }
}
