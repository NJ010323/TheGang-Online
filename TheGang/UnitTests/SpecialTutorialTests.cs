using Model.Game;
using Model.Game.Hand;
using Network;

namespace UnitTests;
public class SpecialTutorialTests
{
    public static IEnumerable<object[]> Cases => Enum.GetValues<GameDifficulty>().Where(d=>d!=GameDifficulty.Standard)
        .SelectMany(d=>SpecialCards.All.Where(c=>d==GameDifficulty.Advanced||c.Id!="E01").Where(c=>d!=GameDifficulty.Gangster||!c.IsComplice).Select(c=>new object[]{d,c.Id}));
    [Theory][MemberData(nameof(Cases))]
    public void EverySpecialCardExerciseCanBeCompletedWithActualGameActions(GameDifficulty mode,string card)
    {
        var tutorial=new SpecialTutorialSession(mode,card);Complete(tutorial);
        Assert.True(tutorial.Complete);Assert.Equal(3,tutorial.Game.RevealedHands.Count);
        Assert.True(tutorial.Game.LastHeistSucceeded);
    }
    [Theory][InlineData(GameDifficulty.Advanced,20)][InlineData(GameDifficulty.Pro,19)][InlineData(GameDifficulty.Gangster,9)]
    public void CompleteDifficultyPathVisitsEveryEligibleCard(GameDifficulty mode,int count)
    {
        var tutorial=new SpecialTutorialSession(mode);Assert.Equal(count,tutorial.ExerciseCount);Complete(tutorial);Assert.True(tutorial.Complete);
    }
    private static void Complete(SpecialTutorialSession tutorial)
    {
        var guard=0;
        while(!tutorial.Complete&&guard++<2000)
        {
            if(tutorial.CanContinue){tutorial.Continue();continue;}
            var g=tutorial.Game;var self=g.Players[0];
            if(g.Proposal is {} proposal){tutorial.Apply(new(GameAction.VoteComplice){ProposalId=proposal.Id,Accepted=true});continue;}
            if(g.SetupKind is not null){tutorial.Apply(new(GameAction.ChooseSpecialSetup){CardIndex=0});continue;}
            if(g.DiscardPlayer is not null){tutorial.Apply(new(GameAction.DiscardSpecial){CardIndex=g.ForbiddenDiscardIndex==0?1:0});continue;}
            if(tutorial.PowerPending){tutorial.Apply(new(GameAction.ProposeComplice){TargetId=self.Id,RecipientId=g.Players[1].Id,Rank=1,CardIndex=0});continue;}
            if(g.Phase==GamePhase.Showdown)
            {
                if(g.NextPlayerToRevealId is null){tutorial.Apply(new(GameAction.ResolveHeist));continue;}
                if(g.NextPlayerToRevealId==g.GuessTarget)
                {
                    var target=g.Players.Single(p=>p.Id==g.GuessTarget);
                    if(g.HasEffraction(4))tutorial.Apply(new(GameAction.SubmitSpecialGuess){GuessKind="E04",Rank=target.Hand[0].Rank});
                    if(g.HasEffraction(9))tutorial.Apply(new(GameAction.SubmitSpecialGuess){GuessKind="E09",Rank=(int)PokerHandEvaluator.Evaluate(target.Hand,g.CommunityCards).Rank});
                }
                tutorial.Apply(new(GameAction.RevealNextHand));continue;
            }
            if(self.SignalToken is null)tutorial.Apply(new(GameAction.TakeToken,tutorial.SuggestedToken));
            else if(!g.AllChoicesConfirmed)tutorial.Apply(new(GameAction.ConfirmChoice));
            else tutorial.Apply(new(GameAction.AdvanceRound));
        }
        Assert.True(guard<2000,"Le tutoriel doit toujours aboutir.");
    }
}
