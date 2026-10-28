using Model.Game;
using Model.Game.Hand;

namespace Network;

/// <summary>Ateliers déterministes utilisant les vraies règles, avec deux complices simulés.</summary>
public sealed class SpecialTutorialSession
{
    private readonly string[] _cards;
    private int _index;
    private bool _introduced;
    private bool _reviewed;
    private bool _lockTested;
    private Guid? _bonusTarget;
    public Game Game { get; private set; } = null!;
    public GameDifficulty Difficulty { get; }
    public string CardId=>_cards[_index];
    public SpecialCardDefinition Card=>SpecialCards.Get(CardId);
    public int ExerciseNumber=>_index+1;
    public int ExerciseCount=>_cards.Length;
    public bool Complete { get; private set; }
    public bool Result=>Game.Phase is GamePhase.HeistComplete or GamePhase.Finished;
    private bool Optional=>CardId is "C01" or "C02" or "C04" or "C05" or "C07" or "C10";
    public bool PowerPending=>Optional&&!Game.CompliceUsed&&(CardId!="C02"||Game.Phase!=GamePhase.PreFlop);
    public bool CanContinue=>!_introduced || Result || !_reviewed&&Game.SetupKind is null&&Game.DiscardPlayer is null&&!PowerPending || NeedsLockTest;
    public bool NeedsLockTest=>_introduced&&_reviewed&&!_lockTested&&CardId is "E02" or "E06"&&Game.Phase==GamePhase.PreFlop;
    public int SuggestedToken
    {
        get
        {
            if(Game.Phase==GamePhase.River)
                return Game.Players.OrderBy(Score).Select((p,i)=>(p.Id,Value:i+1)).Single(p=>p.Id==Game.Players[0].Id).Value;
            if(Game.Phase==GamePhase.PreFlop&&CardId=="E03")return 1;
            if(Game.Phase==GamePhase.PreFlop&&CardId=="E07")return 3;
            return Game.AvailableTokens.First().Value;
        }
    }
    public string ActionText
    {
        get
        {
            if(Complete)return "Parcours terminé. Choisissez un autre atelier ou invitez vos amis.";
            if(!_introduced)return "Lisez l’objectif puis démarrez cet atelier.";
            if(Game.SetupKind is not null)return "Dans l’espace d’effets sous le plateau, faites votre choix privé. Mila et Noé feront le leur.";
            if(Game.DiscardPlayer is not null)return "Sous le plateau, choisissez la carte à défausser.";
            if(Game.Proposal is not null)return "Mila propose de vous montrer une carte secrète. Sous le plateau, votez pour recevoir cette information privée.";
            if(PowerPending)return "Sous le plateau, proposez le pouvoir. Vos complices simulés voteront pour l’accepter.";
            if(!_reviewed)return "Lisez la carte agrandie dans Cartes & effets, puis confirmez sa lecture.";
            if(NeedsLockTest)return "Testez la reprise du jeton sombre avec le bouton ci-dessous : le moteur doit la refuser.";
            if(Result)return "Consultez le débrief, puis passez à l’atelier suivant.";
            if(Game.Phase==GamePhase.Showdown)
            {
                if(Game.NextPlayerToRevealId is null)return "Vérifiez l’ordre des mains avec la roue du plateau.";
                if(Game.NextPlayerToRevealId==Game.GuessTarget&&CardId is "E04" or "E09")return "Sous le plateau, validez l’estimation commune avant de révéler la dernière main. "+GuessHelp;
                return "Révélez la main suivante avec la roue du plateau.";
            }
            return Game.Players[0].SignalToken is null?$"Glissez le jeton {SuggestedToken} vers votre main.":Game.AllChoicesConfirmed?"Utilisez la roue du plateau pour poursuivre.":"Confirmez votre choix dans la roue. Mila et Noé confirmeront aussi.";
        }
    }
    public string LessonText=>!_introduced
        ? $"ATELIER JOUABLE · {DifficultyRules.Name(Difficulty)}\n\n{DifficultyRules.Description(Difficulty)}\n\n{Card.Rule}\n\nCe scénario impose cette carte pour vous apprendre son effet. Les tirages d’une vraie partie suivent les règles du mode. Vos deux complices sont simulés ; vous utilisez les commandes habituelles du plateau."
        : Card.Rule+"\n\n"+(Game.PublicHints.Count>0?string.Join("\n",Game.PublicHints)+"\n\n":"")+
            (CardId is "E03" or "E07"?"Dans cet exercice, le flop déclenche volontairement le remplacement de votre main. Regardez vos cartes avant et après le flop.\n\n":"")+
            (CardId=="E08"?"Observez le tableau des jetons : chaque changement de tour efface l’historique. Essayez de mémoriser le signal de chacun.\n\n":"")+
            (CardId=="E05"?"Après le flop, les cartes du turn et de la river apparaîtront sans tour de jetons orange.\n\n":"")+
            (Game.PrivateHints(Game.Players[0].Id).Count>0?"INFORMATION PRIVÉE\n"+string.Join("\n",Game.PrivateHints(Game.Players[0].Id))+"\n\n":"")+
            "Les indications de jeton et d’estimation sont des aides du mentor réservées au tutoriel. Dans une vraie partie, vous décidez sans connaître les mains adverses.";
    public string ContinueText=>Complete?"Choisir un autre tutoriel":!_introduced?"Démarrer cet atelier":Result?(_index+1<_cards.Length?"Atelier suivant":"Terminer le parcours"):NeedsLockTest?"Tester le verrouillage":"J’ai compris l’effet";
    public string GuessHelp
    {
        get
        {
            if(Game.GuessTarget is not {} id)return "";
            var target=Game.Players.Single(p=>p.Id==id);
            return CardId=="E04"?$"Aide du mentor : choisissez {(target.Hand[0].Rank==1?"As":target.Hand[0].Rank.ToString())}. Ce renseignement ne serait pas donné en partie réelle.":$"Aide du mentor : choisissez {Game.HandCategoryName(PokerHandEvaluator.Evaluate(target.Hand,Game.CommunityCards).Rank)}.";
        }
    }
    public SpecialTutorialSession(GameDifficulty difficulty,string? onlyCard=null)
    {
        if(difficulty==GameDifficulty.Standard)throw new ArgumentException("Utilisez le tutoriel du jeu de base.");
        Difficulty=difficulty;
        _cards=onlyCard is null?SpecialCards.All.Where(c=>difficulty!=GameDifficulty.Gangster||!c.IsComplice).Where(c=>difficulty==GameDifficulty.Advanced||c.Id!="E01").Select(c=>c.Id).ToArray():new[]{onlyCard};
        if(_cards.Any(id=>!SpecialCards.All.Any(c=>c.Id==id)))throw new ArgumentException("Carte inconnue.");
        if(difficulty==GameDifficulty.Gangster&&_cards.Any(id=>id.StartsWith('C')||id=="E01")||difficulty==GameDifficulty.Pro&&_cards.Contains("E01"))throw new ArgumentException("Cette carte n’existe pas dans ce mode.");
        ResetExercise();
    }
    private Game CreateGame(int seed)
    {
        var game=new Game(new Random(seed));game.AddPlayer("Vous");game.AddPlayer("Mila · complice");game.AddPlayer("Noé · complice");
        var partner=CardId=="E02"?"E06":"E02";
        var active=Difficulty==GameDifficulty.Advanced?new[]{CardId}:new[]{partner,CardId};
        game.RestoreDifficulty(new(Difficulty,Difficulty==GameDifficulty.Pro?partner:null,active,Enumerable.Range(2,9).Where(n=>!active.Contains($"E{n:00}")).ToArray(),Enumerable.Range(1,10).ToArray()));
        game.StartGame();return game;
    }
    private void ResetExercise()
    {
        var seed=47;
        if(CardId is "E03" or "E07" or "E04" or "E09")
            for(seed=0;seed<200;seed++)
            {
                var probe=CreateGame(seed);
                foreach(var (p,i) in probe.Players.Select((p,i)=>(p,i)))probe.TakeToken(p.Id,i+1);
                foreach(var p in probe.Players)probe.ConfirmChoice(p.Id);probe.RevealFlop();
                var face=probe.CommunityCards.Any(c=>c.Rank is >=11 and <=13);
                if(CardId=="E03"&&face||CardId=="E07"&&!face)break;
                if(CardId is "E04" or "E09")
                {
                    foreach(var (p,i) in probe.Players.Select((p,i)=>(p,i)))probe.TakeToken(p.Id,i+1);
                    foreach(var p in probe.Players)probe.ConfirmChoice(p.Id);probe.RevealTurn();
                    foreach(var (p,i) in probe.Players.Select((p,i)=>(p,i)))probe.TakeToken(p.Id,i+1);
                    foreach(var p in probe.Players)probe.ConfirmChoice(p.Id);probe.RevealRiver();
                    if(probe.Players.OrderBy(p=>PokerHandEvaluator.Evaluate(p.Hand,probe.CommunityCards)).Last().Id!=probe.Players[0].Id)break;
                }
            }
        Game=CreateGame(seed);_introduced=false;_reviewed=false;_lockTested=false;_bonusTarget=null;
    }
    public void Continue()
    {
        if(Complete)throw new InvalidOperationException("Ce parcours est terminé.");
        if(!_introduced)
        {
            _introduced=true;
            if(CardId=="C01")
            {
                Game.ProposeComplice(Game.Players[1].Id,Game.Players[1].Id,Game.Players[0].Id,1,0);
                Game.VoteComplice(Game.Players[2].Id,Game.Proposal!.Id,true);
            }
            return;
        }
        if(Result){if(++_index>=_cards.Length){_index=_cards.Length-1;Complete=true;}else ResetExercise();return;}
        if(!_reviewed&&CanContinue)
        {
            _reviewed=true;
            if(CardId is "E02" or "E06")Game.TakeToken(Game.Players[1].Id,CardId=="E02"?1:3);
            return;
        }
        if(NeedsLockTest)
        {
            try{Game.TakeToken(Game.Players[0].Id,CardId=="E02"?1:3);throw new InvalidOperationException("Le verrouillage n’a pas fonctionné.");}
            catch(InvalidOperationException) when(Game.Players[0].SignalToken is null){_lockTested=true;return;}
        }
        throw new InvalidOperationException(ActionText);
    }
    private HandScore Score(Model.Players.Player player)
    {var score=PokerHandEvaluator.Evaluate(player.Hand,Game.CommunityCards);return new(score.Rank,score.Tiebreakers,player.Id==_bonusTarget);}
    public void Apply(GameActionRequest request)
    {
        if(!_introduced||Complete||Result)throw new InvalidOperationException(ActionText);
        var self=Game.Players[0];
        if(request.Action is GameAction.ChooseSpecialSetup or GameAction.ProposeComplice or GameAction.VoteComplice or GameAction.DiscardSpecial or GameAction.SubmitSpecialGuess)
        {
            HostedLobby.ApplySpecialRequest(Game,self.Id,request);
            if(request.Action==GameAction.ChooseSpecialSetup)foreach(var p in Game.Players.Skip(1))Game.ChooseSetup(p.Id,0);
            if(request.Action==GameAction.ProposeComplice&&Game.Proposal is {} proposal)
            {
                if(CardId=="C10")_bonusTarget=proposal.Target;
                foreach(var p in Game.Players.Skip(1))Game.VoteComplice(p.Id,proposal.Id,true);
                if(Game.DiscardPlayer is {} target&&target!=self.Id)Game.DiscardSpecial(target,0);
            }
            return;
        }
        if(!_reviewed||PowerPending||NeedsLockTest)throw new InvalidOperationException(ActionText);
        switch(request.Action)
        {
            case GameAction.TakeToken:
                if(request.TokenValue!=SuggestedToken)throw new InvalidOperationException($"Pour cet exercice, prenez le jeton {SuggestedToken}.");
                Game.TakeToken(self.Id,request.TokenValue!.Value);
                var bots=Game.Players.Skip(1).Where(p=>p.SignalToken is null);
                if(Game.Phase==GamePhase.River)bots=bots.OrderBy(Score);
                foreach(var p in bots)Game.TakeToken(p.Id,Game.AvailableTokens.First().Value);
                break;
            case GameAction.ConfirmChoice:foreach(var p in Game.Players)Game.ConfirmChoice(p.Id);break;
            case GameAction.WithdrawConfirmation:Game.WithdrawConfirmation(self.Id);break;
            case GameAction.AdvanceRound:
                if(Game.Phase==GamePhase.PreFlop)Game.RevealFlop();else if(Game.Phase==GamePhase.Flop)Game.RevealTurn();else if(Game.Phase==GamePhase.Turn)Game.RevealRiver();else if(Game.Phase==GamePhase.River)Game.GoToShowdown();
                break;
            case GameAction.RevealNextHand:
                if(Game.NextPlayerToRevealId==Game.GuessTarget)
                    foreach(var bot in Game.Players.Skip(1).Where(p=>p.Id!=Game.GuessTarget))
                    {
                        var last=Game.Players.Single(p=>p.Id==Game.GuessTarget);
                        if(Game.HasEffraction(4))Game.SubmitGuess(bot.Id,"E04",last.Hand[0].Rank);
                        if(Game.HasEffraction(9))Game.SubmitGuess(bot.Id,"E09",(int)PokerHandEvaluator.Evaluate(last.Hand,Game.CommunityCards).Rank);
                    }
                Game.RevealNextHand();break;
            case GameAction.ResolveHeist:Game.ResolveHeist();break;
            default:throw new InvalidOperationException(ActionText);
        }
    }
}
