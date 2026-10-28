using Model.Game.Hand;

namespace Model.Game;

/// <summary>Initiation locale : les complices utilisent les mêmes règles que les joueurs.</summary>
public sealed class TutorialSession
{
    public Game Game { get; } = new(new Random(47));
    public int Step { get; private set; }
    public bool Complete => Step == 16;
    public int SuggestedToken => Game.CurrentRound == 4
        ? Game.Players.OrderBy(p => PokerHandEvaluator.Evaluate(p.Hand, Game.CommunityCards))
            .Select((p, i) => (p.Id, Rank: i + 1)).First(p => p.Id == Game.Players[0].Id).Rank
        : 2;

    public TutorialSession()
    {
        Game.AddPlayer("Vous");
        Game.AddPlayer("Mila · complice");
        Game.AddPlayer("Noé · complice");
        Game.StartGame();
    }

    public bool ExpectsToken => Step is 1 or 3 or 6 or 9 or 12;
    public bool ExpectsConfirmation => Step is 4 or 7 or 10 or 13;

    public void ToggleConfirmation()
    {
        if (Step != 5) throw new InvalidOperationException("Cet exercice est disponible avant le flop.");
        var player = Game.Players[0];
        if (player.IsChoiceConfirmed) Game.WithdrawConfirmation(player.Id);
        else Game.ConfirmChoice(player.Id);
    }

    public void TakeToken(int value)
    {
        if (!ExpectsToken) throw new InvalidOperationException("Suivez d’abord la consigne de cette étape.");
        var expected = Step == 1 ? 1 : SuggestedToken;
        if (value != expected)
            throw new InvalidOperationException($"Pour cet exercice guidé, choisissez le jeton {expected}. En partie réelle, vous estimez vous-même votre force relative.");
        Game.TakeToken(Game.Players[0].Id, value);
        if (Step != 1)
        {
            var available = Game.AvailableTokens.Select(t => t.Value).ToArray();
            var companions = Game.Players.Skip(1).Where(p => p.SignalToken is null).AsEnumerable();
            if (Game.CurrentRound == 4)
                companions = companions.OrderBy(p => PokerHandEvaluator.Evaluate(p.Hand, Game.CommunityCards));
            foreach (var (player, index) in companions.Select((p, i) => (p, i)))
                Game.TakeToken(player.Id, available[index]);
        }
        Step++;
    }

    public void RevealNextHand()
    {
        if (Step != 14) throw new InvalidOperationException("Terminez d’abord les quatre tours.");
        if (Game.Phase == GamePhase.River) Game.GoToShowdown();
        if (Game.NextPlayerToRevealId is not null) Game.RevealNextHand();
    }

    public void Resolve()
    {
        if (Step != 14 || Game.Phase != GamePhase.Showdown || Game.NextPlayerToRevealId is not null)
            throw new InvalidOperationException("Révélez chaque main avant de vérifier le classement.");
        Game.ResolveHeist();
        Step = 15;
    }

    public void Continue()
    {
        if (Complete || ExpectsToken) throw new InvalidOperationException("Prenez le jeton indiqué pour continuer.");
        if (Step == 5 && !Game.AllChoicesConfirmed)
            throw new InvalidOperationException("Vous avez retiré votre confirmation : confirmez à nouveau pour révéler le flop.");
        if (ExpectsConfirmation)
        {
            foreach (var player in Game.Players) Game.ConfirmChoice(player.Id);
        }
        switch (Step)
        {
            case 2:
                Game.TakeToken(Game.Players[1].Id, 1);
                break;
            case 5:
                Game.RevealFlop();
                Game.TakeToken(Game.Players[1].Id, 2);
                break;
            case 8: Game.RevealTurn(); break;
            case 11: Game.RevealRiver(); break;
            case 14:
                throw new InvalidOperationException("Révélez les mains une par une, puis vérifiez leur ordre.");
        }
        Step++;
    }
}
