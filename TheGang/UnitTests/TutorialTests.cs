using Model.Game;

namespace UnitTests;

public class TutorialTests
{
    [Fact]
    public void CompleteLessonUsesRealRoundsAndWinsHeist()
    {
        var lesson = new TutorialSession();
        while (!lesson.Complete)
        {
            if (lesson.ExpectsToken) lesson.TakeToken(lesson.Step == 1 ? 1 : lesson.SuggestedToken);
            else if (lesson.Step == 14)
            {
                Assert.Throws<InvalidOperationException>(lesson.Resolve);
                for (var count = 1; count <= 3; count++)
                {
                    lesson.RevealNextHand();
                    Assert.Equal(count, lesson.Game.RevealedHands.Count);
                    if (count < 3) Assert.Throws<InvalidOperationException>(lesson.Resolve);
                }
                lesson.Resolve();
            }
            else lesson.Continue();
        }
        Assert.Equal(GamePhase.HeistComplete, lesson.Game.Phase);
        Assert.Equal(1, lesson.Game.SuccessfulHeists);
        Assert.Equal(3, lesson.Game.RevealedHands.Count);
        Assert.Equal(5, lesson.Game.CommunityCards.Count);
    }

    [Fact]
    public void WrongTokenLeavesLessonRecoverableAndTakeoverFreesLearner()
    {
        var lesson = new TutorialSession();
        lesson.Continue();
        Assert.Throws<InvalidOperationException>(() => lesson.TakeToken(3));
        Assert.Equal(1, lesson.Step);
        Assert.Null(lesson.Game.Players[0].SignalToken);
        lesson.TakeToken(1);
        lesson.Continue();
        Assert.Null(lesson.Game.Players[0].SignalToken);
        Assert.Equal(1, lesson.Game.Players[1].SignalToken!.Value);
        lesson.TakeToken(2);
        lesson.Continue();
        Assert.True(lesson.Game.AllChoicesConfirmed);
        lesson.ToggleConfirmation();
        Assert.False(lesson.Game.AllChoicesConfirmed);
        Assert.Throws<InvalidOperationException>(lesson.Continue);
        lesson.ToggleConfirmation();
        lesson.Continue();
        Assert.Equal(GamePhase.Flop, lesson.Game.Phase);
        Assert.All(lesson.Game.Players, p => Assert.False(p.IsChoiceConfirmed));
    }
}
