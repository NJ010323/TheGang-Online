using System.Windows;
using Model.Lobby;
using Network;

namespace View;

public partial class MainWindow
{
    /// <summary>Conserve le client et son jeton de session pendant les reprises automatiques.</summary>
    private async Task HandleReconnectFailureAsync(DesktopLobbySession session)
    {
        if (_session != session) return;
        var now = DateTimeOffset.UtcNow;
        _reconnectWindow.Start(now);
        if (_reconnectWindow.IsExpired(now))
        {
            await ReturnToMenuAsync("Connexion interrompue. Si l'hôte est toujours actif, utilisez « Reprendre ma partie ».",
                preserveGuestResume: !session.IsHost);
            return;
        }

        _connectionWarning = false;
        if (StatusText.Text == "Action en cours…") HideNotice();
        ReconnectStatusText.Text = "Le jeu réessaie automatiquement pendant 30 secondes. " +
            "Vous gardez votre place si la connexion revient.";
        ReconnectOverlay.Visibility = Visibility.Visible;
        PausedGameOverlay.Visibility = Visibility.Collapsed;
        _refreshTimer.Interval = TimeSpan.FromSeconds(2);
    }

    /// <summary>Réaffiche la table après réception de l'état complet du lobby et de la partie.</summary>
    private void EndReconnection(LobbyStatus status)
    {
        _reconnectWindow.Reset();
        _connectionWarning = false;
        ReconnectOverlay.Visibility = Visibility.Collapsed;
        _refreshTimer.Interval = status == LobbyStatus.Started
            ? TimeSpan.FromMilliseconds(500) : TimeSpan.FromSeconds(2);
        ShowNotice("Connexion rétablie : vous êtes de retour à la table.", NoticeLevel.Success);
    }

    /// <summary>Permet une nouvelle tentative immédiate sans redemander le code d'invitation.</summary>
    private void RetryConnection_Click(object sender, RoutedEventArgs e) => _ = RefreshAsync();

    /// <summary>Demande confirmation avant de quitter pendant une coupure réseau.</summary>
    private void LeaveDuringReconnect_Click(object sender, RoutedEventArgs e) =>
        ShowExitConfirmation(ExitIntent.Reconnect);

    /// <summary>Quitte localement la table, même si le serveur ne répond plus.</summary>
    private async Task LeaveDuringReconnectAsync()
    {
        if (_busy || _session is not { } session) return;
        _busy = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var notified = false;
            try { await session.Client.LeaveAsync(timeout.Token); notified = true; }
            catch (Exception) { /* La sortie locale doit rester possible hors connexion. */ }
            if (_session == session) await ReturnToMenuAsync("Vous avez quitté la table.",
                preserveGuestResume: !session.IsHost &&
                    (!notified || _lastLobbyStatus == LobbyStatus.Started && !DepartureReturnsTeamToLobby));
        }
        finally { _busy = false; }
    }

    /// <summary>Propose une reprise en un clic si un jeton d'invité existe sur ce PC.</summary>
    private void RefreshResumeButton()
    {
        ResumeButton.Visibility = GuestResumeStore.TryLoad() is null
            ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Affiche à tous les joueurs absents et le temps restant avant la fermeture.</summary>
    private void UpdatePauseOverlay(LobbySnapshot lobby)
    {
        if (lobby.AbsentPlayers.Count == 0)
        {
            PausedGameOverlay.Visibility = Visibility.Collapsed;
            return;
        }
        var names = string.Join(", ", lobby.AbsentPlayers.Select(player => player.Name));
        PausedTitleText.Text = lobby.Status == LobbyStatus.Open
            ? "◈  TABLE EN ATTENTE  ◈" : "◈  PARTIE EN PAUSE  ◈";
        PausedExplanationText.Text = lobby.Status == LobbyStatus.Open
            ? "Sa place reste réservée. L'équipe pourra lancer la partie lorsqu'elle sera au complet."
            : "Les cartes et les jetons restent en place. La partie reprendra dès son retour.";
        PausedPlayerText.Text = lobby.AbsentPlayers.Count == 1
            ? $"{names} a quitté la partie. Nous attendons son retour."
            : $"{names} ont quitté la partie. Nous attendons leur retour.";
        var remaining = lobby.AbsentPlayers.Min(player => player.ReconnectUntilUtc) - DateTimeOffset.UtcNow;
        PauseDeadlineText.Text = $"Temps de reprise restant : {Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds))} s";
        PausedGameOverlay.Visibility = Visibility.Visible;
    }
}
