using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace View;

/// <summary>Données nécessaires pour retrouver la place d'un invité après redémarrage du jeu.</summary>
internal sealed record GuestResumeTicket(string Invitation, Guid PlayerId, string SessionToken, string Nickname);

/// <summary>Garde le jeton de reprise chiffré pour le seul compte Windows courant.</summary>
internal static class GuestResumeStore
{
    private static readonly string TicketPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TheGang", "guest-resume.dat");

    /// <summary>Enregistre le jeton de session sans le laisser en clair sur le disque.</summary>
    public static void Save(GuestResumeTicket ticket)
    {
        var directory = Path.GetDirectoryName(TicketPath)!;
        Directory.CreateDirectory(directory);
        var data = JsonSerializer.SerializeToUtf8Bytes(ticket);
        var encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
        var temporaryPath = TicketPath + ".tmp";
        File.WriteAllBytes(temporaryPath, encrypted);
        File.Move(temporaryPath, TicketPath, overwrite: true);
    }

    /// <summary>Lit la dernière session sauvée ; un fichier absent ou illisible n'est pas proposé.</summary>
    public static GuestResumeTicket? TryLoad()
    {
        try
        {
            if (!File.Exists(TicketPath)) return null;
            var encrypted = File.ReadAllBytes(TicketPath);
            var data = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var ticket = JsonSerializer.Deserialize<GuestResumeTicket>(data);
            if (ticket is null || ticket.PlayerId == Guid.Empty ||
                string.IsNullOrWhiteSpace(ticket.Invitation) ||
                string.IsNullOrWhiteSpace(ticket.SessionToken)) return null;
            return ticket;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                      CryptographicException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Efface un jeton dès que la partie se termine définitivement.</summary>
    public static void Clear()
    {
        try { File.Delete(TicketPath); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
