using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace View;

/// <summary>Nom de joueur en lettres vectorielles ivoire, serties d'or, avec troncature et accessibilité.</summary>
public sealed class GildedPlayerName : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(GildedPlayerName),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register(
        nameof(FontSize), typeof(double), typeof(GildedPlayerName),
        new FrameworkPropertyMetadata(16d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender),
        value => value is double size && double.IsFinite(size) && size > 0);

    private static readonly Typeface Lettering = new(new FontFamily("Georgia"), FontStyles.Normal,
        FontWeights.Bold, FontStretches.Normal);
    private static readonly Brush Ivory = FrozenGradient("#FFF9E4", "#F6DE9F", "#E3BD76");
    private static readonly Brush GoldEdge = FrozenGradient("#99611D", "#FFE3A0", "#AC7126");

    public string Text { get => (string?)GetValue(TextProperty) ?? string.Empty; set => SetValue(TextProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }

    public GildedPlayerName()
    {
        Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 1, Opacity = 0.55 };
    }

    private FormattedText Format(double width)
    {
        var text = new FormattedText(Text, CultureInfo.CurrentUICulture, FlowDirection,
            Lettering, FontSize, Ivory, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis
        };
        if (double.IsFinite(width)) text.MaxTextWidth = Math.Max(1, width - 4);
        return text;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var text = Format(availableSize.Width);
        return new Size(Math.Min(text.WidthIncludingTrailingWhitespace + 4, availableSize.Width),
            Math.Ceiling(Math.Max(text.Height + 4, FontSize * 1.45)));
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth <= 4 || ActualHeight <= 0 || Text.Length == 0) return;
        var text = Format(ActualWidth);
        var outline = text.BuildGeometry(new Point(2, Math.Max(2, (ActualHeight - text.Height) / 2)));
        // Le filet reste fin pour les petits noms du tableau ; le clipping contient aussi les accents.
        drawingContext.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        drawingContext.DrawGeometry(Ivory, new Pen(GoldEdge, Math.Clamp(FontSize * 0.035, 0.35, 0.8)), outline);
        drawingContext.Pop();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new NamePeer(this);

    private sealed class NamePeer(GildedPlayerName owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(GildedPlayerName);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
        protected override string GetNameCore() => ((GildedPlayerName)Owner).Text;
    }

    private static Brush FrozenGradient(string dark, string light, string shade)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
            GradientStops =
            [
                new GradientStop((Color)ColorConverter.ConvertFromString(dark), 0),
                new GradientStop((Color)ColorConverter.ConvertFromString(light), 0.45),
                new GradientStop((Color)ColorConverter.ConvertFromString(shade), 1)
            ]
        };
        brush.Freeze();
        return brush;
    }
}

/// <summary>Sépare le pseudo de son rôle pour conserver un nom lisible et un badge stable dans le lobby.</summary>
internal sealed record LobbyPlayerDisplay(string Name, string Role);
