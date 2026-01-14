using Model.Game.Tokens.Interfaces;
using Model.Game.Tokens.TokenEnum;

namespace Model.Game.Tokens;

public class Token : IToken
{
    public TokenColors Color { get; }
    public int Value { get; set; }

    /// <summary>
    /// Initialize Token with a color adn a value
    /// </summary>
    /// <param name="value">Value of the token</param>
    /// <param name="color">Color of the token</param>
    public Token(int value, TokenColors color)
    {
        Value = value;
        Color = color;
    }
}