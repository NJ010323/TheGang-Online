using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace View;

/// <summary>Faces des cartes de résultat et dos communs aux six emplacements du score.</summary>
internal static class ResultCardVisuals
{
    // Les illustrations contiennent une marge peinte ; la découpe garde uniquement la carte.
    public static BitmapSource GoldFace { get; } = Crop("result-gold.png", new Int32Rect(88, 112, 1365, 790));
    public static BitmapSource AlarmFace { get; } = Crop("result-alarm.png", new Int32Rect(32, 68, 1472, 870));

    private static BitmapSource Crop(string name, Int32Rect area)
    {
        var bitmap = new BitmapImage(new Uri($"pack://application:,,,/View;component/Assets/{name}"));
        return new CroppedBitmap(bitmap, area);
    }

    /// <summary>Construit une carte retournable avec son illustration propre à la colonne.</summary>
    public static ScoreCardSlot Slot(bool gold, int position) => new(gold ? GoldFace : AlarmFace,
        gold ? "Carte de braquage réussi" : "Carte d'alarme", position);
}

/// <summary>Carte de score qui conserve son état face cachée ou révélée entre les actualisations réseau.</summary>
internal sealed class ScoreCardSlot : Grid
{
    private readonly Border _back;
    private readonly Border _face;
    private readonly ScaleTransform _flip = new();
    private bool _revealed;

    public ScoreCardSlot(ImageSource illustration, string description, int position)
    {
        Width = 72;
        Height = 42;
        Margin = new Thickness(2, 0, 2, 0);
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = _flip;
        ToolTip = description;

        var seal = new Grid();
        seal.Children.Add(new Ellipse
        {
            Width = 26,
            Height = 26,
            Stroke = new SolidColorBrush(Color.FromRgb(196, 153, 84)),
            StrokeThickness = 1.5
        });
        seal.Children.Add(new TextBlock
        {
            Text = position.ToString(),
            Foreground = new SolidColorBrush(Color.FromRgb(245, 209, 129)),
            FontFamily = new FontFamily("Georgia"),
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        _back = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(41, 28, 31)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(177, 137, 75)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(3),
            Child = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(105, 77, 51)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Child = seal
            }
        };
        var picture = new Image { Source = illustration, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
        _face = new Border
        {
            Child = picture,
            BorderBrush = new SolidColorBrush(Color.FromRgb(245, 207, 133)),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(4),
            Visibility = Visibility.Collapsed
        };
        Children.Add(_back);
        Children.Add(_face);
    }

    /// <summary>Retourne une seule fois la carte lorsque le serveur ajoute un succès ou un échec.</summary>
    public void Reveal(TimeSpan delay)
    {
        if (_revealed) return;
        _revealed = true;
        if (!TableVisuals.AnimationsEnabled)
        {
            _back.Visibility = Visibility.Collapsed;
            _face.Visibility = Visibility.Visible;
            return;
        }

        var close = new DoubleAnimation(1, 0.05, TimeSpan.FromMilliseconds(180)) { BeginTime = delay };
        close.Completed += (_, _) =>
        {
            _back.Visibility = Visibility.Collapsed;
            _face.Visibility = Visibility.Visible;
            _flip.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.05, 1, TimeSpan.FromMilliseconds(240)));
        };
        _flip.BeginAnimation(ScaleTransform.ScaleXProperty, close);
    }
}
