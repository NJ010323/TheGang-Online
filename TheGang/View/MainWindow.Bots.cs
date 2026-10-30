using System.Windows;

namespace View;

public partial class MainWindow
{
    private void OfflineMenu_Click(object sender, RoutedEventArgs e)
    {
        ShowSetup(isHost: true);
        _offlineSetup = true;
        EntryTitle.Text = "Jouer hors ligne";
        EntrySubtitle.Text = "Choisissez votre pseudo, puis ajoutez vos bots dans le salon.";
    }

    private void AddBot_Click(object sender, RoutedEventArgs e) => ChangeBots(1);
    private void RemoveBot_Click(object sender, RoutedEventArgs e) => ChangeBots(-1);

    private async void ChangeBots(int delta)
    {
        if (_session is not { IsHost: true } session || _lastLobbySnapshot is not { } lobby) return;
        await RunAsync(async () =>
        {
            var updated = await session.Client.ChangeBotCountAsync(lobby.Players.Count(p => p.IsBot) + delta);
            if (_session != session) return;
            _lastLobbySnapshot = updated;
            RenderLobbySummary(session, updated);
        }, false);
    }
}
