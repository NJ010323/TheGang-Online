namespace Model.Game;

/// <summary>
/// Définit les phases principales d'une partie.
/// </summary>
public enum GamePhase
{
    WaitingForPlayers,
    PreFlop,
    Flop,
    Turn,
    River,
    Showdown
}
