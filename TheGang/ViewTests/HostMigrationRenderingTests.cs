using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using Model.Game;
using Model.Game.Tokens.TokenEnum;
using Model.Lobby;
using Network;
using View;

namespace ViewTests;

public sealed class HostMigrationRenderingTests
{
    /// <summary>Une relève à deux attend au lobby ; la table ne se reconstruit qu'après l'arrivée d'un troisième joueur.</summary>
    [Fact]
    public void TransferredPairWaitsInLobbyBeforeRenderingAThreePlayerTable()
    {
        // WPF et les images de ressources sont créés sur un thread STA, comme dans le jeu.
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                try
                {
                    var original = new HostedLobby("Alice", 3);
                    var bob = original.Join("Bob", original.InvitationSecret);
                    original.Join("Chloe", original.InvitationSecret);
                    original.Start(original.Code, original.HostIdentity.SessionToken);
                    var migration = original.BeginHostMigration(original.Code, original.HostIdentity.SessionToken);
                    Assert.Equal(bob.PlayerId, migration.SuccessorId);

                    var next = new HostedLobby("Bob", 3);
                    next.ConfigureHostMigration(next.Code, next.HostIdentity.SessionToken,
                        new HostMigrationSetup(migration.ExpectedPlayers, migration.WasStarted,
                            migration.HeistNumber, migration.SuccessfulHeists, migration.FailedHeists, null));
                    var chloe = next.Join("Chloe", next.InvitationSecret);

                    foreach (var viewer in new[] { next.HostIdentity, chloe })
                    {
                        var lobby = next.GetLobby(next.Code, viewer.SessionToken);
                        Assert.Equal(LobbyStatus.Open, lobby.Status);
                        Assert.Equal(2, lobby.Players.Count);
                        Assert.False(lobby.AutoResumeTransferredGame);
                        Assert.Throws<InvalidOperationException>(() => next.GetGame(next.Code, viewer.SessionToken));
                    }
                    Assert.Throws<InvalidOperationException>(() => next.Start(next.Code, next.HostIdentity.SessionToken));
                    var dora = next.Join("Dora", next.InvitationSecret);
                    next.Start(next.Code, next.HostIdentity.SessionToken);
                    foreach (var viewer in new[] { next.HostIdentity, chloe, dora })
                    {
                        var game = next.GetGame(next.Code, viewer.SessionToken);
                        Assert.Equal(GamePhase.PreFlop, game.Phase);
                        Assert.Equal(3, game.Players.Count);
                        Assert.Equal(3, game.AvailableTokens.Count);
                        Assert.Equal(2, game.Players.Single(p => p.Id == viewer.PlayerId).Hand.Count);
                        var positions = TableVisuals.OpponentPositions(game.Players.Count - 1);
                        Assert.Equal(2, positions.Count);
                        // Construire et mesurer les trois places, avec leurs deux cartes chacune.
                        var table = new Canvas { Width = 1280, Height = 720 };
                        var renderedCards = 0;
                        var opponentIndex = 0;
                        foreach (var player in game.Players)
                        {
                            var isSelf = player.Id == viewer.PlayerId;
                            var seat = TableVisuals.Seat(player, isSelf,
                                player.Id == next.HostIdentity.PlayerId, false, game.Phase, TokenColors.White,
                                onCardCreated: (_, _) => renderedCards++);
                            var position = isSelf ? new Point(535, 560) : positions[opponentIndex++];
                            Canvas.SetLeft(seat, position.X);
                            Canvas.SetTop(seat, position.Y);
                            table.Children.Add(seat);
                        }
                        table.Measure(new Size(1280, 720));
                        table.Arrange(new Rect(0, 0, 1280, 720));
                        Assert.Equal(3, table.Children.Count);
                        Assert.Equal(6, renderedCards);
                    }

                    // Un ancien instantané transitoire à deux ne doit plus provoquer l'erreur "count".
                    Assert.Single(TableVisuals.OpponentPositions(1));
                    // Conserver les configurations de placement existantes.
                    for (var opponents = 1; opponents <= 5; opponents++)
                    {
                        var seats = TableVisuals.OpponentPositions(opponents);
                        Assert.Equal(opponents, seats.Count);
                        Assert.Equal(opponents, seats.Distinct().Count());
                        Assert.All(seats, seat =>
                        {
                            Assert.InRange(seat.X, 0, 1280);
                            Assert.InRange(seat.Y, 0, 720);
                        });
                    }
                    Assert.Throws<ArgumentOutOfRangeException>(() => TableVisuals.OpponentPositions(0));
                    Assert.Throws<ArgumentOutOfRangeException>(() => TableVisuals.OpponentPositions(6));

                    // Le contour ne doit ni faire déborder un pseudo long, ni masquer son nom aux lecteurs d'écran.
                    var nickname = new GildedPlayerName { Text = "Éléonore-du-Coffre-12345", FontSize = 16, Width = 90 };
                    nickname.Measure(new Size(90, 40));
                    nickname.Arrange(new Rect(0, 0, 90, 40));
                    var image = new System.Windows.Media.Imaging.RenderTargetBitmap(90, 40, 96, 96,
                        System.Windows.Media.PixelFormats.Pbgra32);
                    image.Render(nickname);
                    var pixels = new byte[90 * 40 * 4];
                    image.CopyPixels(pixels, 90 * 4, 0);
                    Assert.Contains(pixels, value => value != 0);
                    Assert.Equal(90, nickname.ActualWidth);
                    var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(nickname);
                    Assert.Equal(nickname.Text, peer.GetName());
                    nickname.Text = "Léo";
                    nickname.Width = 12;
                    nickname.Measure(new Size(12, 40));
                    nickname.Arrange(new Rect(0, 0, 12, 40));
                    Assert.Equal(12, nickname.ActualWidth);
                    Assert.Equal("Léo", peer.GetName());
                    RematchRenderingChecks.Run();
                    TutorialRenderingChecks.Run();
                    ReleaseNotesRenderingChecks.Run();
                    SpecialCardsRenderingChecks.Run();
                }
                finally { application.Shutdown(); }
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "Le test de rendu WPF n'a pas terminé.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
