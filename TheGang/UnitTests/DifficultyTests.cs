using Model.Game;
using Model.Game.Hand;
using Model.Game.Tokens.TokenEnum;
using Model.Game.Card.CardEnum;
using Model.Game.Cards;
using Model.Game.Cards.Interfaces;
using Network;

namespace UnitTests;

public class DifficultyTests
{
    private static Game Create(params string[] cards)
    {
        var g=new Game(new Random(47));g.AddPlayer("Alice");g.AddPlayer("Bob");g.AddPlayer("Chloé");
        g.RestoreDifficulty(new(GameDifficulty.Advanced,null,cards,Enumerable.Range(1,10).ToArray(),Enumerable.Range(1,10).ToArray()));g.StartGame();return g;
    }
    private static void Confirm(Game g)
    {
        foreach(var (p,i) in g.Players.Select((p,i)=>(p,i)))g.TakeToken(p.Id,i+1);
        foreach(var p in g.Players)g.ConfirmChoice(p.Id);
    }
    private static void ToRiver(Game g)
    {
        if(g.Phase==GamePhase.PreFlop){Confirm(g);g.RevealFlop();}
        if(g.Phase==GamePhase.Flop){Confirm(g);g.RevealTurn();}
        if(g.Phase==GamePhase.Turn){Confirm(g);g.RevealRiver();}
    }
    private static void Resolve(Game g,bool wrongGuess=false)
    {
        ToRiver(g);
        var order=g.Players.OrderBy(p=>PokerHandEvaluator.Evaluate(p.Hand,g.CommunityCards)).ToArray();
        for(var i=0;i<order.Length;i++)g.TakeToken(order[i].Id,i+1);
        foreach(var p in g.Players)g.ConfirmChoice(p.Id);
        g.GoToShowdown();
        while(g.NextPlayerToRevealId is {} next)
        {
            if(next==g.GuessTarget)
            {
                var target=g.Players.Single(p=>p.Id==next);
                foreach(var p in g.Players.Where(p=>p.Id!=next))
                {
                    if(g.HasEffraction(4))g.SubmitGuess(p.Id,"E04",wrongGuess?Enumerable.Range(1,13).First(n=>!target.Hand.Any(c=>c.Rank==n)):target.Hand[0].Rank);
                    if(g.HasEffraction(9))g.SubmitGuess(p.Id,"E09",(int)PokerHandEvaluator.Evaluate(target.Hand,g.CommunityCards).Rank);
                }
            }
            g.RevealNextHand();
        }
        g.ResolveHeist();
    }
    private static void Activate(Game g,Guid target,Guid? recipient=null,int rank=1,int index=0)
    {
        g.ProposeComplice(g.Players[0].Id,target,recipient,rank,index);
        var id=g.Proposal!.Id;
        foreach(var p in g.Players.Skip(1))g.VoteComplice(p.Id,id,true);
    }
    [Theory]
    [InlineData(GameDifficulty.Standard,0,3)]
    [InlineData(GameDifficulty.Advanced,0,3)]
    [InlineData(GameDifficulty.Pro,1,3)]
    [InlineData(GameDifficulty.Gangster,2,2)]
    public void ModesStartWithTheirOfficialCardsAndAlarmLimits(GameDifficulty mode,int count,int limit)
    {
        var g=new Game(new Random(47));foreach(var n in new[]{"A","B","C"})g.AddPlayer(n);g.ConfigureDifficulty(mode);g.StartGame();
        Assert.Equal(count,g.ActiveSpecialCards.Count);Assert.Equal(limit,g.AlarmLimit);
        if(mode is GameDifficulty.Pro or GameDifficulty.Gangster)Assert.DoesNotContain("E01",g.ActiveSpecialCards);
        Assert.Throws<InvalidOperationException>(()=>g.ConfigureDifficulty(GameDifficulty.Standard));
    }
    [Fact] public void AdvancedSuccessAddsOneTemporaryEffractionAndRematchKeepsPileProgress()
    {
        var g=Create();Resolve(g);Assert.True(g.LastHeistSucceeded);g.StartNextHeist();Assert.Equal(new[]{"E01"},g.ActiveSpecialCards);Assert.Equal(GamePhase.Flop,g.Phase);
        Resolve(g);g.StartNextHeist();Assert.Equal(new[]{"E02"},g.ActiveSpecialCards);
    }
    [Theory][InlineData("E02",1)][InlineData("E06",3)]
    public void DarkTokensCannotChangeOwnerButRedTokensCan(string id,int token)
    {
        var g=Create(id);g.TakeToken(g.Players[0].Id,token);
        Assert.Throws<InvalidOperationException>(()=>g.TakeToken(g.Players[1].Id,token));
        Assert.Equal(token,g.Players[0].SignalToken!.Value);
        foreach(var (p,i) in g.Players.Skip(1).Select((p,i)=>(p,i)))g.TakeToken(p.Id,Enumerable.Range(1,3).Where(n=>n!=token).ElementAt(i));
        foreach(var p in g.Players)g.ConfirmChoice(p.Id);g.RevealFlop();
        Confirm(g);g.RevealTurn();Confirm(g);g.RevealRiver();
        g.TakeToken(g.Players[0].Id,token);g.TakeToken(g.Players[1].Id,token);Assert.Null(g.Players[0].SignalToken);
    }
    [Fact] public void FlightSkipsOrangeTokensAndRevealsBothRemainingCards()
    {
        var g=Create("E05");Confirm(g);g.RevealFlop();Confirm(g);g.RevealTurn();Assert.Equal(GamePhase.River,g.Phase);Assert.Equal(5,g.CommunityCards.Count);Assert.All(g.Players,p=>Assert.False(p.Signals.ContainsKey(TokenColors.Orange)));
    }
    [Fact] public void PowerOutageErasesPreviousSignals()
    {
        var g=Create("E08");Confirm(g);g.RevealFlop();Assert.All(g.Players,p=>Assert.Empty(p.Signals));
    }
    [Theory][InlineData("E03",true,0)][InlineData("E07",false,2)]
    public void FlopSensorsReplaceOnlyTheCorrectHandEvenWithPowerOutage(string card,bool trigger,int player)
    {
        var found=false;
        for(var seed=0;seed<40&&!found;seed++)
        {
            var g=new Game(new Random(seed));foreach(var n in new[]{"A","B","C"})g.AddPlayer(n);
            g.RestoreDifficulty(new(GameDifficulty.Pro,"E08",new[]{"E08",card},new[]{2,3,4,5,6,7,9,10},new[]{1,2,3,4,5,6,7,8,9,10}));g.StartGame();
            var hands=g.Players.Select(p=>p.Hand.ToArray()).ToArray();Confirm(g);g.RevealFlop();
            if(g.CommunityCards.Any(c=>c.Rank is >=11 and <=13)!=trigger)continue;
            found=true;Assert.NotEqual(hands[player],g.Players[player].Hand);Assert.Single(g.PublicHints);
            Assert.All(g.Players,p=>Assert.Empty(p.Signals));
        }
        Assert.True(found);
    }
    [Fact] public void CamerasDealThreeCardsAndEvaluatorAcceptsEightAvailableCards()
    {
        var g=Create("E10");Assert.All(g.Players,p=>Assert.Equal(3,p.Hand.Count));Resolve(g);Assert.Equal(3,g.RevealedHands.Count);
    }
    [Fact] public void RetinalScanRequiresUnanimityExcludesTargetAndCanFailCorrectOrder()
    {
        var g=Create("E04");ToRiver(g);Confirm(g);g.GoToShowdown();g.RevealNextHand();g.RevealNextHand();
        var target=g.GuessTarget!.Value;var others=g.Players.Where(p=>p.Id!=target).ToArray();
        Assert.Throws<InvalidOperationException>(g.RevealNextHand);
        Assert.Throws<InvalidOperationException>(()=>g.SubmitGuess(target,"E04",2));
        g.SubmitGuess(others[0].Id,"E04",2);g.SubmitGuess(others[1].Id,"E04",3);Assert.Throws<InvalidOperationException>(g.RevealNextHand);
        var wrong=Enumerable.Range(1,13).First(n=>!g.Players.Single(p=>p.Id==target).Hand.Any(c=>c.Rank==n));
        foreach(var p in others)g.SubmitGuess(p.Id,"E04",wrong);
        g.RevealNextHand();Assert.False(g.ResolveHeist());
    }
    [Fact] public void FingerprintGuessUsesActualCategory()
    {
        var g=Create("E09");Resolve(g);Assert.True(g.LastHeistSucceeded);
    }
    [Fact] public void GangsterLosesAfterTwoAlarmsAndReplacesSmallestCard()
    {
        var g=Create("E04","E09");var state=g.ExportDifficulty() with{Difficulty=GameDifficulty.Gangster};
        g.ReturnToLobby();g.RestoreDifficulty(state);g.StartGame();Resolve(g,true);Assert.Equal(1,g.FailedHeists);
        g.StartNextHeist();Assert.DoesNotContain("E04",g.ActiveSpecialCards);Assert.Contains("E09",g.ActiveSpecialCards);Assert.Equal(2,g.ActiveSpecialCards.Count);
        // Force a second incorrect fingerprint prediction.
        ToRiver(g);Confirm(g);g.GoToShowdown();g.RevealNextHand();g.RevealNextHand();
        var last=g.Players.Single(p=>p.Id==g.GuessTarget);var rank=PokerHandEvaluator.Evaluate(last.Hand,g.CommunityCards).Rank;
        foreach(var p in g.Players.Where(p=>p.Id!=last.Id))g.SubmitGuess(p.Id,"E09",rank==HandRank.HighCard?2:1);
        g.RevealNextHand();g.ResolveHeist();Assert.Equal(GameResult.Lost,g.Result);Assert.Equal(2,g.FailedHeists);
    }
    [Theory][InlineData("C03")][InlineData("C08")]
    public void AutomaticAnnouncementsAreComputedForEveryone(string card)
    {
        var g=Create(card);Assert.Equal(3,g.PublicHints.Count);
        Assert.All(g.Players,p=>Assert.Contains(g.PublicHints,h=>h.StartsWith(p.Name)));
    }
    [Fact] public void InformantRequiresUnanimousVoteAndSharesOnlyOneCardWithRecipient()
    {
        var g=Create("C01");var a=g.Players[0];var b=g.Players[1];var c=g.Players[2];
        g.ProposeComplice(a.Id,a.Id,b.Id,1,0);var id=g.Proposal!.Id;g.VoteComplice(b.Id,id,true);
        Assert.Empty(g.PrivateHints(b.Id));g.VoteComplice(c.Id,id,true);
        Assert.Single(g.PrivateHints(b.Id));Assert.Empty(g.PrivateHints(a.Id));Assert.Empty(g.PrivateHints(c.Id));Assert.True(g.CompliceUsed);
        Assert.Throws<InvalidOperationException>(()=>g.VoteComplice(c.Id,id,true));
    }
    [Theory][InlineData("C02")][InlineData("C04")][InlineData("C10")]
    public void OptionalComplicesExecuteOnlyAfterEveryoneAccepts(string card)
    {
        var g=Create(card);Activate(g,g.Players[0].Id);Assert.True(g.CompliceUsed);Assert.Single(g.PublicHints);
    }
    [Theory][InlineData("C05")][InlineData("C07")]
    public void DrawingRequiresPrivateDiscardAndValetCannotDiscardItself(string card)
    {
        var g=Create(card);var p=g.Players[0];Activate(g,p.Id);Assert.Equal(3,p.Hand.Count);
        Assert.Throws<InvalidOperationException>(()=>g.TakeToken(p.Id,1));
        Assert.Throws<InvalidOperationException>(()=>g.DiscardSpecial(g.Players[1].Id,0));
        if(card=="C07")Assert.Throws<ArgumentOutOfRangeException>(()=>g.DiscardSpecial(p.Id,2));
        g.DiscardSpecial(p.Id,card=="C05"?2:0);Assert.Equal(2,p.Hand.Count);Assert.Null(g.DiscardPlayer);
        if(card=="C07")
        {
            Assert.Contains(p.Hand,c=>c.Rank==11&&!Enum.IsDefined(c.Suit));
            foreach(var viewer in g.Players)
            {var known=Assert.Single(HostedLobby.SpecialSnapshotFor(g,viewer.Id).ShownCards);Assert.Equal(p.Id,known.SourceId);Assert.Equal(1,known.CardIndex);Assert.Equal(11,known.Card.Rank);}
        }
    }
    [Fact] public void CoordinatorExchangesSimultaneouslyAndNeverTakesCardsFromDeck()
    {
        var g=Create("C06");var before=g.Players.Select(p=>p.Hand.ToArray()).ToArray();
        g.ChooseSetup(g.Players[0].Id,0);Assert.Equal(before[0],g.Players[0].Hand);Assert.Throws<InvalidOperationException>(()=>g.TakeToken(g.Players[0].Id,1));
        foreach(var p in g.Players.Skip(1))g.ChooseSetup(p.Id,0);
        for(var i=0;i<3;i++){Assert.Same(before[(i+2)%3][0],g.Players[i].Hand[0]);Assert.Same(before[i][1],g.Players[i].Hand[1]);}
        for(var i=0;i<3;i++)
        {
            var known=Assert.Single(HostedLobby.SpecialSnapshotFor(g,g.Players[i].Id).ShownCards);
            Assert.Equal(g.Players[(i+1)%3].Id,known.SourceId);Assert.Equal(0,known.CardIndex);
            Assert.Equal(before[i][0].Rank,known.Card.Rank);Assert.Equal(before[i][0].Suit,known.Card.Suit);
        }
        Assert.Null(g.SetupKind);
    }
    [Fact] public void SwindlerWaitsForEveryoneAndRedistributesOnlyOriginalCards()
    {
        var g=Create("C09");var before=g.Players.SelectMany(p=>p.Hand).ToArray();
        foreach(var p in g.Players)g.ChooseSetup(p.Id,0);
        Assert.Equal(before.OrderBy(c=>c.ToString()),g.Players.SelectMany(p=>p.Hand).OrderBy(c=>c.ToString()));
        Assert.All(g.Players,p=>Assert.Single(g.PrivateHints(p.Id)));Assert.Null(g.SetupKind);
    }
    [Fact] public void BigArmsWinsWithinCategoryButNotAgainstHigherCategory()
    {
        var weak=new HandScore(HandRank.OnePair,new[]{2,8,7,6},true);var high=new HandScore(HandRank.OnePair,new[]{14,13,12,11});
        Assert.True(weak.CompareTo(high)>0);Assert.True(weak.CompareTo(new(HandRank.TwoPair,new[]{3,2,4}))<0);
    }
    [Fact] public void RefusingVoteDoesNotConsumeCompliceOrLeakPrivateCards()
    {
        var g=Create("C01");g.ProposeComplice(g.Players[0].Id,g.Players[0].Id,g.Players[1].Id,1,0);
        g.VoteComplice(g.Players[2].Id,g.Proposal!.Id,false);Assert.False(g.CompliceUsed);Assert.Null(g.Proposal);Assert.Empty(g.PrivateHints(g.Players[1].Id));
    }
    [Fact] public void HostIsTheOnlyPlayerWhoCanChangeDifficultyAndSnapshotsExposeNoHiddenHands()
    {
        var lobby=new HostedLobby("A",3);var b=lobby.Join("B",lobby.InvitationSecret);lobby.Join("C",lobby.InvitationSecret);
        Assert.ThrowsAny<Exception>(()=>lobby.ChangeDifficulty(lobby.Code,b.SessionToken,GameDifficulty.Pro));
        lobby.ChangeDifficulty(lobby.Code,lobby.HostIdentity.SessionToken,GameDifficulty.Pro);
        var game=lobby.Start(lobby.Code,lobby.HostIdentity.SessionToken);
        Assert.Equal(GameDifficulty.Pro,game.Special!.Difficulty);Assert.All(game.Players.Skip(1),p=>Assert.Empty(p.Hand));
        Assert.Throws<InvalidOperationException>(()=>lobby.ChangeDifficulty(lobby.Code,lobby.HostIdentity.SessionToken,GameDifficulty.Standard));
        Assert.Throws<InvalidOperationException>(()=>lobby.Apply(lobby.Code,b.SessionToken,new(GameAction.ChooseSpecialSetup){ExpectedGameId=Guid.NewGuid(),ExpectedHeist=1}));
    }

    [Fact] public void ProKeepsPermanentCardAndAddsOneTemporaryCard()
    {
        var g=new Game(new Random(47));foreach(var n in new[]{"A","B","C"})g.AddPlayer(n);g.ConfigureDifficulty(GameDifficulty.Pro);g.StartGame();
        var permanent=g.PermanentEffraction;Resolve(g);g.StartNextHeist();
        Assert.Equal(permanent,g.PermanentEffraction);Assert.Contains(permanent!,g.ActiveSpecialCards);Assert.Equal(2,g.ActiveSpecialCards.Count);Assert.DoesNotContain("E01",g.ActiveSpecialCards);
    }
    [Fact] public void FailedHeistGivesACompliceForOnlyOneHeist()
    {
        var g=Create("E04");Resolve(g,true);g.StartNextHeist();Assert.Equal(new[]{"C01"},g.ActiveSpecialCards);
        Resolve(g);g.StartNextHeist();Assert.Equal(new[]{"E01"},g.ActiveSpecialCards);
    }
    [Fact] public void MigrationAfterCompletedHeistDrawsTheNextCardInsteadOfReusingThePreviousOne()
    {
        var g=Create();Resolve(g);var state=g.ExportDifficulty();Assert.False(state.ResumeActive);
        var resumed=new Game(new Random(4));foreach(var n in new[]{"B","C","D"})resumed.AddPlayer(n);resumed.RestoreDifficulty(state);resumed.ContinueAfterHostMigration(2,1,0);
        Assert.Equal(new[]{"E01"},resumed.ActiveSpecialCards);Assert.Equal(GamePhase.Flop,resumed.Phase);
    }
    [Fact] public void MigrationPreservesProModeAndPermanentEffraction()
    {
        var old=new HostedLobby("A",4);foreach(var n in new[]{"B","C","D"})old.Join(n,old.InvitationSecret);
        old.ChangeDifficulty(old.Code,old.HostIdentity.SessionToken,GameDifficulty.Pro);var before=old.Start(old.Code,old.HostIdentity.SessionToken);
        var migration=old.BeginHostMigration(old.Code,old.HostIdentity.SessionToken);
        var next=new HostedLobby("B",3);
        next.ConfigureHostMigration(next.Code,next.HostIdentity.SessionToken,new(migration.ExpectedPlayers,true,1,0,0,null){DifficultyState=migration.DifficultyState});
        next.Join("C",next.InvitationSecret);next.Join("D",next.InvitationSecret);
        var after=next.GetGame(next.Code,next.HostIdentity.SessionToken);
        Assert.Equal(GameDifficulty.Pro,after.Special!.Difficulty);Assert.Equal(before.Special!.Permanent,after.Special.Permanent);Assert.Equal(before.Special.ActiveCards,after.Special.ActiveCards);
    }
    [Fact] public void NetworkInformantNeverSendsPrivateHintToOtherPlayers()
    {
        var lobby=new HostedLobby("B",3);
        lobby.ConfigureHostMigration(lobby.Code,lobby.HostIdentity.SessionToken,new(new[]{"B","C","D"},true,2,0,1,null)
        {DifficultyState=new(GameDifficulty.Advanced,null,new[]{"C01"},Enumerable.Range(1,10).ToArray(),Enumerable.Range(2,9).Append(1).ToArray())});
        var c=lobby.Join("C",lobby.InvitationSecret);var d=lobby.Join("D",lobby.InvitationSecret);var b=lobby.HostIdentity;
        var game=lobby.GetGame(lobby.Code,b.SessionToken);
        var proposal=lobby.Apply(lobby.Code,b.SessionToken,new(GameAction.ProposeComplice)
        {ExpectedGameId=game.GameId,ExpectedHeist=2,TargetId=b.PlayerId,RecipientId=c.PlayerId,CardIndex=0});
        var vote=proposal.Special!.Proposal!.Id;
        foreach(var p in new[]{c,d})lobby.Apply(lobby.Code,p.SessionToken,new(GameAction.VoteComplice){ExpectedGameId=game.GameId,ExpectedHeist=2,ProposalId=vote,Accepted=true});
        Assert.Single(lobby.GetGame(lobby.Code,c.SessionToken).Special!.PrivateHints);
        var shown=Assert.Single(lobby.GetGame(lobby.Code,c.SessionToken).Special!.ShownCards);
        Assert.Equal(0,shown.CardIndex);Assert.Equal(b.PlayerId,shown.SourceId);Assert.Equal(game.Players.Single(p=>p.Id==b.PlayerId).Hand[0],shown.Card);
        foreach(var other in new[]{b,d})Assert.Empty(lobby.GetGame(lobby.Code,other.SessionToken).Special!.ShownCards);

        foreach(var p in new[]{b,d})Assert.Empty(lobby.GetGame(lobby.Code,p.SessionToken).Special!.PrivateHints);
        foreach(var p in new[]{b,c,d})Assert.All(lobby.GetGame(lobby.Code,p.SessionToken).Players.Where(x=>x.Id!=p.PlayerId),x=>Assert.Empty(x.Hand));
    }
    [Fact] public void UnsuitedValetCannotMakeAFlushAndCanBeFourValetsKicker()
    {
        var g=Create("C07");Activate(g,g.Players[0].Id);var valet=g.Players[0].Hand.Last();
        var suited=new[]{2,4,6,8}.Select(n=>(ICard)new Card(CardColors.Coeur,n)).ToArray();
        Assert.NotEqual(HandRank.Flush,PokerHandEvaluator.Evaluate(new[]{valet},suited).Rank);
        var jacks=Enum.GetValues<CardColors>().Select(s=>(ICard)new Card(s,11)).ToArray();
        var score=PokerHandEvaluator.Evaluate(new[]{valet,jacks[0]},jacks.Skip(1).Concat(new[]{new Card(CardColors.Coeur,2),new Card(CardColors.Pique,3)}));
        Assert.Equal(HandRank.FourOfKind,score.Rank);Assert.Equal(new[]{11,11},score.Tiebreakers);
    }
    [Fact] public void BigArmsBonusIsIncludedInRevealedScores()
    {
        var g=Create("C10");var target=g.Players[0];Activate(g,target.Id);ToRiver(g);Confirm(g);g.GoToShowdown();
        while(g.NextPlayerToRevealId is not null)g.RevealNextHand();
        Assert.True(g.RevealedHands.Single(h=>h.PlayerId==target.Id).Score.DominatesCategory);
        Assert.All(g.RevealedHands.Where(h=>h.PlayerId!=target.Id),h=>Assert.False(h.Score.DominatesCategory));
    }
}
