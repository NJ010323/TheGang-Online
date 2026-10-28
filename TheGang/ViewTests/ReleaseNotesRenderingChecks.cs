using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Updates;
using View;

namespace ViewTests;

internal static class ReleaseNotesRenderingChecks
{
    internal static void Run()
    {
        var window = new MainWindow();
        void Invoke(string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
        T Find<T>(string name) => (T)window.FindName(name);
        try
        {
            Invoke("SetFullScreen", false);
            var list = Find<ListBox>("ReleaseVersionsList");
            Assert.True(list.Items.Count >= 5);
            var notes = list.Items.Cast<ReleaseNote>().ToArray();
            Assert.Equal(notes.OrderByDescending(note => note.Version), notes);
            Assert.Contains(notes, note => note.Tag == "v0.0.5");
            Assert.Contains(notes[0].Tag, Find<TextBlock>("LatestReleaseTitle").Text);
            Capture(window, "menu");
            Invoke("OpenReleaseNotes_Click", window, new RoutedEventArgs());
            Assert.Equal(Visibility.Visible, Find<Grid>("ReleaseNotesOverlay").Visibility);
            Capture(window, "notes-latest");
            list.SelectedItem = notes.Single(note => note.Tag == "v0.0.2");
            Assert.Contains("v0.0.2", Find<TextBlock>("SelectedReleaseTitle").Text);
            // Les TextBlocks emploient des Run pour le gras : lire la plage de texte complète.
            var text = string.Join("\n", Find<StackPanel>("ReleaseNotesContent").Children.OfType<TextBlock>()
                .Select(t => new System.Windows.Documents.TextRange(t.ContentStart, t.ContentEnd).Text));
            Assert.Contains("Un jeton pris ne peut plus être rendu", text);
            Assert.DoesNotContain("17 étapes", text);
            Capture(window, "notes-archive");
            Invoke("CloseReleaseNotes_Click", window, new RoutedEventArgs());
            Assert.Equal(Visibility.Collapsed, Find<Grid>("ReleaseNotesOverlay").Visibility);
            Assert.Equal(Visibility.Visible, Find<Grid>("MenuPanel").Visibility);
        }
        finally
        {
            typeof(MainWindow).GetField("_applicationExitConfirmed", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
            window.Close();
        }
    }

    private static void Capture(MainWindow window, string name)
    {
        var root = (FrameworkElement)window.Content;
        foreach (var size in new[] { new Size(1440, 900), new Size(960, 680) })
        {
            root.Measure(size);
            root.Arrange(new Rect(size));
            root.UpdateLayout();
            var image = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
            image.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var file = File.Create(Path.Combine(Path.GetTempPath(), $"TheGang-release-{name}-{size.Width}.png"));
            encoder.Save(file);
        }
    }
}
