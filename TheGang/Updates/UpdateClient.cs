using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace Updates;

public sealed record ReleaseAsset(string Name, long Size, string Digest, [property: JsonPropertyName("browser_download_url")] string Url);
public sealed record GithubRelease([property: JsonPropertyName("tag_name")] string Tag, bool Draft, bool Prerelease, ReleaseAsset[] Assets);
public enum UpdateStage { Search, Download, Prepare, Install, Restart }
public sealed record UpdateProgress(UpdateStage Stage, string Text, double? Percent = null,
    long? DownloadedBytes = null, long? TotalBytes = null);
public sealed record PreparedUpdate(string Directory, string Version);

/// <summary>Consulte GitHub sans jeton, prépare un paquet vérifié et ne modifie jamais le dossier installé.</summary>
public sealed class UpdateClient(HttpClient http)
{
    public const string AssetName = "TheGang-win-x64.zip";
    public async Task<PreparedUpdate?> Prepare(string installation, string state, Version current, IProgress<UpdateProgress> progress, CancellationToken token)
    {
        var configuration = UpdateFiles.Read<UpdateConfiguration>(Path.Combine(installation, "update-settings.json"));
        if (!System.Text.RegularExpressions.Regex.IsMatch(configuration.Repository, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")) throw new InvalidDataException("Dépôt de distribution non configuré.");
        progress.Report(new(UpdateStage.Search, "Recherche de mise à jour…"));
        using var check = CancellationTokenSource.CreateLinkedTokenSource(token);
        check.CancelAfter(TimeSpan.FromSeconds(10));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{configuration.Repository}/releases/latest");
        request.Headers.UserAgent.ParseAdd("TheGang-Updater/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, check.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var release = await response.Content.ReadFromJsonAsync<GithubRelease>(UpdateFiles.Json, check.Token) ?? throw new InvalidDataException("Réponse GitHub vide.");
        if (release.Draft || release.Prerelease) return null;
        var version = UpdateFiles.ParseVersion(release.Tag);
        if (version <= current) return null;
        // Une version échouée est ignorée jusqu'à publication d'un nouveau tag.
        if (File.Exists(Path.Combine(state, "failed-version.txt")) && File.ReadAllText(Path.Combine(state, "failed-version.txt")) == release.Tag) return null;
        var asset = release.Assets.Single(a => a.Name == AssetName);
        if (asset.Size <= 0 || asset.Size > 1024L * 1024 * 1024 || asset.Digest is null || !System.Text.RegularExpressions.Regex.IsMatch(asset.Digest, "^sha256:[a-fA-F0-9]{64}$")) throw new InvalidDataException("Empreinte GitHub absente ou taille invalide.");
        var url = new Uri(asset.Url);
        if (url.Scheme != "https" || url.Host != "github.com" || !url.AbsolutePath.StartsWith($"/{configuration.Repository}/releases/download/", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Adresse de téléchargement interdite.");
        Directory.CreateDirectory(state);
        var work = Path.Combine(state, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            using var download = CancellationTokenSource.CreateLinkedTokenSource(token);
            download.CancelAfter(TimeSpan.FromMinutes(5));
            var zip = Path.Combine(work, "download.zip");
            progress.Report(new(UpdateStage.Download, "Téléchargement de la nouvelle version en cours…", 0, 0, asset.Size));
            using (var data = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, download.Token))
            {
                data.EnsureSuccessStatusCode();
                await using var input = await data.Content.ReadAsStreamAsync(download.Token);
                await using var output = File.Create(zip);
                var buffer = new byte[81920]; long count = 0; int read;
                var reportClock = System.Diagnostics.Stopwatch.StartNew();
                while ((read = await input.ReadAsync(buffer, download.Token)) > 0)
                {
                    count += read;
                    if (count > asset.Size) throw new InvalidDataException("Téléchargement trop volumineux.");
                    await output.WriteAsync(buffer.AsMemory(0, read), download.Token);
                    if (count == asset.Size || reportClock.ElapsedMilliseconds >= 100)
                    {
                        progress.Report(new(UpdateStage.Download, "Téléchargement de la nouvelle version en cours…",
                            count * 100d / asset.Size, count, asset.Size));
                        reportClock.Restart();
                    }
                }
                if (count != asset.Size) throw new InvalidDataException("Téléchargement incomplet.");
            }
            progress.Report(new(UpdateStage.Prepare, "Préparation de la nouvelle version…"));
            await using (var stream = File.OpenRead(zip))
                if (!Convert.ToHexString(await SHA256.HashDataAsync(stream, download.Token)).Equals(asset.Digest[7..], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Le téléchargement est corrompu.");
            var staged = Path.Combine(work, "staged");
            await Task.Run(async () =>
            {
                Extract(zip, staged);
                await UpdateFiles.VerifyPackage(staged, version, download.Token);
            }, download.Token);
            return new PreparedUpdate(staged, release.Tag);
        }
        catch { Directory.Delete(work, true); throw; }
    }
    /// <summary>Extraction bornée et plate : aucune entrée ne peut écrire hors du répertoire temporaire.</summary>
    public static void Extract(string archive, string target)
    {
        Directory.CreateDirectory(target);
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count > 1000) throw new InvalidDataException("Trop de fichiers.");
        long expanded = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            UpdateFiles.ValidateName(entry.FullName);
            expanded += entry.Length;
            if (expanded > 2L * 1024 * 1024 * 1024 || !names.Add(entry.FullName) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("Archive interdite.");
            entry.ExtractToFile(Path.Combine(target, entry.FullName));
        }
    }
}

