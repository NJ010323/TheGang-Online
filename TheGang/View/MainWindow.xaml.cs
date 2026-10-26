using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Model.Game;
using Model.Game.Tokens.TokenEnum;
using Model.Lobby;
using Network;

namespace View;

/// <summary>Affiche le menu, le lobby et le plateau sans accéder aux cartes privées du serveur.</summary>
public partial class MainWindow : Window
{
    // Une seule session peut être active dans cette fenêtre.
    private DesktopLobbySession? _session;
    // Le lobby est sondé toutes les deux secondes ; le plateau actif l'est plus souvent.
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    // Les confirmations brèves s'effacent ; les erreurs attendent une action du joueur.
    private readonly DispatcherTimer _noticeTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    // Une coupure passagère conserve l'identité du joueur et sa place pendant trente secondes.
    private readonly LobbyReconnectWindow _reconnectWindow = new(TimeSpan.FromSeconds(30));
    private bool _connectionWarning;
    // Ces indicateurs empêchent de superposer des requêtes ou des actions utilisateur.
    private bool _refreshing;
    private bool _busy;
    private bool _hostActionAvailable;
    // Le contour de confirmation ne redémarre pas à chaque instantané réseau.
    private bool _confirmCueActive;
    private bool _confirmCueAnimated;
    private LobbyStatus _lastLobbyStatus = LobbyStatus.Open;
    // Dernière liste fournie par le serveur, utilisée aussi dans la confirmation de sortie.
    private LobbySnapshot? _lastLobbySnapshot;
    // L'état précédent sert à animer seulement les nouvelles cartes ou phases.
    private GamePhase? _lastRenderedPhase;
    private int _lastCommunityCount;
    private int _lastHeistNumber;
    private readonly HashSet<Guid> _visibleHands = [];
    // Le curseur est rendu depuis une ressource PNG embarquée, sans dépendance à un fichier voisin.
    private readonly Cursor _gloveCursor;
    // Les dimensions du tableau peuvent être masquées pour agrandir le tapis.
    private bool _historyVisible = true;
    // La géométrie de la fenêtre est restaurée en quittant le plein écran.
    private bool _isFullScreen;
    private WindowState _savedWindowState;
    private WindowStyle _savedWindowStyle;
    private ResizeMode _savedResizeMode;

    public MainWindow()
    {
        InitializeComponent();
        _gloveCursor = GameCursor.Create();
        _refreshTimer.Tick += RefreshTimer_Tick;
        _noticeTimer.Tick += (_, _) => HideNotice();
        _roundTransitionTimer.Tick += (_, _) => HideRoundTransition();
        _dragHeartbeatTimer.Tick += (_, _) => PublishDragPosition();
        OutcomeGoldImage.Source = ResultCardVisuals.GoldFace;
        OutcomeAlarmImage.Source = ResultCardVisuals.AlarmFace;
        TableSurface.SizeChanged += (_, _) => PositionHostActions();
        PopulateRulesCard();
        PopulateMenuCards();
        RefreshResumeButton();
        // Le menu de démarrage occupe tout l'écran ; le mode fenêtré reste accessible dans les paramètres.
        SetFullScreen(true);
    }

    /// <summary>Garde la commande de l'hôte hors des cartes de résultat sur un plateau étroit.</summary>
    private void PositionHostActions()
    {
        var compact = TableSurface.ActualWidth < 1000;
        HostActions.HorizontalAlignment = compact ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        HostActions.Margin = compact ? new Thickness(0, 0, 12, 0) : new Thickness(0, 0, 400, 12);
    }

    /// <summary>Affiche le gant dans le jeu actif et libère le curseur système en dehors.</summary>
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        Mouse.OverrideCursor = _gloveCursor;
    }

    protected override void OnDeactivated(EventArgs e)
    {
        if (Mouse.OverrideCursor == _gloveCursor) Mouse.OverrideCursor = null;
        base.OnDeactivated(e);
    }

    /// <summary>Ouvre les champs de création sans démarrer encore de serveur.</summary>
    private void HostMenu_Click(object sender, RoutedEventArgs e) => ShowSetup(isHost: true);

    /// <summary>Ouvre les champs nécessaires à l'invitation reçue.</summary>
    private void JoinMenu_Click(object sender, RoutedEventArgs e) => ShowSetup(isHost: false);

    /// <summary>Choisit le formulaire hôte ou invité à partir du menu principal.</summary>
    private void ShowSetup(bool isHost)
    {
        MenuPanel.Visibility = Visibility.Collapsed;
        EntryPanel.Visibility = Visibility.Visible;
        HostSetup.Visibility = isHost ? Visibility.Visible : Visibility.Collapsed;
        JoinSetup.Visibility = isHost ? Visibility.Collapsed : Visibility.Visible;
        EntryTitle.Text = isHost ? "Héberger une partie" : "Rejoindre une partie";
        EntrySubtitle.Text = isHost ? "Choisissez votre pseudo et la taille de la table." :
            "Saisissez votre pseudo et l'invitation envoyée par l'hôte.";
        HideNotice();
        NicknameBox.Focus();
    }

    /// <summary>Revient au menu depuis le formulaire de connexion.</summary>
    private void BackMenu_Click(object sender, RoutedEventArgs e)
    {
        EntryPanel.Visibility = Visibility.Collapsed;
        MenuPanel.Visibility = Visibility.Visible;
        HideNotice();
    }

    /// <summary>Demande confirmation avant de fermer toute l'application.</summary>
    private void Quit_Click(object sender, RoutedEventArgs e) => ShowExitConfirmation(ExitIntent.Application);

    /// <summary>Ouvre les paramètres et ajoute la commande de sortie seulement pendant une partie.</summary>
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        WindowedOption.IsChecked = !_isFullScreen;
        FullScreenOption.IsChecked = _isFullScreen;
        AnimationsOnOption.IsChecked = TableVisuals.AnimationsEnabled;
        AnimationsOffOption.IsChecked = !TableVisuals.AnimationsEnabled;
        GameSettingsSection.Visibility = GamePanel.Visibility == Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;
        ExitGameHint.Text = _session?.IsHost == true
            ? "En quittant, proposez au premier invité de reprendre la table."
            : "Votre place sera gardée pendant deux minutes. Les autres joueurs attendront votre retour.";
        SettingsOverlay.Visibility = Visibility.Visible;
    }

    /// <summary>Ferme le panneau des paramètres sans changer la session en cours.</summary>
    private void CloseSettings_Click(object sender, RoutedEventArgs e) =>
        SettingsOverlay.Visibility = Visibility.Collapsed;

    /// <summary>Applique immédiatement le mode d'affichage choisi dans les paramètres.</summary>
    private void WindowedMode_Click(object sender, RoutedEventArgs e) => SetFullScreen(false);
    private void FullScreenMode_Click(object sender, RoutedEventArgs e) => SetFullScreen(true);

    /// <summary>Active ou réduit les mouvements décoratifs sans toucher à la partie.</summary>
    private void AnimationsOn_Click(object sender, RoutedEventArgs e)
    {
        TableVisuals.AnimationsEnabled = true;
        UpdateConfirmCue(GamePanel.Visibility == Visibility.Visible && ConfirmButton.IsEnabled);
    }

    private void AnimationsOff_Click(object sender, RoutedEventArgs e)
    {
        TableVisuals.AnimationsEnabled = false;
        UpdateConfirmCue(GamePanel.Visibility == Visibility.Visible && ConfirmButton.IsEnabled);
    }

    /// <summary>F11 active le plein écran ; Échap en sort.</summary>
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ExitConfirmationOverlay.Visibility == Visibility.Visible)
        {
            DismissExitConfirmation();
            e.Handled = true;
        }
        else if (e.Key == Key.F11)
        {
            SetFullScreen(!_isFullScreen);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && RulesOverlay.Visibility == Visibility.Visible)
        {
            CloseRulesCard();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && SettingsOverlay.Visibility == Visibility.Visible)
        {
            SettingsOverlay.Visibility = Visibility.Collapsed;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && HeistOutcomeOverlay.Visibility == Visibility.Visible)
        {
            HideHeistOutcome();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _isFullScreen)
        {
            SetFullScreen(false);
            e.Handled = true;
        }
    }

    /// <summary>Conserve les réglages de fenêtre pour pouvoir les rétablir exactement.</summary>
    private void SetFullScreen(bool enabled)
    {
        if (_isFullScreen == enabled) return;
        if (enabled)
        {
            _savedWindowState = WindowState;
            _savedWindowStyle = WindowStyle;
            _savedResizeMode = ResizeMode;
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowState = WindowState.Normal;
            WindowStyle = _savedWindowStyle;
            ResizeMode = _savedResizeMode;
            WindowState = _savedWindowState;
        }
        _isFullScreen = enabled;
        WindowedOption.IsChecked = !enabled;
        FullScreenOption.IsChecked = enabled;
    }

    /// <summary>Crée le serveur et son tunnel après validation du pseudo.</summary>
    private async void Create_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var maxPlayers = Players6Option.IsChecked == true ? 6 :
            Players5Option.IsChecked == true ? 5 : Players4Option.IsChecked == true ? 4 : 3;
        _session = await DesktopLobbySession.CreateAsync(NicknameBox.Text, maxPlayers);
        EnterLobby();
    });

    /// <summary>Rejoint une partie distante avec l'invitation complète.</summary>
    private async void Join_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        _session = await DesktopLobbySession.JoinAsync(InvitationInput.Text, NicknameBox.Text);
        EnterLobby();
    });

    /// <summary>Retrouve automatiquement l'invitation et la place réservée du dernier invité.</summary>
    private async void Resume_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var ticket = GuestResumeStore.TryLoad();
        if (ticket is null)
        {
            RefreshResumeButton();
            throw new InvalidOperationException("Aucune partie à reprendre sur cet ordinateur.");
        }
        try
        {
            _session = await DesktopLobbySession.ResumeAsync(ticket.Invitation,
                ticket.PlayerId, ticket.SessionToken);
        }
        catch (LobbySessionClosedException)
        {
            GuestResumeStore.Clear();
            RefreshResumeButton();
            throw new InvalidOperationException("Le délai de reprise est terminé ou la partie a été fermée.");
        }
        NicknameBox.Text = ticket.Nickname;
        EnterLobby();
        ShowNotice("Vous avez retrouvé votre place à la table.", NoticeLevel.Success);
    });

    /// <summary>Affiche le lobby et commence à surveiller les changements de joueurs.</summary>
    private void EnterLobby()
    {
        // Une relève peut arriver au milieu d'une animation de l'ancienne table.
        StopCardDeal();
        HideHeistOutcome();
        HideRoundTransition();
        CloseRulesCard();
        SettingsOverlay.Visibility = Visibility.Collapsed;
        TokenActionWheel.Visibility = Visibility.Collapsed;
        UpdateConfirmCue(false);
        MenuPanel.Visibility = Visibility.Collapsed;
        EntryPanel.Visibility = Visibility.Collapsed;
        GamePanel.Visibility = Visibility.Collapsed;
        LobbyPanel.Visibility = Visibility.Visible;
        InvitationOutput.Text = _session!.Invitation;
        _lastLobbySnapshot = _session.Client.Identity?.Lobby;
        StartButton.Visibility = _session.IsHost ? Visibility.Visible : Visibility.Collapsed;
        _lastLobbyStatus = LobbyStatus.Open;
        _lastRenderedPhase = null;
        _lastHeistNumber = 0;
        _lastSeenEventSequence = 0;
        _reconnectWindow.Reset();
        ReconnectOverlay.Visibility = Visibility.Collapsed;
        PausedGameOverlay.Visibility = Visibility.Collapsed;
        if (_session.IsHost)
        {
            GuestResumeStore.Clear();
            RefreshResumeButton();
        }
        else if (_session.Client.Identity is { } identity)
        {
            try
            {
                GuestResumeStore.Save(new GuestResumeTicket(_session.Invitation, identity.PlayerId,
                    identity.SessionToken, NicknameBox.Text.Trim()));
                RefreshResumeButton();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                          System.Security.Cryptography.CryptographicException)
            {
                ShowNotice("La reprise après fermeture n'a pas pu être enregistrée sur cet ordinateur.",
                    NoticeLevel.Error, autoHide: false);
            }
        }
        if (_lastLobbySnapshot is { } initialLobby)
            RenderLobbySummary(_session, initialLobby);
        _refreshTimer.Interval = TimeSpan.FromSeconds(2);
        _refreshTimer.Start();
        _ = RefreshAsync();
    }

    /// <summary>Copie l'invitation réseau que l'hôte transmet à ses amis.</summary>
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(InvitationOutput.Text);
        ShowNotice("Invitation copiée. Vous pouvez l'envoyer aux autres joueurs.", NoticeLevel.Success);
    }

    /// <summary>Change immédiatement les places disponibles sans recréer l'invitation du lobby.</summary>
    private async void LobbyCapacity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string value } || !int.TryParse(value, out var maxPlayers) ||
            _session is not { IsHost: true } session || _lastLobbySnapshot is not { } lobby)
            return;
        if (maxPlayers == lobby.MaxPlayers) return;
        await RunAsync(async () =>
        {
            var updated = await session.Client.ChangeCapacityAsync(maxPlayers);
            if (_session != session) return;
            _lastLobbySnapshot = updated;
            RenderLobbySummary(session, updated);
            ShowNotice($"La table accueille maintenant jusqu'à {maxPlayers} joueurs.", NoticeLevel.Success);
        }, showProgress: false);
        // En cas de refus du serveur, la sélection revient à la capacité réelle.
        if (_session == session && _lastLobbySnapshot is { } current)
            RenderLobbySummary(session, current);
    }

    /// <summary>L'hôte distribue les cartes lorsque trois joueurs ou plus sont présents.</summary>
    private async void Start_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var session = _session!;
        var game = await session.Client.StartAsync();
        if (_session == session) RenderGame(game);
    });

    /// <summary>Confirme le départ du lobby avant d'avertir le serveur.</summary>
    private void Leave_Click(object sender, RoutedEventArgs e) => ShowExitConfirmation(ExitIntent.Lobby);

    /// <summary>L'hôte ferme la partie ; un invité garde sa place pendant le délai de reprise.</summary>
    private void ExitGame_Click(object sender, RoutedEventArgs e) => ShowExitConfirmation(ExitIntent.Game);

    /// <summary>Agrandit le tapis ou réaffiche le détail de tous les jetons.</summary>
    private void HistoryToggle_Click(object sender, RoutedEventArgs e) =>
        SetHistoryVisible(!_historyVisible);

    /// <summary>Garde synchronisés la colonne, son contenu et la languette de réouverture.</summary>
    private void SetHistoryVisible(bool visible)
    {
        _historyVisible = visible;
        HistoryColumn.Width = new GridLength(_historyVisible ? 306 : 0);
        HistoryPanel.Visibility = _historyVisible ? Visibility.Visible : Visibility.Collapsed;
        HistoryShowButton.Visibility = _historyVisible ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Interroge périodiquement l'hôte pour mettre à jour lobby et partie.</summary>
    private async void RefreshTimer_Tick(object? sender, EventArgs e) => await RefreshAsync();

    /// <summary>Présente à tous la capacité actuelle ; seul l'hôte peut la modifier avant la partie.</summary>
    private void RenderLobbySummary(DesktopLobbySession session, LobbySnapshot lobby)
    {
        LobbyDetails.Text = lobby.ExpectedTransferredPlayers is { } expected
            ? $"Relève · {lobby.Players.Count}/{expected} réunis · {lobby.MaxPlayers} places · code {lobby.Code}"
            : $"Code local : {lobby.Code}   ·   {lobby.Players.Count}/{lobby.MaxPlayers} joueurs";
        PlayersList.ItemsSource = lobby.Players.Select(player =>
            new LobbyPlayerDisplay(player.Name, player.Id == lobby.HostId ? "HÔTE" :
                player.Id == session.Client.Identity?.PlayerId ? "VOUS" : string.Empty)).ToArray();
        var allTransferred = lobby.ExpectedTransferredPlayers is null ||
            lobby.Players.Count >= lobby.ExpectedTransferredPlayers.Value;
        StartButton.IsEnabled = session.IsHost && lobby.Players.Count >= 3 &&
            lobby.AbsentPlayers.Count == 0 && lobby.Status == LobbyStatus.Open &&
            allTransferred && !lobby.AutoResumeTransferredGame && !_busy;
        StartButton.Visibility = session.IsHost && !lobby.AutoResumeTransferredGame
            ? Visibility.Visible : Visibility.Collapsed;
        LeaveButton.Visibility = lobby.Status == LobbyStatus.Open ? Visibility.Visible : Visibility.Collapsed;

        var canChangeCapacity = session.IsHost && lobby.Status == LobbyStatus.Open &&
            !lobby.AutoResumeTransferredGame && lobby.HostMigration is not
                { Stage: HostMigrationStage.Offered or HostMigrationStage.Preparing or HostMigrationStage.Redirecting };
        LobbyCapacityPanel.Visibility = canChangeCapacity ? Visibility.Visible : Visibility.Collapsed;
        LobbyCapacityPanel.IsEnabled = canChangeCapacity && !_busy;
        var minimum = Math.Max(lobby.Players.Count, lobby.ExpectedTransferredPlayers ?? 0);
        LobbyPlayers3Option.IsEnabled = minimum <= 3;
        LobbyPlayers4Option.IsEnabled = minimum <= 4;
        LobbyPlayers5Option.IsEnabled = minimum <= 5;
        LobbyPlayers6Option.IsEnabled = minimum <= 6;
        LobbyPlayers3Option.IsChecked = lobby.MaxPlayers == 3;
        LobbyPlayers4Option.IsChecked = lobby.MaxPlayers == 4;
        LobbyPlayers5Option.IsChecked = lobby.MaxPlayers == 5;
        LobbyPlayers6Option.IsChecked = lobby.MaxPlayers == 6;
    }

    /// <summary>Actualise la session et tente sa reprise si le tunnel cesse temporairement de répondre.</summary>
    private async Task RefreshAsync()
    {
        if (_refreshing || _session is null || _busy || _draggedTokenValue is not null) return;
        var session = _session;
        _refreshing = true;
        try
        {
            var lobby = await session.Client.GetLobbyAsync();
            if (_session != session || _busy) return;
            _lastLobbySnapshot = lobby;
            if (await HandleHostMigrationAsync(session, lobby)) return;
            _lastLobbyStatus = lobby.Status;
            if (lobby.Status == LobbyStatus.Open && GamePanel.Visibility == Visibility.Visible)
                ReturnToWaitingLobby();
            RenderLobbySummary(session, lobby);
            if (lobby.Status == LobbyStatus.Started)
            {
                var game = await session.Client.GetGameAsync();
                if (_session != session || _busy || _draggedTokenValue is not null) return;
                if (game.RecentEvents.LastOrDefault()?.Sequence < _lastSeenEventSequence) return;
                RenderGame(game);
            }
            UpdatePauseOverlay(lobby);
            if (_reconnectWindow.IsActive) EndReconnection(lobby.Status);
            else if (_connectionWarning)
            {
                _connectionWarning = false;
                HideNotice();
            }
        }
        catch (Exception error) when (_session == session &&
                                      LobbyConnectionFailure.IsSessionClosed(error))
        {
            await ReturnToMenuAsync("La partie est terminée : l'hôte l'a fermée ou le délai de reprise a expiré.");
        }
        catch (Exception error) when (_session == session &&
                                      LobbyConnectionFailure.IsHostUnavailable(error))
        {
            await HandleReconnectFailureAsync(session);
        }
        catch (Exception error)
        {
            if (_session == session)
            {
                _connectionWarning = true;
                ShowNotice($"Connexion perdue : {error.Message}", NoticeLevel.Error, autoHide: false);
            }
        }
        finally { _refreshing = false; }
    }

    /// <summary>Suit le retour au salon décidé par le serveur, en annulant tous les visuels de la partie arrêtée.</summary>
    private void ReturnToWaitingLobby()
    {
        StopCardDeal();
        HideHeistOutcome();
        HideRoundTransition();
        CloseRulesCard();
        DismissExitConfirmation();
        SettingsOverlay.Visibility = Visibility.Collapsed;
        TokenActionWheel.Visibility = Visibility.Collapsed;
        HostActions.Visibility = Visibility.Collapsed;
        _hostActionAvailable = false;
        UpdateConfirmCue(false);
        GamePanel.Visibility = Visibility.Collapsed;
        LobbyPanel.Visibility = Visibility.Visible;
        _lastRenderedPhase = null;
        _lastHeistNumber = _lastCommunityCount = 0;
        _lastSeenEventSequence = 0;
        _visibleHands.Clear();
        _remoteDragVisuals.Clear();
        ChipFlightCanvas.Children.Clear();
        SeatsCanvas.Children.Clear();
        CommunityCardsPanel.Children.Clear();
        ResetScoreCards();
        _refreshTimer.Interval = TimeSpan.FromSeconds(2);
        _connectionWarning = false;
        ShowNotice("Moins de trois joueurs : retour au lobby. Invitez un joueur pour lancer une nouvelle partie.",
            NoticeLevel.Info, autoHide: false);
    }

    /// <summary>Construit le plateau à partir de la projection réseau du joueur courant.</summary>
    private void RenderGame(GameSnapshot game)
    {
        if (_session?.Client.Identity is not { } identity) return;
        LobbyPanel.Visibility = Visibility.Collapsed;
        GamePanel.Visibility = Visibility.Visible;
        _refreshTimer.Interval = TimeSpan.FromMilliseconds(500);
        var newHeist = game.HeistNumber != _lastHeistNumber;
        if (newHeist)
        {
            StopCardDeal();
            HideHeistOutcome();
            HideRoundTransition();
            _lastHeistNumber = game.HeistNumber;
            _lastCommunityCount = 0;
            CommunityCardsPanel.Children.Clear();
            _visibleHands.Clear();
            ChipFlightCanvas.Children.Clear();
            _remoteDragVisuals.Clear();
        }
        // Préparer les emplacements cachés avant de construire les places du nouveau braquage.
        if (newHeist && game.Phase == GamePhase.PreFlop && TableVisuals.AnimationsEnabled)
            PrepareCardDeal(game);
        else if (_dealingHeistNumber != 0 && game.Phase != GamePhase.PreFlop)
            StopCardDeal();

        GamePhaseText.Text = PhaseLabel(game);
        TablePhaseText.Text = TableMessage(game);
        // Pendant le choix, le bandeau inférieur donne l'instruction sans masquer les jetons centraux.
        TablePhaseText.Visibility = game.Phase is GamePhase.PreFlop or GamePhase.Flop or
            GamePhase.Turn or GamePhase.River ? Visibility.Collapsed : Visibility.Visible;
        var phaseChanged = _lastRenderedPhase != game.Phase;
        if (phaseChanged)
        {
            if (game.Phase == GamePhase.Showdown) SetHistoryVisible(true);
            TableVisuals.Reveal(GamePhaseText);
            _lastRenderedPhase = game.Phase;
        }

        RenderCommunityCards(game);
        _visibleTokenSources.Clear();
        RenderSeats(game, identity.PlayerId, identity.Lobby.HostId);
        if (newHeist && _dealingHeistNumber == game.HeistNumber)
            StartCardDeal(game, identity.PlayerId);
        RenderTokenBank(game, phaseChanged);
        if (_draggedTokenValue is { } draggedValue &&
            _visibleTokenSources.TryGetValue(draggedValue, out var movingSource))
            movingSource.Opacity = 0.25;
        RenderRemoteDrags(game, identity.PlayerId);
        RenderHistory(game);
        RenderShowdownResults(game);
        RenderActivity(game);
        RenderControls(game, identity.PlayerId);
        ProcessGameEvents(game);
        RenderScoreCards(game);
    }

    /// <summary>Conserve les cartes déjà rendues et retourne seulement les nouvelles, dans l'ordre.</summary>
    private void RenderCommunityCards(GameSnapshot game)
    {
        if (CommunityCardsPanel.Children.Count != 5 || game.CommunityCards.Count < _lastCommunityCount)
        {
            CommunityCardsPanel.Children.Clear();
            for (var index = 0; index < 5; index++)
                CommunityCardsPanel.Children.Add(TableVisuals.Card(null, 74, 105));
            _lastCommunityCount = 0;
        }

        var firstNewCard = _lastCommunityCount;
        for (var index = firstNewCard; index < game.CommunityCards.Count; index++)
        {
            var delay = TimeSpan.FromMilliseconds((index - firstNewCard) * 240);
            var revealed = TableVisuals.FlipCard(game.CommunityCards[index], 74, 105, delay);
            CommunityCardsPanel.Children.RemoveAt(index);
            CommunityCardsPanel.Children.Insert(index, revealed);
        }
        _lastCommunityCount = game.CommunityCards.Count;
    }

    /// <summary>Place le joueur local en bas et répartit les autres sur l'arc supérieur.</summary>
    private void RenderSeats(GameSnapshot game, Guid selfId, Guid hostId)
    {
        SeatsCanvas.Children.Clear();
        _seatCardVisuals.Clear();
        _seatCenters.Clear();
        _selfSeat = null;
        var players = game.Players.ToArray();
        var selfIndex = Array.FindIndex(players, player => player.Id == selfId);
        if (selfIndex < 0) return;
        var self = players[selfIndex];
        AddSeat(self, new Point(535, 560), isSelf: true);

        var opponents = Enumerable.Range(1, players.Length - 1)
            .Select(offset => players[(selfIndex + offset) % players.Length]).ToArray();
        var positions = TableVisuals.OpponentPositions(opponents.Length);
        for (var index = 0; index < opponents.Length; index++)
            AddSeat(opponents[index], positions[index], isSelf: false);

        void AddSeat(PlayerSnapshot player, Point position, bool isSelf)
        {
            var wasVisible = _visibleHands.Contains(player.Id);
            var seat = TableVisuals.Seat(player, isSelf, player.Id == hostId,
                player.Id == game.NextPlayerToRevealId, game.Phase, TableVisuals.RoundColor(game.Phase),
                (chip, value) =>
                {
                    _visibleTokenSources[value] = chip;
                    EnableChipDrag(chip, value, player.Id, game.Phase, !game.AllChoicesConfirmed);
                }, _dealtCardCounts.GetValueOrDefault(player.Id, 2),
                (card, index) => _seatCardVisuals[(player.Id, index)] = card);
            Canvas.SetLeft(seat, position.X);
            Canvas.SetTop(seat, position.Y);
            SeatsCanvas.Children.Add(seat);
            _seatCenters[player.Id] = new Point(position.X + seat.Width / 2, position.Y + seat.Height / 2);
            if (isSelf) _selfSeat = seat;
            if (player.Hand.Count == 2 && !wasVisible && !isSelf)
                TableVisuals.Reveal(seat);
            if (player.Hand.Count == 2) _visibleHands.Add(player.Id);
        }
    }

    /// <summary>Affiche la valeur exacte des jetons blancs, jaunes, orange et rouges par joueur.</summary>
    private void RenderHistory(GameSnapshot game)
    {
        HistoryRowsPanel.Children.Clear();
        var colors = new[] { TokenColors.White, TokenColors.Yellow, TokenColors.Orange, TokenColors.Red };
        var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
        foreach (var _ in colors) header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        for (var index = 0; index < colors.Length; index++)
        {
            var label = new TextBlock
            {
                Text = TableVisuals.ColorName(colors[index])[..1],
                Foreground = new SolidColorBrush(Color.FromRgb(236, 204, 140)),
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(label, index + 1);
            header.Children.Add(label);
        }
        HistoryRowsPanel.Children.Add(header);

        foreach (var player in game.Players)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
            foreach (var _ in colors) row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            row.Children.Add(new GildedPlayerName
            {
                Text = player.Name, FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
            for (var index = 0; index < colors.Length; index++)
            {
                var signal = player.Signals.FirstOrDefault(choice => choice.Color == colors[index]);
                FrameworkElement cell = signal is null
                    ? new TextBlock
                    {
                        Text = "—",
                        Foreground = new SolidColorBrush(Color.FromRgb(149, 129, 105)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                    : TableVisuals.Chip(signal.Value, colors[index], 31);
                Grid.SetColumn(cell, index + 1);
                row.Children.Add(cell);
            }
            HistoryRowsPanel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(130, 56, 38, 36)),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(4),
                Child = row
            });
        }
    }

    /// <summary>N'affiche que les commandes permises par la phase et l'état du joueur.</summary>
    private void RenderControls(GameSnapshot game, Guid selfId)
    {
        var choosing = game.Phase is GamePhase.PreFlop or GamePhase.Flop or GamePhase.Turn or GamePhase.River;
        _availableDebrief = game.Phase is GamePhase.HeistComplete or GamePhase.Finished &&
            game.RevealedHands.Count == game.Players.Count ? game : null;
        ReviewHeistButton.Visibility = _availableDebrief is null ? Visibility.Collapsed : Visibility.Visible;
        var self = game.Players.First(player => player.Id == selfId);
        var choicesComplete = choosing && game.AllChoicesConfirmed;
        TokenActions.Visibility = choosing && !choicesComplete ? Visibility.Visible : Visibility.Collapsed;
        TokenActionWheel.Visibility = choosing && !choicesComplete &&
            RoundTransitionOverlay.Visibility != Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;
        FooterPhaseHint.Visibility = choosing && !choicesComplete ? Visibility.Collapsed : Visibility.Visible;
        FooterPhaseHint.Text = choicesComplete
            ? _session!.IsHost ? "CHOIX CONFIRMÉS · CONTINUEZ AVEC LA ROUE PRÈS DE VOTRE MAIN"
                : "CHOIX CONFIRMÉS · L'HÔTE VA POURSUIVRE"
            : game.Phase switch
        {
            GamePhase.Showdown => "RÉVÉLATION DES MAINS · ORDRE DES JETONS ROUGES",
            GamePhase.HeistComplete => "BRAQUAGE TERMINÉ · L'HÔTE PEUT LANCER LE SUIVANT",
            GamePhase.Finished => "PARTIE TERMINÉE",
            _ => string.Empty
        };
        ReturnTokenButton.IsEnabled = choosing && self.CurrentToken.HasValue && !game.AllChoicesConfirmed;
        ConfirmButton.IsEnabled = choosing && game.AllPlayersHaveTokens && !self.IsChoiceConfirmed;
        UpdateConfirmCue(ConfirmButton.IsEnabled);
        WithdrawButton.IsEnabled = choosing && self.IsChoiceConfirmed;
        TokenActionHint.Text = self.IsChoiceConfirmed ? "CHOIX CONFIRMÉ · EN ATTENTE DES AUTRES"
            : game.AllPlayersHaveTokens ? "TOUS LES JETONS SONT POSÉS · CONFIRMEZ VOTRE CHOIX"
            : "JETONS AU CENTRE · GLISSEZ VERS VOTRE MAIN";

        // Le centre de la roue nomme l'étape précise que son clic va déclencher.
        var (verb, title) = game.Phase switch
        {
            GamePhase.PreFlop => ("DÉVOILER", "LE FLOP"),
            GamePhase.Flop => ("DÉVOILER", "LE TURN"),
            GamePhase.Turn => ("DÉVOILER", "LA RIVER"),
            GamePhase.River => ("PASSER À", "LA RÉVÉLATION"),
            _ => ("CONTINUER", "LA PARTIE")
        };
        AdvanceWheelVerb.Text = verb;
        AdvanceWheelTitle.Text = title;
        AutomationProperties.SetName(AdvanceButton, $"{verb} {title}");
        NextHeistTitle.Text = $"BRAQUAGE {game.HeistNumber + 1}";
        AutomationProperties.SetName(NextHeistButton, $"Lancer le braquage {game.HeistNumber + 1}");
        AdvanceButton.Visibility = choosing && game.AllChoicesConfirmed ? Visibility.Visible : Visibility.Collapsed;
        RevealButton.Visibility = game.Phase == GamePhase.Showdown && game.NextPlayerToRevealId is not null
            ? Visibility.Visible : Visibility.Collapsed;
        ResolveButton.Visibility = game.Phase == GamePhase.Showdown && game.NextPlayerToRevealId is null
            ? Visibility.Visible : Visibility.Collapsed;
        NextHeistButton.Visibility = game.Phase == GamePhase.HeistComplete ? Visibility.Visible : Visibility.Collapsed;
        var hostCanAct = _session!.IsHost && (AdvanceButton.Visibility == Visibility.Visible ||
            RevealButton.Visibility == Visibility.Visible || ResolveButton.Visibility == Visibility.Visible ||
            NextHeistButton.Visibility == Visibility.Visible);
        _hostActionAvailable = hostCanAct;
        HostActions.Visibility = hostCanAct && RoundTransitionOverlay.Visibility != Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Fait pulser le contour de Confirmer dès que tous les jetons sont posés.</summary>
    private void UpdateConfirmCue(bool canConfirm)
    {
        if (!canConfirm)
        {
            ConfirmCueRing.BeginAnimation(OpacityProperty, null);
            ConfirmCueRing.Opacity = 0;
            _confirmCueActive = _confirmCueAnimated = false;
            return;
        }

        if (_confirmCueActive && _confirmCueAnimated == TableVisuals.AnimationsEnabled) return;
        ConfirmCueRing.BeginAnimation(OpacityProperty, null);
        _confirmCueActive = true;
        _confirmCueAnimated = TableVisuals.AnimationsEnabled;
        if (_confirmCueAnimated)
            ConfirmCueRing.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(850))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever
                });
        else
            ConfirmCueRing.Opacity = 0.75;
    }

    /// <summary>Nom lisible du tour dans le bandeau du plateau.</summary>
    private static string PhaseLabel(GameSnapshot game) => game.Phase switch
    {
        GamePhase.PreFlop => "Tour 1  ·  Jetons blancs",
        GamePhase.Flop => "Tour 2  ·  Jetons jaunes",
        GamePhase.Turn => "Tour 3  ·  Jetons orange",
        GamePhase.River => "Tour 4  ·  Jetons rouges",
        GamePhase.Showdown => "Révélation des mains",
        GamePhase.HeistComplete => game.LastHeistSucceeded == true ? "Braquage réussi" : "Braquage raté",
        GamePhase.Finished => game.Result == GameResult.Won ? "Victoire de l'équipe !" : "Défaite de l'équipe",
        _ => "En attente"
    };

    /// <summary>Instruction courte et contextualisée affichée au centre de la table.</summary>
    private static string TableMessage(GameSnapshot game)
    {
        if (game.Phase == GamePhase.Showdown)
        {
            var next = game.Players.FirstOrDefault(player => player.Id == game.NextPlayerToRevealId);
            return next is null ? "Toutes les mains sont révélées. L'hôte peut résoudre le braquage." :
                $"{next.Name} révèle sa main selon l'ordre des jetons rouges.";
        }
        if (game.Phase == GamePhase.HeistComplete)
            return "L'hôte peut commencer le braquage suivant.";
        if (game.Phase == GamePhase.Finished)
            return "La partie est terminée. Merci d'avoir joué !";
        return game.AllChoicesConfirmed ? "Tous les choix sont confirmés. L'hôte peut révéler le tour suivant." :
            game.AllPlayersHaveTokens ? "Chaque joueur possède un jeton : confirmez votre choix." :
            "Prenez un jeton au centre ou reprenez celui d'un autre joueur.";
    }

    /// <summary>Transmet une commande à l'hôte puis affiche le nouvel état autorisé.</summary>
    private async Task ApplyAsync(GameAction action, int? tokenValue = null, Guid? gestureId = null)
    {
        var session = _session!;
        var game = await session.Client.ApplyAsync(new GameActionRequest(action, tokenValue, gestureId));
        if (_session == session) RenderGame(game);
    }

    private async void ReturnToken_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() => ApplyAsync(GameAction.ReturnToken), showProgress: false);
    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        UpdateConfirmCue(false);
        await RunAsync(() => ApplyAsync(GameAction.ConfirmChoice), showProgress: false);
    }
    private async void Withdraw_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() => ApplyAsync(GameAction.WithdrawConfirmation), showProgress: false);
    private async void Advance_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() => ApplyAsync(GameAction.AdvanceRound), showProgress: false);
    private async void Reveal_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() => ApplyAsync(GameAction.RevealNextHand), showProgress: false);
    private async void Resolve_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() => ApplyAsync(GameAction.ResolveHeist), showProgress: false);
    private async void NextHeist_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() => ApplyAsync(GameAction.StartNextHeist), showProgress: false);

    /// <summary>Exécute une action UI unique et affiche une erreur courte si elle échoue.</summary>
    private async Task RunAsync(Func<Task> operation, bool showProgress = true)
    {
        if (_busy) return;
        _busy = true;
        CreateButton.IsEnabled = JoinButton.IsEnabled = ResumeButton.IsEnabled = StartButton.IsEnabled = false;
        HostActions.IsEnabled = false;
        LobbyCapacityPanel.IsEnabled = false;
        try
        {
            _connectionWarning = false;
            if (showProgress) ShowNotice("Action en cours…", autoHide: false);
            await operation();
            if (StatusText.Text == "Action en cours…") HideNotice();
        }
        catch (Exception error) when (_session is not null &&
                                      LobbyConnectionFailure.IsSessionClosed(error))
        {
            await ReturnToMenuAsync("La partie est terminée : l'hôte l'a fermée ou le délai de reprise a expiré.");
        }
        catch (Exception error) when (_session is not null &&
                                      LobbyConnectionFailure.IsHostUnavailable(error))
        {
            await HandleReconnectFailureAsync(_session);
        }
        catch (Exception error) { ShowNotice(error.Message, NoticeLevel.Error, autoHide: false); }
        finally
        {
            _busy = false;
            ConfirmExitButton.IsEnabled = true;
            CreateButton.IsEnabled = JoinButton.IsEnabled = ResumeButton.IsEnabled = true;
            HostActions.IsEnabled = true;
            if (_session is { } session && _lastLobbySnapshot is { } lobby &&
                LobbyPanel.Visibility == Visibility.Visible)
                RenderLobbySummary(session, lobby);
        }
    }

    /// <summary>Arrête le rafraîchissement avant de libérer la connexion et le tunnel éventuel.</summary>
    private async Task CloseSessionAsync()
    {
        _refreshTimer.Stop();
        var session = _session;
        _session = null;
        if (session is not null) await session.DisposeAsync();
    }

    /// <summary>Nettoie les informations de la partie et réaffiche l'accueil.</summary>
    private async Task ReturnToMenuAsync(string message, bool preserveGuestResume = false)
    {
        if (_draggedTokenValue is not null) EndChipDrag();
        StopCardDeal();
        HideHeistOutcome();
        _hostActionAvailable = false;
        HideRoundTransition();
        TokenActionWheel.Visibility = Visibility.Collapsed;
        UpdateConfirmCue(false);
        CloseRulesCard();
        SettingsOverlay.Visibility = Visibility.Collapsed;
        DismissExitConfirmation();
        HideMigrationOverlays();
        await CloseSessionAsync();
        MenuPanel.Visibility = Visibility.Visible;
        EntryPanel.Visibility = LobbyPanel.Visibility = GamePanel.Visibility = Visibility.Collapsed;
        PlayersList.ItemsSource = null;
        SeatsCanvas.Children.Clear();
        CommunityCardsPanel.Children.Clear();
        TokenBankPanel.Children.Clear();
        ChipFlightCanvas.Children.Clear();
        HistoryRowsPanel.Children.Clear();
        ActivityRowsPanel.Children.Clear();
        _remoteDragVisuals.Clear();
        _seatCenters.Clear();
        _bankCenters.Clear();
        _visibleTokenSources.Clear();
        _lastLobbyStatus = LobbyStatus.Open;
        _lastLobbySnapshot = null;
        _lastRenderedPhase = null;
        _lastHeistNumber = _lastCommunityCount = 0;
        _lastSeenEventSequence = 0;
        _lastShowdownRevealCount = -1;
        ShowdownRowsPanel.Children.Clear();
        _availableDebrief = null;
        ReviewHeistButton.Visibility = Visibility.Collapsed;
        ShowdownResultsPanel.Visibility = HistorySubheading.Visibility = Visibility.Collapsed;
        ActivityPanel.Visibility = Visibility.Visible;
        _visibleHands.Clear();
        ResetScoreCards();
        _connectionWarning = false;
        _reconnectWindow.Reset();
        ReconnectOverlay.Visibility = Visibility.Collapsed;
        PausedGameOverlay.Visibility = Visibility.Collapsed;
        if (!preserveGuestResume) GuestResumeStore.Clear();
        RefreshResumeButton();
        if (string.IsNullOrWhiteSpace(message)) HideNotice();
        else ShowNotice(message, NoticeLevel.Info, autoHide: false);
    }

    /// <summary>Affiche un message central selon sa gravité et sa durée de lecture.</summary>
    private void ShowNotice(string message, NoticeLevel level = NoticeLevel.Info, bool autoHide = true)
    {
        _noticeTimer.Stop();
        StatusText.Text = message;
        NoticeIcon.Text = level switch
        {
            NoticeLevel.Success => "✓",
            NoticeLevel.Error => "!",
            _ => "i"
        };
        NoticeBanner.BorderBrush = level switch
        {
            NoticeLevel.Success => new SolidColorBrush(Color.FromRgb(139, 203, 137)),
            NoticeLevel.Error => new SolidColorBrush(Color.FromRgb(242, 117, 104)),
            _ => new SolidColorBrush(Color.FromRgb(229, 185, 88))
        };
        NoticeIcon.Foreground = NoticeBanner.BorderBrush;
        NoticeBanner.Visibility = Visibility.Visible;
        if (autoHide) _noticeTimer.Start();
    }

    /// <summary>Masque le message sans affecter la session de jeu.</summary>
    private void HideNotice()
    {
        _noticeTimer.Stop();
        NoticeBanner.Visibility = Visibility.Collapsed;
        StatusText.Text = string.Empty;
    }

    /// <summary>Permet de fermer explicitement une erreur persistante.</summary>
    private void NoticeClose_Click(object sender, RoutedEventArgs e) => HideNotice();

    private enum NoticeLevel { Info, Success, Error }

    /// <summary>Notifie le serveur du départ avant de fermer l'application.</summary>
    protected override void OnClosed(EventArgs e)
    {
        StopCardDeal();
        if (Mouse.OverrideCursor == _gloveCursor) Mouse.OverrideCursor = null;
        _refreshTimer.Stop();
        _dragHeartbeatTimer.Stop();
        _noticeTimer.Stop();
        _roundTransitionTimer.Stop();
        var session = _session;
        _session = null;
        if (session is not null)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                Task.Run(() => session.Client.LeaveAsync(timeout.Token)).GetAwaiter().GetResult();
                if (!session.IsHost && (_lastLobbyStatus == LobbyStatus.Open || DepartureReturnsTeamToLobby))
                    GuestResumeStore.Clear();
            }
            catch { /* La fermeture locale reste possible si le serveur est déjà indisponible. */ }
            try { session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            catch { /* Le processus se termine même si le tunnel s'est déjà arrêté. */ }
        }
        base.OnClosed(e);
    }
}
