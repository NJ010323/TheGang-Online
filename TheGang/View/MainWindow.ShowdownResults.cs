using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Model.Game;
using Model.Game.Hand;
using Model.Game.Tokens.TokenEnum;
using Network;

namespace View;

/// <summary>Présente les mains dévoilées par force décroissante, sans anticiper les révélations.</summary>
public partial class MainWindow
{
    private int _lastShowdownRevealCount = -1;

    /// <summary>Ajoute une ligne uniquement quand le serveur a réellement dévoilé la main.</summary>
    private void RenderShowdownResults(GameSnapshot game)
    {
        var showing = game.Phase is GamePhase.Showdown or GamePhase.HeistComplete or GamePhase.Finished;
        ShowdownResultsPanel.Visibility = HistorySubheading.Visibility = showing
            ? Visibility.Visible : Visibility.Collapsed;
        ActivityPanel.Visibility = showing ? Visibility.Collapsed : Visibility.Visible;
        if (!showing)
        {
            _lastShowdownRevealCount = -1;
            ShowdownRowsPanel.Children.Clear();
            return;
        }

        // Les rafraîchissements réseau ne doivent pas redémarrer l'animation de la dernière ligne.
        if (_lastShowdownRevealCount == game.RevealedHands.Count) return;
        ShowdownProgressText.Text = game.RevealedHands.Count == game.Players.Count
            ? "PLUS FORTE  →  PLUS FAIBLE"
            : $"{game.RevealedHands.Count}/{game.Players.Count} DÉVOILÉES · PROVISOIRE";
        ShowdownRowsPanel.Children.Clear();
        var revealed = game.RevealedHands.ToDictionary(hand => hand.PlayerId);
        var newlyRevealed = game.RevealedHands.Skip(Math.Max(0, _lastShowdownRevealCount))
            .Select(hand => hand.PlayerId).ToHashSet();

        // Le classement ne porte que sur les mains connues. Les joueurs en attente suivent
        // ensuite l'ordre de révélation des jetons rouges, sans dévoiler leur force en avance.
        int RedToken(PlayerSnapshot player) => player.Signals
            .FirstOrDefault(signal => signal.Color == TokenColors.Red)?.Value ?? int.MaxValue;
        var players = game.Players.Where(player => revealed.ContainsKey(player.Id))
            .OrderByDescending(player => new HandScore(revealed[player.Id].Rank,
                revealed[player.Id].Tiebreakers))
            .ThenBy(RedToken)
            .Concat(game.Players.Where(player => !revealed.ContainsKey(player.Id))
                .OrderBy(RedToken));
        HandScore? previousScore = null;
        var position = 0;
        var place = 0;
        foreach (var player in players)
        {
            var token = player.Signals.FirstOrDefault(signal => signal.Color == TokenColors.Red)?.Value;
            revealed.TryGetValue(player.Id, out var result);
            if (result is not null)
            {
                var score = new HandScore(result.Rank, result.Tiebreakers);
                position++;
                if (previousScore is null || score.CompareTo(previousScore) != 0)
                    place = position;
                previousScore = score;
            }
            var row = CreateShowdownRow(player.Name, token, result, result is null ? null : place,
                player.Id == game.NextPlayerToRevealId);
            ShowdownRowsPanel.Children.Add(row);
            if (result is not null && newlyRevealed.Contains(player.Id))
                TableVisuals.Reveal(row);
        }
        _lastShowdownRevealCount = game.RevealedHands.Count;
    }

    /// <summary>Compose un rang doré et un jeton rouge distincts, suivis du joueur et de sa main.</summary>
    private static Border CreateShowdownRow(string name, int? token,
        RevealedHandSnapshot? result, int? place, bool isNext)
    {
        var label = result is null ? isNext ? "À RÉVÉLER" : "EN ATTENTE" : HandLabel(result);
        var color = result is not null ? Color.FromRgb(255, 223, 159) :
            isNext ? Color.FromRgb(241, 198, 108) : Color.FromRgb(174, 147, 133);
        var cells = new Grid { Height = 28 };
        cells.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(27) });
        cells.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        cells.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        cells.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cells.Children.Add(new Border
        {
            Width = 22, Height = 22, CornerRadius = new CornerRadius(5),
            Background = place is null ? new SolidColorBrush(Color.FromRgb(57, 45, 41)) :
                new SolidColorBrush(Color.FromRgb(104, 69, 30)),
            BorderBrush = place is null ? new SolidColorBrush(Color.FromRgb(107, 83, 66)) :
                new SolidColorBrush(Color.FromRgb(241, 196, 104)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = place?.ToString("00") ?? "—", Foreground =
                    place is null ? new SolidColorBrush(Color.FromRgb(171, 149, 128)) :
                        new SolidColorBrush(Color.FromRgb(255, 226, 164)),
                FontSize = 11, FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        var redToken = new Border
        {
            Width = 21, Height = 21, CornerRadius = new CornerRadius(11),
            Background = new SolidColorBrush(Color.FromRgb(123, 28, 38)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(233, 156, 99)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = token?.ToString() ?? "?", Foreground = Brushes.White,
                FontSize = 12, FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(redToken, 1);
        cells.Children.Add(redToken);
        var player = new TextBlock
        {
            Text = name, Foreground = Brushes.White,
            FontSize = 12, FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(player, 2);
        cells.Children.Add(player);
        var hand = new TextBlock
        {
            Text = label, Foreground = new SolidColorBrush(color),
            FontSize = 12, FontWeight = result is null ? FontWeights.Normal : FontWeights.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(hand, 3);
        cells.Children.Add(hand);
        return new Border
        {
            Child = cells, CornerRadius = new CornerRadius(6),
            Background = result is not null ? new SolidColorBrush(Color.FromRgb(72, 43, 35)) :
                isNext ? new SolidColorBrush(Color.FromRgb(68, 41, 32)) : Brushes.Transparent,
            BorderBrush = isNext ? new SolidColorBrush(Color.FromRgb(219, 175, 98)) :
                new SolidColorBrush(Color.FromRgb(105, 66, 56)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(0, 0, 0, 2)
        };
    }

    /// <summary>Traduit la catégorie et sa valeur décisive sans révéler les autres cartes.</summary>
    private static string HandLabel(RevealedHandSnapshot hand)
    {
        var ranks = hand.Tiebreakers;
        var top = ranks.Count > 0 ? CardValue(ranks[0]) : string.Empty;
        var group = ranks.Count > 0 ? CardValue(ranks[0], plural: true) : string.Empty;
        return hand.Rank switch
        {
            HandRank.RoyalFlush => "Quinte flush royale",
            HandRank.StraightFlush => $"Quinte flush · {top}",
            HandRank.FourOfKind => $"Carré de {group}",
            HandRank.FullHouse => $"Full · {group}",
            HandRank.Flush => "Couleur",
            HandRank.Straight => $"Suite · {top}",
            HandRank.ThreeOfKind => $"Brelan de {group}",
            HandRank.TwoPair => $"Deux paires · {group}",
            HandRank.OnePair => $"Paire de {group}",
            _ => $"Carte haute · {top}"
        };
    }

    /// <summary>Utilise les noms courts des figures pour garder chaque ligne sur une rangée.</summary>
    private static string CardValue(int value, bool plural = false) => value switch
    {
        14 or 1 => "As", 13 => plural ? "Rois" : "Roi",
        12 => plural ? "Dames" : "Dame", 11 => plural ? "Valets" : "Valet",
        _ => value.ToString()
    };
}
