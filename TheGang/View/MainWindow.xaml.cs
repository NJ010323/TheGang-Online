using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    // Les instantanés réseau alimentent le plateau toutes les deux secondes.
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    // Les confirmations brèves s'effacent ; les erreurs attendent une action du joueur.
    private readonly DispatcherTimer _noticeTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _connectionWarning;
    // Ces indicateurs empêchent de superposer des requêtes ou des actions utilisateur.
    private bool _refreshing;
    private bool _busy;
    private LobbyStatus _lastLobbyStatus = LobbyStatus.Open;
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
        _heistOutcomeTimer.Tick += (_, _) => HideHeistOutcome();
        _roundTransitionTimer.Tick += (_, _) => HideRoundTransition();
        _dragHeartbeatTimer.Tick += (_, _) => PublishDragPosition();
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

    /// <summary>Ferme le jeu via le menu.</summary>
    private void Quit_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Ouvre les paramètres d'affichage et synchronise les choix avec la fenêtre.</summary>
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        WindowedOption.IsChecked = !_isFullScreen;
        FullScreenOption.IsChecked = _isFullScreen;
        AnimationsOnOption.IsChecked = TableVisuals.AnimationsEnabled;
        AnimationsOffOption.IsChecked = !TableVisuals.AnimationsEnabled;
        SettingsOverlay.Visibility = Visibility.Visible;
    }

    /// <summary>Ferme le panneau des paramètres sans changer la session en cours.</summary>
    private void CloseSettings_Click(object sender, RoutedEventArgs e) =>
        SettingsOverlay.Visibility = Visibility.Collapsed;

    /// <summary>Applique immédiatement le mode d'affichage choisi dans les paramètres.</summary>
    private void WindowedMode_Click(object sender, RoutedEventArgs e) => SetFullScreen(false);
    private void FullScreenMode_Click(object sender, RoutedEventArgs e) => SetFullScreen(true);

    /// <summary>Active ou réduit les mouvements décoratifs sans toucher à la partie.</summary>
    private void AnimationsOn_Click(object sender, RoutedEventArgs e) => TableVisuals.AnimationsEnabled = true;
    private void AnimationsOff_Click(object sender, RoutedEventArgs e) => TableVisuals.AnimationsEnabled = false;

    /// <summary>F11 active le plein écran ; Échap en sort.</summary>
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            SetFullScreen(!_isFullScreen);
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

    /// <summary>Affiche le lobby et commence à surveiller les changements de joueurs.</summary>
    private void EnterLobby()
    {
        MenuPanel.Visibility = Visibility.Collapsed;
        EntryPanel.Visibility = Visibility.Collapsed;
        GamePanel.Visibility = Visibility.Collapsed;
        LobbyPanel.Visibility = Visibility.Visible;
        InvitationOutput.Text = _session!.Invitation;
        StartButton.Visibility = _session.IsHost ? Visibility.Visible : Visibility.Collapsed;
        _lastLobbyStatus = LobbyStatus.Open;
        _lastRenderedPhase = null;
        _lastHeistNumber = 0;
        _lastSeenEventSequence = 0;
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

    /// <summary>L'hôte distribue les cartes lorsque trois joueurs ou plus sont présents.</summary>
    private async void Start_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var session = _session!;
        var game = await session.Client.StartAsync();
        if (_session == session) RenderGame(game);
    });

    /// <summary>Un départ volontaire ferme le lobby si le joueur est hôte.</summary>
    private async void Leave_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        await _session!.Client.LeaveAsync();
        await ReturnToMenuAsync(string.Empty);
    });

    /// <summary>Une partie commencée s'achève pour tous si un joueur la quitte.</summary>
    private async void ExitGame_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        await _session!.Client.LeaveAsync();
        await ReturnToMenuAsync("La partie a été fermée pour tous les joueurs.");
    });

    /// <summary>Agrandit le tapis ou réaffiche le détail de tous les jetons.</summary>
    private void HistoryToggle_Click(object sender, RoutedEventArgs e)
    {
        _historyVisible = !_historyVisible;
        HistoryColumn.Width = new GridLength(_historyVisible ? 306 : 0);
        HistoryPanel.Visibility = _historyVisible ? Visibility.Visible : Visibility.Collapsed;
        HistoryToggleButton.Content = _historyVisible ? "Masquer le tableau" : "Afficher le tableau";
    }

    /// <summary>Interroge périodiquement l'hôte pour mettre à jour lobby et partie.</summary>
    private async void RefreshTimer_Tick(object? sender, EventArgs e) => await RefreshAsync();

    /// <summary>Revient au menu si l'hôte ferme son application ou perd le tunnel.</summary>
    private async Task RefreshAsync()
    {
        if (_refreshing || _session is null || _busy || _draggedTokenValue is not null) return;
        var session = _session;
        _refreshing = true;
        try
        {
            var lobby = await session.Client.GetLobbyAsync();
            if (_session != session) return;
            _lastLobbyStatus = lobby.Status;
            LobbyDetails.Text = $"Code local : {lobby.Code}   ·   {lobby.Players.Count}/{lobby.MaxPlayers} joueurs";
            PlayersList.ItemsSource = lobby.Players.Select(player =>
                player.Id == lobby.HostId ? $"♠  {player.Name}  ·  hôte" : $"♠  {player.Name}").ToArray();
            StartButton.IsEnabled = session.IsHost && lobby.Players.Count >= 3 && lobby.Status == LobbyStatus.Open;
            LeaveButton.Visibility = lobby.Status == LobbyStatus.Open ? Visibility.Visible : Visibility.Collapsed;
            if (lobby.Status == LobbyStatus.Started)
            {
                var game = await session.Client.GetGameAsync();
                if (_session != session || _busy || _draggedTokenValue is not null) return;
                if (game.RecentEvents.LastOrDefault()?.Sequence < _lastSeenEventSequence) return;
                RenderGame(game);
            }
            if (_connectionWarning)
            {
                _connectionWarning = false;
                HideNotice();
            }
        }
        catch (Exception error) when (_session == session && !session.IsHost &&
                                      LobbyConnectionFailure.IsHostUnavailable(error))
        {
            await ReturnToMenuAsync("La partie s'est terminée : l'hôte ou un joueur a quitté la table.");
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

    /// <summary>Construit le plateau à partir de la projection réseau du joueur courant.</summary>
    private void RenderGame(GameSnapshot game)
    {
        if (_session?.Client.Identity is not { } identity) return;
        LobbyPanel.Visibility = Visibility.Collapsed;
        GamePanel.Visibility = Visibility.Visible;
        _refreshTimer.Interval = TimeSpan.FromMilliseconds(500);
        if (game.HeistNumber != _lastHeistNumber)
        {
            HideHeistOutcome();
            HideRoundTransition();
            _lastHeistNumber = game.HeistNumber;
            _lastCommunityCount = 0;
            CommunityCardsPanel.Children.Clear();
            _visibleHands.Clear();
            ChipFlightCanvas.Children.Clear();
            _remoteDragVisuals.Clear();
        }

        GamePhaseText.Text = PhaseLabel(game);
        GameScoreText.Text = $"Braquage {game.HeistNumber}   ·   {game.SuccessfulHeists} réussis / {game.FailedHeists} ratés";
        TablePhaseText.Text = TableMessage(game);
        var phaseChanged = _lastRenderedPhase != game.Phase;
        if (phaseChanged)
        {
            TableVisuals.Reveal(GamePhaseText);
            _lastRenderedPhase = game.Phase;
        }

        RenderCommunityCards(game);
        _visibleTokenSources.Clear();
        RenderSeats(game, identity.PlayerId, identity.Lobby.HostId);
        RenderTokenBank(game, phaseChanged);
        RenderRemoteDrags(game, identity.PlayerId);
        RenderHistory(game);
        RenderActivity(game);
        RenderControls(game, identity.PlayerId);
        ProcessGameEvents(game);
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
                player.Id == game.NextPlayerToRevealId, TableVisuals.RoundColor(game.Phase),
                (chip, value) =>
                {
                    _visibleTokenSources[value] = chip;
                    EnableChipDrag(chip, value, player.Id, game.Phase, !game.AllChoicesConfirmed);
                });
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
                HorizontalAlignment = HorizontalAlignment.Center,
                ToolTip = $"Tour {index + 1} : jeton {TableVisuals.ColorName(colors[index]).ToLowerInvariant()}"
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
            row.Children.Add(new TextBlock
            {
                Text = player.Name,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = player.Name
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
        var self = game.Players.First(player => player.Id == selfId);
        TokenActions.Visibility = choosing ? Visibility.Visible : Visibility.Collapsed;
        ReturnTokenButton.IsEnabled = choosing && self.CurrentToken.HasValue && !game.AllChoicesConfirmed;
        ConfirmButton.IsEnabled = choosing && game.AllPlayersHaveTokens && !self.IsChoiceConfirmed;
        WithdrawButton.IsEnabled = choosing && self.IsChoiceConfirmed;

        HostActions.Visibility = _session!.IsHost ? Visibility.Visible : Visibility.Collapsed;
        AdvanceButton.Visibility = choosing && game.AllChoicesConfirmed ? Visibility.Visible : Visibility.Collapsed;
        RevealButton.Visibility = game.Phase == GamePhase.Showdown && game.NextPlayerToRevealId is not null
            ? Visibility.Visible : Visibility.Collapsed;
        ResolveButton.Visibility = game.Phase == GamePhase.Showdown && game.NextPlayerToRevealId is null
            ? Visibility.Visible : Visibility.Collapsed;
        NextHeistButton.Visibility = game.Phase == GamePhase.HeistComplete ? Visibility.Visible : Visibility.Collapsed;
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
            "Choisissez ou reprenez un jeton. Les cartes des autres restent cachées.";
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
    private async void Confirm_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(() => ApplyAsync(GameAction.ConfirmChoice), showProgress: false);
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
        CreateButton.IsEnabled = JoinButton.IsEnabled = StartButton.IsEnabled = false;
        try
        {
            _connectionWarning = false;
            if (showProgress) ShowNotice("Action en cours…", autoHide: false);
            await operation();
            if (StatusText.Text == "Action en cours…") HideNotice();
        }
        catch (Exception error) when (_session is { IsHost: false } &&
                                      LobbyConnectionFailure.IsHostUnavailable(error))
        {
            await ReturnToMenuAsync("La partie s'est terminée : l'hôte ou un joueur a quitté la table.");
        }
        catch (Exception error) { ShowNotice(error.Message, NoticeLevel.Error, autoHide: false); }
        finally
        {
            _busy = false;
            CreateButton.IsEnabled = JoinButton.IsEnabled = true;
            if (_session is { IsHost: true } && LobbyPanel.Visibility == Visibility.Visible)
                StartButton.IsEnabled = PlayersList.Items.Count >= 3;
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
    private async Task ReturnToMenuAsync(string message)
    {
        if (_draggedTokenValue is not null) EndChipDrag();
        HideHeistOutcome();
        HideRoundTransition();
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
        _lastRenderedPhase = null;
        _lastHeistNumber = _lastCommunityCount = 0;
        _lastSeenEventSequence = 0;
        _visibleHands.Clear();
        _connectionWarning = false;
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
        if (Mouse.OverrideCursor == _gloveCursor) Mouse.OverrideCursor = null;
        _refreshTimer.Stop();
        _dragHeartbeatTimer.Stop();
        _noticeTimer.Stop();
        _heistOutcomeTimer.Stop();
        _roundTransitionTimer.Stop();
        var session = _session;
        _session = null;
        if (session is not null)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                Task.Run(() => session.Client.LeaveAsync(timeout.Token)).GetAwaiter().GetResult();
            }
            catch { /* La fermeture locale reste possible si le serveur est déjà indisponible. */ }
            try { session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            catch { /* Le processus se termine même si le tunnel s'est déjà arrêté. */ }
        }
        base.OnClosed(e);
    }
}
