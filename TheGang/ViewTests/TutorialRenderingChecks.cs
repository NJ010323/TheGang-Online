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

internal static class TutorialRenderingChecks
{
    internal static void Run()
    {
        var window = new MainWindow();
        var animations = TableVisuals.AnimationsEnabled;
        T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        object? Invoke(string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
        T Find<T>(string name) => (T)window.FindName(name);
        void Apply(GameAction action, int? value = null) => Invoke("ApplyTutorialAction", action, value!);
        try
        {
            TableVisuals.AnimationsEnabled = true;
            Invoke("SetFullScreen", false);
            Invoke("StartTutorial");
            Assert.Null(Field<object?>("_session"));
            var lesson = Field<TutorialSession>("_tutorial");
            Assert.Equal(Visibility.Visible, Find<Grid>("GamePanel").Visibility);
            Assert.Equal(Visibility.Collapsed, Find<Grid>("MenuPanel").Visibility);
            Assert.Equal(3, Find<Canvas>("SeatsCanvas").Children.Count);
            Assert.NotEmpty(Field<System.Collections.IList>("_dealFlightVisuals"));
            Invoke("StopCardDeal");
            Invoke("RenderTutorial");
            while (!lesson.Complete)
            {
                var snapshot = (GameSnapshot)Invoke("TutorialSnapshot")!;
                Assert.All(snapshot.Players.Skip(1).Where(p => !snapshot.RevealedHands.Any(h => h.PlayerId == p.Id)), p => Assert.Empty(p.Hand));
                if (lesson.Step is 1 or 6 or 12 or 15 || lesson.Step == 14 && lesson.Game.Phase == GamePhase.River)
                {
                    var frame = new System.Windows.Threading.DispatcherFrame();
                    var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1100) };
                    timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
                    timer.Start();
                    System.Windows.Threading.Dispatcher.PushFrame(frame);
                    Capture(window, lesson.Step);
                }
                if (lesson.ExpectsToken)
                {
                    var before = lesson.Step;
                    Assert.Throws<TargetInvocationException>(() => Apply(GameAction.TakeToken, lesson.Step == 1 ? 3 : lesson.SuggestedToken % 3 + 1));
                    Assert.Equal(before, lesson.Step);
                    Apply(GameAction.TakeToken, lesson.Step == 1 ? 1 : lesson.SuggestedToken);
                }
                else if (lesson.ExpectsConfirmation) Apply(GameAction.ConfirmChoice);
                else if (lesson.Step is 5 or 8 or 11)
                {
                    if (lesson.Step == 5)
                    {
                        Apply(GameAction.WithdrawConfirmation);
                        Assert.False(lesson.Game.AllChoicesConfirmed);
                        Assert.True(Find<Button>("ConfirmButton").IsEnabled);
                        Assert.Throws<TargetInvocationException>(() => Apply(GameAction.AdvanceRound));
                        Apply(GameAction.ConfirmChoice);
                    }
                    Apply(GameAction.AdvanceRound);
                }
                else if (lesson.Step == 14)
                {
                    if (lesson.Game.Phase == GamePhase.River) Apply(GameAction.AdvanceRound);
                    else if (lesson.Game.NextPlayerToRevealId is not null) Apply(GameAction.RevealNextHand);
                    else Apply(GameAction.ResolveHeist);
                }
                else Invoke("MutateTutorial", (Action)lesson.Continue);
                Invoke("HideRoundTransition");
            }
            Assert.Equal(1, lesson.Game.SuccessfulHeists);
            Assert.Equal(3, lesson.Game.RevealedHands.Count);
            Assert.Equal(Visibility.Visible, Find<Grid>("HeistOutcomeOverlay").Visibility);
            Invoke("HideHeistOutcome");
            Invoke("TutorialExit_Click", window, new RoutedEventArgs());
            Assert.Null(Field<object?>("_tutorial"));
            Assert.Null(Field<object?>("_session"));
            Assert.Equal(Visibility.Collapsed, Find<Border>("TutorialGuide").Visibility);
            Assert.Equal(Visibility.Visible, Find<Grid>("MenuPanel").Visibility);
        }
        finally
        {
            TableVisuals.AnimationsEnabled = animations;
            typeof(MainWindow).GetField("_applicationExitConfirmed", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
            window.Close();
        }
    }

    private static void Capture(MainWindow window, int step)
    {
        var root = (FrameworkElement)window.Content;
        foreach (var size in new[] { new Size(1440, 900), new Size(960, 680) })
        {
            root.Measure(size);
            root.Arrange(new Rect(size));
            root.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(Path.GetTempPath(), $"TheGang-tutorial-table-{step}-{size.Width}.png"));
            encoder.Save(file);
        }
    }
}
