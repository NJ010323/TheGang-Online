using Model.Game;

namespace ViewModel;

/// <summary>
/// ViewModel principal pour piloter une partie.
/// </summary>
public class GameViewModel : BaseVm
{
    private readonly Game _game;
    private readonly List<PlayerViewModel> _players = new();

    public GameViewModel(Game game)
    {
        _game = game;
    }

    public IReadOnlyList<PlayerViewModel> Players => _players;

    public IReadOnlyList<Model.Game.Card.ICard> CommunityCards => _game.CommunityCards;

    public GamePhase Phase => _game.Phase;

    public int CurrentRound => _game.CurrentRound;

    public void AddPlayer(string name)
    {
        _game.AddPlayer(name);
        _players.Clear();
        _players.AddRange(_game.Players.Select(player => new PlayerViewModel(player)));
        OnPropertyChanged(nameof(Players));
    }

    public void StartGame()
    {
        _game.StartGame();
        RefreshAll();
    }

    public void RevealFlop()
    {
        _game.RevealFlop();
        RefreshAll();
    }

    public void RevealTurn()
    {
        _game.RevealTurn();
        RefreshAll();
    }

    public void RevealRiver()
    {
        _game.RevealRiver();
        RefreshAll();
    }

    public void GoToShowdown()
    {
        _game.GoToShowdown();
        RefreshAll();
    }

    private void RefreshAll()
    {
        foreach (var player in _players)
        {
            player.Refresh();
        }

        OnPropertyChanged(nameof(CommunityCards));
        OnPropertyChanged(nameof(Phase));
        OnPropertyChanged(nameof(CurrentRound));
    }
}
