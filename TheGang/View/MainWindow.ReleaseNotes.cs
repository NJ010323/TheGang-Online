using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Updates;

namespace View;

public partial class MainWindow
{
    private IReadOnlyList<ReleaseNote> _releaseNotes = [];

    /// <summary>Les mêmes fichiers Markdown sont embarqués et publiés par le workflow de release.</summary>
    private void InitializeReleaseNotes()
    {
        var assembly = typeof(MainWindow).Assembly;
        var entries = new List<(string Tag, string Body)>();
        foreach (var resource in assembly.GetManifestResourceNames().Where(name => name.StartsWith("ReleaseNotes.") && name.EndsWith(".md")))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            entries.Add((resource["ReleaseNotes.".Length..^3], reader.ReadToEnd()));
        }
        _releaseNotes = ReleaseNotesCatalog.Read(entries);
        ReleaseVersionsList.ItemsSource = _releaseNotes;
        var latest = _releaseNotes.FirstOrDefault();
        LatestReleaseTitle.Text = latest is null ? "Notes de mise à jour" : $"Nouveautés · {latest.Tag}";
        LatestReleasePreview.Text = latest?.Title ?? "L’historique sera disponible avec la prochaine version.";
        OpenReleaseNotesButton.IsEnabled = latest is not null;
        if (latest is not null) ReleaseVersionsList.SelectedIndex = 0;
    }

    private void OpenReleaseNotes_Click(object sender, RoutedEventArgs e)
    {
        if (MenuPanel.Visibility != Visibility.Visible || _releaseNotes.Count == 0) return;
        ReleaseNotesOverlay.Visibility = Visibility.Visible;
        ReleaseVersionsList.Focus();
    }

    private void CloseReleaseNotes_Click(object sender, RoutedEventArgs e)
    {
        ReleaseNotesOverlay.Visibility = Visibility.Collapsed;
        OpenReleaseNotesButton.Focus();
    }

    private void ReleaseVersion_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReleaseVersionsList.SelectedItem is not ReleaseNote note) return;
        SelectedReleaseTitle.Text = note.Tag + (note == _releaseNotes.FirstOrDefault() ? " · Dernière version" : " · Archives");
        ReleaseNotesContent.Children.Clear();
        // Un sous-ensemble lisible du Markdown : aucun HTML, script ou navigateur embarqué.
        foreach (var line in note.Body.Replace("\r", "").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var heading = line.StartsWith('#');
            var bullet = line.StartsWith("- ");
            var text = heading ? line.TrimStart('#', ' ') : bullet ? "•  " + line[2..] : line;
            var block = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = heading ? 20 : 15,
                LineHeight = heading ? 28 : 24,
                FontFamily = new FontFamily(heading ? "Georgia" : "Segoe UI"),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(heading ? "#F0C86A" : "#E7D7C1")),
                FontWeight = heading ? FontWeights.Bold : FontWeights.Normal,
                Margin = new Thickness(bullet ? 6 : 0, heading ? 14 : 0, 0, heading ? 12 : 10)
            };
            var clean = Regex.Replace(text, @"\[([^\]]+)\]\([^)]+\)", "$1");
            var parts = clean.Split("**");
            for (var i = 0; i < parts.Length; i++)
                block.Inlines.Add(new Run(parts[i]) { FontWeight = i % 2 == 1 || heading ? FontWeights.Bold : FontWeights.Normal });
            ReleaseNotesContent.Children.Add(block);
        }
        ReleaseNotesScroll.ScrollToTop();
    }
}
