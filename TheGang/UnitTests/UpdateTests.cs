using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Updates;

namespace UnitTests;

/// <summary>Utilise un faux GitHub et de vrais fichiers temporaires : aucun dépôt ni profil joueur n'est modifié.</summary>
public sealed class UpdateTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "TheGang-update-tests-" + Guid.NewGuid().ToString("N"));
    private string Folder(string name) { var path = Path.Combine(root, name); Directory.CreateDirectory(path); return path; }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private string Package(string version = "1.1.0")
    {
        var path = Folder(Guid.NewGuid().ToString("N"));
        var files = new Dictionary<string, string>();
        foreach (var name in new[] { "TheGang.exe", "TheGang.Updater.exe", "cloudflared.exe", "update-settings.json" })
        {
            File.WriteAllText(Path.Combine(path, name), version);
            files[name] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(path, name))));
        }
        UpdateFiles.Write(Path.Combine(path, "package.json"), new PackageManifest(version, files));
        return path;
    }
    [Theory]
    [InlineData("v1.10.0", "1.9.0", true)]
    [InlineData("v1.0.0", "1.0.0", false)]
    [InlineData("1.0.0", "2.0.0", false)]
    public void ComparesNumericVersions(string candidate, string current, bool newer) => Assert.Equal(newer, UpdateFiles.ParseVersion(candidate) > UpdateFiles.ParseVersion(current));
    [Theory]
    [InlineData("1.0.0-beta.1")]
    [InlineData("v01.0.0")]
    [InlineData("1.0")]
    public void RejectsNonStableVersions(string version) => Assert.Throws<InvalidDataException>(() => UpdateFiles.ParseVersion(version));
    [Theory]
    [InlineData("../outside.exe")]
    [InlineData("C:\\outside.exe")]
    [InlineData("game.exe:secret")]
    [InlineData("sub/file.exe")]
    public void RejectsArchiveTraversal(string name)
    {
        var zipPath = Path.Combine(Folder("zip"), "bad.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create)) zip.CreateEntry(name);
        Assert.Throws<InvalidDataException>(() => UpdateClient.Extract(zipPath, Folder("extracted")));
    }
    [Fact]
    public async Task DetectsTamperedFile()
    {
        var package = Package();
        File.AppendAllText(Path.Combine(package, "TheGang.exe"), "tampered");
        await Assert.ThrowsAsync<InvalidDataException>(() => UpdateFiles.VerifyPackage(package, new Version(1, 1, 0)));
    }
    [Fact]
    public void RollbackAfterInterruptionPreservesPersonalDataAndRemovesNewFiles()
    {
        var installation = Folder("game"); var state = Folder("state"); var staged = Package();
        File.WriteAllText(Path.Combine(installation, "TheGang.exe"), "old");
        File.WriteAllText(Path.Combine(installation, "save.dat"), "personal");
        UpdateFiles.Install(installation, state, staged);
        Assert.Equal("1.1.0", File.ReadAllText(Path.Combine(installation, "TheGang.exe")));
        Assert.True(UpdateFiles.Recover(installation, state));
        Assert.Equal("old", File.ReadAllText(Path.Combine(installation, "TheGang.exe")));
        Assert.Equal("personal", File.ReadAllText(Path.Combine(installation, "save.dat")));
        Assert.False(File.Exists(Path.Combine(installation, "cloudflared.exe")));
        Assert.False(UpdateFiles.Recover(installation, state));
    }
    [Fact]
    public void FailedPartialInstallCanBeRecovered()
    {
        var installation = Folder("game"); var state = Folder("state"); var staged = Package();
        File.WriteAllText(Path.Combine(installation, "TheGang.exe"), "old");
        File.Delete(Path.Combine(staged, "cloudflared.exe")); // panne après les deux premières copies
        Assert.Throws<FileNotFoundException>(() => UpdateFiles.Install(installation, state, staged));
        Assert.True(File.Exists(Path.Combine(state, "journal.json")));
        UpdateFiles.Recover(installation, state);
        Assert.Equal("old", File.ReadAllText(Path.Combine(installation, "TheGang.exe")));
    }
    [Fact]
    public void LockedFileLeavesRecoverableJournal()
    {
        var installation = Folder("game"); var state = Folder("state"); var staged = Package();
        File.WriteAllText(Path.Combine(installation, "TheGang.exe"), "old");
        UpdateFiles.Install(installation, state, staged);
        using (var locked = File.Open(Path.Combine(installation, "TheGang.exe"), FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.True(Record.Exception(() => UpdateFiles.Recover(installation, state)) is IOException or UnauthorizedAccessException);
        Assert.True(File.Exists(Path.Combine(state, "journal.json")));
        Assert.True(UpdateFiles.Recover(installation, state));
    }
    [Fact]
    public void RefusesOverwritingUnownedUserFile()
    {
        var installation = Folder("game"); var state = Folder("state"); var staged = Package();
        var manifest = UpdateFiles.Read<PackageManifest>(Path.Combine(staged, "package.json"));
        manifest.Files["personal.txt"] = "hash";
        UpdateFiles.Write(Path.Combine(staged, "package.json"), manifest);
        File.WriteAllText(Path.Combine(installation, "personal.txt"), "keep");
        Assert.Throws<IOException>(() => UpdateFiles.Install(installation, state, staged));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(installation, "personal.txt")));
        Assert.False(File.Exists(Path.Combine(state, "journal.json")));
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(respond(request));
    }
    private async Task<PreparedUpdate?> Check(string tag, bool draft = false, bool prerelease = false, string? badDownload = null, bool failedBefore = false)
    {
        var installation = Folder("game"); var state = Folder("state");
        UpdateFiles.Write(Path.Combine(installation, "update-settings.json"), new UpdateConfiguration("example/game"));
        if (failedBefore) File.WriteAllText(Path.Combine(state, "failed-version.txt"), tag);
        var zip = Path.Combine(Folder("archive"), "game.zip");
        ZipFile.CreateFromDirectory(Package(), zip);
        var bytes = File.ReadAllBytes(zip);
        var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes));
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.Host == "api.github.com")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new GithubRelease(tag, draft, prerelease, [new ReleaseAsset(UpdateClient.AssetName, bytes.Length, digest, "https://github.com/example/game/releases/download/v1.1.0/game.zip")]), UpdateFiles.Json)) };
            var payload = badDownload == "truncated" ? bytes[..(bytes.Length / 2)] : badDownload == "corrupt" ? new byte[bytes.Length] : bytes;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
        }));
        return await new UpdateClient(http).Prepare(installation, state, new Version(1, 0, 0), new Progress<(string, double?)>(), CancellationToken.None);
    }
    [Fact] public async Task DownloadsAndChecksNewVersion() => Assert.NotNull(await Check("v1.1.0"));
    [Fact] public async Task SameVersionDoesNotRestart() => Assert.Null(await Check("v1.0.0"));
    [Fact] public async Task FailedVersionDoesNotLoop() => Assert.Null(await Check("v1.1.0", failedBefore: true));
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task IgnoresDraftsAndPrereleases(bool draft, bool prerelease) => Assert.Null(await Check("v1.1.0", draft, prerelease));
    [Theory]
    [InlineData("truncated")]
    [InlineData("corrupt")]
    public async Task RejectsInvalidDownloadWithoutTouchingInstallation(string failure)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => Check("v1.1.0", badDownload: failure));
        Assert.False(File.Exists(Path.Combine(root, "game", "TheGang.exe")));
        Assert.False(File.Exists(Path.Combine(root, "state", "journal.json")));
    }
    [Fact]
    public async Task OfflineFailureDoesNotTouchInstallation()
    {
        var installation = Folder("game");
        UpdateFiles.Write(Path.Combine(installation, "update-settings.json"), new UpdateConfiguration("example/game"));
        File.WriteAllText(Path.Combine(installation, "TheGang.exe"), "old");
        using var http = new HttpClient(new Handler(_ => throw new HttpRequestException("offline")));
        await Assert.ThrowsAsync<HttpRequestException>(() => new UpdateClient(http).Prepare(installation, Folder("state"), new Version(1, 0, 0), new Progress<(string, double?)>(), CancellationToken.None));
        Assert.Equal("old", File.ReadAllText(Path.Combine(installation, "TheGang.exe")));
    }
}

