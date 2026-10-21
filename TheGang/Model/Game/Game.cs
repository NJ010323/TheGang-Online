using Model.Game.Cards.Interfaces;
using Model.Game.Factory.TokenFactory;
using Model.Game.Hand;
using Model.Game.Tokens;
using Model.Game.Tokens.TokenEnum;
using Model.Players;

namespace Model.Game;

/// <summary>Gère une partie du jeu de base, composée de plusieurs braquages.</summary>
public class Game
{
    // État conservé pendant toute la partie : les joueurs ne changent pas après le départ.
    private readonly List<Player> _players = new();
    // Cartes communes du braquage en cours (0, 3, 4 puis 5 cartes).
    private readonly List<ICard> _communityCards = new();
    // Ordre fixé par les jetons rouges au début du dévoilement.
    private readonly List<Player> _showdownOrder = new();
    // Seules les mains déjà dévoilées sont publiées aux vues des autres joueurs.
    private readonly List<RevealedHand> _revealedHands = new();
    // Même paquet réinitialisé et mélangé au début de chaque braquage.
    private readonly Deck.Deck _deck;
    // Une nouvelle série de jetons est créée à chaque tour du braquage.
    private IReadOnlyList<Token> _roundTokens = Array.Empty<Token>();

    /// <summary>Crée une partie ; une source aléatoire peut être fournie pour rejouer des tests.</summary>
    public Game(Random? random = null)
    {
        _deck = new Deck.Deck(random);
    }

    /// <summary>Joueurs inscrits, dans l'ordre d'entrée dans la partie.</summary>
    public IReadOnlyList<Player> Players => _players.AsReadOnly();
    /// <summary>Cartes communes visibles pendant le braquage courant.</summary>
    public IReadOnlyList<ICard> CommunityCards => _communityCards.AsReadOnly();
    /// <summary>Ensemble des jetons de la couleur du tour courant.</summary>
    public IReadOnlyList<Token> RoundTokens => _roundTokens;
    /// <summary>Jetons encore au centre, sans propriétaire.</summary>
    public IReadOnlyList<Token> AvailableTokens => _roundTokens
        .Where(token => _players.All(player => player.SignalToken != token))
        .ToList().AsReadOnly();
    /// <summary>Mains déjà dévoilées, dans l'ordre des jetons rouges.</summary>
    public IReadOnlyList<RevealedHand> RevealedHands => _revealedHands.AsReadOnly();
    /// <summary>Identifiant du prochain joueur à dévoiler, ou null si le dévoilement est terminé.</summary>
    public Guid? NextPlayerToRevealId =>
        Phase == GamePhase.Showdown && _revealedHands.Count < _showdownOrder.Count
            ? _showdownOrder[_revealedHands.Count].Id
            : null;
    /// <summary>Vrai lorsque chaque joueur possède un jeton du tour courant.</summary>
    public bool AllPlayersHaveTokens =>
        (Phase is GamePhase.PreFlop or GamePhase.Flop or GamePhase.Turn or GamePhase.River)
        && _players.Count > 0
        && _players.All(player => player.SignalToken is not null);
    /// <summary>Vrai lorsque tous les détenteurs de jetons ont confirmé la répartition actuelle.</summary>
    public bool AllChoicesConfirmed =>
        AllPlayersHaveTokens && _players.All(player => player.IsChoiceConfirmed);

    /// <summary>Étape actuelle du braquage ou de la partie.</summary>
    public GamePhase Phase { get; private set; } = GamePhase.WaitingForPlayers;
    /// <summary>Résultat global, défini après trois succès ou trois échecs.</summary>
    public GameResult Result { get; private set; } = GameResult.InProgress;
    /// <summary>Tour du braquage courant : 1 à 4 ; vaut 0 avant le départ.</summary>
    public int CurrentRound { get; private set; }
    /// <summary>Numéro du braquage courant, à partir de 1.</summary>
    public int HeistNumber { get; private set; }
    /// <summary>Nombre de braquages réussis depuis le début de la partie.</summary>
    public int SuccessfulHeists { get; private set; }
    /// <summary>Nombre de braquages échoués depuis le début de la partie.</summary>
    public int FailedHeists { get; private set; }
    /// <summary>Résultat du braquage précédent ; null avant la première résolution.</summary>
    public bool? LastHeistSucceeded { get; private set; }

    /// <summary>Inscrit un joueur avant le départ, avec un pseudo affichable et unique.</summary>
    public void AddPlayer(string name)
    {
        EnsurePhase(GamePhase.WaitingForPlayers);
        if (_players.Count == 6)
            throw new InvalidOperationException("Le jeu de base accepte au maximum six joueurs.");
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Le nom du joueur est obligatoire.", nameof(name));

        name = name.Trim();
        if (name.Length > 24 || name.Any(char.IsControl))
            throw new ArgumentException("Le pseudo doit contenir au plus 24 caractères affichables.", nameof(name));
        if (_players.Any(player => string.Equals(player.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Le nom du joueur doit être unique.");

        _players.Add(new Player(name));
    }

    /// <summary>Retire un joueur avant le départ, par exemple lorsqu'il quitte un lobby.</summary>
    public void RemovePlayer(Guid playerId)
    {
        EnsurePhase(GamePhase.WaitingForPlayers);
        _players.Remove(FindPlayer(playerId));
    }

    /// <summary>Démarre le premier braquage une fois au moins trois joueurs inscrits.</summary>
    public void StartGame()
    {
        EnsurePhase(GamePhase.WaitingForPlayers);
        if (_players.Count < 3)
            throw new InvalidOperationException("Il faut au moins trois joueurs pour démarrer.");

        StartHeist();
    }

    /// <summary>Prend un jeton au centre ou à un autre joueur et invalide les confirmations.</summary>
    public void TakeToken(Guid playerId, int value)
    {
        EnsureSignalRoundOpen();
        var player = FindPlayer(playerId);
        var token = _roundTokens.FirstOrDefault(candidate => candidate.Value == value)
            ?? throw new ArgumentOutOfRangeException(nameof(value), "Ce jeton n'existe pas pour ce tour.");

        if (player.SignalToken == token)
            return;

        var previousOwner = _players.FirstOrDefault(candidate => candidate.SignalToken == token);
        previousOwner?.ClearSignalToken();
        player.ClearSignalToken();
        player.AssignSignalToken(token);
        ResetConfirmations();
    }

    /// <summary>Rend le jeton du tour courant au centre et invalide les confirmations.</summary>
    public void ReturnToken(Guid playerId)
    {
        EnsureSignalRoundOpen();
        var player = FindPlayer(playerId);
        if (player.SignalToken is null)
            return;

        player.ClearSignalToken();
        ResetConfirmations();
    }

    /// <summary>Valide le choix actuel du joueur après attribution d'un jeton à chacun.</summary>
    public void ConfirmChoice(Guid playerId)
    {
        EnsureSignalRound();
        var player = FindPlayer(playerId);
        if (player.SignalToken is null)
            throw new InvalidOperationException("Le joueur doit choisir un jeton avant de confirmer.");
        if (!AllPlayersHaveTokens)
            throw new InvalidOperationException("Tous les joueurs doivent avoir un jeton avant de confirmer les choix.");

        player.ConfirmChoice();
    }

    /// <summary>Retire une confirmation pour autoriser de nouveaux échanges de jetons.</summary>
    public void WithdrawConfirmation(Guid playerId)
    {
        EnsureSignalRound();
        FindPlayer(playerId).WithdrawConfirmation();
    }

    /// <summary>Dévoile les trois premières cartes communes après accord unanime.</summary>
    public void RevealFlop()
    {
        EnsurePhase(GamePhase.PreFlop);
        EnsureAllChoicesConfirmed();
        DealCommunityCards(3);
        BeginSignalRound(GamePhase.Flop, 2, TokenColors.Yellow);
    }

    /// <summary>Dévoile la quatrième carte commune après accord unanime.</summary>
    public void RevealTurn()
    {
        EnsurePhase(GamePhase.Flop);
        EnsureAllChoicesConfirmed();
        DealCommunityCards(1);
        BeginSignalRound(GamePhase.Turn, 3, TokenColors.Orange);
    }

    /// <summary>Dévoile la cinquième carte commune après accord unanime.</summary>
    public void RevealRiver()
    {
        EnsurePhase(GamePhase.Turn);
        EnsureAllChoicesConfirmed();
        DealCommunityCards(1);
        BeginSignalRound(GamePhase.River, 4, TokenColors.Red);
    }

    /// <summary>Fige l'ordre des jetons rouges après accord unanime.</summary>
    public void GoToShowdown()
    {
        EnsurePhase(GamePhase.River);
        EnsureAllChoicesConfirmed();
        _showdownOrder.Clear();
        _showdownOrder.AddRange(_players.OrderBy(player => player.SignalToken!.Value));
        _revealedHands.Clear();
        Phase = GamePhase.Showdown;
    }

    /// <summary>Dévoile une seule main, selon l'ordre croissant des jetons rouges.</summary>
    public RevealedHand RevealNextHand()
    {
        EnsurePhase(GamePhase.Showdown);
        if (_revealedHands.Count == _showdownOrder.Count)
            throw new InvalidOperationException("Toutes les mains ont déjà été dévoilées.");

        var player = _showdownOrder[_revealedHands.Count];
        var hand = new RevealedHand(
            player.Id,
            player.Name,
            player.SignalToken!.Value,
            Array.AsReadOnly(player.Hand.ToArray()),
            PokerHandEvaluator.Evaluate(player.Hand, _communityCards));
        _revealedHands.Add(hand);
        return hand;
    }

    /// <summary>Classe les joueurs de la main la plus forte à la plus faible.</summary>
    public IReadOnlyList<Player> GetRanking()
    {
        EnsurePhase(GamePhase.Showdown);
        EnsureAllHandsRevealed();
        return _players
            .OrderByDescending(player => PokerHandEvaluator.Evaluate(player.Hand, _communityCards))
            .ToList();
    }

    /// <summary>Résout le braquage après toutes les révélations ; les égalités sont acceptées.</summary>
    public bool ResolveHeist()
    {
        EnsurePhase(GamePhase.Showdown);
        EnsureAllHandsRevealed();
        HandScore? previousScore = null;
        var succeeded = true;

        foreach (var hand in _revealedHands)
        {
            if (previousScore is not null && hand.Score.CompareTo(previousScore) < 0)
                succeeded = false;
            previousScore = hand.Score;
        }

        LastHeistSucceeded = succeeded;
        if (succeeded)
            SuccessfulHeists++;
        else
            FailedHeists++;

        if (SuccessfulHeists == 3 || FailedHeists == 3)
        {
            Result = succeeded ? GameResult.Won : GameResult.Lost;
            Phase = GamePhase.Finished;
        }
        else
        {
            Phase = GamePhase.HeistComplete;
        }

        return succeeded;
    }

    /// <summary>Démarre le braquage suivant tant que la partie n'est pas terminée.</summary>
    public void StartNextHeist()
    {
        EnsurePhase(GamePhase.HeistComplete);
        StartHeist();
    }

    /// <summary>Retourne les jetons du tour courant sans en recréer.</summary>
    public IReadOnlyList<Token> CreateRoundTokens()
    {
        EnsureSignalRound();
        return _roundTokens;
    }

    /// <summary>Remet le paquet et l'état des joueurs à zéro puis distribue deux cartes à chacun.</summary>
    private void StartHeist()
    {
        HeistNumber++;
        _deck.Reset();
        _deck.Shuffle();
        _communityCards.Clear();
        _showdownOrder.Clear();
        _revealedHands.Clear();
        foreach (var player in _players)
            player.ResetForNewHeist();

        for (var cardNumber = 0; cardNumber < 2; cardNumber++)
        {
            foreach (var player in _players)
                player.ReceiveCard(_deck.DealCard());
        }

        BeginSignalRound(GamePhase.PreFlop, 1, TokenColors.White);
    }

    /// <summary>Prépare la nouvelle couleur de jetons sans effacer l'historique des tours précédents.</summary>
    private void BeginSignalRound(GamePhase phase, int round, TokenColors color)
    {
        foreach (var player in _players)
            player.BeginSignalRound();

        _roundTokens = new RoundTokenFactory(color).CreateTokens(_players.Count);
        CurrentRound = round;
        Phase = phase;
    }

    /// <summary>Pioche le nombre demandé de cartes communes.</summary>
    private void DealCommunityCards(int count)
    {
        for (var i = 0; i < count; i++)
            _communityCards.Add(_deck.DealCard());
    }

    /// <summary>Résout un identifiant joueur dans la partie courante.</summary>
    private Player FindPlayer(Guid playerId) =>
        _players.FirstOrDefault(player => player.Id == playerId)
        ?? throw new ArgumentException("Joueur inconnu.", nameof(playerId));

    /// <summary>Empêche une révélation tant qu'une attribution ou une confirmation manque.</summary>
    private void EnsureAllChoicesConfirmed()
    {
        if (!AllChoicesConfirmed)
            throw new InvalidOperationException("Tous les joueurs doivent choisir et confirmer leur jeton avant la révélation.");
    }

    /// <summary>Empêche l'évaluation finale avant le dernier dévoilement.</summary>
    private void EnsureAllHandsRevealed()
    {
        if (_revealedHands.Count != _showdownOrder.Count)
            throw new InvalidOperationException("Toutes les mains doivent être dévoilées dans l'ordre des jetons rouges.");
    }

    /// <summary>Invalide l'accord collectif après chaque changement de jeton.</summary>
    private void ResetConfirmations()
    {
        foreach (var player in _players)
            player.WithdrawConfirmation();
    }

    /// <summary>Vérifie qu'un tour de choix de jetons est actif.</summary>
    private void EnsureSignalRound()
    {
        if (Phase is not (GamePhase.PreFlop or GamePhase.Flop or GamePhase.Turn or GamePhase.River))
            throw new InvalidOperationException("Aucun tour de jetons n'est en cours.");
    }

    /// <summary>Bloque les échanges après confirmation unanime.</summary>
    private void EnsureSignalRoundOpen()
    {
        EnsureSignalRound();
        if (AllChoicesConfirmed)
            throw new InvalidOperationException("Tous les choix sont confirmés. Retirez une confirmation pour modifier les jetons.");
    }

    /// <summary>Protège l'ordre des transitions entre les phases.</summary>
    private void EnsurePhase(GamePhase expected)
    {
        if (Phase != expected)
            throw new InvalidOperationException($"Phase attendue : {expected}, phase actuelle : {Phase}.");
    }
}
