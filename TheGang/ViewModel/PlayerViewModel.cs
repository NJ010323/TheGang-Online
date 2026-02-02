using Model.Game.Cards.Interfaces;
using Model.Players;

namespace ViewModel;

/// <summary>
/// ViewModel simple pour un joueur.
/// </summary>
public class PlayerViewModel : BaseVm
{
    private readonly Player _player;

    public PlayerViewModel(Player player)
    {
        _player = player;
    }

    public string Name => _player.Name;

    public IReadOnlyList<ICard> Hand => _player.Hand;

    public Model.Game.Tokens.Token? SignalToken => _player.SignalToken;

    public void Refresh()
    {
        OnPropertyChanged(nameof(Hand));
        OnPropertyChanged(nameof(SignalToken));
    }
}
