using Model.Game.Tokens.Interfaces;
using Model.Game.Tokens.TokenEnum;

namespace Model.Game.Tokens;

/// <summary>Jeton de signalisation immuable d'un tour.</summary>
public class Token : IToken
{
    /// <summary>Couleur correspondant au tour du braquage.</summary>
    public TokenColors Color { get; }
    /// <summary>Position estimée de la main, de 1 au nombre de joueurs.</summary>
    public int Value { get; }

    /// <summary>
    /// Initialise un jeton avec une couleur et une valeur.
    /// </summary>
    /// <param name="value">Valeur du jeton</param>
    /// <param name="color">Couleur du jeton</param>
    public Token(int value, TokenColors color)
    {
        if (value is < 1 or > 6)
            throw new ArgumentOutOfRangeException(nameof(value));
        if (!Enum.IsDefined(color))
            throw new ArgumentOutOfRangeException(nameof(color));

        Value = value;
        Color = color;
    }

    /// <summary>Retourne un libellé lisible du jeton.</summary>
    public override string ToString()
    {
        return $"Jeton {Value} ({Color})";
    }
}
