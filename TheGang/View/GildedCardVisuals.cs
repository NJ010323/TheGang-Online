using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Network;

namespace View;

/// <summary>Même paquet doré pour le menu, les mains et les cartes communes du plateau.</summary>
internal static class GildedCardVisuals
{
    internal static FrameworkElement Create(CardSnapshot? card, double width, double height)
    {
        FrameworkElement artwork;
        if (card is not null && !Enum.IsDefined(card.Suit))
            artwork = SpecialCardVisuals.Card("C07",193,298);
        else if (card is null)
            artwork = new Image
            {
                Source = new BitmapImage(new Uri("pack://application:,,,/TheGang;component/Assets/card-back.png")),
                Stretch = Stretch.Fill
            };
        else
        {
            var face = (Border)PlayingCardVisuals.Face(card, 193, 298, ornateIndices: true, ornateSymbols: true);
            face.Margin = new Thickness(0);
            face.BorderBrush = MenuGold();
            face.BorderThickness = new Thickness(1.3);
            face.CornerRadius = new CornerRadius(7);
            face.Effect = null;
            if (face.Child is Viewbox { Child: Canvas art })
                art.Background = new LinearGradientBrush(Color.FromRgb(255, 252, 242), Color.FromRgb(239, 225, 195), 120);
            artwork = face;
        }
        return new Viewbox
        {
            Width = width, Height = height, Margin = new Thickness(3),
            Stretch = Stretch.Fill, Child = GildMenuCard(artwork, card is null)
        };
    }
    /// <summary>Ajoute une tranche métallique, un filet lumineux et des gravures aux quatre coins.</summary>
    internal static FrameworkElement GildMenuCard(FrameworkElement artwork, bool back)
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


}
