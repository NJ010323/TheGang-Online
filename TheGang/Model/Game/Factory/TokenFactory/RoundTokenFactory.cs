using Model.Game.Tokens;
using Model.Game.Tokens.TokenEnum;

namespace Model.Game.Factory.TokenFactory;

/// <summary>
/// Fabrique des jetons de signalisation pour un tour donné.
/// </summary>
public class RoundTokenFactory : ITokenFactory
{
    // Couleur commune à tous les jetons produits par cette fabrique.
    private readonly TokenColors _color;

    /// <summary>Prépare une fabrique pour la couleur du tour demandé.</summary>
    public RoundTokenFactory(TokenColors color)
    {
        _color = color;
    }

    /// <summary>Crée exactement un jeton par joueur, numéroté de 1 à l'effectif.</summary>
    public IReadOnlyList<Token> CreateTokens(int playerCount)
    {
        // La variante à deux joueurs n'est accessible qu'après une relève d'hôte ;
        // le démarrage d'une partie ordinaire reste contrôlé à trois joueurs par Game.
        if (playerCount is < 2 or > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(playerCount), "La table accepte deux à six joueurs actifs.");
        }

        var tokens = new List<Token>();
        for (int value = 1; value <= playerCount; value++)
        {
            tokens.Add(new Token(value, _color));
        }

        return tokens.AsReadOnly();
    }
}
