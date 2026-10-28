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
        AddMenuCard(GildMenuCard(new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/TheGang;component/Assets/card-back.png")),
            Stretch = Stretch.Fill
        }, back: true), 65, 165, -32, 0);
        AddMenuCard(GildMenuCard(new Image
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
            var face = PlayingCardVisuals.Face(card, 193, 298, ornateIndices: true, ornateSymbols: true);
            face.Margin = new Thickness(0);
            // La dorure concerne uniquement les cartes du menu ; celles de la table gardent leur rendu.
            if (face is Border border)
            {
                border.BorderBrush = MenuGold();
                border.BorderThickness = new Thickness(1.3);
                border.CornerRadius = new CornerRadius(7);
                border.Effect = null;
                if (border.Child is Viewbox { Child: Canvas artwork })
                    artwork.Background = new LinearGradientBrush(Color.FromRgb(255, 252, 242), Color.FromRgb(239, 225, 195), 120);
            }
            var ornament = GildMenuCard(face, back: false);
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

    /// <summary>Ajoute une tranche métallique, un filet lumineux et des gravures aux quatre coins.</summary>
    private static FrameworkElement GildMenuCard(FrameworkElement artwork, bool back)
    {
        var card = new Grid { Width = 205, Height = 310 };
        card.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(12), Background = MenuGold(),
            BorderBrush = new SolidColorBrush(Color.FromRgb(255, 229, 166)), BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 22, ShadowDepth = 9, Opacity = 0.48 }
        });
        card.Children.Add(new Border
        {
            Margin = new Thickness(5), Padding = new Thickness(1), CornerRadius = new CornerRadius(8),
            Background = back ? new SolidColorBrush(Color.FromRgb(24, 19, 16)) : Brushes.OldLace,
            BorderBrush = new SolidColorBrush(Color.FromRgb(104, 67, 20)), BorderThickness = new Thickness(0.8),
            Child = artwork
        });
        // Ces gravures restent sur le cadre et ne recouvrent pas les indices des cartes.
        card.Children.Add(new Path
        {
            Data = Geometry.Parse("M 3,22 L 3,9 Q 3,3 9,3 L 22,3 M 183,3 L 196,3 Q 202,3 202,9 L 202,22 " +
                                  "M 202,288 L 202,301 Q 202,307 196,307 L 183,307 M 22,307 L 9,307 Q 3,307 3,301 L 3,288"),
            Stroke = new SolidColorBrush(Color.FromRgb(255, 238, 190)), StrokeThickness = 1,
            IsHitTestVisible = false
        });
        return card;
    }

    /// <summary>Un reflet diagonal donne du relief aux tranches dorées, sans texture supplémentaire.</summary>
    private static LinearGradientBrush MenuGold() => new()
    {
        StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
        GradientStops =
        [
            new GradientStop(Color.FromRgb(126, 80, 24), 0),
            new GradientStop(Color.FromRgb(245, 209, 130), 0.18),
            new GradientStop(Color.FromRgb(179, 122, 38), 0.42),
            new GradientStop(Color.FromRgb(255, 236, 180), 0.6),
            new GradientStop(Color.FromRgb(143, 91, 25), 0.82),
            new GradientStop(Color.FromRgb(231, 188, 96), 1)
        ]
    };

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
