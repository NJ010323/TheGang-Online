using Model.Game.Card;
using Model.Game.Cards.Interfaces;
using Model.Game.Factory.TokenFactory;
using Model.Game.Hand;
using Model.Game.Tokens;
using Model.Game.Tokens.TokenEnum;
using Model.Players;

namespace Model.Game;

/// <summary>
/// Contient la logique principale du jeu The Gang.
/// </summary>
public class Game
{
    private readonly List<Player> _players = new();
    private readonly Deck.Deck _deck = new();

    public Game()
    {
        Phase = GamePhase.WaitingForPlayers;
        CommunityCards = new List<ICard>();
    }

    public IReadOnlyList<Player> Players => _players;

    public List<ICard> CommunityCards { get; }

    public GamePhase Phase { get; private set; }

    public int CurrentRound { get; private set; }

    public void AddPlayer(string name)
    {
        if (Phase != GamePhase.WaitingForPlayers)
        {
            throw new InvalidOperationException("Impossible d'ajouter un joueur en cours de partie.");
        }

        if (_players.Any(player => player.Name == name))
        {
            throw new InvalidOperationException("Le nom du joueur doit être unique.");
        }

        _players.Add(new Player(name));
    }

    public void StartGame()
    {
        if (_players.Count < 3)
        {
            throw new InvalidOperationException("Il faut au moins trois joueurs pour démarrer.");
        }

        ResetForNewRound(1);
        DealInitialCards();
        Phase = GamePhase.PreFlop;
    }

    public void RevealFlop()
    {
        EnsurePhase(GamePhase.PreFlop);
        DealCommunityCards(3);
        Phase = GamePhase.Flop;
        CurrentRound = 2;
    }

    public void RevealTurn()
    {
        EnsurePhase(GamePhase.Flop);
        DealCommunityCards(1);
        Phase = GamePhase.Turn;
        CurrentRound = 3;
    }

    public void RevealRiver()
    {
        EnsurePhase(GamePhase.Turn);
        DealCommunityCards(1);
        Phase = GamePhase.River;
        CurrentRound = 4;
    }

    public void GoToShowdown()
    {
        EnsurePhase(GamePhase.River);
        Phase = GamePhase.Showdown;
    }

    public IReadOnlyList<Player> GetRanking()
    {
        EnsurePhase(GamePhase.Showdown);
        return (IReadOnlyList<Player>)_players
            .OrderByDescending(player => PokerHandEvaluator.Evaluate(player.Hand, CommunityCards))
            .ToList();
    }

    public IReadOnlyList<Token> CreateRoundTokens()
    {
        var color = CurrentRound switch
        {
            1 => TokenColors.White,
            2 => TokenColors.Yellow,
            3 => TokenColors.Orange,
            _ => TokenColors.Red
        };

        ITokenFactory factory = new RoundTokenFactory(color);
        return factory.CreateTokens(_players.Count);
    }

    private void DealInitialCards()
    {
        foreach (var player in _players)
        {
            player.ResetHand();
        }

        for (int i = 0; i < 2; i++)
        {
            foreach (var player in _players)
            {
                player.ReceiveCard(_deck.DealCard());
            }
        }
    }

    private void DealCommunityCards(int count)
    {
        for (int i = 0; i < count; i++)
        {
            CommunityCards.Add(_deck.DealCard());
        }
    }

    private void ResetForNewRound(int round)
    {
        _deck.Reset();
        _deck.Shuffle();
        CommunityCards.Clear();
        CurrentRound = round;
        foreach (var player in _players)
        {
            player.ResetHand();
            player.ClearSignalToken();
        }
    }

    private void EnsurePhase(GamePhase expected)
    {
        if (Phase != expected)
        {
            throw new InvalidOperationException($"Phase attendue: {expected}, phase actuelle: {Phase}.");
        }
    }
}
