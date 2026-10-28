using Model.Game.Cards.Interfaces;
using Model.Game.Hand;
using Model.Game.Tokens.TokenEnum;
using Model.Game.Card.CardEnum;

namespace Model.Game;

public sealed record DifficultyState(GameDifficulty Difficulty, string? Permanent, string[] Active, int[] Effractions, int[] Complices,
    bool ResumeActive=true, bool? PreviousSucceeded=null);
public sealed record CompliceProposal(Guid Id, Guid Target, Guid? Recipient, int Rank, int CardIndex, Guid[] Accepted);

public partial class Game
{
    private readonly Random _specialRandom;
    private Queue<int> _effractions = new(Enumerable.Range(1,10));
    private Queue<int> _complices = new(Enumerable.Range(1,10));
    private readonly List<string> _activeSpecialCards = [];
    private string? _permanentEffraction;
    private bool _restoreSpecials;
    private bool? _restoredPrevious;
    private bool _compliceUsed;
    private Guid? _bigArmsPlayer;
    private Guid? _discardPlayer;
    private int _forbiddenDiscard = -1;
    private readonly Dictionary<Guid,int> _setupChoices = [];
    private readonly Dictionary<(Guid Player,string Kind),int> _guesses = [];
    private readonly Dictionary<Guid,List<string>> _privateHints = [];
    private readonly List<string> _publicHints = [];
    public GameDifficulty Difficulty { get; private set; }
    public int AlarmLimit => Difficulty == GameDifficulty.Gangster ? 2 : 3;
    public IReadOnlyList<string> ActiveSpecialCards => _activeSpecialCards.ToArray();
    public string? PermanentEffraction => _permanentEffraction;
    public bool CompliceUsed => _compliceUsed;
    public CompliceProposal? Proposal { get; private set; }
    public Guid? DiscardPlayer => _discardPlayer;
    public int ForbiddenDiscardIndex => _forbiddenDiscard;
    public IReadOnlyList<string> PublicHints => _publicHints.ToArray();
    public string? SpecialFailureReason { get; private set; }
    public IReadOnlyList<string> PrivateHints(Guid viewer) => _privateHints.TryGetValue(viewer, out var hints) ? hints.ToArray() : [];
    public string? SetupKind { get; private set; }
    public bool SetupChosen(Guid viewer) => _setupChoices.ContainsKey(viewer);
    public Guid? GuessTarget => _showdownOrder.LastOrDefault()?.Id;
    public IReadOnlyDictionary<string,int> Guesses(Guid viewer) => _guesses.Where(g => g.Key.Player == viewer).ToDictionary(g=>g.Key.Kind,g=>g.Value);
    public int GuessVotes(string kind) => _guesses.Where(g=>g.Key.Kind==kind).Count();
    public bool HasEffraction(int number) => _activeSpecialCards.Contains($"E{number:00}");
    private bool HasComplice(int number) => _activeSpecialCards.Contains($"C{number:00}");
    public bool IsTokenLocked(int value) => Phase is GamePhase.PreFlop or GamePhase.Flop or GamePhase.Turn &&
        (HasEffraction(2) && value == 1 || HasEffraction(6) && value == _players.Count);

    public void ConfigureDifficulty(GameDifficulty difficulty)
    {
        EnsurePhase(GamePhase.WaitingForPlayers);
        if (!Enum.IsDefined(difficulty)) throw new ArgumentOutOfRangeException(nameof(difficulty));
        if (Difficulty!=difficulty)
        {
            _effractions=new(Enumerable.Range(1,10)); _complices=new(Enumerable.Range(1,10));
        }
        Difficulty = difficulty;
    }
    public DifficultyState ExportDifficulty() => new(Difficulty,_permanentEffraction,_activeSpecialCards.ToArray(),_effractions.ToArray(),_complices.ToArray(),Phase!=GamePhase.HeistComplete,LastHeistSucceeded);
    public void RestoreDifficulty(DifficultyState state)
    {
        if (state.Active is null || state.Effractions is null || state.Complices is null ||
            state.Active.Length > 2 || state.Active.Distinct().Count()!=state.Active.Length || state.Active.Any(id=>!SpecialCards.All.Any(c=>c.Id==id)) ||
            state.Effractions.Any(n=>n<1 || n>10) || state.Complices.Any(n=>n<1 || n>10) ||
            state.Effractions.Distinct().Count()!=state.Effractions.Length || state.Complices.Distinct().Count()!=state.Complices.Length ||
            (state.Permanent is not null && (!state.Active.Contains(state.Permanent) || !state.Permanent.StartsWith('E'))))
            throw new ArgumentException("État de difficulté invalide.");
        ConfigureDifficulty(state.Difficulty);
        _permanentEffraction=state.Permanent;
        _activeSpecialCards.Clear(); _activeSpecialCards.AddRange(state.Active);
        _effractions=new(state.Effractions); _complices=new(state.Complices);
        _restoreSpecials=state.ResumeActive; _restoredPrevious=state.PreviousSucceeded;
    }
    private int DrawSpecial(Queue<int> queue, bool complice)
    {
        if (queue.Count==0) throw new InvalidOperationException("Pile spéciale vide.");
        var result=queue.Dequeue(); queue.Enqueue(result);
        return result;
    }
    private void PrepareSpecialCards()
    {
        _compliceUsed=false; _bigArmsPlayer=null; _discardPlayer=null; _forbiddenDiscard=-1;
        Proposal=null; SetupKind=null; SpecialFailureReason=null; _setupChoices.Clear(); _guesses.Clear(); _privateHints.Clear(); _publicHints.Clear();
        if (_restoreSpecials) { _restoreSpecials=false; return; }
        if (HeistNumber==1)
        {
            _activeSpecialCards.Clear(); _permanentEffraction=null;
            if (Difficulty is GameDifficulty.Pro or GameDifficulty.Gangster)
            {
                var candidates=Enumerable.Range(2,9).ToArray(); _specialRandom.Shuffle(candidates);
                if (Difficulty==GameDifficulty.Pro)
                {
                    _permanentEffraction=$"E{candidates[0]:00}"; _activeSpecialCards.Add(_permanentEffraction);
                    _effractions=new(Enumerable.Range(2,9).Where(n=>n!=candidates[0]));
                }
                else
                {
                    _activeSpecialCards.AddRange(candidates.Take(2).Select(n=>$"E{n:00}"));
                    _effractions=new(candidates.Skip(2));
                }
            }
            return;
        }
        if (Difficulty==GameDifficulty.Standard) { _activeSpecialCards.Clear(); return; }
        if (Difficulty==GameDifficulty.Gangster)
        {
            var discarded=_activeSpecialCards.OrderBy(id=>id).First(); _activeSpecialCards.Remove(discarded);
            var next=_effractions.Dequeue(); _activeSpecialCards.Add($"E{next:00}");
            _effractions.Enqueue(int.Parse(discarded[1..]));
        }
        else
        {
            _activeSpecialCards.Clear();
            if (_permanentEffraction is not null) _activeSpecialCards.Add(_permanentEffraction);
            _activeSpecialCards.Add((LastHeistSucceeded??_restoredPrevious)==true ? $"E{DrawSpecial(_effractions,false):00}" : $"C{DrawSpecial(_complices,true):00}");
            _restoredPrevious=null;
        }
    }
    private void ApplyInitialSpecialEffects()
    {
        if (HasEffraction(10)) foreach(var player in _players) player.ReceiveCard(_deck.DealCard());
        if (HasComplice(3)) foreach(var p in _players) _publicHints.Add($"{p.Name} : {p.Hand.Count(c=>c.Rank is >=11 and <=13)} tête(s).");
        if (HasComplice(8)) foreach(var p in _players) _publicHints.Add($"{p.Name} : somme {p.Hand.Sum(c=>c.Rank==1?11:Math.Min(c.Rank,10))}.");
        if (HasComplice(3)||HasComplice(8)) _compliceUsed=true;
        if (HasComplice(6)) SetupKind="Coordinateur";
        if (HasComplice(9)) SetupKind="Arnaqueuse";
        if (HasEffraction(1)) { DealCommunityCards(3); BeginSignalRound(GamePhase.Flop,2,TokenColors.Yellow); ApplyFlopEffects(); }
    }
    private void ApplyFlopEffects()
    {
        var face=_communityCards.Take(3).Any(c=>c.Rank is >=11 and <=13);
        int? value=HasEffraction(3)&&face ? 1 : HasEffraction(7)&&!face ? _players.Count : null;
        var player=_players.FirstOrDefault(p=>p.Signals.TryGetValue(TokenColors.White,out var t)&&t.Value==value);
        if (player is not null)
        {
            player.ReplaceHand(new[]{_deck.DealCard(),_deck.DealCard()});
            _publicHints.Add($"{player.Name} a dû défausser sa main et recevoir deux nouvelles cartes au flop.");
        }
    }
    private void EnsureSpecialSetupComplete()
    {
        if (SetupKind is not null || _discardPlayer is not null) throw new InvalidOperationException("Terminez l’effet obligatoire de la carte Complice avant de jouer les jetons.");
    }
    public void ChooseSetup(Guid actor,int cardIndex)
    {
        var player=FindPlayer(actor);
        if (SetupKind is null || Phase!=GamePhase.PreFlop || _setupChoices.ContainsKey(actor)) throw new InvalidOperationException("Aucun choix initial disponible.");
        if (SetupKind=="Coordinateur" && (cardIndex<0 || cardIndex>=player.Hand.Count)) throw new ArgumentOutOfRangeException(nameof(cardIndex));
        _setupChoices[actor]=cardIndex;
        if (_setupChoices.Count!=_players.Count) return;
        if (SetupKind=="Coordinateur")
        {
            var selected=_players.Select(p=>p.Hand[_setupChoices[p.Id]]).ToArray();
            for(var i=0;i<_players.Count;i++)
            {
                var p=_players[i]; var cards=p.Hand.ToArray(); cards[_setupChoices[p.Id]]=selected[(i+_players.Count-1)%_players.Count]; p.ReplaceHand(cards);
            }
            _publicHints.Add("Coordinateur : les cartes choisies sont passées simultanément au voisin de gauche.");
        }
        else
        {
            foreach(var p in _players) _privateHints[p.Id]=new(){"Votre ancienne main : "+string.Join(", ",p.Hand)};
            var cards=_players.SelectMany(p=>p.Hand).ToArray(); _specialRandom.Shuffle(cards); var offset=0;
            foreach(var p in _players) { var count=p.Hand.Count; p.ReplaceHand(cards.Skip(offset).Take(count)); offset+=count; }
            _publicHints.Add("Arnaqueuse : seules les cartes personnelles ont été mélangées et redistribuées.");
        }
        SetupKind=null; _compliceUsed=true; ResetConfirmations();
    }
    public void ProposeComplice(Guid actor,Guid target,Guid? recipient,int rank,int cardIndex)
    {
        EnsureSignalRoundOpen(); EnsureSpecialSetupComplete(); FindPlayer(actor); var player=FindPlayer(target);
        if (_compliceUsed || Proposal is not null) throw new InvalidOperationException("Le Complice est utilisé ou un vote est déjà en cours.");
        var id=_activeSpecialCards.SingleOrDefault(id=>id.StartsWith('C')) ?? throw new InvalidOperationException("Aucun Complice actif.");
        if (id is not ("C01" or "C02" or "C04" or "C05" or "C07" or "C10")) throw new InvalidOperationException("Ce Complice est automatique.");
        if (id=="C01")
        {
            if (recipient is null || recipient==target) throw new ArgumentException("Choisissez un autre destinataire.");
            FindPlayer(recipient.Value);
            if (cardIndex<0 || cardIndex>=player.Hand.Count) throw new ArgumentOutOfRangeException(nameof(cardIndex));
            if (actor!=target) throw new InvalidOperationException("Seul le propriétaire peut proposer sa carte secrète.");
        }
        if (id=="C04" && rank is <1 or >13) throw new ArgumentOutOfRangeException(nameof(rank));
        Proposal=new(Guid.NewGuid(),target,recipient,rank,cardIndex,new[]{actor});
        ResetConfirmations();
    }
    public void VoteComplice(Guid actor,Guid proposalId,bool accept)
    {
        FindPlayer(actor);
        if (Proposal is not { } proposal || proposal.Id!=proposalId) throw new InvalidOperationException("Ce vote n’est plus actif.");
        EnsureSignalRoundOpen();
        if(!accept) { Proposal=null; return; }
        var votes=proposal.Accepted.Append(actor).Distinct().ToArray(); Proposal=proposal with {Accepted=votes};
        if(votes.Length!=_players.Count) return;
        var p=FindPlayer(proposal.Target); var id=_activeSpecialCards.Single(id=>id.StartsWith('C'));
        switch(id)
        {
            case "C01":
                var recipient=proposal.Recipient!.Value;
                if(!_privateHints.ContainsKey(recipient))_privateHints[recipient]=[];
                _privateHints[recipient].Add($"Informateur — {p.Name} vous montre : {p.Hand[proposal.CardIndex]}."); break;
            case "C02":
                var cards=p.Hand.Concat(_communityCards).ToArray();
                var category=cards.Length>=5?PokerHandEvaluator.Evaluate(p.Hand,_communityCards).Rank:
                    cards.GroupBy(c=>c.Rank).Max(g=>g.Count())==3?HandRank.ThreeOfKind:
                    cards.Select(c=>c.Rank).Distinct().Count()<cards.Length?HandRank.OnePair:HandRank.HighCard;
                _publicHints.Add($"Conductrice — {p.Name} : {HandCategoryName(category)}."); break;
            case "C04":_publicHints.Add($"Cerveau de la bande — {p.Name} : {p.Hand.Count(c=>c.Rank==proposal.Rank)} carte(s) de rang {(proposal.Rank==1?"As":proposal.Rank.ToString())}.");break;
            case "C05":p.ReceiveCard(_deck.DealCard());_discardPlayer=p.Id;_forbiddenDiscard=-1;break;
            case "C07":p.ReceiveCard(new UnsuitedJack());_discardPlayer=p.Id;_forbiddenDiscard=p.Hand.Count-1;break;
            case "C10":_bigArmsPlayer=p.Id;_publicHints.Add($"Gros bras — {p.Name} gagne les comparaisons entre mains de la même catégorie.");break;
        }
        _compliceUsed=true; Proposal=null; ResetConfirmations();
    }
    public void DiscardSpecial(Guid actor,int index)
    {
        if(_discardPlayer!=actor)throw new InvalidOperationException("Ce joueur ne doit pas défausser.");
        var player=FindPlayer(actor);
        if(index<0||index>=player.Hand.Count||index==_forbiddenDiscard)throw new ArgumentOutOfRangeException(nameof(index),"Choisissez une carte autorisée à défausser.");
        player.DiscardCard(index);_discardPlayer=null;_forbiddenDiscard=-1;ResetConfirmations();
    }
    public void SubmitGuess(Guid actor,string kind,int value)
    {
        EnsurePhase(GamePhase.Showdown); FindPlayer(actor);
        if(actor==GuessTarget || NextPlayerToRevealId!=GuessTarget)throw new InvalidOperationException("Seuls les autres joueurs estiment la dernière main, avant sa révélation.");
        if(kind=="E04" && HasEffraction(4) && value is >=1 and <=13 || kind=="E09" && HasEffraction(9) && Enum.IsDefined((HandRank)value))
            _guesses[(actor,kind)]=value;
        else throw new ArgumentException("Estimation invalide.");
    }
    private void EnsureGuessesBeforeReveal(Guid target)
    {
        if(target!=GuessTarget)return;
        foreach(var kind in new[]{"E04","E09"}.Where(_activeSpecialCards.Contains))
        {
            var values=_guesses.Where(g=>g.Key.Kind==kind).Select(g=>g.Value).ToArray();
            if(values.Length!=_players.Count-1 || values.Distinct().Count()!=1)
                throw new InvalidOperationException("Les autres joueurs doivent tous valider la même estimation avant la dernière révélation.");
        }
    }
    private bool SpecialGuessesSucceeded()
    {
        var player=FindPlayer(GuessTarget!.Value);
        var valid=true;
        var failures=new List<string>();
        if(HasEffraction(4)&&!_guesses.Any(g=>g.Key.Kind=="E04"&&player.Hand.Any(c=>c.Rank==g.Value)))
            failures.Add("Scan rétinien : le rang annoncé ne figure pas dans la dernière main.");
        if(HasEffraction(9)&&!_guesses.Any(g=>g.Key.Kind=="E09"&&(int)PokerHandEvaluator.Evaluate(player.Hand,_communityCards).Rank==g.Value))
            failures.Add("Lecteur d’empreintes digitales : la catégorie annoncée est incorrecte.");
        valid=failures.Count==0; SpecialFailureReason=valid?null:string.Join(" ",failures);
        return valid;
    }
    private HandScore EvaluatePlayer(Guid actor)
    {
        var score=PokerHandEvaluator.Evaluate(FindPlayer(actor).Hand,_communityCards);
        return new HandScore(score.Rank,score.Tiebreakers,actor==_bigArmsPlayer);
    }
    public static string HandCategoryName(HandRank rank) => rank switch {HandRank.HighCard=>"Carte haute",HandRank.OnePair=>"Paire",HandRank.TwoPair=>"Double paire",HandRank.ThreeOfKind=>"Brelan",HandRank.Straight=>"Quinte",HandRank.Flush=>"Couleur",HandRank.FullHouse=>"Full",HandRank.FourOfKind=>"Carré",HandRank.StraightFlush=>"Quinte flush",_=>"Quinte flush royale"};
    private sealed class UnsuitedJack : ICard { public CardColors Suit=>(CardColors)(-1); public int Rank=>11; public override string ToString()=>"Valet sans enseigne"; }
}
