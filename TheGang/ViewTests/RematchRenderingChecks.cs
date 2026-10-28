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

internal static class RematchRenderingChecks
{
    // Exécuté sur le dispatcher STA et dans l'Application WPF du test de rendu existant.
    internal static void Run()
    {
        var lobby = new HostedLobby("Éléonore-du-Coffre", 6);
        var players = new[] { lobby.HostIdentity }.Concat(Enumerable.Range(1, 5)
            .Select(i => lobby.Join($"Braqueur {i}", lobby.InvitationSecret))).ToArray();
        var game = lobby.Start(lobby.Code, players[0].SessionToken);
        while (game.Phase != GamePhase.Finished)
        {
            for (var round = 0; round < 4; round++)
            {
                for (var i = 0; i < players.Length; i++)
                    lobby.Apply(lobby.Code, players[i].SessionToken, new(GameAction.TakeToken, i + 1));
                foreach (var player in players)
                    lobby.Apply(lobby.Code, player.SessionToken, new(GameAction.ConfirmChoice));
                lobby.Apply(lobby.Code, players[0].SessionToken, new(GameAction.AdvanceRound));
            }
            foreach (var _ in players) lobby.Apply(lobby.Code, players[0].SessionToken, new(GameAction.RevealNextHand));
            game = lobby.Apply(lobby.Code, players[0].SessionToken, new(GameAction.ResolveHeist));
            if (game.Phase == GamePhase.HeistComplete)
                game = lobby.Apply(lobby.Code, players[0].SessionToken, new(GameAction.StartNextHeist));
        }
        using var client = new LobbyHttpClient(new Uri("http://127.0.0.1:1"));
        client.SetHostIdentity(lobby.HostIdentity);
        var session = (DesktopLobbySession)Activator.CreateInstance(typeof(DesktopLobbySession),
            BindingFlags.Instance | BindingFlags.NonPublic, null, [client, "test", null, null], null)!;
        var animations = TableVisuals.AnimationsEnabled;
        TableVisuals.AnimationsEnabled = false;
        var window = new MainWindow { StartupUpdateInProgress = true };
        var sessionField = typeof(MainWindow).GetField("_session", BindingFlags.NonPublic | BindingFlags.Instance)!;
        sessionField.SetValue(window, session);
        void Invoke(string method, params object[] args) => typeof(MainWindow).GetMethod(method,
            BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, args);
        T Find<T>(string name) => (T)window.FindName(name);
        try
        {
            var intermediate = game with { Phase = GamePhase.HeistComplete, Result = GameResult.InProgress, Rematch = null };
            Invoke("ShowHeistOutcome", new GameEventSnapshot(0, GameEventKind.HeistResolved,
                players[0].PlayerId, "", intermediate.Phase, intermediate.HeistNumber,
                HeistSucceeded: intermediate.LastHeistSucceeded), intermediate);
            Assert.Equal(Visibility.Visible, Find<Button>("OutcomeReturnToTableButton").Visibility);
            Assert.Equal(Visibility.Collapsed, Find<Border>("RematchPanel").Visibility);
            Invoke("RenderGame", game);
            Assert.Equal(Visibility.Collapsed, Find<Button>("OutcomeReturnToTableButton").Visibility);
            Assert.Equal(Visibility.Visible, Find<Border>("RematchPanel").Visibility);
            Assert.Equal(Visibility.Visible, Find<Grid>("HeistOutcomeOverlay").Visibility);
            Assert.Equal(6, Find<WrapPanel>("RematchPlayers").Children.Count);
            Assert.Contains("0/6", Find<TextBlock>("RematchStatus").Text);
            Assert.True(Find<Button>("RematchAcceptButton").IsEnabled);
            var vote = game.Rematch!;
            lobby.DecideRematch(lobby.Code, players[0].SessionToken, new(vote.Id, true));
            Invoke("RenderGame", lobby.GetGame(lobby.Code, players[0].SessionToken));
            Assert.False(Find<Button>("RematchAcceptButton").IsEnabled);
            Assert.True(Find<Button>("RematchLobbyButton").IsEnabled);
            Assert.Contains("1/6", Find<TextBlock>("RematchStatus").Text);

            var card = Find<Border>("OutcomeCard");
            card.Measure(new Size(865, double.PositiveInfinity));
            card.Arrange(new Rect(0, 0, 865, card.DesiredSize.Height));
            card.UpdateLayout();
            var bitmap = new RenderTargetBitmap(865, (int)Math.Ceiling(card.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(card);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(Path.GetTempPath(), "TheGang-rematch-preview.png"))) png.Save(file);

            foreach (var player in players.Skip(1)) lobby.DecideRematch(lobby.Code, player.SessionToken, new(vote.Id, true));
            var restarted = lobby.GetGame(lobby.Code, players[0].SessionToken);
            Invoke("RenderGame", restarted);
            Assert.Equal(Visibility.Collapsed, Find<Border>("RematchPanel").Visibility);
            Assert.Equal(Visibility.Collapsed, Find<Grid>("HeistOutcomeOverlay").Visibility);
            Assert.Equal(6, Find<Canvas>("SeatsCanvas").Children.Count);
            Assert.Equal(restarted.GameId, typeof(MainWindow).GetField("_lastGameId", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window));
            Assert.Equal(0, typeof(MainWindow).GetField("_shownGoldCards", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window));
            Assert.Equal(0, typeof(MainWindow).GetField("_shownAlarmCards", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window));
            Invoke("ReturnToWaitingLobby", "Le délai de revanche est écoulé : retour au lobby.");
            Assert.Equal(Visibility.Visible, Find<Grid>("LobbyPanel").Visibility);
            Assert.Contains("délai de revanche", Find<TextBlock>("StatusText").Text);
        }
        finally
        {
            sessionField.SetValue(window, null);
            window.Close();
            TableVisuals.AnimationsEnabled = animations;
        }
    }
}
