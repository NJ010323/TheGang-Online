using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace View;

/// <summary>Faces des cartes de résultat et dos distincts pour l'or et les alarmes.</summary>
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

    /// <summary>Construit une carte retournable dans la pile correspondante.</summary>
    public static ScoreCardSlot Slot(bool gold) => new(gold ? GoldFace : AlarmFace, gold);
}

/// <summary>Carte de score qui conserve son état face cachée ou révélée entre les actualisations réseau.</summary>
internal sealed class ScoreCardSlot : Grid
{
    private readonly Border _back;
    private readonly Border _face;
    private readonly ScaleTransform _flip = new();
    private bool _revealed;

    public ScoreCardSlot(ImageSource illustration, bool gold)
    {
        Width = 72;
        Height = 58;
        Margin = new Thickness(2, 0, 2, 0);
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = _flip;

        // Le dos reprend les deux familles du jeu physique : porte de coffre ou boîtier d'alarme.
        var seal = new Grid { Background = new SolidColorBrush(Color.FromRgb(36, 40, 40)) };
        seal.Children.Add(new Path
        {
            Data = Geometry.Parse("M 0,12 L 72,12 M 0,28 L 72,28 M 16,0 L 16,58 M 52,0 L 52,58"),
            Stroke = new SolidColorBrush(Color.FromRgb(65, 69, 67)),
            StrokeThickness = 0.7,
            IsHitTestVisible = false
        });
        if (gold)
        {
            seal.Children.Add(new Ellipse
            {
                Width = 38, Height = 38, Fill = new SolidColorBrush(Color.FromRgb(36, 45, 42)),
                Stroke = new SolidColorBrush(Color.FromRgb(218, 211, 175)), StrokeThickness = 2.5
            });
            seal.Children.Add(new Ellipse
            {
                Width = 25, Height = 25, Stroke = new SolidColorBrush(Color.FromRgb(158, 148, 114)),
                StrokeThickness = 1.5
            });
            seal.Children.Add(new Path
            {
                Data = Geometry.Parse("M 25,29 L 47,29 M 36,18 L 36,40 M 28,21 L 44,37 M 44,21 L 28,37"),
                Stroke = new SolidColorBrush(Color.FromRgb(211, 193, 135)), StrokeThickness = 1.2,
                IsHitTestVisible = false
            });
            seal.Children.Add(new Ellipse
            {
                Width = 8, Height = 8, Fill = new SolidColorBrush(Color.FromRgb(208, 178, 104)),
                Stroke = Brushes.Black, StrokeThickness = 1
            });
        }
        else
        {
            seal.Children.Add(new Border
            {
                Width = 24, Height = 27, CornerRadius = new CornerRadius(3, 3, 6, 6),
                Background = new SolidColorBrush(Color.FromRgb(149, 151, 148)),
                BorderBrush = Brushes.WhiteSmoke, BorderThickness = new Thickness(1.2),
                VerticalAlignment = VerticalAlignment.Center
            });
            seal.Children.Add(new Ellipse
            {
                Width = 24, Height = 10, Margin = new Thickness(0, 0, 0, 17),
                Fill = new SolidColorBrush(Color.FromRgb(205, 205, 198)),
                Stroke = Brushes.WhiteSmoke, StrokeThickness = 1
            });
        }
        _back = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(31, 33, 33)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(232, 222, 197)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(2),
            Child = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(104, 106, 100)),
                BorderThickness = new Thickness(0.5),
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
            ClipToBounds = true,
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
