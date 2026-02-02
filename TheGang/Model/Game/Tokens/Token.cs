using Model.Game.Tokens.Interfaces;
using Model.Game.Tokens.TokenEnum;

namespace Model.Game.Tokens;

public class Token : IToken
{
    public TokenColors Color { get; }
    public int Value { get; set; }

    /// <summary>
    /// Initialise un jeton avec une couleur et une valeur.
    /// </summary>
    /// <param name="value">Valeur du jeton</param>
    /// <param name="color">Couleur du jeton</param>
    public Token(int value, TokenColors color)
    {
        Value = value;
        Color = color;
    }

    public override string ToString()
    {
        return $"Jeton {Value} ({Color})";
    }
}
