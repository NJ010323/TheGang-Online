using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Network;
using Model.Game;

namespace View;
public partial class MainWindow
{
    private string _inlineEffectsKey="";
    private string _activeCardsKey="";
    private string _chatViewKey="";
    private void RenderTableDossier(GameSnapshot game)
    {
        var special=game.Special;
        PrivateCardsPanel.Children.Clear();
        foreach(var shown in special?.ShownCards??[])
        {
            var label=SpecialText("PRIVÉ · "+shown.SourceName,12);label.TextAlignment=TextAlignment.Center;PrivateCardsPanel.Children.Add(label);
            PrivateCardsPanel.Children.Add(TableVisuals.Card(shown.Card,76,110));
        }
        PositionTableChat();
        var cardsKey=$"{game.GameId}/{game.HeistNumber}/{string.Join(',',special?.ActiveCards??[])}/{special?.CompliceUsed}";
        if(cardsKey!=_activeCardsKey)
        {
            _activeCardsKey=cardsKey;TableActiveCards.Children.Clear();
            if(special?.ActiveCards.Length>0)
            {
                var row=new StackPanel{Orientation=Orientation.Horizontal};
                foreach(var id in special.ActiveCards)
                {
                    var def=SpecialCards.Get(id);
                    var button=new Button{Content=SpecialImage(id,94),Margin=new Thickness(0,0,10,0),Padding=new Thickness(0),Cursor=Cursors.Hand};
                    var template=new ControlTemplate(typeof(Button));
                    var content=new FrameworkElementFactory(typeof(ContentPresenter));
                    template.VisualTree=content;button.Template=template;
                    System.Windows.Automation.AutomationProperties.SetName(button,def.Name+" · ouvrir la fiche");
                    button.Click+=(_,_)=>{OpenSpecialInterface();ShowSpecialDetail(def);};row.Children.Add(button);
                }
                TableActiveCards.Children.Add(row);
            }
        }
        var key=$"{game.GameId}/{game.HeistNumber}/{game.Phase}/{game.RevealedHands.Count}/{special?.SetupKind}/{special?.SetupChosen}/{special?.DiscardPlayer}/{special?.CompliceUsed}/{special?.Proposal?.Id}/{special?.Proposal?.Accepted.Length}/{string.Join(';',special?.OwnGuesses??new())}/{string.Join(';',special?.GuessVoteCounts??new())}/{string.Join(';',special?.PublicHints??[])}/{string.Join(';',special?.PrivateHints??[])}";
        if(key!=_inlineEffectsKey)
        {
            _inlineEffectsKey=key;InlineEffectsBody.Children.Clear();
            if(special is not null)
            {
                foreach(var hint in special.PublicHints)InlineEffectsBody.Children.Add(SpecialText("ANNONCE · "+hint,13));
                foreach(var hint in special.PrivateHints.Where(h=>special.ShownCards.Count==0||!h.StartsWith("Informateur —")))InlineEffectsBody.Children.Add(SpecialText("POUR VOUS UNIQUEMENT · "+hint,13));
                BuildSpecialActions(game,special,InlineEffectsBody);
                foreach(var element in InlineEffectsBody.Children.OfType<FrameworkElement>())
                {
                    if(element is TextBlock text){text.FontSize=Math.Min(text.FontSize,14);text.LineHeight=19;text.Margin=new Thickness(0,0,0,6);}
                    else if(element is ComboBox choice)choice.Margin=new Thickness(0,0,10,6);
                    else if(element is Button button){button.Padding=new Thickness(10,5,10,5);button.Margin=new Thickness(0,3,10,4);}
                }
            }
            InlineEffectsFrame.Visibility=InlineEffectsBody.Children.Count>0?Visibility.Visible:Visibility.Collapsed;
            var required=special?.SetupKind is not null||special?.DiscardPlayer is not null||special?.Proposal is not null||game.Phase==GamePhase.Showdown&&game.NextPlayerToRevealId==special?.GuessTarget;
            InlineEffectsScroll.Visibility=required?Visibility.Visible:Visibility.Collapsed;
            EffectsToggleButton.Content=required?"DÉCISION DE LA BANDE · RÉDUIRE":"CARTES & EFFETS · OUVRIR";
        }
        var tutorial=TutorialGame is not null;
        ChatInput.IsEnabled=ChatSendButton.IsEnabled=!tutorial;
        if(tutorial)
        {
            if(_chatViewKey!="tutorial")
            {
                _chatViewKey="tutorial";ChatMessagesPanel.Children.Clear();
                ChatMessagesPanel.Children.Add(SpecialText("Entraînement hors ligne : les complices sont simulés. Les décisions se jouent dans le volet d’effets.",11));
            }
            return;
        }
        var chatKey=$"{game.GameId}/{game.ChatMessages.LastOrDefault()?.Sequence}";
        if(chatKey==_chatViewKey)return;
        _chatViewKey=chatKey;
        var follow=ChatScroll.ScrollableHeight-ChatScroll.VerticalOffset<20;
        ChatMessagesPanel.Children.Clear();
        foreach(var message in game.ChatMessages)
        {
            var line=SpecialText(message.PlayerName+" · "+message.Text,12);line.FontFamily=new System.Windows.Media.FontFamily("Georgia");line.Margin=new Thickness(0,0,0,4);ChatMessagesPanel.Children.Add(line);
        }
        if(game.ChatMessages.Count==0)ChatMessagesPanel.Children.Add(SpecialText("Coordonnez les effets sans révéler vos cartes.",11));
        if(follow)ChatScroll.ScrollToEnd();
    }
    private void PositionTableChat()
    {
        var width=TableSurface.ActualWidth;var height=TableSurface.ActualHeight;
        if(width<=0||height<=0)return;
        var feltWidth=Math.Min(width,height*1280/720);
        var feltHeight=feltWidth*720/1280;
        InlineEffectsFrame.Width=Math.Clamp(feltWidth*.32,255,360);
        InlineEffectsFrame.MaxHeight=Math.Max(170,feltHeight*.55);
        InlineEffectsScroll.MaxHeight=Math.Max(105,feltHeight*.55-70);
        InlineEffectsFrame.Margin=new Thickness(0,(height-feltHeight)/2+12,(width-feltWidth)/2+12,0);
        TableChat.Width=feltWidth*.245;
        TableChat.Height=Math.Clamp(feltHeight*.225,80,165);
        TableChat.Margin=new Thickness(0,0,(width-feltWidth)/2+feltWidth*.025,(height-feltHeight)/2+feltHeight*.025);
    }
    private void EffectsToggle_Click(object sender,RoutedEventArgs e)
    {
        var open=InlineEffectsScroll.Visibility!=Visibility.Visible;
        InlineEffectsScroll.Visibility=open?Visibility.Visible:Visibility.Collapsed;
        EffectsToggleButton.Content=open?"CARTES & EFFETS · RÉDUIRE":"CARTES & EFFETS · OUVRIR";
    }
    private void ChatInput_KeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.Enter){e.Handled=true;SendChat();}
    }
    private void ChatSend_Click(object sender,RoutedEventArgs e)=>SendChat();
    private async void SendChat()
    {
        var text=ChatInput.Text.Trim();
        if(text.Length==0||!ChatSendButton.IsEnabled||_session is not {} session)return;
        ChatSendButton.IsEnabled=false;
        await RunAsync(async()=>
        {
            var result=await session.Client.SendChatAsync(text);
            if(_session!=session)return;
            if(ChatInput.Text.Trim()==text)ChatInput.Clear();
            RenderGame(result);
        },false);
        ChatSendButton.IsEnabled=true;
    }
}
