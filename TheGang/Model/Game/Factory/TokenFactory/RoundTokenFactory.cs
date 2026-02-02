using Model.Game.Tokens;
using Model.Game.Tokens.TokenEnum;

namespace Model.Game.Factory.TokenFactory;

/// <summary>
/// Fabrique des jetons de signalisation pour un tour donné.
/// </summary>
public class RoundTokenFactory : ITokenFactory
{
    private readonly TokenColors _color;

    public RoundTokenFactory(TokenColors color)
    {
        _color = color;
    }

    public IReadOnlyList<Token> CreateTokens(int playerCount)
    {
        if (playerCount <= 0)
        {
            throw new ArgumentException("Le nombre de joueurs doit être supérieur à zéro.");
        }

        var tokens = new List<Token>();
        for (int value = 1; value <= playerCount; value++)
        {
            tokens.Add(new Token(value, _color));
        }

        return tokens;
    }
}
