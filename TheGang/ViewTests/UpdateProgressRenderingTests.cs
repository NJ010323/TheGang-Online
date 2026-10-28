using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Updates;

namespace ViewTests;

public sealed class UpdateProgressRenderingTests
{
    [Fact]
    public void RendersDownloadAndAllInstallationStages()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var panel = new UpdateProgressPanel();
                foreach (var stage in Enum.GetValues<UpdateStage>())
                {
                    panel.Report(new(stage, stage == UpdateStage.Download
                        ? "Téléchargement de la nouvelle version en cours…" : stage.ToString(),
                        stage == UpdateStage.Download ? 42 : null,
                        stage == UpdateStage.Download ? 42_000_000 : null,
                        stage == UpdateStage.Download ? 100_000_000 : null));
                    panel.Measure(new Size(840, double.PositiveInfinity));
                    panel.Arrange(new Rect(0, 0, 840, panel.DesiredSize.Height));
                    panel.UpdateLayout();
                    var labels = Descendants(panel).Select(element => System.Windows.Automation.AutomationProperties.GetName(element)).ToArray();
                    var names = new[] { "Recherche", "Téléchargement", "Préparation", "Installation", "Redémarrage" };
                    for (var i = 0; i < names.Length; i++)
                        Assert.Contains(names[i] + " : " + (i < (int)stage ? "✓ TERMINÉ" : i == (int)stage ? "EN COURS" : "À VENIR"), labels);
                    Assert.Equal(6, Descendants(panel).OfType<Image>().Count());
                    var texts = Descendants(panel).OfType<TextBlock>().ToArray();
                    Assert.DoesNotContain(texts, text => new[] { "01", "02", "03", "04", "05", "THE GANG" }.Contains(text.Text));
                    var message = Assert.Single(texts.Where(text => text.Text == (stage == UpdateStage.Download
                        ? "Téléchargement de la nouvelle version en cours…" : stage.ToString())));
                    Assert.Equal("Segoe UI", message.FontFamily.Source);
                    Assert.Equal(FontWeights.SemiBold, message.FontWeight);
                    var description = System.Windows.Automation.AutomationProperties.GetName(panel);
                    if (stage == UpdateStage.Download)
                    {
                        Assert.Contains("42 %", description);
                        Assert.Contains("42,0 / 100,0 Mo", description);
                        var bitmap = new RenderTargetBitmap(840, (int)Math.Ceiling(panel.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(panel);
                        var png = new PngBitmapEncoder();
                        png.Frames.Add(BitmapFrame.Create(bitmap));
                        using var file = File.Create(Path.Combine(Path.GetTempPath(), "TheGang-update-preview.png"));
                        png.Save(file);
                    }
                    else
                    {
                        Assert.DoesNotContain("%", description);
                        Assert.DoesNotContain("Mo", description);
                    }
                }
                var viewport = new Viewbox { Child = panel, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
                viewport.Measure(new Size(560, 420));
                viewport.Arrange(new Rect(0, 0, 560, 420));
                viewport.UpdateLayout();
                var bounds = panel.TransformToAncestor(viewport).TransformBounds(new Rect(panel.RenderSize));
                Assert.InRange(bounds.Width, 1, 560.1);
                Assert.InRange(bounds.Height, 1, 420.1);
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
