using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Model.Game;
using Model.Game.Tokens.TokenEnum;
using Network;

namespace View;

/// <summary>Interaction avec les jetons physiques et diffusion des gestes en cours.</summary>
public partial class MainWindow
{
    private readonly DispatcherTimer _dragHeartbeatTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    // Les actions viennent de l'hôte, dans un ordre commun à tous les clients.
    private long _lastSeenEventSequence;
    private readonly Dictionary<Guid, Point> _seatCenters = [];
    private readonly Dictionary<int, Point> _bankCenters = [];
    private readonly Dictionary<int, FrameworkElement> _visibleTokenSources = [];
    private Border? _selfSeat;
    private Rect _bankDropArea;
    // Un seul jeton suit la souris ; l'attribution est envoyée à l'hôte au dépôt.
    private int? _draggedTokenValue;
    private Guid? _draggedFromPlayerId;
    private FrameworkElement? _dragSource;
    private FrameworkElement? _dragVisual;
    private TokenColors _dragColor;
    private Brush? _selfSeatOriginalBrush;
    private Thickness _selfSeatOriginalThickness;
    private Guid _dragGestureId;
    private long _dragRevision;
    private Point _lastDragPosition;
    private DateTimeOffset _lastDragSentAt;
    private int _pendingDragUpdates;
    private readonly Dictionary<Guid, RemoteDragVisual> _remoteDragVisuals = [];

    /// <summary>Pose les jetons libres au centre du tapis ; chacun garde la même place sur tous les clients.</summary>
    private void RenderTokenBank(GameSnapshot game, bool phaseChanged)
    {
        TokenBankPanel.Children.Clear();
        _bankCenters.Clear();
        var choosing = game.Phase is GamePhase.PreFlop or GamePhase.Flop or GamePhase.Turn or GamePhase.River;
        TokenBankPanel.Visibility = choosing ? Visibility.Visible : Visibility.Collapsed;
        if (!choosing) return;

        var count = game.Players.Count;
        const double chipSize = 72;
        const double spacing = 82;
        // La rangée se trouve juste sous les cartes communes, loin de la main et de la roue d'action.
        const double trayTop = 370;
        const double trayHeight = 100;
        var trayWidth = (count - 1) * spacing + 110;
        var trayLeft = (1280 - trayWidth) / 2;
        _bankDropArea = new Rect(trayLeft, trayTop, trayWidth, trayHeight);

        // L'ovale discret indique où rendre un jeton, sans intercepter le glisser-déposer.
        var tray = new Border
        {
            Width = trayWidth,
            Height = trayHeight,
            CornerRadius = new CornerRadius(trayHeight / 2),
            Background = new SolidColorBrush(Color.FromArgb(55, 29, 13, 17)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(125, 174, 130, 67)),
            BorderThickness = new Thickness(1.5),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(tray, trayLeft);
        Canvas.SetTop(tray, trayTop);
        TokenBankPanel.Children.Add(tray);

        var available = game.AvailableTokens.ToHashSet();
        for (var value = 1; value <= count; value++)
        {
            var offset = value - 1 - (count - 1) / 2.0;
            var centerX = 640 + offset * spacing;
            var distance = Math.Abs(offset) / ((count - 1) / 2.0);
            var centerY = 410 + 12 * distance * distance;
            _bankCenters[value] = new Point(centerX, centerY);
            if (!available.Contains(value)) continue;

            var chip = TableVisuals.Chip(value, TableVisuals.RoundColor(game.Phase), chipSize);
            Canvas.SetLeft(chip, centerX - chipSize / 2);
            Canvas.SetTop(chip, centerY - chipSize / 2);
            TokenBankPanel.Children.Add(chip);
            _visibleTokenSources[value] = chip;
            EnableChipDrag(chip, value, null, game.Phase, !game.AllChoicesConfirmed);
            if (phaseChanged) TableVisuals.Reveal(chip);
        }
    }

    /// <summary>Montre la position des jetons tenus par les autres joueurs avant leur dépôt.</summary>
    private void RenderRemoteDrags(GameSnapshot game, Guid selfId)
    {
        var visible = game.ActiveDrags.Where(drag => drag.PlayerId != selfId).ToArray();
        var ids = visible.Select(drag => drag.PlayerId).ToHashSet();
        foreach (var playerId in _remoteDragVisuals.Keys.Where(id => !ids.Contains(id)).ToArray())
        {
            ChipFlightCanvas.Children.Remove(_remoteDragVisuals[playerId].Element);
            _remoteDragVisuals.Remove(playerId);
        }
        foreach (var drag in visible)
        {
            if (_visibleTokenSources.TryGetValue(drag.TokenValue, out var source))
                source.Opacity = 0.25;
            if (!_remoteDragVisuals.TryGetValue(drag.PlayerId, out var existing) ||
                existing.GestureId != drag.GestureId)
            {
                if (existing is not null) ChipFlightCanvas.Children.Remove(existing.Element);
                var visual = new StackPanel { Width = 110, IsHitTestVisible = false };
                visual.Children.Add(new TextBlock
                {
                    Text = $"{drag.PlayerName} déplace",
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    FontSize = 13,
                    TextAlignment = TextAlignment.Center,
                    Background = new SolidColorBrush(Color.FromArgb(220, 29, 19, 18))
                });
                var chip = TableVisuals.Chip(drag.TokenValue, TableVisuals.RoundColor(game.Phase), 58);
                chip.HorizontalAlignment = HorizontalAlignment.Center;
                visual.Children.Add(chip);
                ChipFlightCanvas.Children.Add(visual);
                Canvas.SetLeft(visual, drag.X - 55);
                Canvas.SetTop(visual, drag.Y - 49);
                _remoteDragVisuals[drag.PlayerId] = new RemoteDragVisual(drag.GestureId, drag.Revision, visual);
                continue;
            }
            if (drag.Revision <= existing.Revision) continue;
            existing.Revision = drag.Revision;
            if (TableVisuals.AnimationsEnabled)
            {
                existing.Element.BeginAnimation(Canvas.LeftProperty,
                    new DoubleAnimation(drag.X - 55, TimeSpan.FromMilliseconds(220)));
                existing.Element.BeginAnimation(Canvas.TopProperty,
                    new DoubleAnimation(drag.Y - 49, TimeSpan.FromMilliseconds(220)));
            }
            else
            {
                existing.Element.BeginAnimation(Canvas.LeftProperty, null);
                existing.Element.BeginAnimation(Canvas.TopProperty, null);
                Canvas.SetLeft(existing.Element, drag.X - 55);
                Canvas.SetTop(existing.Element, drag.Y - 49);
            }
        }
        _refreshTimer.Interval = visible.Length > 0 ? TimeSpan.FromMilliseconds(200) :
            TimeSpan.FromMilliseconds(500);
    }

    private sealed class RemoteDragVisual(Guid gestureId, long revision, FrameworkElement element)
    {
        public Guid GestureId { get; } = gestureId;
        public long Revision { get; set; } = revision;
        public FrameworkElement Element { get; } = element;
    }

    /// <summary>Autorise la prise d'un jeton libre ou la reprise du jeton d'un autre joueur.</summary>
    private void EnableChipDrag(FrameworkElement chip, int value, Guid? ownerId, GamePhase phase,
        bool roundOpen)
    {
        if (!roundOpen || phase is not (GamePhase.PreFlop or GamePhase.Flop or GamePhase.Turn or GamePhase.River))
            return;
        chip.Cursor = Cursors.Hand;
        chip.MouseLeftButtonDown += (_, e) =>
        {
            if (_busy || _draggedTokenValue is not null) return;
            _draggedTokenValue = value;
            _draggedFromPlayerId = ownerId;
            _dragSource = chip;
            _dragColor = TableVisuals.RoundColor(phase);
            _dragGestureId = Guid.NewGuid();
            _dragRevision = 0;
            _lastDragSentAt = DateTimeOffset.UtcNow;
            chip.Opacity = 0.35;
            _dragVisual = TableVisuals.Chip(value, _dragColor, 62);
            ChipFlightCanvas.Children.Add(_dragVisual);
            if (_selfSeat is not null)
            {
                _selfSeatOriginalBrush = _selfSeat.BorderBrush;
                _selfSeatOriginalThickness = _selfSeat.BorderThickness;
            }
            MoveDraggedChip(e.GetPosition(ChipFlightCanvas));
            PublishDragPosition(force: true);
            _dragHeartbeatTimer.Start();
            Mouse.Capture(this);
            e.Handled = true;
        };
    }

    /// <summary>Déplace le jeton au-dessus du tapis pendant que la souris est capturée.</summary>
    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedTokenValue is not null) MoveDraggedChip(e.GetPosition(ChipFlightCanvas));
    }

    /// <summary>Garde le jeton sous la souris et souligne la main qui peut le recevoir.</summary>
    private void MoveDraggedChip(Point point)
    {
        if (_dragVisual is null) return;
        Canvas.SetLeft(_dragVisual, point.X - 31);
        Canvas.SetTop(_dragVisual, point.Y - 31);
        _lastDragPosition = point;
        if (DateTimeOffset.UtcNow - _lastDragSentAt > TimeSpan.FromMilliseconds(120))
            PublishDragPosition();
        if (_selfSeat is not null)
        {
            var overHand = new Rect(535, 560, _selfSeat.Width, _selfSeat.Height).Contains(point);
            _selfSeat.BorderBrush = overHand ? Brushes.Gold : _selfSeatOriginalBrush;
            _selfSeat.BorderThickness = overHand ? new Thickness(3) : _selfSeatOriginalThickness;
        }
    }

    /// <summary>Le dépôt sur sa main prend le jeton ; le dépôt au centre rend son propre jeton.</summary>
    private async void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedTokenValue is not { } value) return;
        var point = e.GetPosition(ChipFlightCanvas);
        var selfId = _session?.Client.Identity?.PlayerId;
        var onHand = _selfSeat is not null &&
            new Rect(535, 560, _selfSeat.Width, _selfSeat.Height).Contains(point);
        var onBank = _bankDropArea.Contains(point);
        var fromPlayerId = _draggedFromPlayerId;
        var gestureId = _dragGestureId;
        var acceptedDrop = selfId is not null &&
            (onHand && fromPlayerId != selfId || onBank && fromPlayerId == selfId);
        EndChipDrag(keepSourceDimmed: acceptedDrop);
        e.Handled = true;
        if (onHand && selfId is not null && fromPlayerId != selfId)
            await RunAsync(() => ApplyAsync(GameAction.TakeToken, value, gestureId), showProgress: false);
        else if (onBank && selfId is not null && fromPlayerId == selfId)
            await RunAsync(() => ApplyAsync(GameAction.ReturnToken, gestureId: gestureId), showProgress: false);
    }

    /// <summary>Annule un glissement interrompu par un changement de fenêtre.</summary>
    private void Window_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_draggedTokenValue is not null) EndChipDrag();
    }

    /// <summary>Libère la capture même si le joueur annule son geste hors du tapis.</summary>
    private void EndChipDrag(bool keepSourceDimmed = false)
    {
        var value = _draggedTokenValue;
        _dragHeartbeatTimer.Stop();
        PublishDragPosition(isActive: false, force: true);
        _draggedTokenValue = null;
        _dragGestureId = Guid.Empty;
        Mouse.Capture(null);
        if (!keepSourceDimmed && _dragSource is not null) _dragSource.Opacity = 1;
        if (!keepSourceDimmed && value is { } tokenValue &&
            _visibleTokenSources.TryGetValue(tokenValue, out var currentSource))
            currentSource.Opacity = 1;
        if (_dragVisual is not null) ChipFlightCanvas.Children.Remove(_dragVisual);
        if (_selfSeat is not null)
        {
            _selfSeat.BorderBrush = _selfSeatOriginalBrush;
            _selfSeat.BorderThickness = _selfSeatOriginalThickness;
        }
        _draggedFromPlayerId = null;
        _dragSource = null;
        _dragVisual = null;
    }

    /// <summary>Envoie une position limitée au tapis sans attendre le serveur dans la boucle souris.</summary>
    private void PublishDragPosition(bool isActive = true, bool force = false)
    {
        if (_draggedTokenValue is not { } value || _dragGestureId == Guid.Empty || _session is not { } session)
            return;
        if (isActive && !force && _pendingDragUpdates >= 3) return;
        var request = new GameDragRequest(_dragGestureId, value, ++_dragRevision,
            Math.Clamp(_lastDragPosition.X, 0, 1280), Math.Clamp(_lastDragPosition.Y, 0, 720), isActive);
        _lastDragSentAt = DateTimeOffset.UtcNow;
        _pendingDragUpdates++;
        _ = SendDragAsync(session, request);
    }

    /// <summary>Les erreurs de déplacement temporaire ne bloquent pas la prise finale.</summary>
    private async Task SendDragAsync(DesktopLobbySession session, GameDragRequest request)
    {
        try { await session.Client.UpdateDragAsync(request); }
        catch (Exception) { /* Le prochain état du lobby signalera une perte de connexion durable. */ }
        finally { _pendingDragUpdates--; }
    }

    /// <summary>Présente les derniers gestes du groupe sous le tableau des jetons.</summary>
    private void RenderActivity(GameSnapshot game)
    {
        ActivityRowsPanel.Children.Clear();
        foreach (var action in game.RecentEvents.TakeLast(5).Reverse())
        {
            ActivityRowsPanel.Children.Add(new TextBlock
            {
                Text = EventLabel(action),
                TextWrapping = TextWrapping.Wrap,
                Foreground = action.Kind == GameEventKind.HeistResolved
                    ? action.HeistSucceeded == true ? Brushes.LightGreen : Brushes.Salmon
                    : action.Kind == GameEventKind.TokenStolen ? new SolidColorBrush(Color.FromRgb(255, 203, 116))
                    : new SolidColorBrush(Color.FromRgb(221, 207, 184)),
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 7)
            });
        }
    }

    /// <summary>Ne rejoue chaque événement du serveur qu'une seule fois sur ce client.</summary>
    private void ProcessGameEvents(GameSnapshot game)
    {
        var unseen = game.RecentEvents.Where(action => action.Sequence > _lastSeenEventSequence).ToArray();
        if (unseen.Length == 0) return;
        _lastSeenEventSequence = unseen[^1].Sequence;
        foreach (var action in unseen.TakeLast(8))
        {
            if (action.Kind is GameEventKind.TokenTaken or GameEventKind.TokenStolen or GameEventKind.TokenReturned)
                AnimateChipMove(action);
            if (action.Kind == GameEventKind.RoundRevealed && action.HeistNumber == game.HeistNumber &&
                action.Phase == game.Phase)
                ShowRoundTransition(action.Phase);
            if (action.Kind == GameEventKind.HeistResolved && action.HeistNumber == game.HeistNumber)
                ShowHeistOutcome(action, game);
            if (action.Kind is GameEventKind.HeistStarted or GameEventKind.TokenTaken or
                GameEventKind.TokenStolen or GameEventKind.TokenReturned or
                GameEventKind.HandRevealed or GameEventKind.HeistResolved)
            {
                var level = action.Kind == GameEventKind.HeistResolved
                    ? action.HeistSucceeded == true ? NoticeLevel.Success : NoticeLevel.Error
                    : NoticeLevel.Info;
                ShowNotice(EventLabel(action), level);
            }
        }
    }

    /// <summary>Anime le trajet observé par tous, depuis la banque ou la main précédente.</summary>
    private void AnimateChipMove(GameEventSnapshot action)
    {
        if (!TableVisuals.AnimationsEnabled || action.TokenValue is not { } value) return;
        if (!_bankCenters.TryGetValue(value, out var bank)) return;
        var from = action.Kind switch
        {
            GameEventKind.TokenStolen when action.FromPlayerId is { } previous &&
                _seatCenters.TryGetValue(previous, out var oldSeat) => oldSeat,
            GameEventKind.TokenReturned when _seatCenters.TryGetValue(action.ActorId, out var owner) => owner,
            _ => bank
        };
        var to = action.Kind == GameEventKind.TokenReturned ? bank :
            _seatCenters.TryGetValue(action.ActorId, out var seat) ? seat : bank;
        if (from == to) return;
        var movingChip = TableVisuals.Chip(value, TableVisuals.RoundColor(action.Phase), 55);
        ChipFlightCanvas.Children.Add(movingChip);
        Canvas.SetLeft(movingChip, from.X - 27);
        Canvas.SetTop(movingChip, from.Y - 27);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
        var horizontal = new DoubleAnimation(to.X - 27, TimeSpan.FromMilliseconds(560))
        {
            EasingFunction = ease
        };
        var vertical = new DoubleAnimation(to.Y - 27, TimeSpan.FromMilliseconds(560))
        {
            EasingFunction = ease
        };
        horizontal.Completed += (_, _) => ChipFlightCanvas.Children.Remove(movingChip);
        movingChip.BeginAnimation(Canvas.LeftProperty, horizontal);
        movingChip.BeginAnimation(Canvas.TopProperty, vertical);
    }

    /// <summary>Construit des phrases courtes identiques pour le bandeau et le journal.</summary>
    private static string EventLabel(GameEventSnapshot action) => action.Kind switch
    {
        GameEventKind.HeistStarted => $"Braquage {action.HeistNumber} : les cartes sont distribuées.",
        GameEventKind.TokenTaken => $"{action.ActorName} prend le jeton {action.TokenValue}.",
        GameEventKind.TokenStolen => $"{action.ActorName} reprend le jeton {action.TokenValue} à {action.FromPlayerName}.",
        GameEventKind.TokenReturned => $"{action.ActorName} rend le jeton {action.TokenValue} au centre.",
        GameEventKind.ChoiceConfirmed => $"{action.ActorName} confirme son choix.",
        GameEventKind.ConfirmationWithdrawn => $"{action.ActorName} retire sa confirmation.",
        GameEventKind.RoundRevealed => action.Phase switch
        {
            GamePhase.Flop => "Le flop est dévoilé : choisissez les jetons jaunes.",
            GamePhase.Turn => "Le turn est dévoilé : choisissez les jetons orange.",
            GamePhase.River => "La river est dévoilée : choisissez les jetons rouges.",
            _ => "Les jetons rouges sont fixés : révélez les mains dans l'ordre."
        },
        GameEventKind.HandRevealed => $"{action.ActorName} révèle sa main (jeton {action.TokenValue}).",
        GameEventKind.HeistResolved when action.Phase == GamePhase.Finished =>
            action.HeistSucceeded == true ? "Victoire de l'équipe !" : "La partie est perdue.",
        GameEventKind.HeistResolved => action.HeistSucceeded == true
            ? $"Braquage {action.HeistNumber} réussi !" : $"Braquage {action.HeistNumber} raté.",
        _ => string.Empty
    };
}

