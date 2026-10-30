using Model.Game;
using Model.Game.Card.CardEnum;
using Model.Game.Cards.Interfaces;
using Model.Game.Hand;

namespace Network.Bots;

/// <summary>Frontière de confidentialité : aucune main adverse ni référence au moteur ou au paquet.</summary>
public sealed record BotObservation(Guid Id, GamePhase Phase, int Heist, Guid GameId,
    IReadOnlyList<CardSnapshot> Hand, IReadOnlyList<CardSnapshot> Board,
    IReadOnlyList<BotPublicPlayer> Players, IReadOnlyList<int> AvailableTokens,
    bool AllHaveTokens, bool AllConfirmed, Guid? NextReveal, SpecialGameSnapshot? Special)
{
    /// <summary>Ne copie jamais les mains des autres, même depuis un instantané complet.</summary>
    public static BotObservation FromSnapshot(GameSnapshot view, Guid id) => new(id, view.Phase,
        view.HeistNumber, view.GameId, view.Players.Single(p => p.Id == id).Hand,
        view.CommunityCards, view.Players.Select(p => new BotPublicPlayer(p.Id, p.IsBot,
            p.HandCount, p.CurrentToken, p.IsChoiceConfirmed, p.Signals)).ToArray(),
        view.AvailableTokens, view.AllPlayersHaveTokens, view.AllChoicesConfirmed,
        view.NextPlayerToRevealId, view.Special);
}
public sealed record BotPublicPlayer(Guid Id, bool IsBot, int HandCount, int? Token, bool Confirmed,
    IReadOnlyList<SignalSnapshot> Signals);

/// <summary>Un raisonneur par bot, sans transport ni accès à l'état autoritaire.</summary>
public sealed class BotDecision
{
    private readonly Random _random;
    private string _round = "";
    private string _cards = "";
    private double _strength;
    private int _steals;
    public BotDecision(Random? random = null) => _random = random ?? new Random();

    public GameActionRequest? Decide(BotObservation view)
    {
        var self = view.Players.Single(p => p.Id == view.Id);
        var round = $"{view.GameId}/{view.Heist}/{view.Phase}";
        if (_round != round) { _round = round; _cards = ""; _steals = 0; }
        var special = view.Special;
        GameActionRequest Stamp(GameActionRequest action) => action with
            { ExpectedGameId = view.GameId, ExpectedHeist = view.Heist };
        if (special?.SetupKind is not null)
            return special.SetupChosen ? null : Stamp(new(GameAction.ChooseSpecialSetup)
                { CardIndex = WeakestCard(view, -1) });
        if (special?.DiscardPlayer is { } discarder)
            return discarder != view.Id ? null : Stamp(new(GameAction.DiscardSpecial)
                { CardIndex = WeakestCard(view, special.ForbiddenDiscard) });
        if (special?.Proposal is { } proposal)
            return proposal.Accepted.Contains(view.Id) ? null : Stamp(new(GameAction.VoteComplice)
                { ProposalId = proposal.Id, Accepted = true });
        if (view.Phase == GamePhase.Showdown && special is not null &&
            view.NextReveal == special.GuessTarget && view.Id != special.GuessTarget)
        {
            foreach (var kind in new[] { "E04", "E09" }.Where(special.ActiveCards.Contains))
            {
                // La discussion de ces deux effets est expressément autorisée. Le porteur
                // de la dernière main est exclu ; aucune information privée n'est annoncée.
                var announcements = special.PublicGuesses.Where(g => g.Kind == kind && g.PlayerId != special.GuessTarget).ToArray();
                var human = announcements.FirstOrDefault(g => view.Players.Any(p => p.Id == g.PlayerId && !p.IsBot));
                var value = human?.Value ?? announcements.FirstOrDefault()?.Value ?? EstimateGuess(view, kind);
                if (!special.OwnGuesses.TryGetValue(kind, out var own) || own != value)
                    return Stamp(new(GameAction.SubmitSpecialGuess) { GuessKind = kind, Rank = value });
            }
            return null;
        }
        if (view.Phase is not (GamePhase.PreFlop or GamePhase.Flop or GamePhase.Turn or GamePhase.River) || view.AllConfirmed)
            return null;
        if (self.Token is not null)
            return view.AllHaveTokens && !self.Confirmed ? new(GameAction.ConfirmChoice) : null;

        var key = string.Join(';', view.Hand.Concat(view.Board).Select(c => $"{c.Suit}:{c.Rank}"));
        if (key != _cards) { _cards = key; _strength = EstimateStrength(view.Hand, view.Board); }
        var count = view.Players.Count;
        var desired = 1 + _strength * (count - 1);
        // Le jeton du tour précédent constitue un signal public, utile surtout avant
        // le flop. Son poids décroît quand les nouvelles cartes précisent la main.
        var previous = self.Signals.LastOrDefault();
        if (previous is not null && view.Phase != GamePhase.PreFlop)
            desired = desired * .9 + previous.Value * .1;
        // Les jetons adverses expriment des estimations, jamais des forces certaines.
        // On combine ce signal modeste au percentile plutôt que d'en déduire des cartes.
        var signals = view.Players.Where(p => p.Id != view.Id)
            .Select(p => p.Token ?? p.Signals.LastOrDefault()?.Value).ToArray();
        if (signals.Any(value => value.HasValue))
        {
            var relative = 1 + signals.Sum(value => value is { } token
                ? 1 / (1 + Math.Exp(-6 * (_strength - (token - .5) / count))) : _strength);
            desired = desired * .8 + relative * .2;
        }
        var available = view.AvailableTokens.OrderBy(v => Math.Abs(v - desired)).FirstOrDefault();
        var best = Enumerable.Range(1, count)
            .Where(v => view.AvailableTokens.Contains(v) || special?.LockedTokens.Contains(v) != true)
            .OrderBy(v => Math.Abs(v - desired)).First();
        // Un déplacement est une information publique. Après une contestation, accepter
        // la meilleure place libre évite les cycles sans jamais contourner une règle.
        if (available != 0 && (_steals > 0 || Math.Abs(best - desired) + .8 >= Math.Abs(available - desired))) best = available;
        if (!view.AvailableTokens.Contains(best)) _steals++;
        return new(GameAction.TakeToken, best);
    }

    private int WeakestCard(BotObservation view, int forbidden)
    {
        return Enumerable.Range(0, view.Hand.Count).Where(i => i != forbidden)
            .OrderByDescending(i => EstimateStrength(view.Hand.Where((_, index) => index != i).ToArray(), view.Board))
            .First();
    }

    // Échantillons hypothétiques indépendants de la vraie distribution. Le score est
    // un percentile contre une main inconnue, recalculé à chaque révélation.
    private double EstimateStrength(IReadOnlyList<CardSnapshot> hand, IReadOnlyList<CardSnapshot> board)
    {
        var deck = UnknownCards(hand.Concat(board));
        double wins = 0;
        for (var n = 0; n < 96; n++)
        {
            _random.Shuffle(deck);
            var missing = Math.Max(0, 5 - hand.Count - board.Count);
            // Dès le flop on évalue la main actuelle ; avant, seules les cartes
            // nécessaires à une évaluation légale sont simulées.
            var common = board.Concat(deck.Take(missing)).Select(ToCard).ToArray();
            var mine = PokerHandEvaluator.Evaluate(hand.Select(ToCard), common);
            var theirs = PokerHandEvaluator.Evaluate(deck.Skip(missing).Take(hand.Count).Select(ToCard), common);
            var comparison = mine.CompareTo(theirs);
            wins += comparison > 0 ? 1 : comparison == 0 ? .5 : 0;
        }
        return wins / 96;
    }

    private int EstimateGuess(BotObservation view, string kind)
    {
        var known = view.Hand.Concat(view.Board).Concat(view.Special!.ShownCards.Select(c => c.Card));
        var deck = UnknownCards(known);
        var target = view.Players.Single(p => p.Id == view.Special.GuessTarget);
        var frequencies = new Dictionary<int, double>();
        for (var n = 0; n < 128; n++)
        {
            _random.Shuffle(deck);
            var cards = deck.Take(target.HandCount).ToArray();
            var score = PokerHandEvaluator.Evaluate(cards.Select(ToCard), view.Board.Select(ToCard));
            // Le dernier jeton rouge signale une main forte, sans en révéler le contenu.
            var weight = 1 + (int)score.Rank;
            var values = kind == "E04" ? cards.Select(c => c.Rank).Distinct() : new[] { (int)score.Rank };
            foreach (var value in values) frequencies[value] = frequencies.GetValueOrDefault(value) + weight;
        }
        return frequencies.OrderByDescending(p => p.Value).First().Key;
    }

    private static CardSnapshot[] UnknownCards(IEnumerable<CardSnapshot> visible)
    {
        var known = visible.ToHashSet();
        return Enum.GetValues<CardColors>().SelectMany(s => Enumerable.Range(1, 13).Select(r => new CardSnapshot(s, r)))
            .Where(c => !known.Contains(c)).ToArray();
    }
    // Accepte aussi le valet sans enseigne créé par le moteur pour C07.
    private sealed record EvaluationCard(CardColors Suit, int Rank) : ICard;
    private static ICard ToCard(CardSnapshot card) => new EvaluationCard(card.Suit, card.Rank);
}
