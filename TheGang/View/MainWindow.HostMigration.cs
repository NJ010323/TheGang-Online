using System.Windows;
using Model.Lobby;
using Network;

namespace View;

public partial class MainWindow
{
    // L'offre est gardée localement pour éviter de rouvrir la fenêtre à chaque sondage.
    private HostMigrationSnapshot? _visibleMigrationOffer;
    private LobbySnapshot? _migrationSourceLobby;
    private bool _followingMigration;
    private bool _cancelMigrationRequested;
    private bool _forceMigrationExitRequested;
    private bool _migrationDestinationPublished;
    private Guid? _activeMigrationId;

    /// <summary>Affiche la proposition au successeur et suit automatiquement la nouvelle invitation.</summary>
    private async Task<bool> HandleHostMigrationAsync(DesktopLobbySession session, LobbySnapshot lobby)
    {
        if (session.IsHost) return false;
        var migration = lobby.HostMigration;
        if (migration is null || migration.Stage is HostMigrationStage.Declined or HostMigrationStage.Expired)
        {
            MigrationOfferOverlay.Visibility = Visibility.Collapsed;
            MigrationProgressOverlay.Visibility = Visibility.Collapsed;
            _visibleMigrationOffer = null;
            _migrationSourceLobby = null;
            return false;
        }

        if (migration.Stage == HostMigrationStage.Redirecting && migration.Invitation is not null)
        {
            MigrationOfferOverlay.Visibility = Visibility.Collapsed;
            await FollowHostMigrationAsync(session, migration);
            return _session != session;
        }

        if (migration.Stage == HostMigrationStage.Offered &&
            session.Client.Identity?.PlayerId == migration.SuccessorId)
        {
            if (_visibleMigrationOffer?.Id != migration.Id)
            {
                _visibleMigrationOffer = migration;
                _migrationSourceLobby = lobby;
                MigrationOfferText.Text = $"{lobby.Players[0].Name} souhaite quitter la table. " +
                    "Vous êtes le premier invité : acceptez-vous d'héberger la suite de la mission ?";
                MigrationOfferExplanation.Text = migration.WasStarted && migration.ExpectedPlayers.Count < 3
                    ? "Il restera moins de trois joueurs : retour au lobby pour inviter d'autres joueurs et lancer une nouvelle partie."
                    : migration.WasStarted
                    ? "Le braquage en cours sera redistribué. Les cartes d'or et les alarmes déjà obtenues seront conservées."
                    : "Les joueurs du lobby rejoindront automatiquement votre nouvelle table. Vous pourrez encore inviter des amis avant de lancer la partie.";
            }
            MigrationProgressOverlay.Visibility = Visibility.Collapsed;
            MigrationOfferOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            MigrationOfferOverlay.Visibility = Visibility.Collapsed;
            if (migration.Stage == HostMigrationStage.Preparing &&
                session.Client.Identity?.PlayerId == migration.SuccessorId)
                MigrationProgressText.Text = "Ouverture de votre nouveau tunnel et préparation de la table…";
            else
                MigrationProgressText.Text = migration.Stage == HostMigrationStage.Offered
                    ? $"{migration.SuccessorName} doit accepter de reprendre l'hébergement."
                    : $"{migration.SuccessorName} prépare le nouveau serveur. La table est en pause.";
            CancelMigrationButton.Visibility = Visibility.Collapsed;
            MigrationProgressOverlay.Visibility = Visibility.Visible;
        }
        return false;
    }

    /// <summary>Le successeur ouvre d'abord son tunnel, puis publie sa destination à l'ancien serveur.</summary>
    private async void AcceptMigration_Click(object sender, RoutedEventArgs e)
    {
        if (_visibleMigrationOffer is not { } migration || _migrationSourceLobby is null ||
            _session is not { } oldSession) return;
        MigrationOfferOverlay.Visibility = Visibility.Collapsed;
        MigrationProgressText.Text = "Ouverture du nouveau serveur et de son tunnel…";
        CancelMigrationButton.Visibility = Visibility.Collapsed;
        MigrationProgressOverlay.Visibility = Visibility.Visible;
        await RunAsync(async () =>
        {
            await oldSession.Client.DecideHostMigrationAsync(new HostMigrationDecision(migration.Id, true));
            DesktopLobbySession? successor = null;
            try
            {
                var expected = migration.ExpectedPlayers.ToList();
                // L'hôte qui quitte ne réserve pas artificiellement une place dans le nouveau lobby.
                successor = await DesktopLobbySession.CreateAsync(migration.SuccessorName,
                    migration.MaxPlayers);
                await successor.Client.ConfigureHostMigrationAsync(new HostMigrationSetup(expected,
                    migration.WasStarted, migration.HeistNumber, migration.SuccessfulHeists,
                    migration.FailedHeists, null) { DifficultyState=migration.DifficultyState });
                await oldSession.Client.PublishHostMigrationAsync(
                    new HostMigrationDestination(migration.Id, successor.Invitation));
                try { await oldSession.Client.AcknowledgeHostMigrationAsync(migration.Id); }
                catch { /* La nouvelle table reste active même si l'ancien accusé se perd. */ }
                _session = successor;
                try { await oldSession.DisposeAsync(); }
                catch { /* Le nouveau serveur appartient désormais à cette fenêtre. */ }
                HideMigrationOverlays();
                EnterLobby();
                ShowNotice(migration.WasStarted && migration.ExpectedPlayers.Count < 3
                        ? "Vous êtes le nouvel hôte. Moins de trois joueurs : invitez d'autres joueurs depuis le lobby."
                        : "Vous êtes le nouvel hôte. Les autres joueurs rejoignent la table automatiquement.",
                    NoticeLevel.Success, autoHide: false);
            }
            catch
            {
                if (successor is not null && _session != successor)
                    await successor.DisposeAsync();
                try
                {
                    await oldSession.Client.DecideHostMigrationAsync(
                        new HostMigrationDecision(migration.Id, false));
                }
                catch { /* L'offre peut avoir expiré pendant la création du tunnel. */ }
                HideMigrationOverlays();
                throw;
            }
        }, showProgress: false);
    }

    /// <summary>Refuse la relève sans interrompre la partie de l'hôte actuel.</summary>
    private async void DeclineMigration_Click(object sender, RoutedEventArgs e)
    {
        if (_visibleMigrationOffer is not { } migration || _session is not { } session) return;
        MigrationOfferOverlay.Visibility = Visibility.Collapsed;
        await RunAsync(async () =>
        {
            await session.Client.DecideHostMigrationAsync(new HostMigrationDecision(migration.Id, false));
            _visibleMigrationOffer = null;
            _migrationSourceLobby = null;
            ShowNotice("Vous avez refusé la relève. L'hôte reste à la table.");
        }, showProgress: false);
    }

    /// <summary>Un invité rejoint la nouvelle adresse avant d'abandonner sa session d'origine.</summary>
    private async Task FollowHostMigrationAsync(DesktopLobbySession oldSession,
        HostMigrationSnapshot migration)
    {
        if (_followingMigration || migration.Invitation is null) return;
        _followingMigration = true;
        MigrationProgressText.Text = "Connexion à la table du nouvel hôte…";
        CancelMigrationButton.Visibility = Visibility.Collapsed;
        MigrationProgressOverlay.Visibility = Visibility.Visible;
        try
        {
            var nickname = oldSession.Client.Identity?.Lobby.Players
                .FirstOrDefault(player => player.Id == oldSession.Client.Identity.PlayerId)?.Name
                ?? NicknameBox.Text.Trim();
            var newSession = await DesktopLobbySession.JoinAsync(migration.Invitation, nickname);
            if (_session != oldSession)
            {
                await newSession.DisposeAsync();
                return;
            }
            try { await oldSession.Client.AcknowledgeHostMigrationAsync(migration.Id); }
            catch { /* La nouvelle table est déjà jointe ; la reprise prime sur l'ancien accusé. */ }
            _session = newSession;
            NicknameBox.Text = nickname;
            try { await oldSession.DisposeAsync(); }
            catch { /* La nouvelle connexion fonctionne déjà. */ }
            HideMigrationOverlays();
            EnterLobby();
            ShowNotice(migration.WasStarted && migration.ExpectedPlayers.Count < 3
                    ? "Vous avez rejoint le lobby du nouvel hôte. Il faut au moins trois joueurs pour relancer une partie."
                    : "Vous avez rejoint le nouvel hôte. Le braquage reprend avec l'équipe présente.",
                NoticeLevel.Success);
        }
        catch (Exception error)
        {
            MigrationProgressText.Text = "Le nouveau serveur ne répond pas encore. Nouvelle tentative automatique…";
            ShowNotice(error.Message, NoticeLevel.Error, autoHide: false);
        }
        finally { _followingMigration = false; }
    }

    /// <summary>Attend l'accord et tous les accusés avant de fermer l'ancien tunnel.</summary>
    private async Task MigrateHostAndExitAsync(ExitIntent intent)
    {
        var oldSession = _session ?? throw new InvalidOperationException("Aucune table active.");
        var lobby = await oldSession.Client.GetLobbyAsync();
        var gameFinished = lobby.Status == LobbyStatus.Started &&
            (await oldSession.Client.GetGameAsync()).Phase == Model.Game.GamePhase.Finished;
        if (lobby.Players.Count < 2 || gameFinished)
        {
            // Sans successeur (ou après la fin de la partie), la sortie confirmée
            // suit le chemin ordinaire. RunAsync est déjà actif ici.
            if (intent == ExitIntent.Application)
            {
                _applicationExitConfirmed = true;
                Close();
            }
            else
            {
                await oldSession.Client.LeaveAsync();
                if (_session == oldSession)
                    await ReturnToMenuAsync("La table a été fermée pour tous les joueurs.");
            }
            return;
        }
        var migration = await oldSession.Client.BeginHostMigrationAsync();
        _activeMigrationId = migration.Id;
        _cancelMigrationRequested = false;
        _forceMigrationExitRequested = false;
        _migrationDestinationPublished = false;
        MigrationProgressText.Text = $"Demande de relève envoyée à {migration.SuccessorName}…";
        CancelMigrationButton.Content = "Annuler la relève";
        CancelMigrationButton.Visibility = Visibility.Visible;
        MigrationProgressOverlay.Visibility = Visibility.Visible;
        try
        {
            while (migration.Stage != HostMigrationStage.Redirecting)
            {
                if (_cancelMigrationRequested) return;
                if (migration.Stage is HostMigrationStage.Declined or HostMigrationStage.Expired)
                    throw new InvalidOperationException(migration.Stage == HostMigrationStage.Declined
                        ? $"{migration.SuccessorName} a refusé la relève. Vous pouvez rester ou fermer la table sans relève."
                        : "La proposition de relève a expiré. Vous pouvez réessayer.");
                MigrationProgressText.Text = migration.Stage == HostMigrationStage.Offered
                    ? $"En attente de l'accord de {migration.SuccessorName}…"
                    : $"{migration.SuccessorName} prépare le nouveau tunnel…";
                await Task.Delay(600);
                migration = (await oldSession.Client.GetLobbyAsync()).HostMigration
                    ?? throw new InvalidOperationException("La relève n'est plus disponible.");
            }

            _migrationDestinationPublished = true;
            CancelMigrationButton.Visibility = Visibility.Collapsed;
            if (migration.Invitation is null)
                throw new InvalidOperationException("Le nouveau serveur n'a pas publié d'invitation.");

            migration = await oldSession.Client.AcknowledgeHostMigrationAsync(migration.Id);

            var waitingSince = DateTimeOffset.UtcNow;
            while (migration.AcknowledgedPlayers < migration.PlayersToRedirect)
            {
                MigrationProgressText.Text = $"{migration.AcknowledgedPlayers}/{migration.PlayersToRedirect} " +
                    "joueurs ont rejoint la nouvelle table. Attente des autres…";
                if (DateTimeOffset.UtcNow - waitingSince > TimeSpan.FromSeconds(60))
                {
                    MigrationProgressText.Text = "Certains joueurs n'ont pas encore rejoint le nouvel hôte. " +
                        "Vous pouvez attendre ou fermer l'ancienne table.";
                    CancelMigrationButton.Content = "Quitter quand même";
                    CancelMigrationButton.Visibility = Visibility.Visible;
                    if (_forceMigrationExitRequested) break;
                }
                await Task.Delay(750);
                migration = (await oldSession.Client.GetLobbyAsync()).HostMigration
                    ?? throw new InvalidOperationException("La relève n'est plus disponible.");
            }

            if (intent == ExitIntent.Application)
            {
                _applicationExitConfirmed = true;
                Close();
            }
            else
                await ReturnToMenuAsync("La relève est terminée. Les autres joueurs sont sur la nouvelle table.");
        }
        finally
        {
            _activeMigrationId = null;
            HideMigrationOverlays();
        }
    }

    /// <summary>Arrête la proposition avant publication, ou permet une fermeture explicite après délai.</summary>
    private async void CancelMigration_Click(object sender, RoutedEventArgs e)
    {
        if (_migrationDestinationPublished)
        {
            _forceMigrationExitRequested = true;
            return;
        }
        if (_activeMigrationId is not { } id || _session is not { } session) return;
        CancelMigrationButton.IsEnabled = false;
        try
        {
            await session.Client.CancelHostMigrationAsync(id);
            _cancelMigrationRequested = true;
            ShowNotice("La relève a été annulée. La partie peut continuer.");
        }
        catch (Exception error) { ShowNotice(error.Message, NoticeLevel.Error, autoHide: false); }
        finally { CancelMigrationButton.IsEnabled = true; }
    }

    /// <summary>Écarte toutes les fenêtres de relève après succès ou retour au menu.</summary>
    private void HideMigrationOverlays()
    {
        MigrationOfferOverlay.Visibility = Visibility.Collapsed;
        MigrationProgressOverlay.Visibility = Visibility.Collapsed;
    }
}
