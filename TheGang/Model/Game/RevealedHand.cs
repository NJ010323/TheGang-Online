using Model.Game.Cards.Interfaces;
using Model.Game.Hand;

namespace Model.Game;

/// <summary>Instantané public d'une main dévoilée pendant le showdown, avec son jeton rouge.</summary>
/// <param name="PlayerId">Joueur qui vient de dévoiler sa main.</param>
/// <param name="PlayerName">Nom public du joueur.</param>
/// <param name="TokenValue">Valeur du jeton rouge qui détermine l'ordre de dévoilement.</param>
/// <param name="Cards">Copie en lecture seule des deux cartes privées.</param>
/// <param name="Score">Force de la meilleure main de cinq cartes.</param>
public sealed record RevealedHand(
    Guid PlayerId,
    string PlayerName,
    int TokenValue,
    IReadOnlyList<ICard> Cards,
    HandScore Score);
