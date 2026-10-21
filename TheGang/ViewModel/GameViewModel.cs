using Model.Game;
using Model.Game.Cards.Interfaces;

namespace ViewModel;

/// <summary>
/// ViewModel principal pour piloter une partie.
/// </summary>
public class GameViewModel : BaseVm
{
    // Moteur de règles commun à toutes les projections de joueurs.
    private readonly Game _game;
    // Identité du joueur qui regarde cette vue ; null pour un observateur sans main privée.
    private readonly Guid? _viewerPlayerId;
    // Projections individuelles dont les propriétés sont actualisées après chaque action.
    private readonly List<PlayerViewModel> _players = new();

    /// <summary>Crée une vue de partie en filtrant les cartes privées selon le joueur local.</summary>
    public GameViewModel(Game game, Guid? viewerPlayerId = null)
    {
        _game = game;
        _viewerPlayerId = viewerPlayerId;
        _players.AddRange(_game.Players.Select(player => new PlayerViewModel(player, _game, _viewerPlayerId)));
    }

    /// <summary>Joueurs et jetons consultables depuis cette vue.</summary>
    public IReadOnlyList<PlayerViewModel> Players => _players.AsReadOnly();

    /// <summary>Cartes communes visibles de tous.</summary>
    public IReadOnlyList<ICard> CommunityCards => _game.CommunityCards;

    /// <summary>Phase du braquage ou de la partie.</summary>
    public GamePhase Phase => _game.Phase;

    /// <summary>Tour de jetons actuel, de 1 à 4.</summary>
    public int CurrentRound => _game.CurrentRound;

    /// <summary>Numéro du braquage en cours.</summary>
    public int HeistNumber => _game.HeistNumber;

    /// <summary>Nombre de braquages réussis.</summary>
    public int SuccessfulHeists => _game.SuccessfulHeists;

    /// <summary>Nombre de braquages échoués.</summary>
    public int FailedHeists => _game.FailedHeists;

    /// <summary>Résultat global de la partie.</summary>
    public GameResult Result => _game.Result;

    /// <summary>Résultat du dernier braquage résolu.</summary>
    public bool? LastHeistSucceeded => _game.LastHeistSucceeded;

    /// <summary>Tous les jetons de la couleur du tour courant.</summary>
    public IReadOnlyList<Model.Game.Tokens.Token> RoundTokens => _game.RoundTokens;

    /// <summary>Jetons encore au centre de la table.</summary>
    public IReadOnlyList<Model.Game.Tokens.Token> AvailableTokens => _game.AvailableTokens;

    /// <summary>Vrai si tous les joueurs ont confirmé leur choix.</summary>
    public bool AllChoicesConfirmed => _game.AllChoicesConfirmed;

    /// <summary>Vrai dès que chaque joueur possède un jeton du tour.</summary>
    public bool AllPlayersHaveTokens => _game.AllPlayersHaveTokens;

    /// <summary>Identifiant du prochain joueur dont la main peut être dévoilée.</summary>
    public Guid? NextPlayerToRevealId => _game.NextPlayerToRevealId;

    /// <summary>Mains déjà dévoilées dans l'ordre des jetons rouges.</summary>
    public IReadOnlyList<RevealedHand> RevealedHands => _game.RevealedHands;

    /// <summary>Ajoute un joueur et reconstruit les projections avant le départ.</summary>
    public void AddPlayer(string name)
    {
        _game.AddPlayer(name);
        _players.Clear();
        _players.AddRange(_game.Players.Select(player => new PlayerViewModel(player, _game, _viewerPlayerId)));
        OnPropertyChanged(nameof(Players));
    }

    /// <summary>Démarre le premier braquage.</summary>
    public void StartGame()
    {
        _game.StartGame();
        RefreshAll();
    }

    /// <summary>Attribue ou reprend un jeton du tour courant.</summary>
    public void TakeToken(Guid playerId, int value)
    {
        _game.TakeToken(playerId, value);
        RefreshAll();
    }

    /// <summary>Replace le jeton du joueur au centre.</summary>
    public void ReturnToken(Guid playerId)
    {
        _game.ReturnToken(playerId);
        RefreshAll();
    }

    /// <summary>Enregistre la confirmation d'un joueur après la répartition des jetons.</summary>
    public void ConfirmChoice(Guid playerId)
    {
        _game.ConfirmChoice(playerId);
        RefreshAll();
    }

    /// <summary>Permet à un joueur de revenir sur sa confirmation.</summary>
    public void WithdrawConfirmation(Guid playerId)
    {
        _game.WithdrawConfirmation(playerId);
        RefreshAll();
    }

    /// <summary>Révèle les trois premières cartes communes.</summary>
    public void RevealFlop()
    {
        _game.RevealFlop();
        RefreshAll();
    }

    /// <summary>Révèle la quatrième carte commune.</summary>
    public void RevealTurn()
    {
        _game.RevealTurn();
        RefreshAll();
    }

    /// <summary>Révèle la cinquième carte commune.</summary>
    public void RevealRiver()
    {
        _game.RevealRiver();
        RefreshAll();
    }

    /// <summary>Ouvre le dévoilement des mains selon les jetons rouges.</summary>
    public void GoToShowdown()
    {
        _game.GoToShowdown();
        RefreshAll();
    }

    /// <summary>Dévoile la prochaine main autorisée.</summary>
    public RevealedHand RevealNextHand()
    {
        var hand = _game.RevealNextHand();
        RefreshAll();
        return hand;
    }

    /// <summary>Enregistre le succès ou l'échec du braquage après toutes les révélations.</summary>
    public bool ResolveHeist()
    {
        var succeeded = _game.ResolveHeist();
        RefreshAll();
        return succeeded;
    }

    /// <summary>Distribue un nouveau braquage si la partie continue.</summary>
    public void StartNextHeist()
    {
        _game.StartNextHeist();
        RefreshAll();
    }

    /// <summary>Notifie les propriétés qui ont pu changer après une action de jeu.</summary>
    private void RefreshAll()
    {
        foreach (var player in _players)
        {
            player.Refresh();
        }

        OnPropertyChanged(nameof(CommunityCards));
        OnPropertyChanged(nameof(Phase));
        OnPropertyChanged(nameof(CurrentRound));
        OnPropertyChanged(nameof(HeistNumber));
        OnPropertyChanged(nameof(SuccessfulHeists));
        OnPropertyChanged(nameof(FailedHeists));
        OnPropertyChanged(nameof(Result));
        OnPropertyChanged(nameof(LastHeistSucceeded));
        OnPropertyChanged(nameof(RoundTokens));
        OnPropertyChanged(nameof(AvailableTokens));
        OnPropertyChanged(nameof(AllChoicesConfirmed));
        OnPropertyChanged(nameof(AllPlayersHaveTokens));
        OnPropertyChanged(nameof(NextPlayerToRevealId));
        OnPropertyChanged(nameof(RevealedHands));
    }
}
