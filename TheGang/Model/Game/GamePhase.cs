namespace Model.Game;

/// <summary>
/// Définit les phases successives d'un braquage et de la partie.
/// </summary>
public enum GamePhase
{
    WaitingForPlayers,
    PreFlop,
    Flop,
    Turn,
    River,
    Showdown,
    HeistComplete,
    Finished
}
