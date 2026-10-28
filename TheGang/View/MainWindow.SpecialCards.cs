using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Model.Game;
using Model.Game.Hand;
using Network;

namespace View;

public partial class MainWindow
{
    private Grid? _specialOverlay;
    private StackPanel? _specialBody;
    private ScrollViewer? _specialScroll;
    private GameSnapshot? _lastSpecialGame;
    private bool _specialDetail;
    private string _specialViewKey="";
    private string _specialNoticeKey="";
    private string _specialPrivateKey="";
    private static Brush SpecialBrush(string value) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
    private TextBlock SpecialText(string text,int size=15) => new(){Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap,
        FontFamily=new FontFamily(size>=23?"Georgia":"Segoe UI"),Foreground=SpecialBrush("#F0C86A"),LineHeight=size*1.5,Margin=new Thickness(0,0,0,12)};
    private Button SpecialButton(string label,Action action,bool enabled=true)
    {
        var b=new Button {Content=new TextBlock {Text=label,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center},Style=(Style)FindResource("TableButton"),Margin=new Thickness(0,4,10,8),Padding=new Thickness(12,8,12,8),IsEnabled=enabled};
        b.Click+=(_,_)=>action();return b;
    }
    private FrameworkElement SpecialImage(string id,double width=96) => SpecialCardVisuals.Card(id,width);
    private void EnsureSpecialOverlay()
    {
        if(_specialOverlay is not null)return;
        _specialBody=new StackPanel();
        var scroll=new ScrollViewer{Content=_specialBody,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        _specialScroll=scroll;
        scroll.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar),ReleaseNotesOverlay.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)]);
        var dock=new DockPanel();
        var toolbar=new Grid{Margin=new Thickness(0,0,0,16)}; toolbar.Children.Add(SpecialText("◆  DOSSIER DE BRAQUAGE",13)); var close=SpecialButton("Fermer",CloseSpecialInterface); close.HorizontalAlignment=HorizontalAlignment.Right;close.MinWidth=110;toolbar.Children.Add(close);DockPanel.SetDock(toolbar,Dock.Top);dock.Children.Add(toolbar);dock.Children.Add(scroll);
        var frame=new Border{Child=dock,Background=SpecialBrush("#211619"),BorderBrush=SpecialBrush("#D9AD60"),BorderThickness=new Thickness(1.5),CornerRadius=new CornerRadius(14),Padding=new Thickness(24),Margin=new Thickness(26),MaxWidth=1100,MaxHeight=850};
        _specialOverlay=new Grid{Background=SpecialBrush("#EB080608"),Visibility=Visibility.Collapsed};
        System.Windows.Input.KeyboardNavigation.SetTabNavigation(_specialOverlay,System.Windows.Input.KeyboardNavigationMode.Cycle);
        _specialOverlay.Children.Add(frame);Panel.SetZIndex(_specialOverlay,44);Grid.SetRowSpan(_specialOverlay,2);((Grid)GamePanel.Parent).Children.Add(_specialOverlay);
    }
    private void CloseSpecialInterface(){if(_specialOverlay is not null)_specialOverlay.Visibility=Visibility.Collapsed;_specialDetail=false;}
    private void SpecialCards_Click(object sender,RoutedEventArgs e)=>OpenSpecialInterface();
    private void OpenSpecialInterface()
    {
        EnsureSpecialOverlay();_specialDetail=false;_specialOverlay!.Visibility=Visibility.Visible;BuildSpecialInterface();_specialScroll?.ScrollToTop();
    }
    private void RenderSpecialCards(GameSnapshot game)
    {
        _lastSpecialGame=game;
        RenderTableDossier(game);
        var s=game.Special;
        GameDifficultyButton.Visibility=s is null?Visibility.Collapsed:Visibility.Visible;
        SpecialCardsPanel.Children.Clear();SpecialCardsPanel.Visibility=Visibility.Collapsed;
        if(s is null)return;
        var noticeKey=$"{game.GameId}/{game.HeistNumber}/{s.SetupKind}/{s.DiscardPlayer}/{s.Proposal?.Id}";
        if(s.ActiveCards.Length>0&&noticeKey!=_specialNoticeKey)
        {
            _specialNoticeKey=noticeKey;
            ShowNotice("Cartes spéciales : "+string.Join(" · ",s.ActiveCards.Select(id=>SpecialCards.Get(id).Name))+". Les décisions apparaissent sous le plateau.",autoHide:false);
        }
        var privateKey=$"{game.GameId}/{game.HeistNumber}/{string.Join(';',s.PrivateHints)}";
        if(s.PrivateHints.Length>0&&privateKey!=_specialPrivateKey)
        {
            _specialPrivateKey=privateKey;
            if(s.ShownCards.Count==0)ShowNotice("Une information privée apparaît dans votre volet d’effets.",autoHide:false);
        }
        if(!s.CompliceUsed&&s.ActiveCards.Any(id=>id.StartsWith('C'))&&game.AllChoicesConfirmed&&
            game.Phase is GamePhase.PreFlop or GamePhase.Flop or GamePhase.Turn or GamePhase.River)
        {
            TokenActions.Visibility=Visibility.Visible;
            if(RoundTransitionOverlay.Visibility!=Visibility.Visible)TokenActionWheel.Visibility=Visibility.Visible;
        }
        GameDifficultyButton.Content=$"{DifficultyRules.Name(s.Difficulty)} · {s.AlarmLimit} alarmes · Cartes & effets";
        var required=s.SetupKind is not null||s.DiscardPlayer is not null||s.Proposal is not null||game.Phase==GamePhase.Showdown&&game.NextPlayerToRevealId==s.GuessTarget&&(s.ActiveCards.Contains("E04")||s.ActiveCards.Contains("E09"));
        if(required)TokenActionHint.Text="CARTE SPÉCIALE · DÉCISION SOUS LE PLATEAU";
        foreach(var value in s.LockedTokens)
        {
            if(!_visibleTokenSources.TryGetValue(value,out var chip))continue;
            TableVisuals.DarkenChip(chip);
            if(game.Players.Any(p=>p.CurrentToken==value))chip.IsHitTestVisible=false;
        }
        if(s.ActiveCards.Contains("E05")&&game.Phase==GamePhase.Flop){AdvanceWheelTitle.Text="LA RIVIÈRE";AdvanceWheelVerb.Text="PASSER À";}
        var key=$"{game.GameId}/{game.HeistNumber}/{game.Phase}/{game.RevealedHands.Count}/{s.SetupKind}/{s.SetupChosen}/{s.CompliceUsed}/{s.DiscardPlayer}/{s.Proposal?.Id}/{s.Proposal?.Accepted.Length}/{string.Join(';',s.OwnGuesses)}/{string.Join(';',s.GuessVoteCounts)}/{string.Join(';',s.PublicHints)}/{string.Join(';',s.PrivateHints)}";
        if(_specialOverlay?.Visibility==Visibility.Visible&&!_specialDetail&&key!=_specialViewKey){_specialViewKey=key;BuildSpecialInterface();}
    }
    private void BuildSpecialInterface()
    {
        if(_specialBody is null)return;
        _specialBody.Children.Clear();
        var game=GamePanel.Visibility==Visibility.Visible?_lastSpecialGame:null;
        var mode=game?.Special?.Difficulty??_lastLobbySnapshot?.Difficulty??GameDifficulty.Standard;
        _specialBody.Children.Add(SpecialText("DIFFICULTÉS & CARTES SPÉCIALES",28));
        _specialBody.Children.Add(SpecialText("Choisissez le défi de votre bande. Trois coffres pour gagner, une difficulté à partager."));
        if(game is null)
        {
            _specialBody.Children.Add(DifficultyTiles(mode));
            _specialBody.Children.Add(SpecialText(_session?.IsHost==true?"Le choix est partagé avec toute la table. Une revanche conserve la difficulté.":"Seul l’hôte peut changer la difficulté avant le départ.",13));
        }
        else if(game.Special is {} special)
        {
            _specialBody.Children.Add(DifficultyTiles(mode,true));
            if(special.ActiveCards.Length==0)_specialBody.Children.Add(SpecialText(mode==GameDifficulty.Standard?"Aucune carte spéciale en Standard.":"Premier braquage : aucune carte temporaire. La prochaine dépendra de votre résultat."));
            foreach(var id in special.ActiveCards)
            {
                var def=SpecialCards.Get(id);
                _specialBody.Children.Add(SpecialButton($"{def.Family} {def.Number} · {def.Name} · Agrandir",()=>ShowSpecialDetail(def)));
                _specialBody.Children.Add(SpecialText(def.Rule));
                _specialBody.Children.Add(SpecialText(id==special.Permanent?"PERMANENTE · Toute la partie":special.Difficulty==GameDifficulty.Gangster?"ACTIVE · La carte au plus petit numéro sera remplacée au prochain braquage":def.IsComplice&&special.CompliceUsed?"UTILISÉE · Expire à la fin du braquage":"ACTIVE · Pour ce braquage uniquement",12));
            }
            foreach(var hint in special.PublicHints)_specialBody.Children.Add(SpecialText("Annonce autorisée · "+hint));
            foreach(var hint in special.PrivateHints)_specialBody.Children.Add(SpecialText("PRIVÉ · "+hint));
            BuildSpecialActions(game,special);
        }
        _specialBody.Children.Add(SpecialFamilies());
        var catalog=new CenteredCardPanel();
        foreach(var def in SpecialCards.All)
        {
            var item=new StackPanel{Width=155,HorizontalAlignment=HorizontalAlignment.Center}; item.Children.Add(SpecialImage(def.Id,110));
            var caption=SpecialText(def.Name,13);caption.TextAlignment=TextAlignment.Center;item.Children.Add(caption);
            var b=SpecialButton("",()=>ShowSpecialDetail(def));b.Content=item;b.Margin=new Thickness(6);b.VerticalContentAlignment=VerticalAlignment.Center;b.MinHeight=230;catalog.Children.Add(b);
        }
        _specialBody.Children.Add(new Expander{Header="Le matériel du braquage · 20 cartes à découvrir",Content=catalog,Style=(Style)FindResource("HeistCatalogExpander"),Margin=new Thickness(0,18,0,10)});
    }
    private async void ChangeDifficulty(GameDifficulty difficulty)
    {
        if(_session is not {} session)return;
        await RunAsync(async()=>{var lobby=await session.Client.ChangeDifficultyAsync(difficulty);_lastLobbySnapshot=lobby;RenderLobbySummary(session,lobby);BuildSpecialInterface();},false);
    }
    private void ShowSpecialDetail(SpecialCardDefinition def)
    {
        DisplaySpecialDossier(def);
    }
    private ComboBox SpecialChoice(IEnumerable<(string Label,object Value)> items)
    {
        var box=new ComboBox{FontFamily=new FontFamily("Georgia"),Style=(Style)FindResource("HeistChoice"),Margin=new Thickness(0,0,10,12),MinHeight=36,MinWidth=180,FontSize=16,Background=SpecialBrush("#36252A"),Foreground=SpecialBrush("#F0C86A"),BorderBrush=SpecialBrush("#D9AD60")};
        foreach(var item in items)box.Items.Add(new ComboBoxItem{Style=(Style)FindResource("HeistChoiceItem"),Content=item.Label,Tag=item.Value,Background=SpecialBrush("#36252A"),Foreground=SpecialBrush("#F0C86A"),Padding=new Thickness(8)});box.SelectedIndex=0;return box;
    }
    private static object? Choice(ComboBox box)=>(box.SelectedItem as ComboBoxItem)?.Tag;
    private async void SendSpecial(GameActionRequest request)
    {
        if(_specialTutorial is not null)
        {
            await RunAsync(()=>{MutateTutorial(()=>_specialTutorial.Apply(request));return Task.CompletedTask;},false);return;
        }
        if(_session is not {} session||_lastSpecialGame is not {} game)return;
        await RunAsync(async()=>{var result=await session.Client.ApplyAsync(request with{ExpectedGameId=game.GameId,ExpectedHeist=game.HeistNumber});if(_session==session)RenderGame(result);},false);
    }
    private void BuildSpecialActions(GameSnapshot game,SpecialGameSnapshot special, Panel? destination=null)
    {
        var body=destination??_specialBody!;
        var self=game.Players.First(p=>p.Id==TablePlayerId);
        if(special.SetupKind is not null)
        {
            body.Children.Add(SpecialText(special.SetupKind=="Coordinateur"?"CHOIX PRIVÉ · Choisissez la carte à passer à votre voisin de gauche.":"MÉMORISEZ VOTRE MAIN · Confirmez uniquement après avoir regardé vos cartes.",19));
            if(special.SetupChosen)body.Children.Add(SpecialText("Votre choix est enregistré. En attente des autres joueurs."));
            else if(special.SetupKind=="Coordinateur")AddCardChoices(self,-1,index=>SendSpecial(new(GameAction.ChooseSpecialSetup){CardIndex=index}),body);
            else body.Children.Add(SpecialButton("J’ai vu et mémorisé ma main",()=>SendSpecial(new(GameAction.ChooseSpecialSetup))));
            return;
        }
        if(special.DiscardPlayer is {} discarder)
        {
            body.Children.Add(SpecialText(discarder==self.Id?"Choisissez secrètement la carte à défausser.":"Un complice choisit sa défausse. Les jetons attendent sa décision.",20));
            if(discarder==self.Id)AddCardChoices(self,special.ForbiddenDiscard,index=>SendSpecial(new(GameAction.DiscardSpecial){CardIndex=index}),body);return;
        }
        if(special.Proposal is {} vote)
        {
            var target=game.Players.First(p=>p.Id==vote.Target);
            body.Children.Add(SpecialText($"VOTE COMPLICE · {target.Name} · {vote.Accepted.Length}/{game.Players.Count} accords",21));
            if(vote.Recipient is {} recipient)body.Children.Add(SpecialText("Destinataire secret : "+game.Players.First(p=>p.Id==recipient).Name));
            if(special.ActiveCards.Contains("C04"))body.Children.Add(SpecialText("Rang choisi : "+(vote.Rank==1?"As":vote.Rank.ToString())));
            body.Children.Add(SpecialText("Le pouvoir ne s’active que si tout le monde accepte. Un refus annule la proposition sans consommer la carte."));
            body.Children.Add(SpecialButton("Accepter",()=>SendSpecial(new(GameAction.VoteComplice){ProposalId=vote.Id,Accepted=true}),!vote.Accepted.Contains(self.Id)));
            body.Children.Add(SpecialButton("Refuser / annuler",()=>SendSpecial(new(GameAction.VoteComplice){ProposalId=vote.Id,Accepted=false})));return;
        }
        if(game.Phase==GamePhase.Showdown&&game.NextPlayerToRevealId==special.GuessTarget)
        {
            foreach(var kind in new[]{"E04","E09"}.Where(special.ActiveCards.Contains))
            {
                body.Children.Add(SpecialText(SpecialCards.Get(kind).Name+" · Estimation collective",20));
                if(self.Id==special.GuessTarget){body.Children.Add(SpecialText("Vous possédez le plus grand jeton rouge : ne participez pas à la discussion et ne donnez aucun indice."));continue;}
                var choices=kind=="E04"?Enumerable.Range(1,13).Select(n=>(n==1?"As":n==11?"Valet":n==12?"Dame":n==13?"Roi":n.ToString(),(object)n)):
                    Enum.GetValues<HandRank>().Select(r=>(Model.Game.Game.HandCategoryName(r),(object)(int)r));
                var select=SpecialChoice(choices);body.Children.Add(select);
                body.Children.Add(SpecialText($"{special.GuessVoteCounts.GetValueOrDefault(kind)}/{game.Players.Count-1} avis enregistrés. Tous doivent retenir la même valeur. Vous pouvez modifier votre avis avant la révélation."));
                if(special.OwnGuesses.TryGetValue(kind,out var own))foreach(ComboBoxItem item in select.Items)if((int)item.Tag==own)select.SelectedItem=item;
                body.Children.Add(SpecialButton("Valider mon estimation",()=>SendSpecial(new(GameAction.SubmitSpecialGuess){GuessKind=kind,Rank=(int)Choice(select)!})));
            }
        }
        var complice=special.ActiveCards.FirstOrDefault(id=>id.StartsWith('C'));
        if(complice is null||special.CompliceUsed||game.Phase is not (GamePhase.PreFlop or GamePhase.Flop or GamePhase.Turn or GamePhase.River))return;
        body.Children.Add(SpecialText("PROPOSER L’UTILISATION DU COMPLICE",20));
        body.Children.Add(SpecialText(complice=="C01"?"Choisissez le destinataire, puis la carte à lui montrer. La bande doit donner son accord.":"Choisissez le bénéficiaire et demandez l’accord de la bande."));
        var targetBox=SpecialChoice(game.Players.Where(p=>complice!="C01"||p.Id==self.Id).Select(p=>(p.Name,(object)p.Id)));if(complice!="C01")body.Children.Add(targetBox);
        var recipientBox=SpecialChoice(game.Players.Where(p=>p.Id!=self.Id).Select(p=>(p.Name,(object)p.Id)));
        var rankBox=SpecialChoice(Enumerable.Range(1,13).Select(n=>(n==1?"As":n.ToString(),(object)n)));
        var cardBox=SpecialChoice(self.Hand.Select((c,i)=>($"Carte {i+1} · {c.Rank} {c.Suit}",(object)i)));
        var selectedCard=0;
        if(complice=="C01")
        {
            body.Children.Add(recipientBox);
            var cards=new WrapPanel();var buttons=new List<Button>();
            for(var i=0;i<self.Hand.Count;i++)
            {
                var index=i;var button=SpecialButton("",()=>{});button.Content=TableVisuals.Card(self.Hand[i],52,75);
                button.Style=(Style)FindResource("HeistCardChoice");button.Tag=i==0?"selected":null;
                button.Click+=(_,_)=>{selectedCard=index;foreach(var other in buttons){other.Opacity=.65;other.Tag=null;}button.Opacity=1;button.Tag="selected";};
                button.Opacity=i==0?1:.55;buttons.Add(button);cards.Children.Add(button);
            }
            body.Children.Add(cards);
        }
        if(complice=="C04")body.Children.Add(rankBox);
        body.Children.Add(SpecialButton("Proposer au groupe",()=>SendSpecial(new(GameAction.ProposeComplice){TargetId=(Guid)Choice(targetBox)!,RecipientId=(Guid?)Choice(recipientBox),Rank=(int)Choice(rankBox)!,CardIndex=complice=="C01"?selectedCard:(int)Choice(cardBox)!})));
    }
    private void AddCardChoices(PlayerSnapshot self,int forbidden,Action<int> select, Panel? destination=null)
    {
        var row=new WrapPanel();
        for(var i=0;i<self.Hand.Count;i++)
        {
            if(i==forbidden)continue;var index=i;
            var b=SpecialButton("",()=>select(index));var content=new StackPanel();content.Children.Add(TableVisuals.Card(self.Hand[i],54,78));
            if(_lastSpecialGame?.Special?.ActiveCards.Contains("C05")==true&&i==self.Hand.Count-1)
            {var label=SpecialText("PIOCHÉE",10);label.Margin=new Thickness(0,3,0,0);label.TextAlignment=TextAlignment.Center;content.Children.Add(label);}
            b.Content=content;row.Children.Add(b);
        }
        (destination??_specialBody!).Children.Add(row);
    }
}
