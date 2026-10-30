using System.ComponentModel;
using System.Windows;

namespace View;

public partial class MainWindow
{
    /// <summary>Origine de la demande de sortie ; chaque choix a une conséquence différente.</summary>
    private enum ExitIntent { Application, Lobby, Game, Reconnect }

    private ExitIntent? _pendingExit;
    private bool _applicationExitConfirmed;
    // Aucune session au démarrage : Alt+F4 peut annuler la vérification réseau.
    internal bool StartupUpdateInProgress { get; set; }

    // Le ticket de reprise n'est plus valable si ce départ provoque l'arrêt de la partie.
    private bool DepartureReturnsTeamToLobby => _lastLobbyStatus == Model.Lobby.LobbyStatus.Started &&
        _lastLobbySnapshot is { } lobby && lobby.Players.Count - lobby.AbsentPlayers.Count - 1 < 3;

    /// <summary>Intercepte aussi la croix de la fenêtre et Alt+F4.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_applicationExitConfirmed && !StartupUpdateInProgress)
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
        var canOfferRelief = isHost && _lastLobbySnapshot?.Players.Any(p => p.Id != _lastLobbySnapshot.HostId && !p.IsBot) == true &&
            _lastRenderedPhase != Model.Game.GamePhase.Finished;
        var gameStarted = GamePanel.Visibility == Visibility.Visible ||
            _lastLobbyStatus == Model.Lobby.LobbyStatus.Started;
        var consequence = _session is null
            ? "Le jeu sera fermé."
            : gameStarted
                ? isHost
                    ? canOfferRelief
                        ? _lastLobbySnapshot!.Players.Count - 1 < 3
                            ? "Le premier invité pourra reprendre l'hébergement. Moins de trois joueurs resteront : retour au lobby pour inviter d'autres joueurs."
                            : "Le premier invité pourra reprendre l'hébergement. Le braquage en cours sera redistribué et les résultats acquis seront conservés."
                        : "Vous êtes l'hôte : la partie se terminera pour toute l'équipe."
                    : _lastLobbySnapshot is { } current && current.Players.Count - current.AbsentPlayers.Count - 1 < 3
                        ? "Moins de trois joueurs resteront : la partie sera arrêtée et les autres joueurs retourneront au lobby."
                        : "Votre place et vos cartes resteront réservées pendant deux minutes. L'équipe attendra votre retour."
                : isHost
                    ? canOfferRelief
                        ? "Le premier invité pourra reprendre le lobby après votre départ."
                        : "Vous êtes l'hôte : le lobby sera fermé pour tous les joueurs."
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
        ConfirmExitButton.Content = canOfferRelief ? "Proposer la relève" : intent switch
        {
            ExitIntent.Application => "Quitter le jeu",
            ExitIntent.Lobby => "Quitter le lobby",
            _ => "Quitter la partie"
        };
        ConfirmExitButton.IsEnabled = !_busy;
        ForceExitButton.Visibility = canOfferRelief ? Visibility.Visible : Visibility.Collapsed;
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
        // Le serveur décide avec sa liste actuelle. Le compteur du ListBox peut être
        // momentanément vide ou périmé lors d'un changement d'écran.
        if (_session is { IsHost: true } && intent != ExitIntent.Reconnect)
        {
            await RunAsync(() => MigrateHostAndExitAsync(intent), showProgress: false);
            return;
        }
        await ExitWithoutMigrationAsync(intent);
    }

    /// <summary>Ferme explicitement l'ancienne table quand l'hôte renonce à la relève.</summary>
    private async void ForceExit_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _pendingExit is not { } intent) return;
        DismissExitConfirmation();
        await ExitWithoutMigrationAsync(intent);
    }

    /// <summary>Conserve le comportement de sortie immédiate lorsque personne ne peut reprendre.</summary>
    private async Task ExitWithoutMigrationAsync(ExitIntent intent)
    {
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
            var returnToLobby = DepartureReturnsTeamToLobby;
            await session.Client.LeaveAsync();
            if (_session == session)
                await ReturnToMenuAsync(isHost ? "La partie a été fermée pour tous les joueurs." :
                    returnToLobby ? "Vous avez quitté la partie. Les autres joueurs sont retournés au lobby." :
                        "Votre place est réservée pendant deux minutes. Utilisez « Reprendre ma partie » pour revenir.",
                    preserveGuestResume: !isHost && !returnToLobby);
        });
    }
}
