using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
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
                    panel.Measure(new Size(620, 500));
                    panel.Arrange(new Rect(0, 0, 620, panel.DesiredSize.Height));
                    panel.UpdateLayout();
                    var description = System.Windows.Automation.AutomationProperties.GetName(panel);
                    if (stage == UpdateStage.Download)
                    {
                        Assert.Contains("42 %", description);
                        Assert.Contains("42,0 / 100,0 Mo", description);
                        var bitmap = new RenderTargetBitmap(620, (int)Math.Ceiling(panel.ActualHeight), 96, 96, PixelFormats.Pbgra32);
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
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
