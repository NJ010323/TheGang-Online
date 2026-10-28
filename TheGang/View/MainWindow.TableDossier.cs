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
        var cardsKey=$"{game.GameId}/{game.HeistNumber}/{string.Join(',',special?.ActiveCards??[])}/{special?.CompliceUsed}";
        if(cardsKey!=_activeCardsKey)
        {
            _activeCardsKey=cardsKey;TableActiveCards.Children.Clear();
            if(special?.ActiveCards.Length>0)
            {
                TableActiveCards.Children.Add(SpecialText("MATÉRIEL ACTIF · AGRANDIR AU CLIC",11));
                var row=new StackPanel{Orientation=Orientation.Horizontal};
                foreach(var id in special.ActiveCards)
                {
                    var def=SpecialCards.Get(id);
                    var button=SpecialButton("",()=>{OpenSpecialInterface();ShowSpecialDetail(def);});
                    var content=new StackPanel();content.Children.Add(SpecialImage(id,62));
                    var label=SpecialText(def.Name+(def.IsComplice&&special.CompliceUsed?" · utilisé":""),10);label.TextAlignment=TextAlignment.Center;content.Children.Add(label);
                    button.Width=105;button.Content=content;row.Children.Add(button);
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
                foreach(var hint in special.PrivateHints)InlineEffectsBody.Children.Add(SpecialText("POUR VOUS UNIQUEMENT · "+hint,13));
                BuildSpecialActions(game,special,InlineEffectsBody);
                foreach(var element in InlineEffectsBody.Children.OfType<FrameworkElement>())
                {
                    if(element is TextBlock text){text.FontSize=Math.Min(text.FontSize,14);text.LineHeight=19;text.Margin=new Thickness(0,0,0,6);}
                    else if(element is ComboBox choice)choice.Margin=new Thickness(0,0,10,6);
                    else if(element is Button button){button.Padding=new Thickness(10,5,10,5);button.Margin=new Thickness(0,3,10,4);}
                }
            }
            InlineEffectsFrame.Visibility=InlineEffectsBody.Children.Count>0?Visibility.Visible:Visibility.Collapsed;
        }
        var tutorial=TutorialGame is not null;
        ChatInput.IsEnabled=ChatSendButton.IsEnabled=!tutorial;
        if(tutorial)
        {
            if(_chatViewKey!="tutorial")
            {
                _chatViewKey="tutorial";ChatMessagesPanel.Children.Clear();
                ChatMessagesPanel.Children.Add(SpecialText("Entraînement hors ligne : les complices sont simulés. Les décisions se jouent dans l’espace d’effets.",11));
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
            var line=SpecialText(message.PlayerName+" · "+message.Text,12);line.Margin=new Thickness(0,0,0,4);ChatMessagesPanel.Children.Add(line);
        }
        if(game.ChatMessages.Count==0)ChatMessagesPanel.Children.Add(SpecialText("La radio est ouverte à toute la bande.",11));
        if(follow)ChatScroll.ScrollToEnd();
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
