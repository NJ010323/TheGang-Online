using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Model.Game.Card.CardEnum;
using Network;

namespace View;

/// <summary>Carte d'aide locale : déroulement des tours et exemples illustrés des mains de poker.</summary>
public partial class MainWindow
{
    /// <summary>Crée le contenu une seule fois ; le petit rappel reste près de la main du joueur.</summary>
    private void PopulateRulesCard()
    {
        AddRound("01", "PRÉFLOP · BLANC", "Deux cartes privées ; choisissez votre jeton blanc.",
            Color.FromRgb(230, 230, 220));
        AddRound("02", "FLOP · JAUNE", "Trois cartes communes ; choisissez votre jeton jaune.",
            Color.FromRgb(238, 196, 78));
        AddRound("03", "TURN · ORANGE", "Une quatrième carte commune ; choisissez votre jeton orange.",
            Color.FromRgb(237, 145, 63));
        AddRound("04", "RIVER · ROUGE", "La cinquième carte commune ; choisissez votre jeton rouge.",
            Color.FromRgb(213, 72, 71));

        AddHand("Quinte flush royale", (10, CardColors.Coeur), (11, CardColors.Coeur),
            (12, CardColors.Coeur), (13, CardColors.Coeur), (1, CardColors.Coeur));
        AddHand("Quinte flush", (5, CardColors.Pique), (6, CardColors.Pique),
            (7, CardColors.Pique), (8, CardColors.Pique), (9, CardColors.Pique));
        AddHand("Carré", (6, CardColors.Pique), (6, CardColors.Coeur),
            (6, CardColors.Carreau), (6, CardColors.Trefle), (13, CardColors.Pique));
        AddHand("Full", (2, CardColors.Pique), (2, CardColors.Coeur),
            (2, CardColors.Carreau), (10, CardColors.Trefle), (10, CardColors.Carreau));
        AddHand("Couleur", (1, CardColors.Pique), (13, CardColors.Pique),
            (9, CardColors.Pique), (6, CardColors.Pique), (3, CardColors.Pique));
        AddHand("Suite", (7, CardColors.Pique), (8, CardColors.Carreau),
            (9, CardColors.Trefle), (10, CardColors.Coeur), (11, CardColors.Carreau));
        AddHand("Brelan", (2, CardColors.Coeur), (2, CardColors.Trefle),
            (2, CardColors.Carreau), (1, CardColors.Pique), (6, CardColors.Pique));
        AddHand("Deux paires", (8, CardColors.Pique), (8, CardColors.Carreau),
            (3, CardColors.Coeur), (3, CardColors.Trefle), (12, CardColors.Pique));
        AddHand("Paire", (5, CardColors.Pique), (5, CardColors.Carreau),
            (11, CardColors.Trefle), (10, CardColors.Coeur), (3, CardColors.Pique));
        AddHand("Carte haute", (13, CardColors.Coeur), (11, CardColors.Carreau),
            (8, CardColors.Trefle), (6, CardColors.Coeur), (2, CardColors.Pique));
    }

    /// <summary>Ajoute un tour et la couleur du jeton correspondant dans la carte de règles.</summary>
    private void AddRound(string number, string title, string explanation, Color chipColor)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            Background = new SolidColorBrush(chipColor),
            BorderBrush = new SolidColorBrush(Color.FromRgb(83, 59, 41)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = number,
                Foreground = Brushes.Black,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        var words = new StackPanel();
        words.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = new SolidColorBrush(Color.FromRgb(63, 39, 32)),
            FontWeight = FontWeights.Bold,
            FontSize = 14
        });
        words.Children.Add(new TextBlock
        {
            Text = explanation,
            Foreground = new SolidColorBrush(Color.FromRgb(83, 61, 46)),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        });
        Grid.SetColumn(words, 1);
        row.Children.Add(words);
        RuleRoundsPanel.Children.Add(row);
    }

    /// <summary>Illustre une catégorie de main avec cinq vraies cartes du paquet du jeu.</summary>
    private void AddHand(string name, params (int Rank, CardColors Suit)[] cards)
    {
        var index = PokerHandRowsPanel.Children.Count + 1;
        var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock
        {
            Text = index.ToString("00"),
            Foreground = new SolidColorBrush(Color.FromRgb(142, 72, 54)),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        });
        var illustration = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (rank, suit) in cards)
            illustration.Children.Add(TableVisuals.Card(new CardSnapshot(suit, rank), 32, 43));
        Grid.SetColumn(illustration, 1);
        row.Children.Add(illustration);
        var label = new TextBlock
        {
            Text = name,
            Foreground = new SolidColorBrush(Color.FromRgb(57, 37, 30)),
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(label, 2);
        row.Children.Add(label);
        PokerHandRowsPanel.Children.Add(row);
    }

    /// <summary>Agrandit la carte de règles sans modifier la partie ni exposer les cartes adverses.</summary>
    private void OpenRulesCard_Click(object sender, RoutedEventArgs e) =>
        RulesOverlay.Visibility = Visibility.Visible;

    private void CloseRulesCard_Click(object sender, RoutedEventArgs e) => CloseRulesCard();

    private void CloseRulesCard() => RulesOverlay.Visibility = Visibility.Collapsed;
}
