using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Updates;

/// <summary>Seul le nom public du dépôt est distribué ; aucun identifiant GitHub n'est requis.</summary>
public sealed record UpdateConfiguration(string Repository);
/// <summary>Liste exhaustive des fichiers du paquet, avec leurs empreintes après extraction.</summary>
public sealed record PackageManifest(string Version, Dictionary<string, string> Files);
/// <summary>Permet de distinguer les fichiers à restaurer des fichiers ajoutés à supprimer.</summary>
public sealed record InstallJournal(string[] Files, string[] Existed);

/// <summary>Validation des paquets et transaction réexécutable, indépendante de WPF et du réseau de jeu.</summary>
public static partial class UpdateFiles
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    public static Version ParseVersion(string value)
    {
        var match = StableVersion().Match(value);
        if (!match.Success) throw new InvalidDataException("Version stable invalide.");
        return new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value));
    }
    [GeneratedRegex(@"^v?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$")]
    private static partial Regex StableVersion();
    public static string StateDirectory(string installation) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TheGang", "Updates",
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(installation).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant())))[..24]);
    public static string LockName(string installation) => @"Local\TheGang-" + Path.GetFileName(StateDirectory(installation));
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("Fichier de mise à jour invalide.");
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var stream = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(stream, value, Json); stream.Flush(true); }
        File.Move(path + ".tmp", path, true);
    }
    public static void Log(string state, string message)
    {
        try { Directory.CreateDirectory(state); File.AppendAllText(Path.Combine(state, "update.log"), $"{DateTimeOffset.UtcNow:O} {message}\n"); } catch { }
    }
    public static void ValidateName(string name)
    {
        // Distribution plate uniquement : interdit traversées, ADS NTFS, liens et chemins absolus.
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name != Path.GetFileName(name) || name.Contains(':') || name.Contains('/') || name.Contains('\\') || name.EndsWith('.') || name.EndsWith(' ') || name is "." or "..")
            throw new InvalidDataException("Chemin interdit dans la mise à jour.");
    }
    public static async Task VerifyPackage(string folder, Version expected, CancellationToken token = default)
    {
        var manifest = Read<PackageManifest>(Path.Combine(folder, "package.json"));
        if (ParseVersion(manifest.Version) != expected || !manifest.Files.ContainsKey("TheGang.exe") || !manifest.Files.ContainsKey("TheGang.Updater.exe") || !manifest.Files.ContainsKey("cloudflared.exe") || !manifest.Files.ContainsKey("update-settings.json"))
            throw new InvalidDataException("Le paquet ne correspond pas à la version attendue.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, hash) in manifest.Files)
        {
            ValidateName(name);
            if (!names.Add(name) || name.Equals("package.json", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Fichier dupliqué.");
            var path = Path.Combine(folder, name);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Lien interdit.");
            await using var stream = File.OpenRead(path);
            if (!Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Somme SHA-256 incorrecte.");
        }
        if (Directory.EnumerateFiles(folder).Count() != names.Count + 1 || Directory.EnumerateDirectories(folder).Any()) throw new InvalidDataException("Fichiers inattendus.");
    }
    public static void AtomicCopy(string source, string target)
    {
        File.Copy(source, target + ".update-tmp", true);
        using (var stream = new FileStream(target + ".update-tmp", FileMode.Open, FileAccess.Write)) stream.Flush(true);
        // Un antivirus ou un indexeur peut retenir brièvement l'exécutable après copie.
        // Attente bornée ; au-delà, le journal permet la restauration au prochain essai.
        for (var attempt = 0; ; attempt++)
        {
            try { File.Move(target + ".update-tmp", target, true); break; }
            catch (Exception error) when (attempt < 40 && error is IOException or UnauthorizedAccessException)
            { Thread.Sleep(100); }
        }
    }
    /// <summary>Restaure tous les fichiers sauvegardés ; le journal reste présent si une copie échoue.</summary>
    public static bool Recover(string installation, string state)
    {
        var journalPath = Path.Combine(state, "journal.json");
        if (!File.Exists(journalPath)) return false;
        var journal = Read<InstallJournal>(journalPath);
        foreach (var name in journal.Files)
        {
            ValidateName(name);
            var target = Path.Combine(installation, name);
            if (journal.Existed.Contains(name, StringComparer.OrdinalIgnoreCase)) AtomicCopy(Path.Combine(state, "backup", name), target);
            else File.Delete(target);
        }
        File.Delete(journalPath);
        Log(state, "Installation précédente restaurée.");
        return true;
    }
    /// <summary>Sauvegarde les fichiers possédés par le jeu avant de publier le journal et de remplacer les fichiers.</summary>
    public static void Install(string installation, string state, string staged)
    {
        var manifest = Read<PackageManifest>(Path.Combine(staged, "package.json"));
        var files = manifest.Files.Keys.Append("package.json").ToArray();
        var existed = files.Where(n => File.Exists(Path.Combine(installation, n))).ToArray();
        var oldManifestPath = Path.Combine(installation, "package.json");
        var owned = File.Exists(oldManifestPath) ? Read<PackageManifest>(oldManifestPath).Files.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase) : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "TheGang.exe", "TheGang.Updater.exe", "cloudflared.exe", "cloudflared-LICENSE.txt", "TheGang.pdb", "update-settings.json" };
        Directory.CreateDirectory(Path.Combine(state, "backup"));
        foreach (var name in files)
        {
            ValidateName(name);
            var target = Path.Combine(installation, name);
            if (File.Exists(target))
            {
                if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0 || (name != "package.json" && !owned.Contains(name))) throw new IOException("Un fichier personnel empêche la mise à jour.");
                AtomicCopy(target, Path.Combine(state, "backup", name));
            }
        }
        // Le journal est durable avant la première modification ; les copies sont réexécutables.
        Write(Path.Combine(state, "journal.json"), new InstallJournal(files, existed));
        foreach (var name in files) AtomicCopy(Path.Combine(staged, name), Path.Combine(installation, name));
    }
}

