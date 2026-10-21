using Model.Game.Cards.Interfaces;
using Model.Game.Tokens;
using Model.Game.Tokens.TokenEnum;

namespace Model.Players;

/// <summary>
/// Représente un joueur dans la partie.
/// </summary>
public class Player
{
    // Cartes privées du braquage en cours.
    private readonly List<ICard> _hand = new();
    // Jeton choisi dans chaque tour du braquage courant.
    private readonly Dictionary<TokenColors, Token> _signals = new();

    /// <summary>Crée un joueur avec un identifiant stable et un nom non vide.</summary>
    public Player(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Le nom du joueur est obligatoire.", nameof(name));

        Id = Guid.NewGuid();
        Name = name.Trim();
    }

    /// <summary>Identifiant utilisé pour les actions de jeu et les vues.</summary>
    public Guid Id { get; }

    /// <summary>Nom affiché aux autres joueurs.</summary>
    public string Name { get; }

    /// <summary>Cartes privées ; le moteur reste seul responsable de leur distribution.</summary>
    public IReadOnlyList<ICard> Hand => _hand.AsReadOnly();

    /// <summary>Jetons du tour courant et des tours précédents du même braquage.</summary>
    public IReadOnlyDictionary<TokenColors, Token> Signals =>
        new System.Collections.ObjectModel.ReadOnlyDictionary<TokenColors, Token>(_signals);

    /// <summary>Jeton du tour en cours, ou null si le joueur doit encore en choisir un.</summary>
    public Token? SignalToken { get; private set; }

    /// <summary>Indique si le joueur valide la répartition actuelle des jetons.</summary>
    public bool IsChoiceConfirmed { get; private set; }

    /// <summary>
    /// Ajoute une carte à la main du joueur.
    /// </summary>
    internal void ReceiveCard(ICard card)
    {
        _hand.Add(card);
    }

    /// <summary>
    /// Prépare le joueur pour un nouveau braquage, sans conserver les anciens jetons.
    /// </summary>
    internal void ResetForNewHeist()
    {
        _hand.Clear();
        _signals.Clear();
        SignalToken = null;
        IsChoiceConfirmed = false;
    }

    /// <summary>Commence un nouveau tour de jetons et conserve les signaux précédents.</summary>
    internal void BeginSignalRound()
    {
        SignalToken = null;
        IsChoiceConfirmed = false;
    }

    /// <summary>Confirme le jeton actuel ; Game vérifie d'abord les règles du tour.</summary>
    internal void ConfirmChoice() => IsChoiceConfirmed = true;

    /// <summary>Retire la confirmation sans changer le jeton.</summary>
    internal void WithdrawConfirmation() => IsChoiceConfirmed = false;

    /// <summary>
    /// Associe un jeton de signalisation au joueur.
    /// </summary>
    internal void AssignSignalToken(Token token)
    {
        SignalToken = token;
        _signals[token.Color] = token;
    }

    /// <summary>
    /// Retire le jeton de signalisation du joueur.
    /// </summary>
    internal void ClearSignalToken()
    {
        if (SignalToken is not null)
            _signals.Remove(SignalToken.Color);
        SignalToken = null;
    }
}
