using Model.Game.Tokens;

namespace Model.Game.Factory.TokenFactory;

public interface ITokenFactory
{
    // A voir c'est une base
    IReadOnlyList<Token> CreateTokens(int playerCount);
}