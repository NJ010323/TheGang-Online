using System.Net;
using System.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Threading.RateLimiting;

namespace Network;

/// <summary>Serveur HTTP privé de l'hôte, destiné à être publié par un tunnel sortant.</summary>
public sealed class LobbyHttpServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private LobbyHttpServer(WebApplication app, HostedLobby lobby, Uri localAddress)
    {
        _app = app;
        Lobby = lobby;
        LocalAddress = localAddress;
    }

    /// <summary>État de partie auquel seul ce serveur donne accès.</summary>
    public HostedLobby Lobby { get; }

    /// <summary>Adresse locale liée exclusivement à 127.0.0.1.</summary>
    public Uri LocalAddress { get; }

    /// <summary>Démarre un serveur sur un port libre de la machine de l'hôte.</summary>
    public static async Task<LobbyHttpServer> StartAsync(string nickname, int maxPlayers,
        CancellationToken cancellationToken = default)
    {
        var lobby = new HostedLobby(nickname, maxPlayers);
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 0);
            options.Limits.MaxRequestBodySize = 8 * 1024;
        });
        // Réduit les essais automatiques sur le seul point d'entrée sans session.
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddFixedWindowLimiter("join", limiter =>
            {
                limiter.PermitLimit = 12;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
                limiter.AutoReplenishment = true;
            });
        });
        var app = builder.Build();
        // Permet au client de distinguer la fermeture du jeu d'une page d'erreur du tunnel.
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-TheGang-Server"] = "1";
            await next(context);
        });
        MapEndpoints(app, lobby);
        await app.StartAsync(cancellationToken);
        var address = app.Urls.Single(url => url.StartsWith("http://127.0.0.1:", StringComparison.Ordinal));
        return new LobbyHttpServer(app, lobby, new Uri(address));
    }

    /// <summary>Arrête le serveur lorsque l'hôte ferme le jeu.</summary>
    public async ValueTask DisposeAsync() => await _app.DisposeAsync().ConfigureAwait(false);

    /// <summary>Déclare seulement les commandes nécessaires au lobby et à la partie.</summary>
    private static void MapEndpoints(WebApplication app, HostedLobby lobby)
    {
        app.UseRateLimiter();
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        app.MapPost("/join", (JoinLobbyRequest request) =>
            Respond(() => lobby.Join(request.Nickname, request.InvitationSecret)))
            .RequireRateLimiting("join");
        app.MapGet("/lobbies/{code}", (string code, HttpRequest request) =>
            Respond(() => lobby.GetLobby(code, SessionToken(request))));
        app.MapPost("/lobbies/{code}/capacity", (string code, HttpRequest request,
            ChangeLobbyCapacityRequest change) =>
            Respond(() => lobby.ChangeCapacity(code, SessionToken(request), change.MaxPlayers)));
        app.MapGet("/lobbies/{code}/session", (string code, HttpRequest request) =>
            Respond(() => lobby.Resume(code, SessionToken(request))));
        app.MapPost("/lobbies/{code}/difficulty", (string code, HttpRequest request, ChangeDifficultyRequest change) =>
            Respond(() => lobby.ChangeDifficulty(code,SessionToken(request),change.Difficulty)));
        app.MapPost("/lobbies/{code}/leave", (string code, HttpRequest request) =>
            Respond(() => { lobby.Leave(code, SessionToken(request)); return true; }));
        app.MapPost("/lobbies/{code}/start", (string code, HttpRequest request) =>
            Respond(() => lobby.Start(code, SessionToken(request))));
        app.MapGet("/lobbies/{code}/game", (string code, HttpRequest request) =>
            Respond(() => lobby.GetGame(code, SessionToken(request))));
        app.MapPost("/lobbies/{code}/game/chat", (string code, HttpRequest request, ChatPostRequest message) =>
            Respond(() => lobby.PostChat(code, SessionToken(request), message)));
        app.MapPost("/lobbies/{code}/game/actions", (string code, HttpRequest request,
            GameActionRequest action) =>
            Respond(() => lobby.Apply(code, SessionToken(request), action)));
        app.MapPost("/lobbies/{code}/game/rematch", (string code, HttpRequest request,
            RematchDecision decision) =>
            Respond(() => lobby.DecideRematch(code, SessionToken(request), decision)));
        app.MapPost("/lobbies/{code}/game/drag", (string code, HttpRequest request,
            GameDragRequest drag) =>
            Respond(() => lobby.UpdateDrag(code, SessionToken(request), drag)));
        app.MapPost("/lobbies/{code}/migration/begin", (string code, HttpRequest request) =>
            Respond(() => lobby.BeginHostMigration(code, SessionToken(request))));
        app.MapPost("/lobbies/{code}/migration/decision", (string code, HttpRequest request,
            HostMigrationDecision decision) =>
            Respond(() => lobby.DecideHostMigration(code, SessionToken(request), decision)));
        app.MapPost("/lobbies/{code}/migration/destination", (string code, HttpRequest request,
            HostMigrationDestination destination) =>
            Respond(() => lobby.PublishHostMigration(code, SessionToken(request), destination)));
        app.MapPost("/lobbies/{code}/migration/acknowledge", (string code, HttpRequest request,
            HostMigrationReference reference) =>
            Respond(() => lobby.AcknowledgeHostMigration(code, SessionToken(request), reference.Id)));
        app.MapPost("/lobbies/{code}/migration/cancel", (string code, HttpRequest request,
            HostMigrationReference reference) =>
            Respond(() => lobby.CancelHostMigration(code, SessionToken(request), reference.Id)));
        app.MapPost("/lobbies/{code}/migration/configure", (string code, HttpRequest request,
            HostMigrationSetup setup) =>
            Respond(() => lobby.ConfigureHostMigration(code, SessionToken(request), setup)));
    }

    /// <summary>Lit le secret de session sans accepter d'identifiant de joueur fourni par le client.</summary>
    private static string SessionToken(HttpRequest request)
    {
        var authorization = request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Jeton de session manquant.");
        return authorization[7..].Trim();
    }

    /// <summary>Traduit les erreurs de domaine en statuts HTTP sans divulguer d'état privé.</summary>
    private static IResult Respond<T>(Func<T> action)
    {
        try { return Results.Ok(action()); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
        catch (SecurityException) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
        catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
    }
}
