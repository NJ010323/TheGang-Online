using Updates;

namespace UnitTests;

public class ReleaseNotesTests
{
    [Fact]
    public void HistorySortsVersionsNumericallyAndRetainsFullNotes()
    {
        var notes = ReleaseNotesCatalog.Read(new[] { ("v0.0.9", "## Corrections\n- Avant"),
            ("v0.0.10", "## Nouveautés\n- Après"), ("v0.0.8", " ") });
        Assert.Equal(new[] { "v0.0.10", "v0.0.9" }, notes.Select(n => n.Tag));
        Assert.Equal("Nouveautés", notes[0].Title);
        Assert.Contains("- Après", notes[0].Body);
    }
}
