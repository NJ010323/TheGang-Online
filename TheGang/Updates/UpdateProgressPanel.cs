using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Updates;

// Source partagée entre le jeu et son auxiliaire, sans dépendance du moteur sur WPF.
public sealed class UpdateProgressPanel : Border
{
    private static readonly Brush Gold = Brush("#E5B958");
    private readonly TextBlock title = new();
    private readonly TextBlock amount = new();
    private readonly TextBlock percent = new();
    private readonly TextBlock[] steps;
    private readonly Border fill = new() { Background = Gold, CornerRadius = new CornerRadius(6) };
    private readonly Grid track = new() { Height = 14, ClipToBounds = true };
    private readonly TranslateTransform sweep = new();
    private UpdateProgress current = new(UpdateStage.Search, "Recherche de mise à jour…");

    public UpdateProgressPanel()
    {
        Width = 620;
        MaxWidth = 620;
        Padding = new Thickness(30);
        CornerRadius = new CornerRadius(20);
        BorderThickness = new Thickness(2);
        BorderBrush = Gold;
        Background = Brush("#251A20");
        var body = new StackPanel();
        Child = body;
        body.Children.Add(new TextBlock { Text = "THE GANG  /  MISE À JOUR", Foreground = Gold,
            FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 20) });
        title.Foreground = Brush("#FFF0D8");
        title.FontSize = 23;
        title.FontWeight = FontWeights.SemiBold;
        title.TextWrapping = TextWrapping.Wrap;
        title.MinHeight = 64;
        body.Children.Add(title);
        var stages = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 };
        steps = new[] { "Recherche", "Téléchargement", "Préparation", "Installation", "Redémarrage" }
            .Select((name, i) => new TextBlock { Text = $"{i + 1:00}\n{name}", FontSize = 11,
                LineHeight = 20, Margin = new Thickness(0, 0, 8, 0) }).ToArray();
        foreach (var step in steps) stages.Children.Add(step);
        body.Children.Add(stages);
        track.Margin = new Thickness(0, 22, 0, 12);
        track.Children.Add(new Border { Background = Brush("#120F14"), CornerRadius = new CornerRadius(6) });
        fill.HorizontalAlignment = HorizontalAlignment.Left;
        fill.RenderTransform = sweep;
        track.Children.Add(fill);
        body.Children.Add(track);
        var details = new DockPanel();
        percent.Foreground = Gold;
        percent.FontWeight = FontWeights.Bold;
        percent.FontSize = 18;
        DockPanel.SetDock(percent, Dock.Right);
        details.Children.Add(percent);
        amount.Foreground = Brush("#BFA991");
        amount.FontSize = 14;
        details.Children.Add(amount);
        body.Children.Add(details);
        SizeChanged += (_, _) => DrawProgress();
        Unloaded += (_, _) => sweep.BeginAnimation(TranslateTransform.XProperty, null);
        Report(current);
    }

    public void Report(UpdateProgress value)
    {
        current = value;
        title.Text = value.Text;
        for (var i = 0; i < steps.Length; i++)
        {
            steps[i].Foreground = i <= (int)value.Stage ? Gold : Brush("#8C7C77");
            steps[i].FontWeight = i == (int)value.Stage ? FontWeights.Bold : FontWeights.Normal;
        }
        percent.Text = value.Percent is { } number ? $"{Math.Clamp(number, 0, 100):0} %" : "";
        amount.Text = value.DownloadedBytes is { } received && value.TotalBytes is { } total
            ? $"{Megabytes(received)} / {Megabytes(total)} Mo"
            : value.Stage switch
            {
                UpdateStage.Search => "Consultation des versions disponibles…",
                UpdateStage.Prepare => "Vérification et préparation des fichiers…",
                UpdateStage.Install => "Remplacement des fichiers du jeu…",
                UpdateStage.Restart => "Ouverture du jeu, encore un instant…",
                _ => "Connexion au serveur de téléchargement…"
            };
        System.Windows.Automation.AutomationProperties.SetName(this, value.Text + " " + percent.Text + " " + amount.Text);
        DrawProgress();
    }

    private void DrawProgress()
    {
        var width = track.ActualWidth;
        if (width <= 0) return;
        sweep.BeginAnimation(TranslateTransform.XProperty, null);
        sweep.X = 0;
        if (current.Percent is { } number)
        {
            var target = width * Math.Clamp(number, 0, 100) / 100;
            var start = double.IsNaN(fill.Width) ? 0 : fill.ActualWidth;
            fill.BeginAnimation(WidthProperty, null);
            fill.Width = target;
            if (SystemParameters.ClientAreaAnimation)
                fill.BeginAnimation(WidthProperty, new DoubleAnimation(start, target, TimeSpan.FromMilliseconds(100)));
        }
        else
        {
            fill.BeginAnimation(WidthProperty, null);
            fill.Width = width * .24;
            if (SystemParameters.ClientAreaAnimation)
                sweep.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(-fill.Width, width, TimeSpan.FromSeconds(1.5)) { RepeatBehavior = RepeatBehavior.Forever });
        }
    }

    private static string Megabytes(long bytes) => (bytes / 1_000_000d).ToString("N1", CultureInfo.GetCultureInfo("fr-FR"));
    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
}
