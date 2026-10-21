using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Model.Game;

namespace View;

/// <summary>Annonce la fin d'un tour à partir de l'événement partagé par le serveur.</summary>
public partial class MainWindow
{
    // L'annonce ne bloque pas les commandes ; elle disparaît après le retournement des cartes.
    private readonly DispatcherTimer _roundTransitionTimer = new();

    /// <summary>Affiche la prochaine phase et la couleur des jetons rendus disponibles.</summary>
    private void ShowRoundTransition(GamePhase phase)
    {
        var (eyebrow, title, detail, accent) = phase switch
        {
            GamePhase.Flop => ("TOUR 1 TERMINÉ", "LE FLOP", "3 cartes révélées  ·  Jetons jaunes à choisir",
                Color.FromRgb(240, 202, 95)),
            GamePhase.Turn => ("TOUR 2 TERMINÉ", "LE TURN", "1 carte révélée  ·  Jetons orange à choisir",
                Color.FromRgb(245, 159, 72)),
            GamePhase.River => ("TOUR 3 TERMINÉ", "LA RIVER", "Dernière carte révélée  ·  Jetons rouges à choisir",
                Color.FromRgb(242, 107, 90)),
            GamePhase.Showdown => ("TOUR 4 TERMINÉ", "PLACE À LA RÉVÉLATION",
                "Les jetons rouges sont fixés  ·  Révélez les mains dans l'ordre",
                Color.FromRgb(242, 107, 90)),
            _ => (string.Empty, string.Empty, string.Empty, Colors.Transparent)
        };
        if (title.Length == 0) return;
        HideRoundTransition();

        RoundTransitionEyebrow.Text = eyebrow;
        RoundTransitionTitle.Text = title;
        RoundTransitionDetail.Text = detail;
        var brush = new SolidColorBrush(accent);
        RoundTransitionCard.BorderBrush = brush;
        RoundTransitionTitle.Foreground = brush;
        RoundTransitionProgress.Background = brush;
        RoundTransitionOverlay.Visibility = Visibility.Visible;

        if (TableVisuals.AnimationsEnabled)
        {
            var fade = new DoubleAnimationUsingKeyFrames();
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2100))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2500))));
            RoundTransitionOverlay.BeginAnimation(UIElement.OpacityProperty, fade);

            var appear = new DoubleAnimation(0.88, 1, TimeSpan.FromMilliseconds(380))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            RoundTransitionScale.BeginAnimation(ScaleTransform.ScaleXProperty, appear);
            RoundTransitionScale.BeginAnimation(ScaleTransform.ScaleYProperty, appear);
            RoundTransitionProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(2500)));
            _roundTransitionTimer.Interval = TimeSpan.FromMilliseconds(2600);
        }
        else _roundTransitionTimer.Interval = TimeSpan.FromMilliseconds(1800);

        _roundTransitionTimer.Start();
    }

    /// <summary>Retire l'annonce et réinitialise les propriétés animées pour le tour suivant.</summary>
    private void HideRoundTransition()
    {
        _roundTransitionTimer.Stop();
        RoundTransitionOverlay.Visibility = Visibility.Collapsed;
        RoundTransitionOverlay.BeginAnimation(UIElement.OpacityProperty, null);
        RoundTransitionScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        RoundTransitionScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        RoundTransitionProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        RoundTransitionOverlay.Opacity = 1;
        RoundTransitionScale.ScaleX = RoundTransitionScale.ScaleY = 1;
        RoundTransitionProgressScale.ScaleX = 1;
    }
}
