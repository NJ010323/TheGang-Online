using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Model.Game;
using Network;
using View;

namespace ViewTests;
internal static class SpecialCardsRenderingChecks
{
    internal static void Run()
    {
        var lobby=new HostedLobby("Alice",6);
        var others=Enumerable.Range(1,5).Select(n=>lobby.Join("Complice "+n,lobby.InvitationSecret)).ToArray();
        lobby.ChangeDifficulty(lobby.Code,lobby.HostIdentity.SessionToken,GameDifficulty.Gangster);
        using var client=new LobbyHttpClient(new Uri("http://127.0.0.1:1"));client.SetHostIdentity(lobby.HostIdentity);
        var session=(DesktopLobbySession)Activator.CreateInstance(typeof(DesktopLobbySession),BindingFlags.Instance|BindingFlags.NonPublic,null,[client,"test",null,null],null)!;
        var window=new MainWindow{StartupUpdateInProgress=true};
        void Invoke(string name,params object[] args)=>typeof(MainWindow).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,args);
        void Field(string name,object? value)=>typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,value);
        T Find<T>(string name)=>(T)window.FindName(name);
        var animations=TableVisuals.AnimationsEnabled;
        try
        {
            TableVisuals.AnimationsEnabled=false;Invoke("SetFullScreen",false);Field("_session",session);
            var snapshot=lobby.GetLobby(lobby.Code,lobby.HostIdentity.SessionToken);Field("_lastLobbySnapshot",snapshot);
            Find<Grid>("MenuPanel").Visibility=Visibility.Collapsed;Find<Grid>("LobbyPanel").Visibility=Visibility.Visible;
            Invoke("RenderLobbySummary",session,snapshot);Assert.Contains("Gangster",Find<Button>("LobbyDifficultyButton").Content.ToString());
            Capture(window,"difficulty-lobby");
            Invoke("OpenSpecialInterface");Capture(window,"difficulty-selection");Invoke("CloseSpecialInterface");
            var game=lobby.Start(lobby.Code,lobby.HostIdentity.SessionToken);Invoke("RenderGame",game);
            Assert.Equal(2,Find<StackPanel>("AlarmResultCardsPanel").Children.Count);
            Assert.Equal(Visibility.Visible,Find<StackPanel>("SpecialCardsPanel").Visibility);
            Capture(window,"special-table");Invoke("OpenSpecialInterface");Capture(window,"special-effects");
            var specialBody=(StackPanel)typeof(MainWindow).GetField("_specialBody",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            var catalog=specialBody.Children.OfType<Expander>().Single();catalog.IsExpanded=true;
            var specialScroll=(ScrollViewer)typeof(MainWindow).GetField("_specialScroll",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            specialScroll.ScrollToBottom();Capture(window,"catalog-centered");
            foreach(var card in SpecialCards.All)
            {
                Invoke("ShowSpecialDetail",card);Capture(window,card.Id);
            }
            Invoke("CloseSpecialInterface");
            var camera=game with{Special=game.Special! with{ActiveCards=new[]{"E10","E04"}},Players=game.Players.Select(p=>p with{HandCount=3}).ToArray()};
            Invoke("RenderGame",camera);Capture(window,"three-cards");
            Invoke("OpenRules_Click",window,new RoutedEventArgs());Capture(window,"rules-readable");Invoke("CloseRulesCard");
            Field("_session",null);Invoke("OpenTutorialMenu");Capture(window,"tutorial-menu");
            Invoke("StartSpecialTutorial",GameDifficulty.Pro,"C05");Capture(window,"tutorial-special-intro");
            var lesson=(SpecialTutorialSession)typeof(MainWindow).GetField("_specialTutorial",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            lesson.Continue();Invoke("RenderTutorial");Capture(window,"tutorial-special-action");
            Assert.Equal(Visibility.Visible,Find<Border>("InlineEffectsFrame").Visibility);
            Assert.NotEmpty(Find<StackPanel>("TableActiveCards").Children.Cast<UIElement>());
            Assert.NotNull(Find<ScrollViewer>("TutorialGuideScroll").Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)]);
            lesson.Apply(new(GameAction.ProposeComplice){TargetId=lesson.Game.Players[0].Id});Invoke("RenderTutorial");Capture(window,"tutorial-private-discard");
            Assert.NotNull(lesson.Game.DiscardPlayer);
            Invoke("StartSpecialTutorial",GameDifficulty.Advanced,"C06");Capture(window,"tutorial-pass-card");
            Invoke("StartSpecialTutorial",GameDifficulty.Advanced,"C01");
            var informant=(SpecialTutorialSession)typeof(MainWindow).GetField("_specialTutorial",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            informant.Continue();Invoke("RenderTutorial");Capture(window,"tutorial-inline-vote");
            Assert.NotNull(informant.Game.Proposal);
            Invoke("StartSpecialTutorial",GameDifficulty.Advanced,"E02");Capture(window,"tutorial-dark-token");

        }
        finally{TableVisuals.AnimationsEnabled=animations;Field("_session",null);Field("_applicationExitConfirmed",true);window.Close();}
    }
    private static void Capture(MainWindow window,string name)
    {
        var root=(FrameworkElement)window.Content;
        foreach(var size in new[]{new Size(1440,900),new Size(960,680)})
        {
            root.Measure(size);root.Arrange(new Rect(size));root.UpdateLayout();
            var image=new RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32);image.Render(root);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
            var folder=Path.Combine(Path.GetTempPath(),"TheGang-SpecialCards");Directory.CreateDirectory(folder);
            using var file=File.Create(Path.Combine(folder,$"{name}-{size.Width}.png"));encoder.Save(file);
        }
    }
}
