namespace Updates;

public sealed record ReleaseNote(string Tag, Version Version, string Body)
{
    public string Title => Body.Split('\n').FirstOrDefault(line => line.StartsWith("## "))?[3..].Trim()
        ?? "Notes de mise à jour";
}

/// <summary>Historique stable trié par version, indépendant des dates ou de l'ordre des fichiers.</summary>
public static class ReleaseNotesCatalog
{
    public static IReadOnlyList<ReleaseNote> Read(IEnumerable<(string Tag, string Body)> entries) => entries
        .Where(entry => !string.IsNullOrWhiteSpace(entry.Body))
        .Select(entry => new ReleaseNote(entry.Tag, UpdateFiles.ParseVersion(entry.Tag), entry.Body))
        .OrderByDescending(entry => entry.Version).ToArray();
}
