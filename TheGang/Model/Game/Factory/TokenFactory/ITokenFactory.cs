using Model.Game.Tokens;

namespace Model.Game.Factory.TokenFactory;

public interface ITokenFactory
{
    IReadOnlyList<Token> CreateTokens(int playerCount);
}