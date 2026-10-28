using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Model.Game;
using Network;

namespace View;

public partial class MainWindow
{
    private Guid _lastGameId;
    private RematchSnapshot? _currentRematch;

    private void RenderRematch(GameSnapshot game)
    {
        _currentRematch = game.Rematch;
        RematchPanel.Visibility = game.Phase == GamePhase.Finished && game.Rematch is not null
            ? Visibility.Visible : Visibility.Collapsed;
        if (game.Rematch is not { } vote) return;
        var remaining = Math.Max(0, (vote.ExpiresAtUtc - vote.ServerNowUtc).TotalSeconds);
        var seconds = (int)Math.Ceiling(remaining);
        var accepted = vote.AcceptedPlayers.Contains(_session?.Client.Identity?.PlayerId ?? Guid.Empty);
        RematchCountdown.Text = $"{seconds:00} s";
        RematchStatus.Text = $"{vote.AcceptedPlayers.Count}/{game.Players.Count} prêts · " +
            "Accord unanime pour repartir. Un refus ou la fin du délai ramène tout le monde au lobby.";
        RematchAcceptButton.Content = accepted ? "✓  REVANCHE ACCEPTÉE" : "REVANCHE";
        RematchAcceptButton.IsEnabled = !accepted && seconds > 0 && !_busy;
        RematchLobbyButton.IsEnabled = seconds > 0 && !_busy;
        RematchPlayers.Children.Clear();
        foreach (var player in game.Players)
        {
            var ready = vote.AcceptedPlayers.Contains(player.Id);
            RematchPlayers.Children.Add(new Border
            {
                Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 7, 5),
                CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(ready ? Color.FromRgb(229, 185, 88) : Color.FromRgb(118, 93, 74)),
                Background = new SolidColorBrush(ready ? Color.FromRgb(77, 57, 29) : Color.FromRgb(33, 25, 25)),
                Child = new TextBlock { Text = $"{(ready ? "✓" : "…")}  {player.Name}", FontSize = 13,
                    Foreground = new SolidColorBrush(ready ? Color.FromRgb(248, 216, 141) : Color.FromRgb(191, 169, 145)) }
            });
        }
        var ratio = Math.Clamp(remaining / HostedLobby.RematchDuration.TotalSeconds, 0, 1);
        var previous = RematchTimeScale.ScaleX;
        RematchTimeScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        RematchTimeScale.ScaleX = ratio;
        if (TableVisuals.AnimationsEnabled)
            RematchTimeScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(previous, ratio, TimeSpan.FromMilliseconds(350)));
        FooterPhaseHint.Text = $"REVANCHE : {vote.AcceptedPlayers.Count}/{game.Players.Count} PRÊTS · {seconds} s · OUVREZ LE DÉBRIEF POUR CHOISIR";
    }

    private async void AcceptRematch_Click(object sender, RoutedEventArgs e) => await SendRematchDecision(true);
    private async void DeclineRematch_Click(object sender, RoutedEventArgs e) => await SendRematchDecision(false);

    private async Task SendRematchDecision(bool accepted)
    {
        if (_currentRematch is not { } vote || _session is not { } session) return;
        await RunAsync(async () =>
        {
            RematchAcceptButton.IsEnabled = RematchLobbyButton.IsEnabled = false;
            var lobby = await session.Client.DecideRematchAsync(new RematchDecision(vote.Id, accepted));
            if (_session != session) return;
            _lastLobbySnapshot = lobby;
            _lastLobbyStatus = lobby.Status;
            if (lobby.Status == Model.Lobby.LobbyStatus.Open)
            {
                ReturnToWaitingLobby(lobby.GameReturnReason);
                RenderLobbySummary(session, lobby);
            }
            else
            {
                var game = await session.Client.GetGameAsync();
                if (_session == session) RenderGame(game);
            }
        }, showProgress: false);
    }
}
