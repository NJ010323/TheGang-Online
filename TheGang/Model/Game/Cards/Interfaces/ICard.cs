using Model.Game.Card.CardEnum;

namespace Model.Game.Cards.Interfaces;

/// <summary>Contrat en lecture seule d'une carte utilisable par le moteur.</summary>
public interface ICard
{
    /// <summary>Enseigne de la carte.</summary>
    public CardColors Suit { get; }

    /// <summary>Valeur de la carte, avec 1 pour l'as.</summary>
    public int Rank { get; }

    /// <summary>Représentation textuelle de la carte.</summary>
    public string ToString();
}
