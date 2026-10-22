using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Model.Game.Card.CardEnum;
using Network;

namespace View;

/// <summary>Dessine les faces du paquet en vecteurs pour garder les mêmes cartes sur la table et l'aide.</summary>
internal static class PlayingCardVisuals
{
    private const double ArtWidth = 100;
    private const double ArtHeight = 144;
    private static readonly Brush Paper = Brush("#FFF9ED");
    private static readonly Brush Edge = Brush("#D8BD89");
    private static readonly Brush Red = Brush("#B52732");
    private static readonly Brush Black = Brush("#17212A");
    private static readonly Brush Gold = Brush("#D2A14B");
    private static readonly Brush Skin = Brush("#F1CAA0");

    /// <summary>Construit une carte complète ; seules les données de la carte déterminent la face.</summary>
    public static FrameworkElement Face(CardSnapshot card, double width, double height)
    {
        var (symbol, ink) = Suit(card.Suit);
        var rank = card.Rank switch { 1 => "A", 11 => "V", 12 => "D", 13 => "R", _ => card.Rank.ToString() };
        var art = new Canvas { Width = ArtWidth, Height = ArtHeight, Background = Paper };

        if (card.Rank == 1)
            AddAce(art, symbol, ink);
        else if (card.Rank >= 11)
            AddCourt(art, card.Rank, symbol, ink);
        else
            AddNumberPips(art, card.Rank, symbol, ink);

        AddCorner(art, rank, symbol, ink, turned: false);
        AddCorner(art, rank, symbol, ink, turned: true);

        return new Border
        {
            Width = width,
            Height = height,
            Margin = new Thickness(3),
            Background = Paper,
            BorderBrush = Edge,
            BorderThickness = new Thickness(width < 40 ? 1.2 : 1.8),
            CornerRadius = new CornerRadius(width < 40 ? 4 : 6),
            ClipToBounds = true,
            Effect = width >= 45 ? new DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 4, ShadowDepth = 1, Opacity = 0.22
            } : null,
            Child = new Viewbox { Stretch = Stretch.Fill, Child = art }
        };
    }

    /// <summary>Place les indices dans les coins opposés comme sur un paquet français.</summary>
    private static void AddCorner(Canvas art, string rank, string symbol, Brush ink, bool turned)
    {
        var corner = new Canvas { Width = ArtWidth, Height = ArtHeight, IsHitTestVisible = false };
        if (turned) corner.RenderTransform = new RotateTransform(180, ArtWidth / 2, ArtHeight / 2);
        Place(corner, Label(rank, ink, rank == "10" ? 19 : 24, FontWeights.Bold, 23), 3, 0);
        Place(corner, Label(symbol, ink, 19, FontWeights.Normal, 20), 4, 24);
        art.Children.Add(corner);
    }

    /// <summary>Répartit exactement autant d'enseignes que la valeur, en colonnes symétriques.</summary>
    private static void AddNumberPips(Canvas art, int rank, string symbol, Brush ink)
    {
        foreach (var (x, y) in PipPositions(rank))
        {
            var pip = Label(symbol, ink, rank >= 7 ? 25 : 29, FontWeights.Normal, 30);
            pip.TextAlignment = TextAlignment.Center;
            if (y > ArtHeight / 2)
                pip.RenderTransform = new RotateTransform(180, 15, 15);
            Place(art, pip, x - 15, y - 15);
        }
    }

    private static IReadOnlyList<(double X, double Y)> PipPositions(int rank) => rank switch
    {
        2 => [(50, 39), (50, 105)],
        3 => [(50, 39), (50, 72), (50, 105)],
        4 => [(33, 39), (67, 39), (33, 105), (67, 105)],
        5 => [(33, 39), (67, 39), (50, 72), (33, 105), (67, 105)],
        6 => [(33, 35), (67, 35), (33, 72), (67, 72), (33, 109), (67, 109)],
        7 => [(33, 35), (67, 35), (50, 54), (33, 72), (67, 72), (33, 109), (67, 109)],
        8 => [(33, 35), (67, 35), (50, 54), (33, 72), (67, 72), (50, 90), (33, 109), (67, 109)],
        9 => [(33, 31), (67, 31), (33, 58), (67, 58), (50, 72),
              (33, 86), (67, 86), (33, 113), (67, 113)],
        10 => [(33, 31), (67, 31), (50, 47), (33, 58), (67, 58),
               (33, 86), (67, 86), (50, 97), (33, 113), (67, 113)],
        _ => []
    };

    /// <summary>L'as garde une grande enseigne et un encadrement discret au centre.</summary>
    private static void AddAce(Canvas art, string symbol, Brush ink)
    {
        Place(art, new Ellipse
        {
            Width = 68, Height = 78, Stroke = Gold, StrokeThickness = 1.5,
            Fill = Brushes.Transparent
        }, 16, 33);
        Place(art, Label(symbol, ink, 72, FontWeights.Normal, 82), 9, 33);
        Place(art, new Rectangle { Width = 22, Height = 1.5, Fill = Gold }, 39, 28);
        Place(art, new Rectangle { Width = 22, Height = 1.5, Fill = Gold }, 39, 114);
    }

    /// <summary>Les figures ont un cadre, une silhouette et deux portraits inversés.</summary>
    private static void AddCourt(Canvas art, int rank, string symbol, Brush ink)
    {
        var uniform = rank switch
        {
            11 => Brush("#1E4962"),
            12 => Brush("#8E3342"),
            _ => Brush("#34345A")
        };
        Place(art, new Rectangle
        {
            Width = 59, Height = 94, Fill = Brush("#F8E9C8"),
            Stroke = Gold, StrokeThickness = 2
        }, 20.5, 25);
        Place(art, new Rectangle
        {
            Width = 53, Height = 88, Fill = Brushes.Transparent,
            Stroke = ink, StrokeThickness = 0.8
        }, 23.5, 28);

        AddCourtHalf(art, rank, symbol, ink, uniform, turned: false);
        AddCourtHalf(art, rank, symbol, ink, uniform, turned: true);
        Place(art, new Rectangle { Width = 53, Height = 2, Fill = Gold }, 23.5, 71);
    }

    private static void AddCourtHalf(Canvas art, int rank, string symbol, Brush ink,
        Brush uniform, bool turned)
    {
        var half = new Canvas { Width = ArtWidth, Height = ArtHeight, IsHitTestVisible = false };
        if (turned) half.RenderTransform = new RotateTransform(180, ArtWidth / 2, ArtHeight / 2);

        // Épaules, revers contrastés et col rappellent les portraits des jeux de cartes.
        Place(half, Polygon("25,70 31,51 42,48 58,48 69,51 75,70", uniform, ink, 1), 0, 0);
        Place(half, Polygon("30,54 40,52 50,67 45,70 34,63", Gold, ink, 0.6), 0, 0);
        Place(half, Polygon("70,54 60,52 50,67 55,70 66,63", Gold, ink, 0.6), 0, 0);
        Place(half, Polygon("39,47 50,58 61,47 56,47 50,52 44,47", Paper, ink, 0.6), 0, 0);
        Place(half, new Ellipse { Width = 27, Height = 29, Fill = Brush("#3C2927") }, 36.5, 31);
        Place(half, new Ellipse
        {
            Width = 21, Height = 25, Fill = Skin, Stroke = Brush("#7A4A3A"), StrokeThickness = 0.7
        }, 39.5, 31);
        Place(half, new Ellipse { Width = 1.8, Height = 1.8, Fill = ink }, 45, 42);
        Place(half, new Ellipse { Width = 1.8, Height = 1.8, Fill = ink }, 53, 42);
        Place(half, new Rectangle { Width = 6, Height = 1, Fill = Brush("#974C43") }, 47, 51);

        if (rank == 11)
        {
            Place(half, Polygon("36,35 39,25 57,25 64,35 53,32 43,32", uniform, Gold, 1.2), 0, 0);
            Place(half, Polygon("58,27 68,19 64,31", Red, Gold, 0.6), 0, 0);
            Place(half, new Rectangle { Width = 2, Height = 15, Fill = Gold }, 68, 51);
        }
        else
        {
            var crown = rank == 12
                ? "37,35 36,24 43,29 50,20 57,29 64,24 63,35"
                : "35,35 34,22 42,29 50,18 58,29 66,22 65,35";
            Place(half, Polygon(crown, Gold, ink, 1), 0, 0);
            Place(half, new Ellipse { Width = 5, Height = 5, Fill = Red }, 47.5, 27);
            if (rank == 13)
            {
                Place(half, Polygon("44,52 50,60 56,52 51,55 49,55", Brush("#3C2927"), ink, 0.5), 0, 0);
                Place(half, new Rectangle { Width = 2.4, Height = 18, Fill = Gold }, 69, 45);
                Place(half, new Ellipse { Width = 8, Height = 8, Fill = Gold }, 66, 41);
            }
            else
            {
                Place(half, new Ellipse { Width = 5, Height = 5, Fill = Gold }, 67, 51);
            }
        }

        Place(half, Label(symbol, ink, 15, FontWeights.Bold, 16), 42, 56);
        art.Children.Add(half);
    }

    private static Polygon Polygon(string points, Brush fill, Brush stroke, double thickness)
    {
        var result = new Polygon { Fill = fill, Stroke = stroke, StrokeThickness = thickness };
        foreach (var pair in points.Split(' '))
        {
            var coordinates = pair.Split(',');
            result.Points.Add(new Point(double.Parse(coordinates[0], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(coordinates[1], System.Globalization.CultureInfo.InvariantCulture)));
        }
        return result;
    }

    private static TextBlock Label(string text, Brush color, double fontSize, FontWeight weight, double width) =>
        new()
        {
            Text = text, Width = width, Foreground = color, FontSize = fontSize,
            FontWeight = weight, FontFamily = new FontFamily("Segoe UI Symbol"),
            LineHeight = fontSize * 1.05, TextAlignment = TextAlignment.Center,
            IsHitTestVisible = false
        };

    private static void Place(Canvas canvas, UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        canvas.Children.Add(element);
    }

    private static (string Symbol, Brush Ink) Suit(CardColors suit) => suit switch
    {
        CardColors.Coeur => ("♥", Red),
        CardColors.Carreau => ("♦", Red),
        CardColors.Pique => ("♠", Black),
        _ => ("♣", Black)
    };

    private static Brush Brush(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
}
