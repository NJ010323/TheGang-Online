using Model.Game.Tokens;

namespace Model.Game.Factory.TokenFactory;

/// <summary>Crée la série de jetons d'un tour pour un effectif donné.</summary>
public interface ITokenFactory
{
    /// <summary>Retourne les jetons numérotés de 1 au nombre de joueurs.</summary>
    IReadOnlyList<Token> CreateTokens(int playerCount);
}
