using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Model.Game.Card.CardEnum;
using Model.Game.Tokens.TokenEnum;
using Network;

namespace View;

/// <summary>Fabrique les cartes, jetons et places du plateau à partir des seules données autorisées.</summary>
internal static class TableVisuals
{
    private static readonly ImageSource CardBack = new BitmapImage(
        new Uri("pack://application:,,,/View;component/Assets/card-back.png", UriKind.Absolute));
    private static readonly BitmapSource ChipAtlas = new BitmapImage(
        new Uri("pack://application:,,,/View;component/Assets/chip-atlas.png", UriKind.Absolute));
    private static readonly IReadOnlyDictionary<TokenColors, BitmapSource> ChipSprites = CreateChipSprites();

    /// <summary>Découpe les quatre cases du sprite atlas une seule fois au chargement.</summary>
    private static IReadOnlyDictionary<TokenColors, BitmapSource> CreateChipSprites()
    {
        var width = ChipAtlas.PixelWidth / 2;
        var height = ChipAtlas.PixelHeight / 2;
        return new Dictionary<TokenColors, BitmapSource>
        {
            [TokenColors.White] = new CroppedBitmap(ChipAtlas, new Int32Rect(0, 0, width, height)),
            [TokenColors.Yellow] = new CroppedBitmap(ChipAtlas, new Int32Rect(width, 0, width, height)),
            [TokenColors.Orange] = new CroppedBitmap(ChipAtlas, new Int32Rect(0, height, width, height)),
            [TokenColors.Red] = new CroppedBitmap(ChipAtlas, new Int32Rect(width, height, width, height))
        };
    }

    /// <summary>Positions des adversaires autour de la moitié haute de la table, pour trois à six joueurs.</summary>
    public static IReadOnlyList<Point> OpponentPositions(int count) => count switch
    {
        2 => [new Point(320, 125), new Point(750, 125)],
        3 => [new Point(125, 280), new Point(535, 105), new Point(945, 280)],
        4 => [new Point(125, 355), new Point(345, 115), new Point(725, 115), new Point(945, 355)],
        5 => [new Point(110, 370), new Point(275, 135), new Point(535, 85),
              new Point(795, 135), new Point(960, 370)],
        _ => throw new ArgumentOutOfRangeException(nameof(count))
    };

    /// <summary>Crée une carte cachée avec le sprite ou une carte visible avec sa valeur réelle.</summary>
    public static FrameworkElement Card(CardSnapshot? card, double width = 58, double height = 84)
    {
        if (card is null)
            return new Image
            {
                Source = CardBack,
                Width = width,
                Height = height,
                Stretch = Stretch.Fill,
                Margin = new Thickness(3),
                ToolTip = "Carte cachée"
            };

        var (symbol, isRed) = card.Suit switch
        {
            CardColors.Coeur => ("♥", true),
            CardColors.Carreau => ("♦", true),
            CardColors.Pique => ("♠", false),
            _ => ("♣", false)
        };
        var rank = card.Rank switch { 1 => "A", 11 => "V", 12 => "D", 13 => "R", _ => card.Rank.ToString() };
        var ink = Brushes.Black;
        if (isRed) ink = new SolidColorBrush(Color.FromRgb(183, 39, 40));

        var face = new Grid();
        face.Children.Add(new TextBlock
        {
            Text = symbol,
            FontSize = height * 0.45,
            Foreground = ink,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        face.Children.Add(new TextBlock
        {
            Text = $"{rank}{symbol}",
            FontSize = Math.Max(12, width * 0.22),
            FontWeight = FontWeights.Bold,
            Foreground = ink,
            Margin = new Thickness(5, 3, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        });
        return new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromRgb(255, 248, 228)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(214, 189, 143)),
            BorderThickness = new Thickness(2),
            Margin = new Thickness(3),
            Child = face,
            ToolTip = $"{rank} de {card.Suit}"
        };
    }

    /// <summary>Dessine un jeton lisible même après réduction du plateau.</summary>
    public static FrameworkElement Chip(int value, TokenColors color, double size = 42)
    {
        var chip = new Grid { Width = size, Height = size, Margin = new Thickness(2) };
        chip.Children.Add(new Image
        {
            Source = ChipSprites[color],
            Stretch = Stretch.Fill,
            Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.45 }
        });
        chip.Children.Add(new TextBlock
        {
            Text = value.ToString(),
            Foreground = Brushes.Black,
            FontSize = size * 0.38,
            FontWeight = FontWeights.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        chip.ToolTip = $"Jeton {ColorName(color)} numéro {value}";
        return chip;
    }

    /// <summary>Construit une place avec nom, deux cartes, jeton et état de confirmation.</summary>
    public static Border Seat(PlayerSnapshot player, bool isSelf, bool isHost,
        bool isNextToReveal, TokenColors roundColor)
    {
        var frame = new Border
        {
            Width = 210,
            Height = 137,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(235, 23, 15, 18)),
            BorderBrush = isNextToReveal ? new SolidColorBrush(Color.FromRgb(255, 214, 115))
                : isSelf ? new SolidColorBrush(Color.FromRgb(210, 159, 82))
                : new SolidColorBrush(Color.FromRgb(111, 84, 57)),
            BorderThickness = new Thickness(isNextToReveal ? 3 : 1.5),
            Padding = new Thickness(10, 7, 10, 5)
        };
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = player.Name + (isSelf ? "  •  VOUS" : isHost ? "  •  HÔTE" : string.Empty),
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            FontSize = 16,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var cardRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        for (var index = 0; index < 2; index++)
            cardRow.Children.Add(Card(player.Hand.Count > index ? player.Hand[index] : null, 46, 66));
        if (player.CurrentToken is { } token)
            cardRow.Children.Add(Chip(token, roundColor, 40));
        content.Children.Add(cardRow);
        content.Children.Add(new TextBlock
        {
            Text = isNextToReveal ? "À révéler maintenant" : player.IsChoiceConfirmed ? "✓ Choix confirmé" :
                player.CurrentToken is null ? "Choisit son jeton…" : "En attente de confirmation",
            Foreground = player.IsChoiceConfirmed ? new SolidColorBrush(Color.FromRgb(157, 221, 158)) :
                new SolidColorBrush(Color.FromRgb(220, 188, 135)),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        frame.Child = content;
        frame.ToolTip = player.Signals.Count == 0 ? "Aucun tour confirmé" :
            string.Join("  •  ", player.Signals.Select(signal => $"{ColorName(signal.Color)} : {signal.Value}"));
        if (isNextToReveal) Pulse(frame);
        return frame;
    }

    /// <summary>Convertit la phase réseau en couleur du jeton courant.</summary>
    public static TokenColors RoundColor(Model.Game.GamePhase phase) => phase switch
    {
        Model.Game.GamePhase.PreFlop => TokenColors.White,
        Model.Game.GamePhase.Flop => TokenColors.Yellow,
        Model.Game.GamePhase.Turn => TokenColors.Orange,
        _ => TokenColors.Red
    };

    /// <summary>Nom français stable de la couleur, utilisé dans les infobulles.</summary>
    public static string ColorName(TokenColors color) => color switch
    {
        TokenColors.White => "Blanc",
        TokenColors.Yellow => "Jaune",
        TokenColors.Orange => "Orange",
        _ => "Rouge"
    };

    /// <summary>Anime brièvement l'apparition d'une carte ou d'une place révélée.</summary>
    public static void Reveal(UIElement element)
    {
        element.Opacity = 0;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1,
            TimeSpan.FromMilliseconds(440)));
    }

    /// <summary>Fait respirer discrètement la place du prochain joueur à révéler.</summary>
    private static void Pulse(UIElement element) => element.BeginAnimation(UIElement.OpacityProperty,
        new DoubleAnimation(0.72, 1, TimeSpan.FromMilliseconds(850))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        });
}
