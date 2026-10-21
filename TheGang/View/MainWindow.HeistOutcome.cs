using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Model.Game;
using Network;

namespace View;

/// <summary>Présente à toute la table l'issue d'un braquage reçue du serveur.</summary>
public partial class MainWindow
{
    // Les résultats intermédiaires laissent ensuite place au prochain braquage.
    private readonly DispatcherTimer _heistOutcomeTimer = new() { Interval = TimeSpan.FromSeconds(8) };

    /// <summary>Affiche une carte de résultat ; seul le troisième succès ouvre le coffre.</summary>
    private void ShowHeistOutcome(GameEventSnapshot action, GameSnapshot game)
    {
        if (action.HeistSucceeded is not { } succeeded) return;
        HideHeistOutcome();
        HideRoundTransition();
        CloseRulesCard();
        var vaultOpened = game.Result == GameResult.Won;

        OutcomeEyebrow.Text = $"BRAQUAGE {action.HeistNumber}";
        OutcomeTitle.Text = vaultOpened ? "COFFRE OUVERT !" : succeeded
            ? "CARTE D'OR GAGNÉE !" : "ALARME DÉCLENCHÉE !";
        OutcomeSubtitle.Text = game.Result switch
        {
            GameResult.Won => "Trois braquages réussis : la bande remporte la partie !",
            GameResult.Lost => "Trois alarmes : la mission est terminée.",
            _ when succeeded => $"L'ordre des mains est correct. Encore {3 - game.SuccessfulHeists} " +
                $"carte{(3 - game.SuccessfulHeists > 1 ? "s" : string.Empty)} d'or pour ouvrir le coffre.",
            _ => "L'ordre des mains est incorrect. Une alarme s'ajoute à la partie."
        };
        OutcomeScore.Text = $"OR  {game.SuccessfulHeists}/3    ·    ALARMES  {game.FailedHeists}/3";
        OutcomeTitle.Foreground = succeeded
            ? new SolidColorBrush(Color.FromRgb(248, 209, 115))
            : new SolidColorBrush(Color.FromRgb(255, 137, 122));
        OutcomeCard.BorderBrush = succeeded
            ? new SolidColorBrush(Color.FromRgb(229, 185, 88))
            : new SolidColorBrush(Color.FromRgb(214, 87, 77));
        OutcomeClosedImage.Visibility = OutcomeOpenImage.Visibility = vaultOpened
            ? Visibility.Visible : Visibility.Collapsed;
        OutcomeGoldImage.Visibility = succeeded && !vaultOpened ? Visibility.Visible : Visibility.Collapsed;
        OutcomeAlarmImage.Visibility = succeeded ? Visibility.Collapsed : Visibility.Visible;
        OutcomeClosedImage.Opacity = vaultOpened ? 1 : 0;
        OutcomeOpenImage.Opacity = 0;
        OutcomeGoldImage.Opacity = 0;
        OutcomeAlarmImage.Opacity = 0;
        OutcomeGlow.Opacity = 0;
        OutcomeAlarmGlow.Opacity = 0;
        HeistOutcomeOverlay.Visibility = Visibility.Visible;

        if (TableVisuals.AnimationsEnabled)
            AnimateHeistOutcome(succeeded, vaultOpened);
        else if (vaultOpened)
        {
            OutcomeClosedImage.Opacity = 0;
            OutcomeOpenImage.Opacity = 1;
            OutcomeGlow.Opacity = 0.8;
        }
        else if (succeeded)
        {
            OutcomeGoldImage.Opacity = 1;
            OutcomeGlow.Opacity = 0.7;
        }
        else
        {
            OutcomeAlarmImage.Opacity = 1;
            OutcomeAlarmGlow.Opacity = 0.7;
        }

        // La fin de partie reste à l'écran jusqu'au choix explicite du joueur.
        if (game.Phase != GamePhase.Finished) _heistOutcomeTimer.Start();
    }

    /// <summary>Anime la carte de la manche ou l'ouverture du coffre lors de la victoire globale.</summary>
    private void AnimateHeistOutcome(bool succeeded, bool vaultOpened)
    {
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        OutcomeCard.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)));
        OutcomeScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });
        OutcomeScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });

        if (vaultOpened)
        {
            var openDelay = TimeSpan.FromMilliseconds(650);
            var openDuration = TimeSpan.FromMilliseconds(650);
            OutcomeClosedImage.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(1, 0, openDuration) { BeginTime = openDelay });
            OutcomeOpenImage.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, openDuration) { BeginTime = openDelay });
            OutcomeGlow.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 0.85, TimeSpan.FromMilliseconds(900))
                {
                    BeginTime = openDelay
                });
            return;
        }

        if (succeeded)
        {
            OutcomeGoldImage.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(520)));
            OutcomeGoldScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.82, 1, TimeSpan.FromMilliseconds(560)) { EasingFunction = ease });
            OutcomeGoldScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.82, 1, TimeSpan.FromMilliseconds(560)) { EasingFunction = ease });
            OutcomeGlow.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 0.8, TimeSpan.FromMilliseconds(700)));
            return;
        }

        // La sirène et sa lumière continuent de scintiller jusqu'à la fermeture du panneau.
        OutcomeAlarmImage.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(480))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            });
        OutcomeAlarmGlow.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0.15, 0.95, TimeSpan.FromMilliseconds(480))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            });
    }

    /// <summary>Libère le plateau et remet les sprites dans leur état initial pour le prochain résultat.</summary>
    private void HideHeistOutcome()
    {
        _heistOutcomeTimer.Stop();
        HeistOutcomeOverlay.Visibility = Visibility.Collapsed;
        OutcomeCard.BeginAnimation(UIElement.OpacityProperty, null);
        OutcomeScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        OutcomeScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        OutcomeClosedImage.BeginAnimation(UIElement.OpacityProperty, null);
        OutcomeOpenImage.BeginAnimation(UIElement.OpacityProperty, null);
        OutcomeGlow.BeginAnimation(UIElement.OpacityProperty, null);
        OutcomeGoldImage.BeginAnimation(UIElement.OpacityProperty, null);
        OutcomeAlarmImage.BeginAnimation(UIElement.OpacityProperty, null);
        OutcomeAlarmGlow.BeginAnimation(UIElement.OpacityProperty, null);
        OutcomeGoldScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        OutcomeGoldScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        OutcomeCard.Opacity = OutcomeScale.ScaleX = OutcomeScale.ScaleY = 1;
        OutcomeGoldScale.ScaleX = OutcomeGoldScale.ScaleY = 1;
        FlipPendingScoreCards();
    }

    /// <summary>Permet de reprendre la table immédiatement après l'annonce.</summary>
    private void CloseHeistOutcome_Click(object sender, RoutedEventArgs e) => HideHeistOutcome();
}
