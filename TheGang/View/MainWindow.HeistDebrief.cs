using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Model.Game.Hand;
using Network;

namespace View;

/// <summary>Explique le résultat à partir des mains publiques et des jetons rouges figés.</summary>
public partial class MainWindow
{
    // Le dernier résultat reste consultable jusqu'au début du braquage suivant.
    private GameSnapshot? _availableDebrief;

    /// <summary>Rouvre le résultat public après la fermeture volontaire du panneau.</summary>
    private void ReviewHeist_Click(object sender, RoutedEventArgs e)
    {
        if (_availableDebrief is not { LastHeistSucceeded: { } succeeded } game) return;
        ShowHeistOutcome(new GameEventSnapshot(0, GameEventKind.HeistResolved,
            Guid.Empty, string.Empty, game.Phase, game.HeistNumber,
            HeistSucceeded: succeeded), game);
    }

    /// <summary>Compare l'ordre choisi à la force réelle, en acceptant les mains égales.</summary>
    private void PopulateHeistDebrief(GameSnapshot game)
    {
        OutcomeDebriefRowsPanel.Children.Clear();
        var hands = game.RevealedHands.OrderBy(hand => hand.TokenValue).ToArray();
        if (hands.Length != game.Players.Count)
        {
            OutcomeDebriefSummary.Text = "Le détail des mains n'est pas encore disponible.";
            return;
        }

        var scores = hands.ToDictionary(hand => hand.PlayerId,
            hand => new HandScore(hand.Rank, hand.Tiebreakers,hand.DominatesCategory));
        var byStrength = hands.OrderByDescending(hand => scores[hand.PlayerId])
            .ThenBy(hand => hand.TokenValue).ToArray();
        var ranks = new Dictionary<Guid, int>();
        HandScore? previous = null;
        var place = 0;
        for (var index = 0; index < byStrength.Length; index++)
        {
            var score = scores[byStrength[index].PlayerId];
            if (previous is null || score.CompareTo(previous) != 0)
                place = index + 1;
            ranks[byStrength[index].PlayerId] = place;
            previous = score;
        }

        // Un écart existe quand un jeton plus petit annonce une main strictement plus forte
        // que celle du jeton suivant. Les deux joueurs sont marqués comme une paire, sans blâme.
        var mismatched = new HashSet<Guid>();
        var breaks = new List<(int LowerToken, int HigherToken)>();
        for (var index = 1; index < hands.Length; index++)
        {
            if (scores[hands[index - 1].PlayerId].CompareTo(scores[hands[index].PlayerId]) <= 0)
                continue;
            mismatched.Add(hands[index - 1].PlayerId);
            mismatched.Add(hands[index].PlayerId);
            breaks.Add((hands[index - 1].TokenValue, hands[index].TokenValue));
        }
        OutcomeDebriefSummary.Text = breaks.Count == 0
            ? "Les jetons suivent la force des mains, de la plus faible à la plus forte."
            : $"Ordre rompu entre les jetons {breaks[0].LowerToken} et {breaks[0].HigherToken} : " +
              $"la main du jeton {breaks[0].LowerToken} est plus forte." +
              (breaks.Count > 1 ? $" {breaks.Count - 1} autre(s) écart(s)." : string.Empty);
        if(game.Special?.FailureReason is {} reason) OutcomeDebriefSummary.Text=reason;
        OutcomeDebriefSummary.Foreground = breaks.Count == 0 && game.Special?.FailureReason is null
            ? new SolidColorBrush(Color.FromRgb(231, 205, 151))
            : new SolidColorBrush(Color.FromRgb(255, 165, 146));

        var names = game.Players.ToDictionary(player => player.Id, player => player.Name);
        foreach (var hand in hands)
            OutcomeDebriefRowsPanel.Children.Add(CreateDebriefRow(hand, names[hand.PlayerId],
                ranks[hand.PlayerId], mismatched.Contains(hand.PlayerId)));
    }

    /// <summary>Affiche côte à côte le jeton rouge, le joueur, sa combinaison et son rang réel.</summary>
    private static Border CreateDebriefRow(RevealedHandSnapshot hand, string name, int rank,
        bool involvedInBreak)
    {
        var line = new Grid { Height = 32 };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(91) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        line.Children.Add(new Border
        {
            Width = 23, Height = 23, CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromRgb(131, 30, 41)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(238, 156, 126)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = hand.TokenValue.ToString(), Foreground = Brushes.White,
                FontWeight = FontWeights.Bold, FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        var player = new GildedPlayerName
        {
            Text = name, FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(player, 1);
        line.Children.Add(player);
        var label = HandLabel(hand);
        var combination = new TextBlock
        {
            Text = label, Foreground = new SolidColorBrush(Color.FromRgb(255, 223, 167)),
            FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(combination, 2);
        line.Children.Add(combination);
        var actualRank = new Border
        {
            Width = 27, Height = 24, CornerRadius = new CornerRadius(5),
            BorderBrush = new SolidColorBrush(Color.FromRgb(232, 191, 106)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(94, 63, 34)),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = new TextBlock
            {
                Text = rank.ToString("00"), Foreground = new SolidColorBrush(Color.FromRgb(255, 225, 162)),
                FontSize = 11, FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(actualRank, 3);
        line.Children.Add(actualRank);
        return new Border
        {
            Child = line, CornerRadius = new CornerRadius(6),
            Background = involvedInBreak ? new SolidColorBrush(Color.FromRgb(99, 43, 43)) :
                new SolidColorBrush(Color.FromRgb(64, 43, 37)),
            BorderBrush = involvedInBreak ? new SolidColorBrush(Color.FromRgb(233, 111, 95)) :
                new SolidColorBrush(Color.FromRgb(119, 83, 59)),
            BorderThickness = new Thickness(1), Padding = new Thickness(5, 1, 5, 1),
            Margin = new Thickness(0, 0, 0, 4)
        };
    }
}
