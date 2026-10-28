using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Network;

namespace View;

/// <summary>Distribue visuellement les deux cartes privées sans exposer celles des adversaires.</summary>
public partial class MainWindow
{
    // Les places sont reconstruites à chaque instantané ; ces compteurs conservent l'avancement.
    private readonly Dictionary<Guid, int> _dealtCardCounts = [];
    private readonly Dictionary<(Guid PlayerId, int CardIndex), FrameworkElement> _seatCardVisuals = [];
    private readonly List<FrameworkElement> _dealFlightVisuals = [];
    private int _dealingHeistNumber;
    private int _dealGeneration;

    /// <summary>Cache les deux cartes de chaque place avant le premier rendu du braquage.</summary>
    private void PrepareCardDeal(GameSnapshot game)
    {
        _dealingHeistNumber = game.HeistNumber;
        foreach (var player in game.Players)
            _dealtCardCounts[player.Id] = 0;
    }

    /// <summary>Fait partir une carte du paquet vers chaque joueur, puis recommence pour la seconde.</summary>
    private void StartCardDeal(GameSnapshot game, Guid selfId)
    {
        if (_dealingHeistNumber != game.HeistNumber || game.Players.Count == 0) return;
        var generation = _dealGeneration;
        var deckPosition = new Point(345, 359);
        var deck = TableVisuals.Card(null, 53, 76);
        deck.RenderTransform = new RotateTransform(-9, 26, 38);
        Canvas.SetLeft(deck, deckPosition.X - 26);
        Canvas.SetTop(deck, deckPosition.Y - 38);
        ChipFlightCanvas.Children.Add(deck);
        _dealFlightVisuals.Add(deck);

        var remaining = 0;
        for (var pass = 0; pass < game.Players.Max(p=>p.HandCount); pass++)
        {
            for (var index = 0; index < game.Players.Count; index++)
            {
                var player = game.Players[index];
                if (pass >= player.HandCount) continue;
                if (!_seatCenters.TryGetValue(player.Id, out var seatCenter)) continue;
                remaining++;
                var card = TableVisuals.Card(player.Id == selfId && player.Hand.Count > pass
                    ? player.Hand[pass] : null, 46, 66);
                card.RenderTransformOrigin = new Point(0.5, 0.5);
                var transforms = new TransformGroup();
                var scale = new ScaleTransform(0.72, 0.72);
                var turn = new RotateTransform(-12);
                transforms.Children.Add(scale);
                transforms.Children.Add(turn);
                card.RenderTransform = transforms;
                ChipFlightCanvas.Children.Add(card);
                _dealFlightVisuals.Add(card);
                Canvas.SetLeft(card, deckPosition.X - 26);
                Canvas.SetTop(card, deckPosition.Y - 36);

                var delay = TimeSpan.FromMilliseconds((pass * game.Players.Count + index) * 170);
                var duration = TimeSpan.FromMilliseconds(440);
                var ease = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
                var targetX = seatCenter.X + (pass == 0 ? -26 : 26) - 26;
                var targetY = seatCenter.Y - 15 - 36;
                var horizontal = new DoubleAnimation(targetX, duration)
                    { BeginTime = delay, EasingFunction = ease };
                var vertical = new DoubleAnimation(targetY, duration)
                    { BeginTime = delay, EasingFunction = ease };
                var cardIndex = pass;
                horizontal.Completed += (_, _) =>
                {
                    if (generation != _dealGeneration) return;
                    ChipFlightCanvas.Children.Remove(card);
                    _dealFlightVisuals.Remove(card);
                    _dealtCardCounts[player.Id] = cardIndex + 1;
                    if (_seatCardVisuals.TryGetValue((player.Id, cardIndex), out var destination))
                        TableVisuals.Reveal(destination);
                    if (--remaining == 0) StopCardDeal();
                };
                card.BeginAnimation(Canvas.LeftProperty, horizontal);
                card.BeginAnimation(Canvas.TopProperty, vertical);
                scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(0.72, 1, duration) { BeginTime = delay });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                    new DoubleAnimation(0.72, 1, duration) { BeginTime = delay });
                turn.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(-12, 0, duration) { BeginTime = delay });
            }
        }
        if (remaining == 0) StopCardDeal();
    }

    /// <summary>Annule les vols en cours lors d'un changement de braquage ou d'écran.</summary>
    private void StopCardDeal()
    {
        _dealGeneration++;
        _dealingHeistNumber = 0;
        _dealtCardCounts.Clear();
        _seatCardVisuals.Clear();
        foreach (var visual in _dealFlightVisuals)
            ChipFlightCanvas.Children.Remove(visual);
        _dealFlightVisuals.Clear();
    }
}
