using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Updates;

// Source partagée par le jeu et son auxiliaire ; toutes les images sont embarquées dans chacun.
public sealed class UpdateProgressPanel : Border
{
    private static readonly Brush Gold = Brush("#E5B958");
    private readonly TextBlock title = new();
    private readonly TextBlock amount = new();
    private readonly TextBlock percent = new();
    private readonly TextBlock stageNumber = new();
    private readonly StageCard[] cards;
    private readonly Border fill = new() { Background = new LinearGradientBrush(
        Color.FromRgb(162, 110, 39), Color.FromRgb(250, 218, 137), 0), CornerRadius = new CornerRadius(8) };
    private readonly Grid track = new() { Height = 20, ClipToBounds = true };
    private readonly TranslateTransform sweep = new();
    private readonly Border restartGlow = new() { BorderBrush = Brush("#FFE4A0"),
        BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(22), Opacity = 0, IsHitTestVisible = false };
    private UpdateProgress current = new(UpdateStage.Search, "Recherche de mise à jour…");
    private UpdateStage? renderedStage;
    private bool Animate => IsLoaded && SystemParameters.ClientAreaAnimation;

    public UpdateProgressPanel()
    {
        Width = 840;
        CornerRadius = new CornerRadius(24);
        BorderThickness = new Thickness(2);
        BorderBrush = Gold;
        Background = new ImageBrush(Asset("table-felt.png")) { Stretch = Stretch.UniformToFill };
        var interior = new Border
        {
            Padding = new Thickness(34, 26, 34, 26), CornerRadius = new CornerRadius(22),
            Background = new LinearGradientBrush(Color.FromArgb(235, 28, 19, 23), Color.FromArgb(245, 12, 13, 15), 90)
        };
        var layers = new Grid();
        layers.Children.Add(interior);
        layers.Children.Add(restartGlow);
        Child = layers;
        var body = new StackPanel();
        interior.Child = body;
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        stageNumber.Foreground = Gold;
        stageNumber.FontSize = 14;
        stageNumber.FontWeight = FontWeights.Bold;
        stageNumber.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(stageNumber, Dock.Right);
        header.Children.Add(stageNumber);
        header.Children.Add(new Image { Source = Asset("logo-reference.png"), Width = 230, Height = 76,
            HorizontalAlignment = HorizontalAlignment.Left, Stretch = Stretch.Uniform });
        body.Children.Add(header);
        body.Children.Add(new TextBlock { Text = "PRÉPARATION DU PROCHAIN BRAQUAGE", Foreground = Gold,
            FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
        title.Foreground = Brush("#FFF0D8");
        title.FontFamily = new FontFamily("Segoe UI");
        title.FontSize = 28;
        title.FontWeight = FontWeights.SemiBold;
        title.TextWrapping = TextWrapping.Wrap;
        title.MinHeight = 72;
        body.Children.Add(title);
        var stages = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Margin = new Thickness(0, 14, 0, 0) };
        var names = new[] { "Recherche", "Téléchargement", "Préparation", "Installation", "Redémarrage" };
        var suits = new[] { "♠", "♦", "♣", "♥", "♠" };
        cards = names.Select((name, i) => new StageCard(name, suits[i], i)).ToArray();
        foreach (var card in cards) stages.Children.Add(card.Root);
        body.Children.Add(stages);
        track.Margin = new Thickness(0, 23, 0, 14);
        track.Children.Add(new Border { Background = Brush("#0C090B"), BorderBrush = Brush("#71512D"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) });
        fill.HorizontalAlignment = HorizontalAlignment.Left;
        fill.RenderTransform = sweep;
        track.Children.Add(fill);
        body.Children.Add(track);
        var details = new DockPanel();
        percent.Foreground = Brush("#F8D88D");
        percent.FontWeight = FontWeights.Bold;
        percent.FontSize = 30;
        DockPanel.SetDock(percent, Dock.Right);
        details.Children.Add(percent);
        amount.Foreground = Brush("#D9C6AC");
        amount.FontSize = 18;
        amount.VerticalAlignment = VerticalAlignment.Center;
        amount.TextWrapping = TextWrapping.Wrap;
        details.Children.Add(amount);
        body.Children.Add(details);
        body.Children.Add(new TextBlock { Text = "La bande prépare la prochaine mission.",
            Foreground = Brush("#AC9578"), FontSize = 13, Margin = new Thickness(0, 16, 0, 0) });
        SizeChanged += (_, _) => DrawProgress();
        Loaded += (_, _) =>
        {
            for (var i = 0; i < cards.Length; i++)
            {
                cards[i].SetState(i - (int)current.Stage, false);
                cards[i].Deal(Animate);
            }
            DrawProgress();
            if (current.Stage == UpdateStage.Restart) GlowForRestart();
        };
        Unloaded += (_, _) =>
        {
            sweep.BeginAnimation(TranslateTransform.XProperty, null);
            fill.BeginAnimation(WidthProperty, null);
            foreach (var card in cards) card.Stop();
            restartGlow.BeginAnimation(OpacityProperty, null);
        };
        Report(current);
    }

    public void Report(UpdateProgress value)
    {
        current = value;
        title.Text = value.Text;
        stageNumber.Text = $"MISE À JOUR · {(int)value.Stage + 1} / 5";
        if (renderedStage != value.Stage)
        {
            for (var i = 0; i < cards.Length; i++)
                cards[i].SetState(i - (int)value.Stage, Animate);
            renderedStage = value.Stage;
            if (value.Stage == UpdateStage.Restart) GlowForRestart();
        }
        percent.Text = value.Percent is { } number ? $"{Math.Clamp(number, 0, 100):0} %" : "";
        amount.Text = value.DownloadedBytes is { } received && value.TotalBytes is { } total
            ? $"{Megabytes(received)} / {Megabytes(total)} Mo téléchargés"
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

    private void GlowForRestart()
    {
        if (Animate) restartGlow.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, .85, TimeSpan.FromMilliseconds(650)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(1) });
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
            if (Animate) fill.BeginAnimation(WidthProperty,
                new DoubleAnimation(start, target, TimeSpan.FromMilliseconds(100)));
        }
        else
        {
            fill.BeginAnimation(WidthProperty, null);
            fill.Width = width * .24;
            if (Animate) sweep.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(-fill.Width, width, TimeSpan.FromSeconds(1.7)) { RepeatBehavior = RepeatBehavior.Forever });
        }
    }

    private sealed class StageCard
    {
        public StackPanel Root { get; } = new() { Margin = new Thickness(5, 0, 5, 0) };
        private readonly Border face;
        private readonly Image back;
        private readonly Border halo;
        private readonly TextBlock state = new() { FontSize = 10, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center };
        private readonly TextBlock name;
        private readonly ScaleTransform flip = new(1, 1);
        private readonly TranslateTransform deal = new();
        private readonly int index;
        private int? previousState;

        public StageCard(string label, string suit, int index)
        {
            this.index = index;
            var holder = new Grid { Width = 112, Height = 150, Margin = new Thickness(0, 8, 0, 12),
                RenderTransformOrigin = new Point(.5, .5) };
            var transforms = new TransformGroup();
            transforms.Children.Add(new RotateTransform((index - 2) * 3));
            transforms.Children.Add(deal);
            holder.RenderTransform = transforms;
            Root.Children.Add(holder);
            var surfaces = new Grid { RenderTransform = flip, RenderTransformOrigin = new Point(.5, .5) };
            holder.Children.Add(surfaces);
            face = new Border { CornerRadius = new CornerRadius(10), BorderBrush = Gold, BorderThickness = new Thickness(2),
                Background = new LinearGradientBrush(Color.FromRgb(67, 39, 32), Color.FromRgb(22, 18, 22), 90), Padding = new Thickness(9) };
            var design = new Grid();
            design.Children.Add(new TextBlock { Text = suit, Foreground = Brush("#F8D88D"), FontSize = 52,
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
            face.Child = design;
            surfaces.Children.Add(face);
            back = new Image { Source = Asset("card-back.png"), Stretch = Stretch.Fill };
            surfaces.Children.Add(back);
            halo = new Border { BorderBrush = Brush("#FFE5A5"), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(10),
                IsHitTestVisible = false, Opacity = 0 };
            holder.Children.Add(halo);
            name = new TextBlock { Text = label, Foreground = Gold, FontSize = 14, FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 1, 0, 7) };
            Root.Children.Add(name);
            Root.Children.Add(state);
        }

        public void SetState(int relative, bool animate)
        {
            Stop();
            state.Text = relative < 0 ? "✓ TERMINÉ" : relative == 0 ? "EN COURS" : "À VENIR";
            state.Foreground = name.Foreground = relative <= 0 ? Gold : Brush("#AE9A80");
            back.Opacity = relative > 0 ? .68 : 0;
            face.Opacity = relative > 0 ? 0 : 1;
            halo.Opacity = relative == 0 ? .7 : 0;
            if (animate && relative == 0)
            {
                if (previousState is > 0)
                {
                    var turn = new DoubleAnimationUsingKeyFrames();
                    turn.KeyFrames.Add(new LinearDoubleKeyFrame(1, TimeSpan.Zero));
                    turn.KeyFrames.Add(new EasingDoubleKeyFrame(.02, TimeSpan.FromMilliseconds(190)));
                    turn.KeyFrames.Add(new EasingDoubleKeyFrame(1, TimeSpan.FromMilliseconds(420)));
                    flip.BeginAnimation(ScaleTransform.ScaleXProperty, turn);
                    var reveal = new DoubleAnimationUsingKeyFrames();
                    reveal.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, TimeSpan.Zero));
                    reveal.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, TimeSpan.FromMilliseconds(190)));
                    back.BeginAnimation(OpacityProperty, reveal);
                }
                halo.BeginAnimation(OpacityProperty, new DoubleAnimation(.25, .95, TimeSpan.FromSeconds(1.1))
                    { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            }
            previousState = relative;
            System.Windows.Automation.AutomationProperties.SetName(Root, name.Text + " : " + state.Text);
        }

        public void Deal(bool animate)
        {
            if (!animate) return;
            deal.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(35, 0, TimeSpan.FromMilliseconds(550))
                { BeginTime = TimeSpan.FromMilliseconds(index * 75), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            var appear = new DoubleAnimationUsingKeyFrames();
            appear.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, TimeSpan.Zero));
            appear.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, TimeSpan.FromMilliseconds(index * 75)));
            appear.KeyFrames.Add(new LinearDoubleKeyFrame(1, TimeSpan.FromMilliseconds(index * 75 + 400)));
            Root.BeginAnimation(OpacityProperty, appear);
            if (previousState == 0) halo.BeginAnimation(OpacityProperty,
                new DoubleAnimation(.25, .95, TimeSpan.FromSeconds(1.1)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        }

        public void Stop()
        {
            halo.BeginAnimation(OpacityProperty, null);
            flip.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            back.BeginAnimation(OpacityProperty, null);
            deal.BeginAnimation(TranslateTransform.YProperty, null);
            Root.BeginAnimation(OpacityProperty, null);
        }
    }

    private static BitmapImage Asset(string filename)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri($"pack://application:,,,/{typeof(UpdateProgressPanel).Assembly.GetName().Name};component/Assets/{filename}");
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
    private static string Megabytes(long bytes) => (bytes / 1_000_000d).ToString("N1", CultureInfo.GetCultureInfo("fr-FR"));
    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
}
