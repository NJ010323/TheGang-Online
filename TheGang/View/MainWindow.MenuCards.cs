using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Model.Game.Card.CardEnum;
using Network;

namespace View;

public partial class MainWindow
{
    /// <summary>Compose une main décorative aléatoire, indépendante de la partie et renouvelée au lancement.</summary>
    private void PopulateMenuCards()
    {
        // Le paquet complet est mélangé une seule fois : aucune carte visible n'est tirée deux fois.
        var deck = Enum.GetValues<CardColors>()
            .SelectMany(suit => Enumerable.Range(1, 13).Select(rank => new CardSnapshot(suit, rank)))
            .ToArray();
        Random.Shared.Shuffle(deck);
        MenuCardsCanvas.Children.Clear();

        // Une lumière douce détache l'éventail du tapis, sans ajouter d'animation au menu.
        var glow = new Ellipse
        {
            Width = 600, Height = 500,
            Fill = new RadialGradientBrush
            {
                GradientStops = [new GradientStop(Color.FromArgb(48, 221, 173, 73), 0),
                    new GradientStop(Colors.Transparent, 0.85)]
            }
        };
        MenuCardsCanvas.Children.Add(glow);

        // Deux dos reprennent le motif noir et or d'origine, derrière les faces du paquet.
        AddMenuCard(GildedCardVisuals.GildMenuCard(new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/TheGang;component/Assets/card-back.png")),
            Stretch = Stretch.Fill
        }, back: true), 65, 165, -32, 0);
        AddMenuCard(GildedCardVisuals.GildMenuCard(new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/TheGang;component/Assets/card-back.png")),
            Stretch = Stretch.Fill
        }, back: true), 325, 165, 32, 0);

        (double Left, double Top, double Angle, int Layer)[] positions =
        [
            (70, 115, -22, 1),
            (135, 88, -11, 2),
            (195, 65, 0, 5),
            (255, 88, 11, 4),
            (320, 115, 22, 3)
        ];

        for (var index = 0; index < positions.Length; index++)
        {
            var card = deck[index];
            var ornament = GildedCardVisuals.Create(card, 205, 310);
            ornament.Margin = new Thickness(0);
            var rank = card.Rank switch { 1 => "As", 11 => "Valet", 12 => "Dame", 13 => "Roi", _ => card.Rank.ToString() };
            var suit = card.Suit switch
            {
                CardColors.Pique => "pique", CardColors.Coeur => "cœur",
                CardColors.Carreau => "carreau", _ => "trèfle"
            };
            AutomationProperties.SetName(ornament, $"{rank} de {suit}");
            var (left, top, angle, layer) = positions[index];
            AddMenuCard(ornament, left, top, angle, layer);
        }
    }

    /// <summary>Place les cartes dans l'éventail ; la carte centrale reste toujours au premier plan.</summary>
    private void AddMenuCard(FrameworkElement card, double left, double top, double angle, int layer)
    {
        card.RenderTransformOrigin = new Point(0.5, 0.5);
        card.RenderTransform = new RotateTransform(angle);
        Canvas.SetLeft(card, left);
        Canvas.SetTop(card, top);
        Panel.SetZIndex(card, layer);
        MenuCardsCanvas.Children.Add(card);
    }
}
