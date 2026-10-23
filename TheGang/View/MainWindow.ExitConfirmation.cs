using System.ComponentModel;
using System.Windows;

namespace View;

public partial class MainWindow
{
    /// <summary>Origine de la demande de sortie ; chaque choix a une conséquence différente.</summary>
    private enum ExitIntent { Application, Lobby, Game, Reconnect }

    private ExitIntent? _pendingExit;
    private bool _applicationExitConfirmed;

    /// <summary>Intercepte aussi la croix de la fenêtre et Alt+F4.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_applicationExitConfirmed)
        {
            e.Cancel = true;
            ShowExitConfirmation(ExitIntent.Application);
        }
        base.OnClosing(e);
    }

    /// <summary>Adapte la confirmation à l'hôte, à l'invité et au moment de la partie.</summary>
    private void ShowExitConfirmation(ExitIntent intent)
    {
        if (intent != ExitIntent.Application && _session is null) return;
        _pendingExit = intent;
        var isHost = _session?.IsHost == true;
        var gameStarted = GamePanel.Visibility == Visibility.Visible ||
            _lastLobbyStatus == Model.Lobby.LobbyStatus.Started;
        var consequence = _session is null
            ? "Le jeu sera fermé."
            : gameStarted
                ? isHost
                    ? "Vous êtes l'hôte : la partie se terminera pour toute l'équipe."
                    : "Votre place et vos cartes resteront réservées pendant deux minutes. L'équipe attendra votre retour."
                : isHost
                    ? "Vous êtes l'hôte : le lobby sera fermé pour tous les joueurs."
                    : "Votre place dans le lobby sera libérée.";

        ExitConfirmationTitle.Text = intent switch
        {
            ExitIntent.Application => "QUITTER LE JEU ?",
            ExitIntent.Lobby => "QUITTER LE LOBBY ?",
            ExitIntent.Game => "ABANDONNER LA MISSION ?",
            _ => "QUITTER LA TABLE ?"
        };
        ExitConfirmationQuestion.Text = intent == ExitIntent.Lobby
            ? "Voulez-vous quitter votre équipe ?"
            : "Voulez-vous abandonner votre mission ?";
        ExitConfirmationDetail.Text = consequence;
        CancelExitButton.Content = intent == ExitIntent.Application && _session is null
            ? "Rester dans le jeu" : "Rester à la table";
        ConfirmExitButton.Content = intent switch
        {
            ExitIntent.Application => "Quitter le jeu",
            ExitIntent.Lobby => "Quitter le lobby",
            _ => "Quitter la partie"
        };
        ConfirmExitButton.IsEnabled = !_busy;
        ExitConfirmationOverlay.Visibility = Visibility.Visible;
        CancelExitButton.Focus();
    }

    /// <summary>Ferme la confirmation sans modifier la partie ou la fenêtre.</summary>
    private void DismissExitConfirmation()
    {
        _pendingExit = null;
        ExitConfirmationOverlay.Visibility = Visibility.Collapsed;
    }

    private void CancelExit_Click(object sender, RoutedEventArgs e) => DismissExitConfirmation();

    /// <summary>Exécute uniquement la sortie explicitement validée par le joueur.</summary>
    private async void ConfirmExit_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _pendingExit is not { } intent) return;
        DismissExitConfirmation();
        if (intent == ExitIntent.Application)
        {
            _applicationExitConfirmed = true;
            Close();
            return;
        }

        if (intent == ExitIntent.Reconnect)
        {
            await LeaveDuringReconnectAsync();
            return;
        }

        if (intent == ExitIntent.Lobby)
        {
            await RunAsync(async () =>
            {
                if (_session is not { } session) return;
                await session.Client.LeaveAsync();
                if (_session == session) await ReturnToMenuAsync(string.Empty);
            });
            return;
        }

        SettingsOverlay.Visibility = Visibility.Collapsed;
        await RunAsync(async () =>
        {
            if (_session is not { } session) return;
            var isHost = session.IsHost;
            await session.Client.LeaveAsync();
            if (_session == session)
                await ReturnToMenuAsync(isHost ? "La partie a été fermée pour tous les joueurs." :
                    "Votre place est réservée pendant deux minutes. Utilisez « Reprendre ma partie » pour revenir.",
                    preserveGuestResume: !isHost);
        });
    }
}
