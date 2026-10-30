using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Globalization;
using Model.Game;
using Model.Game.Tokens.TokenEnum;
using Network;

namespace View;

/// <summary>Fabrique les cartes, jetons et places du plateau à partir des seules données autorisées.</summary>
internal static class TableVisuals
{
    /// <summary>Préférence locale : les mouvements du plateau peuvent être réduits dans les paramètres.</summary>
    public static bool AnimationsEnabled { get; set; } = true;

    private static readonly BitmapSource ChipAtlas = new BitmapImage(
        new Uri("pack://application:,,,/TheGang;component/Assets/chip-atlas.png", UriKind.Absolute));
    private static readonly IReadOnlyDictionary<TokenColors, BitmapSource> ChipSprites = CreateChipSprites();
    private static readonly Dictionary<(int Value, double Size), Geometry> NumberOutlines = new();

    /// <summary>Recadre chaque cercle sur son centre réel, sans les marges inégales de l'atlas.</summary>
    private static IReadOnlyDictionary<TokenColors, BitmapSource> CreateChipSprites()
    {
        // L'atlas intégré mesure 1278 × 1230 px ; ces carrés de 520 px ont le même centre visuel.
        return new Dictionary<TokenColors, BitmapSource>
        {
            [TokenColors.White] = new CroppedBitmap(ChipAtlas, new Int32Rect(75, 41, 520, 520)),
            [TokenColors.Yellow] = new CroppedBitmap(ChipAtlas, new Int32Rect(681, 41, 520, 520)),
            [TokenColors.Orange] = new CroppedBitmap(ChipAtlas, new Int32Rect(75, 654, 520, 520)),
            [TokenColors.Red] = new CroppedBitmap(ChipAtlas, new Int32Rect(681, 654, 520, 520))
        };
    }

    /// <summary>Positions des adversaires autour de la table, sans erreur pendant un changement d'effectif.</summary>
    public static IReadOnlyList<Point> OpponentPositions(int count) => count switch
    {
        // Défense contre un ancien instantané à deux : la politique actuelle renvoie ce cas au lobby.
        1 => [new Point(535, 90)],
        2 => [new Point(320, 95), new Point(750, 95)],
        3 => [new Point(125, 280), new Point(535, 90), new Point(945, 280)],
        4 => [new Point(125, 355), new Point(345, 95), new Point(725, 95), new Point(945, 355)],
        5 => [new Point(110, 370), new Point(275, 95), new Point(535, 85),
              new Point(795, 95), new Point(960, 370)],
        _ => throw new ArgumentOutOfRangeException(nameof(count))
    };

    /// <summary>Crée une carte cachée avec le sprite ou une carte visible avec sa valeur réelle.</summary>
    public static FrameworkElement Card(CardSnapshot? card, double width = 58, double height = 84)
    {
        return GildedCardVisuals.Create(card, width, height);
    }

    /// <summary>Retourne une carte commune de dos vers sa face après un délai, sans reconstruire le plateau.</summary>
    public static FrameworkElement FlipCard(CardSnapshot card, double width, double height, TimeSpan delay)
    {
        if (!AnimationsEnabled) return Card(card, width, height);

        var back = Card(null, width, height);
        var face = Card(card, width, height);
        face.Visibility = Visibility.Collapsed;
        var holder = new Grid
        {
            Width = width + 6,
            Height = height + 6,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform()
        };
        holder.Children.Add(back);
        holder.Children.Add(face);
        var scale = (ScaleTransform)holder.RenderTransform;
        var close = new DoubleAnimation(1, 0.04, TimeSpan.FromMilliseconds(190))
        {
            BeginTime = delay,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        close.Completed += (_, _) =>
        {
            back.Visibility = Visibility.Collapsed;
            face.Visibility = Visibility.Visible;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.04, 1, TimeSpan.FromMilliseconds(230))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, close);
        return holder;
    }

    /// <summary>Pose un médaillon art déco sur le sprite et centre précisément le chiffre.</summary>
    public static FrameworkElement Chip(int value, TokenColors color, double size = 42)
    {
        // Le fond transparent rend toute la surface du jeton cliquable pour le glisser-déposer.
        // Les éléments décoratifs ne participent pas au hit test : le Grid reçoit le clic.
        var chip = new Grid { Width = size, Height = size, Margin = new Thickness(2), Background = Brushes.Transparent };
        var sprite = new Image
        {
            Source = ChipSprites[color],
            Stretch = Stretch.Fill,
            Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.45 },
            IsHitTestVisible = false
        };
        RenderOptions.SetBitmapScalingMode(sprite, BitmapScalingMode.HighQuality);
        chip.Children.Add(sprite);

        chip.Children.Add(new Ellipse
        {
            Width = size * 0.64,
            Height = size * 0.64,
            Fill = new RadialGradientBrush(Color.FromRgb(79, 57, 45), Color.FromRgb(29, 21, 24)),
            Stroke = new SolidColorBrush(Color.FromRgb(240, 204, 125)),
            StrokeThickness = Math.Max(0.8, size * 0.028),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        });
        chip.Children.Add(new Ellipse
        {
            Width = size * 0.53,
            Height = size * 0.53,
            Fill = Brushes.Transparent,
            Stroke = new SolidColorBrush(Color.FromRgb(133, 99, 55)),
            StrokeThickness = Math.Max(0.5, size * 0.012),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        });
        chip.Children.Add(NumberShape(value, size));
        chip.Tag=color;
        return chip;
    }

    // Même silhouette et même couronne ; la face sombre inverse le contraste du médaillon.
    internal static void DarkenChip(FrameworkElement element)
    {
        if(element is not Grid chip || chip.Children.Count<4)return;
        var face=(Ellipse)chip.Children[1];
        var color=chip.Tag is TokenColors token ? token : TokenColors.White;
        var pigment=color switch {TokenColors.Yellow=>"#F3CC2C",TokenColors.Orange=>"#F69B35",TokenColors.Red=>"#EC4545",_=>"#F3EFE5"};
        var ink=new SolidColorBrush((Color)ColorConverter.ConvertFromString(pigment));
        face.Fill=new SolidColorBrush(Color.FromRgb(8,9,13));
        face.Stroke=ink;
        ((Ellipse)chip.Children[2]).Stroke=ink;
        ((Path)chip.Children[3]).Fill=face.Stroke;
        chip.Opacity=1;
    }

    /// <summary>Centre les contours réels du chiffre, y compris ceux du 1 et des valeurs à deux chiffres.</summary>
    private static Path NumberShape(int value, double size)
    {
        if (!NumberOutlines.TryGetValue((value, size), out var outline))
        {
            var fontSize = size * (value >= 10 ? 0.34 : 0.43);
            var number = new FormattedText(value.ToString(CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Georgia"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                fontSize, Brushes.White, 1);
            outline = number.BuildGeometry(new Point());
            var bounds = outline.Bounds;
            outline.Transform = new TranslateTransform(
                size / 2 - (bounds.Left + bounds.Right) / 2,
                size / 2 - (bounds.Top + bounds.Bottom) / 2);
            outline.Freeze();
            NumberOutlines[(value, size)] = outline;
        }
        return new Path
        {
            Data = outline,
            Width = size,
            Height = size,
            Stretch = Stretch.None,
            Fill = new SolidColorBrush(Color.FromRgb(255, 239, 194)),
            IsHitTestVisible = false
        };
    }

    /// <summary>Construit une place avec nom, deux cartes, jeton et état de confirmation.</summary>
    public static Border Seat(PlayerSnapshot player, bool isSelf, bool isHost,
        bool isNextToReveal, GamePhase phase, TokenColors roundColor,
        Action<FrameworkElement, int>? onTokenCreated = null, int shownCardCount = int.MaxValue,
        Action<FrameworkElement, int>? onCardCreated = null, IReadOnlyDictionary<int,CardSnapshot>? knownCards=null)
    {
        var frame = new Border
        {
            Width = 210,
            Height = 142,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(235, 23, 15, 18)),
            BorderBrush = isNextToReveal ? new SolidColorBrush(Color.FromRgb(255, 214, 115))
                : isSelf ? new SolidColorBrush(Color.FromRgb(210, 159, 82))
                : new SolidColorBrush(Color.FromRgb(111, 84, 57)),
            BorderThickness = new Thickness(isNextToReveal ? 3 : 1.5),
            Padding = new Thickness(10, 7, 10, 5)
        };
        var content = new StackPanel();
        var identity = new Grid();
        identity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        identity.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        identity.Children.Add(new GildedPlayerName { Text = player.IsBot ? player.Name + " · BOT" : player.Name, FontSize = 16 });
        if (isSelf || isHost)
        {
            var badge = new Border
            {
                CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 1, 4, 1),
                Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                BorderBrush = new SolidColorBrush(Color.FromRgb(134, 100, 48)), BorderThickness = new Thickness(0.7),
                Child = new TextBlock
                {
                    Text = isSelf ? "VOUS" : "HÔTE", FontSize = 9, FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(228, 195, 130))
                }
            };
            Grid.SetColumn(badge, 1);
            identity.Children.Add(badge);
        }
        content.Children.Add(identity);
        var cardRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        for (var index = 0; index < player.HandCount; index++)
        {
            var width = player.HandCount >= 3 ? 34 : 46;
            var known=knownCards?.GetValueOrDefault(index);
            var card = Card(player.Hand.Count > index ? player.Hand[index] : known, width, width * 1.45);
            if(known is not null)System.Windows.Automation.AutomationProperties.SetName(card,$"Carte montrée · {known.Rank} {known.Suit}");
            card.Opacity = index < shownCardCount ? 1 : 0;
            cardRow.Children.Add(card);
            onCardCreated?.Invoke(card, index);
        }
        if (player.CurrentToken is { } token)
        {
            var tokenVisual = Chip(token, roundColor, 40);
            cardRow.Children.Add(tokenVisual);
            onTokenCreated?.Invoke(tokenVisual, token);
        }
        content.Children.Add(cardRow);
        // La petite plaque reprend les tons du plateau ; les points gardent l'historique des quatre tours.
        var handVisible = !isSelf && player.Hand.Count >= 2;
        var showingHands = phase is GamePhase.Showdown or GamePhase.HeistComplete or GamePhase.Finished;
        var handRevealed = showingHands && (handVisible || phase is GamePhase.HeistComplete or GamePhase.Finished);
        var statusText = isNextToReveal ? "À RÉVÉLER" : showingHands
            ? handRevealed ? "MAIN RÉVÉLÉE" : "EN ATTENTE"
            : player.IsChoiceConfirmed ? "CHOIX CONFIRMÉ" :
                player.CurrentToken is null ? "CHOIX EN COURS" : "À CONFIRMER";
        var statusColor = isNextToReveal ? Color.FromRgb(243, 207, 126) :
            showingHands ? handRevealed ? Color.FromRgb(228, 207, 164) : Color.FromRgb(178, 151, 119) :
            player.IsChoiceConfirmed ? Color.FromRgb(228, 207, 164) :
                player.CurrentToken is null ? Color.FromRgb(178, 151, 119) : Color.FromRgb(225, 176, 103);
        var statusRow = new Grid { Margin = new Thickness(0, 3, 0, 0) };
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var statusContents = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        statusContents.Children.Add(new TextBlock
        {
            Text = "◆", Foreground = new SolidColorBrush(statusColor),
            FontSize = 7, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0)
        });
        statusContents.Children.Add(new TextBlock
        {
            Text = statusText, Foreground = new SolidColorBrush(statusColor),
            FontSize = 10, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        statusRow.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(48, 31, 29)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(139, 104, 67)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 2, 5, 2),
            Child = statusContents
        });
        var progress = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        foreach (var color in new[] { TokenColors.White, TokenColors.Yellow, TokenColors.Orange, TokenColors.Red })
        {
            var signal = player.Signals.FirstOrDefault(choice => choice.Color == color);
            progress.Children.Add(new Border
            {
                Width = 9,
                Height = 9,
                CornerRadius = new CornerRadius(5),
                Margin = new Thickness(1.5, 0, 0, 0),
                BorderBrush = new SolidColorBrush(Color.FromRgb(201, 177, 137)),
                BorderThickness = new Thickness(1),
                Background = signal is null ? Brushes.Transparent : color switch
                {
                    TokenColors.White => Brushes.White,
                    TokenColors.Yellow => Brushes.Gold,
                    TokenColors.Orange => Brushes.DarkOrange,
                    _ => Brushes.IndianRed
                }
            });
        }
        Grid.SetColumn(progress, 1);
        statusRow.Children.Add(progress);
        content.Children.Add(statusRow);
        frame.Child = content;
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
        if (!AnimationsEnabled)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
            return;
        }
        element.Opacity = 0;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1,
            TimeSpan.FromMilliseconds(440)));
    }

    /// <summary>Fait respirer discrètement la place du prochain joueur à révéler.</summary>
    private static void Pulse(UIElement element)
    {
        if (!AnimationsEnabled) return;
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0.72, 1, TimeSpan.FromMilliseconds(850))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        });
    }
}
