using Model.Game.TokenEnum;

namespace Model.Game.Token;

public interface IToken
{
    public TokenColors Color { get; }
    
    public int Value { get; set; } // Nombre de joueur minimum à partir du momement où Joueur > 3
                                   // Egal au nombre de joueur max si Joueur => 3
    public string ToString();
}