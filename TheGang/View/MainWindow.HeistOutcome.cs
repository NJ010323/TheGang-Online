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

    /// <summary>Affiche le coffre fermé ou son ouverture selon l'événement autoritaire.</summary>
    private void ShowHeistOutcome(GameEventSnapshot action, GameSnapshot game)
    {
        if (action.HeistSucceeded is not { } succeeded) return;
        HideHeistOutcome();
        HideRoundTransition();

        OutcomeEyebrow.Text = $"BRAQUAGE {action.HeistNumber}";
        OutcomeTitle.Text = succeeded ? "COFFRE OUVERT !" : "COFFRE VERROUILLÉ !";
        OutcomeSubtitle.Text = game.Result switch
        {
            GameResult.Won => "Trois braquages réussis : la bande remporte la partie !",
            GameResult.Lost => "Trois braquages ratés : la partie est terminée.",
            _ when succeeded => "Les mains sont dans le bon ordre. L'or est à vous !",
            _ => "L'ordre des mains ne correspond pas. Le coffre reste fermé."
        };
        OutcomeScore.Text = $"{game.SuccessfulHeists} réussis  ·  {game.FailedHeists} ratés";
        OutcomeTitle.Foreground = succeeded
            ? new SolidColorBrush(Color.FromRgb(248, 209, 115))
            : new SolidColorBrush(Color.FromRgb(255, 137, 122));
        OutcomeCard.BorderBrush = succeeded
            ? new SolidColorBrush(Color.FromRgb(229, 185, 88))
            : new SolidColorBrush(Color.FromRgb(214, 87, 77));
        OutcomeClosedImage.Opacity = 1;
        OutcomeOpenImage.Opacity = 0;
        OutcomeGlow.Opacity = 0;
        HeistOutcomeOverlay.Visibility = Visibility.Visible;

        if (TableVisuals.AnimationsEnabled)
            AnimateHeistOutcome(succeeded);
        else if (succeeded)
        {
            OutcomeClosedImage.Opacity = 0;
            OutcomeOpenImage.Opacity = 1;
            OutcomeGlow.Opacity = 0.8;
        }

        // La fin de partie reste à l'écran jusqu'au choix explicite du joueur.
        if (game.Phase != GamePhase.Finished) _heistOutcomeTimer.Start();
    }

    /// <summary>Fait apparaître le coffre, puis l'ouvre ou le secoue sans changer l'état du jeu.</summary>
    private void AnimateHeistOutcome(bool succeeded)
    {
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        OutcomeCard.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)));
        OutcomeScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });
        OutcomeScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });

        if (succeeded)
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

        var shake = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(450) };
        shake.KeyFrames.Add(new EasingDoubleKeyFrame(-13, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70))));
        shake.KeyFrames.Add(new EasingDoubleKeyFrame(11, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))));
        shake.KeyFrames.Add(new EasingDoubleKeyFrame(-8, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(230))));
        shake.KeyFrames.Add(new EasingDoubleKeyFrame(5, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(310))));
        shake.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(390))));
        OutcomeVaultShake.BeginAnimation(TranslateTransform.XProperty, shake);
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
        OutcomeVaultShake.BeginAnimation(TranslateTransform.XProperty, null);
        OutcomeCard.Opacity = OutcomeScale.ScaleX = OutcomeScale.ScaleY = 1;
        OutcomeVaultShake.X = 0;
    }

    /// <summary>Permet de reprendre la table immédiatement après l'annonce.</summary>
    private void CloseHeistOutcome_Click(object sender, RoutedEventArgs e) => HideHeistOutcome();
}
