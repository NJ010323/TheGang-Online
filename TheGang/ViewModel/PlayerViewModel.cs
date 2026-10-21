using Model.Game.Cards.Interfaces;
using Model.Players;

namespace ViewModel;

/// <summary>
/// ViewModel simple pour un joueur.
/// </summary>
public class PlayerViewModel : BaseVm
{
    // État métier du joueur ; ses cartes ne sont pas exposées directement aux autres vues.
    private readonly Player _player;
    // Nécessaire pour savoir quelles mains adverses ont été dévoilées.
    private readonly Model.Game.Game _game;
    // Identité de la vue pour afficher uniquement sa propre main avant le showdown.
    private readonly Guid? _viewerPlayerId;

    /// <summary>Crée une projection de joueur pour une vue donnée.</summary>
    internal PlayerViewModel(Player player, Model.Game.Game game, Guid? viewerPlayerId)
    {
        _player = player;
        _game = game;
        _viewerPlayerId = viewerPlayerId;
    }

    /// <summary>Nom public du joueur.</summary>
    public string Name => _player.Name;

    /// <summary>Identifiant public du joueur.</summary>
    public Guid Id => _player.Id;

    /// <summary>Main visible : la sienne ou une main adverse déjà dévoilée.</summary>
    public IReadOnlyList<ICard> Hand => _viewerPlayerId == Id
        ? _player.Hand
        : _game.RevealedHands.FirstOrDefault(hand => hand.PlayerId == Id)?.Cards
            ?? Array.Empty<ICard>();

    /// <summary>Jeton détenu pendant le tour courant.</summary>
    public Model.Game.Tokens.Token? SignalToken => _player.SignalToken;

    /// <summary>Confirmation du choix actuel.</summary>
    public bool IsChoiceConfirmed => _player.IsChoiceConfirmed;

    /// <summary>Jetons du tour courant et des tours précédents du braquage.</summary>
    public IReadOnlyDictionary<Model.Game.Tokens.TokenEnum.TokenColors, Model.Game.Tokens.Token> Signals => _player.Signals;

    /// <summary>Notifie la vue après un changement de main, de jeton ou de confirmation.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Hand));
        OnPropertyChanged(nameof(SignalToken));
        OnPropertyChanged(nameof(IsChoiceConfirmed));
        OnPropertyChanged(nameof(Signals));
    }
}
