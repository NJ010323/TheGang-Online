using Model.Game.Tokens.TokenEnum;

namespace Model.Game.Tokens.Interfaces;

/// <summary>Contrat en lecture seule d'un jeton de signalisation.</summary>
public interface IToken
{
    /// <summary>Couleur du tour.</summary>
    public TokenColors Color { get; }
    
    /// <summary>Nombre d'étoiles.</summary>
    public int Value { get; }
    /// <summary>Représentation textuelle du jeton.</summary>
    public string ToString();
}
